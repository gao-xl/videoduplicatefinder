// /*
//     Copyright (C) 2026 0x90d
//     This file is part of VideoDuplicateFinder
//     VideoDuplicateFinder is free software: you can redistribute it and/or modify
//     it under the terms of the GNU Affero General Public License as published by
//     the Free Software Foundation, either version 3 of the License, or
//     (at your option) any later version.
//     VideoDuplicateFinder is distributed in the hope that it will be useful,
//     but WITHOUT ANY WARRANTY without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//     GNU Affero General Public License for more details.
//     You should have received a copy of the GNU Affero General Public License
//     along with VideoDuplicateFinder.  If not, see <http://www.gnu.org/licenses/>.
// */

using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using VDF.Core.FFTools;
using VDF.Core.Utils;
using VDF.Core.ViewModels;

namespace VDF.Core {
	public sealed partial class ScanEngine {

		void ScanForPartialDuplicates() {
			Logger.Instance.Info("Partial clip detection: building fingerprint index...");

			var alreadyGrouped = new HashSet<string>(
				Duplicates.Select(d => d.Path),
				CoreUtils.IsWindows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

			var videos = DatabaseUtils.Database
				.Where(e => !e.invalid && !e.IsImage &&
						!e.Flags.Has(EntryFlags.SilentAudioTrack) &&
						e.AudioFingerprint != null && e.AudioFingerprint.Length >= 2 &&
						!IsSilentFingerprint(e.AudioFingerprint) &&
						!alreadyGrouped.Contains(e.Path))
				.OrderByDescending(e => e.mediaInfo?.Duration ?? TimeSpan.Zero)
				.ToList();

			if (videos.Count < 2) {
				Logger.Instance.Info("Partial clip detection: fewer than 2 eligible videos, skipping.");
				return;
			}

			Logger.Instance.Info($"Partial clip detection: comparing {videos.Count} video(s) (fingerprint blocks: min={videos.Min(e => e.AudioFingerprint!.Length)}, max={videos.Max(e => e.AudioFingerprint!.Length)})...");

			float simThreshold = (float)Settings.PartialClipSimilarityThreshold;

			var matches = new ConcurrentBag<(int sourceIdx, int clipIdx, float sim, int offsetSec)>();
			int pairsChecked = 0;

			Parallel.For(0, videos.Count - 1,
				new ParallelOptions {
					CancellationToken = cancelationTokenSource.Token,
					MaxDegreeOfParallelism = Math.Max(1, Settings.MaxDegreeOfParallelism)
				},
				i => {
					FileEntry source = videos[i];
					double sourceSec = (source.mediaInfo?.Duration ?? TimeSpan.Zero).TotalSeconds;
					if (sourceSec < 1.0) return;

					for (int j = i + 1; j < videos.Count; j++) {
						if (cancelationTokenSource.IsCancellationRequested) break;
						FileEntry clip = videos[j];
						double clipSec = (clip.mediaInfo?.Duration ?? TimeSpan.Zero).TotalSeconds;
						if (clipSec < 1.0) continue;

						if (clipSec / sourceSec < Settings.PartialClipMinRatio) continue;
						if (clipSec / sourceSec >= 0.95) continue;

						uint[] fpSource = source.AudioFingerprint!;
						uint[] fpClip = clip.AudioFingerprint!;
						if (fpClip.Length >= fpSource.Length) continue;

						Interlocked.Increment(ref pairsChecked);
						var (sim, offsetSec) = SlidingWindowCompare(fpClip, fpSource, simThreshold);

						if (sim >= simThreshold)
							matches.Add((i, j, sim, offsetSec));
					}
				});

			var assignments = AssignPartialClipGroups(matches);

			if (Settings.PartialClipRequireVisualMatch && assignments.Count > 0) {
				int beforeCount = assignments.Count;
				int dropped = 0;
				var verified = new ConcurrentBag<(int, int, float, int, Guid)>();
				try {
					Parallel.ForEach(assignments, new ParallelOptions {
						CancellationToken = cancelationTokenSource.Token,
						MaxDegreeOfParallelism = Math.Max(1, Settings.MaxDegreeOfParallelism)
					}, a => {
						bool pass = VerifyPartialClipVisually(videos[a.sourceIdx], videos[a.clipIdx], a.offsetSec, out float visualSim);
						if (pass) {
							verified.Add(a);
						}
						else {
							Interlocked.Increment(ref dropped);
							if (Settings.ExtendedFFToolsLogging)
								Logger.Instance.Info($"[Partial] Visual gate dropped {System.IO.Path.GetFileName(videos[a.clipIdx].Path)} in {System.IO.Path.GetFileName(videos[a.sourceIdx].Path)}: visualSim={visualSim:P1} (threshold {Settings.PartialClipVisualThreshold:P0})");
						}
					});
				}
				catch (OperationCanceledException) { }
				assignments = verified.OrderBy(a => a.Item1).ThenBy(a => a.Item2).ToList();
				Logger.Instance.Info($"Partial clip detection: visual gate kept {assignments.Count}/{beforeCount} assignment(s), dropped {dropped}");
			}

			var addedSources = new HashSet<int>();

			foreach (var (si, ci, sim, offsetSec, groupId) in assignments) {
				FileEntry source = videos[si];
				FileEntry clip = videos[ci];

				if (Settings.ExtendedFFToolsLogging)
					Logger.Instance.Info($"[Partial] {System.IO.Path.GetFileName(clip.Path)} in {System.IO.Path.GetFileName(source.Path)}: sim={sim:P1} @ {offsetSec}s (threshold {Settings.PartialClipSimilarityThreshold:P0}, fp {clip.AudioFingerprint!.Length}/{source.AudioFingerprint!.Length} blocks)");

				if (addedSources.Add(si))
					Duplicates.Add(new DuplicateItem(source, 0f, groupId, DuplicateFlags.None));

				Duplicates.Add(new DuplicateItem(clip, 1f - sim, groupId, DuplicateFlags.PartialClip) {
					PartialClipOffset = TimeSpan.FromSeconds(offsetSec)
				});
			}

			Logger.Instance.Info($"Partial clip detection: checked {pairsChecked} pair(s), found {matches.Count} candidate match(es), formed {assignments.Count} clip-source assignment(s).");
		}

		bool VerifyPartialClipVisually(FileEntry source, FileEntry clip, int offsetSec, out float visualSim) {
			visualSim = 0f;
			double sourceSec = (source.mediaInfo?.Duration ?? TimeSpan.Zero).TotalSeconds;
			double clipSec = (clip.mediaInfo?.Duration ?? TimeSpan.Zero).TotalSeconds;
			if (sourceSec <= 0 || clipSec <= 0) return true;

			var clipTimes = new List<double>(3);
			if (clipSec >= 9.0) {
				clipTimes.Add(clipSec * 0.25);
				clipTimes.Add(clipSec * 0.50);
				clipTimes.Add(clipSec * 0.75);
			}
			else if (clipSec >= 3.0) {
				clipTimes.Add(clipSec * 0.33);
				clipTimes.Add(clipSec * 0.66);
			}
			else {
				clipTimes.Add(clipSec * 0.5);
			}

			bool useP = Settings.UsePHashing;
			double threshold = Settings.PartialClipVisualThreshold;
			int comparisons = 0;
			float simSum = 0f;

			var srcSampleTimes = new List<double>(clipTimes.Count);
			var clipSampleTimes = new List<double>(clipTimes.Count);
			foreach (double t in clipTimes) {
				double srcAt = offsetSec + t;
				if (srcAt >= sourceSec - 0.1 || t >= clipSec - 0.1) continue;
				srcSampleTimes.Add(srcAt);
				clipSampleTimes.Add(t);
			}
			if (srcSampleTimes.Count == 0) return true;

			byte[]?[] srcFrames = FfmpegEngine.GetGrayFrames(source.Path, srcSampleTimes, Settings.ExtendedFFToolsLogging);
			byte[]?[] clipFrames = FfmpegEngine.GetGrayFrames(clip.Path, clipSampleTimes, Settings.ExtendedFFToolsLogging);

			for (int i = 0; i < srcSampleTimes.Count; i++) {
				byte[]? srcFrame = srcFrames[i];
				byte[]? clipFrame = clipFrames[i];
				if (srcFrame == null || clipFrame == null) continue;

				float pairSim;
				if (useP) {
					ulong hSrc = pHash.PerceptualHash.ComputePHashFromGray32x32(srcFrame);
					ulong hClip = pHash.PerceptualHash.ComputePHashFromGray32x32(clipFrame);
					pHash.PHashCompare.IsDuplicateByPercent(hSrc, hClip, out pairSim, threshold, strict: true);
				}
				else {
					float diff = GrayBytesUtils.PercentageDifference(srcFrame, clipFrame);
					pairSim = 1f - diff;
				}
				simSum += pairSim;
				comparisons++;
			}

			if (comparisons == 0) return true;
			visualSim = simSum / comparisons;
			return visualSim >= threshold;
		}

		internal static List<(int sourceIdx, int clipIdx, float sim, int offsetSec, Guid groupId)>
			AssignPartialClipGroups(IEnumerable<(int sourceIdx, int clipIdx, float sim, int offsetSec)> matches) {
			var sourceGroupId = new Dictionary<int, Guid>();
			var assignedClips = new HashSet<int>();
			var assignments = new List<(int, int, float, int, Guid)>();

			foreach (var (si, ci, sim, offsetSec) in matches.OrderBy(m => m.sourceIdx).ThenBy(m => m.clipIdx)) {
				if (!assignedClips.Add(ci)) continue;

				if (!sourceGroupId.TryGetValue(si, out Guid groupId)) {
					groupId = Guid.NewGuid();
					sourceGroupId[si] = groupId;
				}
				assignments.Add((si, ci, sim, offsetSec, groupId));
			}
			return assignments;
		}

		internal static (float similarity, int offsetBlocks) SlidingWindowCompare(uint[] shorter, uint[] longer, float minSim = 0f) {
			int lenS = shorter.Length;
			int lenL = longer.Length;
			int maxOffset = lenL - lenS;
			int totalBitsCapacity = lenS * 32;

			float bestSim = 0f;
			int bestOffset = 0;

			for (int offset = 0; offset <= maxOffset; offset++) {
				int maxAllowedBits = (int)((1f - Math.Max(bestSim, minSim)) * totalBitsCapacity);

				int totalBits = HammingDistance(shorter, longer, offset, lenS, maxAllowedBits);

				if (totalBits > maxAllowedBits)
					continue;

				float sim = 1f - (float)totalBits / totalBitsCapacity;
				if (sim > bestSim) {
					bestSim = sim;
					bestOffset = offset;
				}
			}

			return (bestSim, bestOffset);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		static int HammingDistance(uint[] a, uint[] b, int offset, int len, int maxAllowedBits) {
			int totalBits = 0;
			int k = 0;

			if (Vector256.IsHardwareAccelerated && len >= 8) {
				ref uint aRef = ref MemoryMarshal.GetArrayDataReference(a);
				ref uint bRef = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(b), offset);

				for (; k + 8 <= len; k += 8) {
					var va = Vector256.LoadUnsafe(ref aRef, (nuint)k);
					var vb = Vector256.LoadUnsafe(ref bRef, (nuint)k);
					var xored = (va ^ vb).AsUInt64();

					totalBits += BitOperations.PopCount(xored.GetElement(0))
							   + BitOperations.PopCount(xored.GetElement(1))
							   + BitOperations.PopCount(xored.GetElement(2))
							   + BitOperations.PopCount(xored.GetElement(3));

					if (totalBits > maxAllowedBits) return totalBits;
				}
			}
			else if (Vector128.IsHardwareAccelerated && len >= 4) {
				ref uint aRef = ref MemoryMarshal.GetArrayDataReference(a);
				ref uint bRef = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(b), offset);

				for (; k + 4 <= len; k += 4) {
					var va = Vector128.LoadUnsafe(ref aRef, (nuint)k);
					var vb = Vector128.LoadUnsafe(ref bRef, (nuint)k);
					var xored = (va ^ vb).AsUInt64();

					totalBits += BitOperations.PopCount(xored.GetElement(0))
							   + BitOperations.PopCount(xored.GetElement(1));

					if (totalBits > maxAllowedBits) return totalBits;
				}
			}

			for (; k < len; k++) {
				totalBits += BitOperations.PopCount(a[k] ^ b[offset + k]);
			}

			return totalBits;
		}
	}
}
