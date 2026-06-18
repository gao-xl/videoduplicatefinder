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

		void PrepareCompare() {
			if (positionList.Count == 0) {
				BuildPositionList();
			}
			else if (Settings.ThumbnailCount != positionList.Count) {
				throw new Exception("Number of thumbnails can't be changed between quick rescans! Rescan has been aborted.");
			}
			NormalizeScanPaths();
			_includeMatcher ??= new PathMatcher(Settings.IncludeList);
			if (DatabaseUtils.Database.Count == 0) {
				DatabaseUtils.CustomDatabaseFolder = Settings.CustomDatabaseFolder;
				DatabaseUtils.InvalidateDatabaseFolder();
				DatabaseUtils.LoadDatabase();
				foreach (FileEntry entry in DatabaseUtils.Database) {
					entry.invalid = InvalidEntry(entry, out _, out string? reason);
					if (entry.invalid && reason != null)
						LogExcludedFile(entry, reason);
				}
			}

			CancelAllTasks();

			Duplicates.Clear();
			SearchTimer.Reset();
			if (!ElapsedTimer.IsRunning)
				ElapsedTimer.Reset();

			isScanning = true;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		double GetGrayBytesIndex(FileEntry entry, float position) =>
			entry.GetGrayBytesIndex(position, Settings.MaxSamplingDurationSeconds);

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		static int GetEntryPixelCount(FileEntry entry) {
			var stream = entry.mediaInfo?.Streams?.FirstOrDefault(s => s.Width > 0 && s.Height > 0);
			return stream != null ? stream.Width * stream.Height : 0;
		}

		static byte[]?[] CreateFlippedGrayBytes(FileEntry entry, ConcurrentBag<byte[]> rentedBuffers) {
			byte[]?[] source = entry.compareGray!;
			var flipped = new byte[]?[source.Length];
			for (int j = 0; j < source.Length; j++) {
				int len = source[j]!.Length;
				byte[] buf = System.Buffers.ArrayPool<byte>.Shared.Rent(len);
				rentedBuffers.Add(buf);
				flipped[j] = GrayBytesUtils.FlipGrayScale(source[j]!, buf);
			}
			return flipped;
		}

		static ulong?[] CreateFlippedPHashes(byte[]?[] flippedGray, bool usePHashing) {
			if (!usePHashing || flippedGray == null) return Array.Empty<ulong?>();
			var result = new ulong?[flippedGray.Length];
			for (int j = 0; j < flippedGray.Length; j++) {
				if (flippedGray[j] != null)
					result[j] = pHash.PerceptualHash.ComputePHashFromGray32x32(flippedGray[j]!);
			}
			return result;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		static bool QuickPHashPreFilterMulti(FileEntry a, FileEntry b) {
			ulong?[]? phashesA = a.comparePHashes;
			ulong?[]? phashesB = b.comparePHashes;
			if (phashesA == null || phashesB == null || phashesA.Length != phashesB.Length)
				return QuickPHashPreFilter(a, b);
			for (int i = 0; i < phashesA.Length; i++) {
				if (phashesA[i] == null || phashesB[i] == null) continue;
				int hamming = BitOperations.PopCount(phashesA[i]!.Value ^ phashesB[i]!.Value);
				if (hamming > 32) return false;
			}
			return true;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		static bool QuickPHashPreFilter(FileEntry a, FileEntry b) {
			ulong? phashA = a.comparePHash;
			ulong? phashB = b.comparePHash;
			if (phashA == null || phashB == null) return true;
			int hamming = BitOperations.PopCount(phashA.Value ^ phashB.Value);
			return hamming <= 32;
		}

		bool TryBuildCompareSnapshot(FileEntry entry, bool usePHashing) {
			DatabaseUtils.EnsureHeavyFieldsLoaded(entry);

			if (entry.IsImage) {
				if (!entry.grayBytes.TryGetValue(0, out byte[]? imageGray) || imageGray == null)
					return false;
				entry.compareGray = new[] { imageGray };
				return true;
			}

			var gray = new byte[]?[positionList.Count];
			for (int j = 0; j < positionList.Count; j++) {
				double idx = GetGrayBytesIndex(entry, positionList[j]);
				if (!entry.grayBytes.TryGetValue(idx, out byte[]? data) || data == null)
					return false;
				gray[j] = data;
			}
			entry.compareGray = gray;

			if (usePHashing) {
				var phashes = new ulong?[positionList.Count];
				for (int j = 0; j < positionList.Count; j++) {
					double idx = GetGrayBytesIndex(entry, positionList[j]);
					if (!entry.PHashes.TryGetValue(idx, out ulong? phash)) {
						phash = pHash.PerceptualHash.ComputePHashFromGray32x32(gray[j]);
						entry.PHashes[idx] = phash;
						entry.dirty = true;
					}
					if (phash == null)
						LogMissingPHash(entry.Path);
					phashes[j] = phash;
				}
				entry.comparePHashes = phashes;
				entry.comparePHash = phashes[0];
			}
			if (!usePHashing && entry.comparePHash == null && gray[0] != null) {
				double idx0 = GetGrayBytesIndex(entry, positionList[0]);
				if (!entry.PHashes.TryGetValue(idx0, out ulong? cachedHash)) {
					cachedHash = pHash.PerceptualHash.ComputePHashFromGray32x32(gray[0]);
					entry.PHashes[idx0] = cachedHash;
					entry.dirty = true;
				}
				entry.comparePHash = cachedHash;
			}
			return true;
		}

		bool CheckIfDuplicate(FileEntry entry, byte[]?[]? overrideGray, ulong?[]? overridePHashes, FileEntry compItem, out float difference) {
			byte[]?[] grayBytes = overrideGray ?? entry.compareGray!;
			float differenceLimit = 1.0f - Settings.Percent / 100f;
			bool ignoreBlackPixels = Settings.IgnoreBlackPixels;
			bool ignoreWhitePixels = Settings.IgnoreWhitePixels;
			difference = 1f;

			if (entry.IsImage) {
				difference = ignoreBlackPixels || ignoreWhitePixels ?
								GrayBytesUtils.PercentageDifferenceWithoutSpecificPixels(grayBytes[0]!, compItem.compareGray![0]!, ignoreBlackPixels, ignoreWhitePixels) :
								GrayBytesUtils.PercentageDifference(grayBytes[0]!, compItem.compareGray![0]!);
				return difference <= differenceLimit;
			}

			if (Settings.UsePHashing) {
				float differenceLimitpHash = Settings.Percent / 100f;

				ulong?[]? phashes = overrideGray != null ? overridePHashes : entry.comparePHashes;
				ulong?[]? compPhashes = compItem.comparePHashes;

				if (phashes == null || compPhashes == null) {
					ulong? phash = overrideGray != null ? (overridePHashes != null && overridePHashes.Length > 0 ? overridePHashes[0] : null) : entry.comparePHash;
					ulong? phash_comp = compItem.comparePHash;
					if (phash == null || phash_comp == null) {
						difference = 1f;
						return false;
					}
					bool isDup = pHash.PHashCompare.IsDuplicateByPercent(phash.Value, phash_comp.Value, out float similarity, differenceLimitpHash, strict: true);
					difference = 1f - similarity;
					return isDup;
				}

				float totalSimilarity = 0f;
				for (int j = 0; j < phashes.Length; j++) {
					if (phashes[j] == null || compPhashes[j] == null) {
						difference = 1f;
						return false;
					}
					bool posIsDup = pHash.PHashCompare.IsDuplicateByPercent(phashes[j]!.Value, compPhashes[j]!.Value, out float posSimilarity, differenceLimitpHash, strict: true);
					if (!posIsDup) {
						difference = 1f - posSimilarity;
						return false;
					}
					totalSimilarity += posSimilarity;
				}
				difference = 1f - totalSimilarity / phashes.Length;
				return true;
			}

			byte[]?[] compGray = compItem.compareGray!;
			differenceLimit *= grayBytes.Length;
			float diffSum = 0;
			for (int j = 0; j < grayBytes.Length; j++) {
				diffSum += ignoreBlackPixels || ignoreWhitePixels ?
							GrayBytesUtils.PercentageDifferenceWithoutSpecificPixels(
								grayBytes[j]!, compGray[j]!, ignoreBlackPixels, ignoreWhitePixels) :
							GrayBytesUtils.PercentageDifference(grayBytes[j]!, compGray[j]!);
				if (diffSum > differenceLimit)
					return false;
			}
			difference = diffSum / grayBytes.Length;
			return !float.IsNaN(difference);
		}

		internal void ScanForDuplicates() {
			var duplicateDict = new ConcurrentDictionary<string, DuplicateItem>(CoreUtils.IsWindows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
			var rentedBuffers = new ConcurrentBag<byte[]>();
			var groupRepresentatives = new ConcurrentDictionary<Guid, FileEntry>();
			var groupMembers = new ConcurrentDictionary<Guid, List<DuplicateItem>>();
			var groupLocks = new ConcurrentDictionary<Guid, object>();
			object GetGroupLock(Guid groupId) => groupLocks.GetOrAdd(groupId, _ => new object());
			int mergesBlocked = 0;
			missingPHashFiles.Clear();

			List<FileEntry> ScanList = new();

			Logger.Instance.Info("Prepare list of items to compare...");
			foreach (FileEntry entry in DatabaseUtils.Database) {
				if (!InvalidEntryForDuplicateCheck(entry)) {
					ScanList.Add(entry);
				}
			}

			bool usePHashing = Settings.UsePHashing;
			int droppedSnapshots = 0;
			{
				List<FileEntry> validated = new(ScanList.Count);
				foreach (FileEntry entry in ScanList) {
					if (TryBuildCompareSnapshot(entry, usePHashing)) {
						entry.compareIndex = validated.Count;
						validated.Add(entry);
					}
					else
						droppedSnapshots++;
				}
				ScanList = validated;
			}
			if (droppedSnapshots > 0)
				Logger.Instance.Info($"Excluded {droppedSnapshots} file(s) with incomplete cached scan data (missing gray bytes for the current thumbnail positions). Rescan to repopulate.");

			pHash.PHashLSHIndex? lshIndex = null;
			if (usePHashing) {
				var lshItems = ScanList
					.Where(e => !e.IsImage && e.comparePHash != null)
					.Select(e => (e, e.comparePHash!.Value))
					.ToList();
				if (lshItems.Count > 0) {
					int maxHammingBits = (int)Math.Floor((1.0 - Settings.Percent / 100.0) * 64);
					lshIndex = new pHash.PHashLSHIndex(hammingThreshold: Math.Max(maxHammingBits, 6));
					lshIndex.Build(lshItems);
					Logger.Instance.Info($"LSH index built: {lshItems.Count} video entries indexed (hammingThreshold={Math.Max(maxHammingBits, 6)})");
				}
			}

			Logger.Instance.Info($"Scanning for duplicates in {ScanList.Count:N0} files");

			InitProgress(ScanList.Count);

			const int bucketSizeSeconds = 1;
			const int bucketActivationThreshold = 5000;
			var imageEntries = new List<FileEntry>();
			var videoEntries = new List<FileEntry>();
			var videoBuckets = new Dictionary<int, List<FileEntry>>();
			const int largeBucketThreshold = 400;

			for (int i = 0; i < ScanList.Count; i++) {
				var entry = ScanList[i];
				if (entry.IsImage) {
					imageEntries.Add(entry);
					continue;
				}
				videoEntries.Add(entry);
				int bucketKey = (int)Math.Floor(entry.mediaInfo!.Duration.TotalSeconds / bucketSizeSeconds);
				if (!videoBuckets.TryGetValue(bucketKey, out var bucket)) {
					bucket = new List<FileEntry>();
					videoBuckets.Add(bucketKey, bucket);
				}
				bucket.Add(entry);
			}

			void MergeDuplicate(FileEntry entry, FileEntry compItem, float difference, DuplicateFlags flags) {
				bool foundBase = duplicateDict.TryGetValue(entry.Path, out DuplicateItem? existingBase);
				bool foundComp = duplicateDict.TryGetValue(compItem.Path, out DuplicateItem? existingComp);

				if (foundBase && foundComp) {
					if (existingBase!.GroupId != existingComp!.GroupId) {
						Guid lock1, lock2;
						if (existingBase.GroupId.CompareTo(existingComp.GroupId) < 0) {
							lock1 = existingBase.GroupId;
							lock2 = existingComp.GroupId;
						} else {
							lock1 = existingComp.GroupId;
							lock2 = existingBase.GroupId;
						}
						lock (GetGroupLock(lock1)) {
							lock (GetGroupLock(lock2)) {
								if (!duplicateDict.TryGetValue(entry.Path, out existingBase) ||
									!duplicateDict.TryGetValue(compItem.Path, out existingComp) ||
									existingBase.GroupId == existingComp.GroupId)
									return;

								if (groupRepresentatives.TryGetValue(existingBase.GroupId, out var repBase) &&
									groupRepresentatives.TryGetValue(existingComp.GroupId, out var repComp) &&
									!CheckIfDuplicate(repBase, null, null, repComp, out _)) {
									Interlocked.Increment(ref mergesBlocked);
									return;
								}

								Guid absorbedGroupId = existingComp.GroupId;
								if (!groupMembers.TryGetValue(existingBase.GroupId, out List<DuplicateItem> baseMembers)) {
									return;
								}
								lock (baseMembers) {
									if (groupMembers.TryGetValue(absorbedGroupId, out var absorbedMembers)) {
										lock (absorbedMembers) {
											foreach (DuplicateItem dup in absorbedMembers) {
												dup.GroupId = existingBase.GroupId;
												baseMembers.Add(dup);
											}
										}
										groupMembers.TryRemove(absorbedGroupId, out _);
										groupRepresentatives.TryRemove(absorbedGroupId, out _);
										groupLocks.TryRemove(absorbedGroupId, out _);
									}
								}
							}
						}
					}
				}
				else if (foundBase) {
					Guid groupId = existingBase!.GroupId;
					lock (GetGroupLock(groupId)) {
						if (groupRepresentatives.TryGetValue(groupId, out var rep) &&
							!CheckIfDuplicate(rep, null, null, compItem, out _)) {
							Interlocked.Increment(ref mergesBlocked);
							return;
						}
						var newItem = new DuplicateItem(compItem, difference, groupId, flags);
						if (duplicateDict.TryAdd(compItem.Path, newItem)) {
							if (groupMembers.TryGetValue(groupId, out var members)) {
								lock (members) { members.Add(newItem); }
							}
						}
					}
				}
				else if (foundComp) {
					Guid groupId = existingComp!.GroupId;
					lock (GetGroupLock(groupId)) {
						if (groupRepresentatives.TryGetValue(groupId, out var rep) &&
							!CheckIfDuplicate(rep, null, null, entry, out _)) {
							Interlocked.Increment(ref mergesBlocked);
							return;
						}
						var newItem = new DuplicateItem(entry, difference, groupId, DuplicateFlags.None);
						if (duplicateDict.TryAdd(entry.Path, newItem)) {
							if (groupMembers.TryGetValue(groupId, out var members)) {
								lock (members) { members.Add(newItem); }
							}
						}
					}
				}
				else {
					var groupId = Guid.NewGuid();
					var compDup = new DuplicateItem(compItem, difference, groupId, flags);
					var entryDup = new DuplicateItem(entry, difference, groupId, DuplicateFlags.None);
					if (duplicateDict.TryAdd(compItem.Path, compDup) && duplicateDict.TryAdd(entry.Path, entryDup)) {
						groupMembers[groupId] = new List<DuplicateItem> { compDup, entryDup };
						groupRepresentatives[groupId] = entry;
					}
				}
			}

			bool TryCheckDuplicate(FileEntry entry, FileEntry compItem, byte[]?[]? flippedGrayBytes, ulong?[]? flippedPHashes, out float difference, out DuplicateFlags flags) {
				flags = DuplicateFlags.None;
				difference = 0;
				bool isDuplicate = CheckIfDuplicate(entry, null, (ulong?[]?)null, compItem, out difference);
				if (Settings.CompareHorizontallyFlipped &&
					CheckIfDuplicate(entry, flippedGrayBytes, flippedPHashes, compItem, out float flippedDifference)) {
					if (!isDuplicate || flippedDifference < difference) {
						flags |= DuplicateFlags.Flipped;
						isDuplicate = true;
						difference = flippedDifference;
					}
				}
				return isDuplicate;
			}

			double GetDurationToleranceSeconds(double durationSeconds) =>
				Settings.GetDurationToleranceSeconds(durationSeconds);

			void CompareEntry(FileEntry entry, int entryIndex, IEnumerable<int> candidateBucketKeys) {
				pauseTokenSource.WaitWhilePaused(cancelationTokenSource.Token);

				float difference = 0;
				bool isDuplicate;
				DuplicateFlags flags;
				double entryDurationSeconds = entry.mediaInfo!.Duration.TotalSeconds;
				double entryToleranceSeconds = GetDurationToleranceSeconds(entryDurationSeconds);

				if (Settings.CompareHorizontallyFlipped && entry.compareFlippedGray == null) {
					entry.compareFlippedGray = CreateFlippedGrayBytes(entry, rentedBuffers);
					if (usePHashing)
						entry.compareFlippedPHashes = CreateFlippedPHashes(entry.compareFlippedGray, usePHashing);
				}

				foreach (int bucketKey in candidateBucketKeys) {
					if (!videoBuckets.TryGetValue(bucketKey, out var bucketEntries))
						continue;
					foreach (var compItem in bucketEntries) {
						int compIndex = compItem.compareIndex;
						if (compIndex <= entryIndex)
							continue;

						if (!QuickPHashPreFilterMulti(entry, compItem))
							continue;

						if (!entry.IsImage) {
							double compDurationSeconds = compItem.mediaInfo!.Duration.TotalSeconds;
							double compToleranceSeconds = GetDurationToleranceSeconds(compDurationSeconds);
							double allowedSeconds = Math.Min(entryToleranceSeconds, compToleranceSeconds);
							double diffSeconds = Math.Abs(entryDurationSeconds - compDurationSeconds);
							if (diffSeconds > allowedSeconds)
								continue;
						}

						if (Settings.FileSizeTolerancePercent > 0 && !entry.IsImage) {
							long minSize = (long)(entry.FileSize * (1.0 - Settings.FileSizeTolerancePercent / 100.0));
							long maxSize = (long)(entry.FileSize * (1.0 + Settings.FileSizeTolerancePercent / 100.0));
							if (compItem.FileSize < minSize || compItem.FileSize > maxSize)
								continue;
						}

						if (Settings.EnableResolutionPreFilter && !entry.IsImage) {
							int entryPixels = GetEntryPixelCount(entry);
							int compPixels = GetEntryPixelCount(compItem);
							if (entryPixels > 0 && compPixels > 0) {
								int smaller = Math.Min(entryPixels, compPixels);
								int larger = Math.Max(entryPixels, compPixels);
								if (smaller < larger / 2)
									continue;
							}
						}

						if (Settings.FolderMatchMode == FolderMatchMode.SameFolderOnly &&
							!SameFolderAtDepth(entry.Folder, compItem.Folder, Settings.SameFolderDepth))
							continue;
						if (Settings.FolderMatchMode == FolderMatchMode.DifferentFolderOnly &&
							SameFolderAtDepth(entry.Folder, compItem.Folder, Settings.SameFolderDepth))
							continue;

						isDuplicate = TryCheckDuplicate(entry, compItem, entry.compareFlippedGray, entry.compareFlippedPHashes, out difference, out flags);

						if (isDuplicate &&
							entry.FileSize == compItem.FileSize &&
							entry.mediaInfo!.Duration == compItem.mediaInfo!.Duration &&
							Settings.ExcludeHardLinks &&
							HardLinkUtils.AreSameFile(entry.Path, compItem.Path)) {
							isDuplicate = false;
						}

						if (isDuplicate)
							MergeDuplicate(entry, compItem, difference, flags);
					}
				}
				IncrementProgress(entry.Path);
			}

			void CompareImages() {
				Action<int> compareAction = i => {
					var entry = imageEntries[i];
					if (Settings.CompareHorizontallyFlipped && entry.compareFlippedGray == null)
						entry.compareFlippedGray = CreateFlippedGrayBytes(entry, rentedBuffers);
					for (int n = i + 1; n < imageEntries.Count; n++) {
					var compItem = imageEntries[n];

					if (!QuickPHashPreFilterMulti(entry, compItem))
						continue;

					if (Settings.FileSizeTolerancePercent > 0) {
						long minSize = (long)(entry.FileSize * (1.0 - Settings.FileSizeTolerancePercent / 100.0));
						long maxSize = (long)(entry.FileSize * (1.0 + Settings.FileSizeTolerancePercent / 100.0));
						if (compItem.FileSize < minSize || compItem.FileSize > maxSize)
							continue;
					}

					float difference = 0;
						DuplicateFlags flags;
						if (Settings.FolderMatchMode == FolderMatchMode.SameFolderOnly &&
							!SameFolderAtDepth(entry.Folder, compItem.Folder, Settings.SameFolderDepth))
							continue;
						if (Settings.FolderMatchMode == FolderMatchMode.DifferentFolderOnly &&
							SameFolderAtDepth(entry.Folder, compItem.Folder, Settings.SameFolderDepth))
							continue;
						bool isDuplicate = TryCheckDuplicate(entry, compItem, entry.compareFlippedGray, null, out difference, out flags);

						if (isDuplicate &&
							entry.FileSize == compItem.FileSize &&
							Settings.ExcludeHardLinks &&
							HardLinkUtils.AreSameFile(entry.Path, compItem.Path)) {
							isDuplicate = false;
						}

						if (isDuplicate)
							MergeDuplicate(entry, compItem, difference, flags);
					}
					IncrementProgress(entry.Path);
				};

				try {
					if (imageEntries.Count >= largeBucketThreshold) {
						Parallel.For(0, imageEntries.Count, new ParallelOptions { CancellationToken = cancelationTokenSource.Token, MaxDegreeOfParallelism = Settings.GetEffectiveParallelism() }, compareAction);
					}
					else {
						for (int i = 0; i < imageEntries.Count; i++)
							compareAction(i);
					}
				}
				catch (OperationCanceledException) { }
			}

			void CompareVideosLinear() {
				Action<int> compareAction = i => {
					pauseTokenSource.WaitWhilePaused(cancelationTokenSource.Token);

					var entry = videoEntries[i];
					float difference = 0;
					DuplicateFlags flags;
					double entryDurationSeconds = entry.mediaInfo!.Duration.TotalSeconds;
					double entryToleranceSeconds = GetDurationToleranceSeconds(entryDurationSeconds);

					if (Settings.CompareHorizontallyFlipped && entry.compareFlippedGray == null) {
						entry.compareFlippedGray = CreateFlippedGrayBytes(entry, rentedBuffers);
						if (usePHashing)
							entry.compareFlippedPHashes = CreateFlippedPHashes(entry.compareFlippedGray, usePHashing);
					}

					for (int n = i + 1; n < videoEntries.Count; n++) {
						var compItem = videoEntries[n];

						if (!QuickPHashPreFilterMulti(entry, compItem))
							continue;

						double compDurationSeconds = compItem.mediaInfo!.Duration.TotalSeconds;
						double compToleranceSeconds = GetDurationToleranceSeconds(compDurationSeconds);
						double allowedSeconds = Math.Min(entryToleranceSeconds, compToleranceSeconds);
						double diffSeconds = Math.Abs(entryDurationSeconds - compDurationSeconds);
						if (diffSeconds > allowedSeconds)
							continue;

						if (Settings.FileSizeTolerancePercent > 0) {
							long minSize = (long)(entry.FileSize * (1.0 - Settings.FileSizeTolerancePercent / 100.0));
							long maxSize = (long)(entry.FileSize * (1.0 + Settings.FileSizeTolerancePercent / 100.0));
							if (compItem.FileSize < minSize || compItem.FileSize > maxSize)
								continue;
						}

						if (Settings.EnableResolutionPreFilter) {
							int entryPixels = GetEntryPixelCount(entry);
							int compPixels = GetEntryPixelCount(compItem);
							if (entryPixels > 0 && compPixels > 0) {
								int smaller = Math.Min(entryPixels, compPixels);
								int larger = Math.Max(entryPixels, compPixels);
								if (smaller < larger / 2)
									continue;
							}
						}

						if (Settings.FolderMatchMode == FolderMatchMode.SameFolderOnly &&
							!SameFolderAtDepth(entry.Folder, compItem.Folder, Settings.SameFolderDepth))
							continue;
						if (Settings.FolderMatchMode == FolderMatchMode.DifferentFolderOnly &&
							SameFolderAtDepth(entry.Folder, compItem.Folder, Settings.SameFolderDepth))
							continue;

						bool isDuplicate = TryCheckDuplicate(entry, compItem, entry.compareFlippedGray, entry.compareFlippedPHashes, out difference, out flags);
					if (isDuplicate &&
						entry.FileSize == compItem.FileSize &&
						entry.mediaInfo!.Duration == compItem.mediaInfo!.Duration &&
							Settings.ExcludeHardLinks &&
							HardLinkUtils.AreSameFile(entry.Path, compItem.Path)) {
							isDuplicate = false;
						}

						if (isDuplicate)
							MergeDuplicate(entry, compItem, difference, flags);
					}

					IncrementProgress(entry.Path);
				};

				try {
					if (videoEntries.Count >= largeBucketThreshold) {
						Parallel.For(0, videoEntries.Count, new ParallelOptions { CancellationToken = cancelationTokenSource.Token, MaxDegreeOfParallelism = Settings.GetEffectiveParallelism() }, compareAction);
					}
					else {
						for (int i = 0; i < videoEntries.Count; i++)
							compareAction(i);
					}
				}
				catch (OperationCanceledException) { }
			}

			void CompareVideosLSH() {
				Action<int> compareAction = i => {
					pauseTokenSource.WaitWhilePaused(cancelationTokenSource.Token);

					var entry = videoEntries[i];
					float difference = 0;
					DuplicateFlags flags;
					double entryDurationSeconds = entry.mediaInfo!.Duration.TotalSeconds;
					double entryToleranceSeconds = GetDurationToleranceSeconds(entryDurationSeconds);

					if (Settings.CompareHorizontallyFlipped && entry.compareFlippedGray == null) {
						entry.compareFlippedGray = CreateFlippedGrayBytes(entry, rentedBuffers);
						if (usePHashing)
							entry.compareFlippedPHashes = CreateFlippedPHashes(entry.compareFlippedGray, usePHashing);
					}

					if (entry.comparePHash != null) {
						var candidates = lshIndex!.Query(entry.comparePHash.Value, entry.compareIndex);
						foreach (var compItem in candidates) {
							double compDurationSeconds = compItem.mediaInfo!.Duration.TotalSeconds;
							double compToleranceSeconds = GetDurationToleranceSeconds(compDurationSeconds);
							double allowedSeconds = Math.Min(entryToleranceSeconds, compToleranceSeconds);
							double diffSeconds = Math.Abs(entryDurationSeconds - compDurationSeconds);
							if (diffSeconds > allowedSeconds)
								continue;

							if (Settings.FileSizeTolerancePercent > 0) {
								long minSize = (long)(entry.FileSize * (1.0 - Settings.FileSizeTolerancePercent / 100.0));
								long maxSize = (long)(entry.FileSize * (1.0 + Settings.FileSizeTolerancePercent / 100.0));
								if (compItem.FileSize < minSize || compItem.FileSize > maxSize)
									continue;
							}

							if (Settings.EnableResolutionPreFilter) {
								int entryPixels = GetEntryPixelCount(entry);
								int compPixels = GetEntryPixelCount(compItem);
								if (entryPixels > 0 && compPixels > 0) {
									int smaller = Math.Min(entryPixels, compPixels);
									int larger = Math.Max(entryPixels, compPixels);
									if (smaller < larger / 2)
										continue;
								}
							}

							if (Settings.FolderMatchMode == FolderMatchMode.SameFolderOnly &&
								!SameFolderAtDepth(entry.Folder, compItem.Folder, Settings.SameFolderDepth))
								continue;
							if (Settings.FolderMatchMode == FolderMatchMode.DifferentFolderOnly &&
								SameFolderAtDepth(entry.Folder, compItem.Folder, Settings.SameFolderDepth))
								continue;

							bool isDuplicate = TryCheckDuplicate(entry, compItem, entry.compareFlippedGray, entry.compareFlippedPHashes, out difference, out flags);

							if (isDuplicate &&
								entry.FileSize == compItem.FileSize &&
								entry.mediaInfo!.Duration == compItem.mediaInfo!.Duration &&
								Settings.ExcludeHardLinks &&
								HardLinkUtils.AreSameFile(entry.Path, compItem.Path)) {
								isDuplicate = false;
							}

							if (isDuplicate)
								MergeDuplicate(entry, compItem, difference, flags);
						}

						for (int n = i + 1; n < videoEntries.Count; n++) {
							var compItem = videoEntries[n];
							if (compItem.comparePHash != null)
								continue;

							double compDurationSeconds = compItem.mediaInfo!.Duration.TotalSeconds;
							double compToleranceSeconds = GetDurationToleranceSeconds(compDurationSeconds);
							double allowedSeconds = Math.Min(entryToleranceSeconds, compToleranceSeconds);
							double diffSeconds = Math.Abs(entryDurationSeconds - compDurationSeconds);
							if (diffSeconds > allowedSeconds)
								continue;

							if (Settings.FileSizeTolerancePercent > 0) {
								long minSize = (long)(entry.FileSize * (1.0 - Settings.FileSizeTolerancePercent / 100.0));
								long maxSize = (long)(entry.FileSize * (1.0 + Settings.FileSizeTolerancePercent / 100.0));
								if (compItem.FileSize < minSize || compItem.FileSize > maxSize)
									continue;
							}

							if (Settings.EnableResolutionPreFilter) {
								int entryPixels = GetEntryPixelCount(entry);
								int compPixels = GetEntryPixelCount(compItem);
								if (entryPixels > 0 && compPixels > 0) {
									int smaller = Math.Min(entryPixels, compPixels);
									int larger = Math.Max(entryPixels, compPixels);
									if (smaller < larger / 2)
										continue;
								}
							}

							if (Settings.FolderMatchMode == FolderMatchMode.SameFolderOnly &&
								!SameFolderAtDepth(entry.Folder, compItem.Folder, Settings.SameFolderDepth))
								continue;
							if (Settings.FolderMatchMode == FolderMatchMode.DifferentFolderOnly &&
								SameFolderAtDepth(entry.Folder, compItem.Folder, Settings.SameFolderDepth))
								continue;

							bool isDuplicate = TryCheckDuplicate(entry, compItem, entry.compareFlippedGray, entry.compareFlippedPHashes, out difference, out flags);

							if (isDuplicate &&
								entry.FileSize == compItem.FileSize &&
								entry.mediaInfo!.Duration == compItem.mediaInfo!.Duration &&
								Settings.ExcludeHardLinks &&
								HardLinkUtils.AreSameFile(entry.Path, compItem.Path)) {
								isDuplicate = false;
							}

							if (isDuplicate)
								MergeDuplicate(entry, compItem, difference, flags);
						}
					}
					else {
						for (int n = i + 1; n < videoEntries.Count; n++) {
							var compItem = videoEntries[n];

							if (!QuickPHashPreFilterMulti(entry, compItem))
								continue;

							double compDurationSeconds = compItem.mediaInfo!.Duration.TotalSeconds;
							double compToleranceSeconds = GetDurationToleranceSeconds(compDurationSeconds);
							double allowedSeconds = Math.Min(entryToleranceSeconds, compToleranceSeconds);
							double diffSeconds = Math.Abs(entryDurationSeconds - compDurationSeconds);
							if (diffSeconds > allowedSeconds)
								continue;

							if (Settings.FileSizeTolerancePercent > 0) {
								long minSize = (long)(entry.FileSize * (1.0 - Settings.FileSizeTolerancePercent / 100.0));
								long maxSize = (long)(entry.FileSize * (1.0 + Settings.FileSizeTolerancePercent / 100.0));
								if (compItem.FileSize < minSize || compItem.FileSize > maxSize)
									continue;
							}

							if (Settings.EnableResolutionPreFilter) {
								int entryPixels = GetEntryPixelCount(entry);
								int compPixels = GetEntryPixelCount(compItem);
								if (entryPixels > 0 && compPixels > 0) {
									int smaller = Math.Min(entryPixels, compPixels);
									int larger = Math.Max(entryPixels, compPixels);
									if (smaller < larger / 2)
										continue;
								}
							}

							if (Settings.FolderMatchMode == FolderMatchMode.SameFolderOnly &&
								!SameFolderAtDepth(entry.Folder, compItem.Folder, Settings.SameFolderDepth))
								continue;
							if (Settings.FolderMatchMode == FolderMatchMode.DifferentFolderOnly &&
								SameFolderAtDepth(entry.Folder, compItem.Folder, Settings.SameFolderDepth))
								continue;

							bool isDuplicate = TryCheckDuplicate(entry, compItem, entry.compareFlippedGray, entry.compareFlippedPHashes, out difference, out flags);

							if (isDuplicate &&
								entry.FileSize == compItem.FileSize &&
								entry.mediaInfo!.Duration == compItem.mediaInfo!.Duration &&
								Settings.ExcludeHardLinks &&
								HardLinkUtils.AreSameFile(entry.Path, compItem.Path)) {
								isDuplicate = false;
							}

							if (isDuplicate)
								MergeDuplicate(entry, compItem, difference, flags);
						}
					}

					IncrementProgress(entry.Path);
				};

				try {
					if (videoEntries.Count >= largeBucketThreshold) {
						Parallel.For(0, videoEntries.Count, new ParallelOptions { CancellationToken = cancelationTokenSource.Token, MaxDegreeOfParallelism = Settings.GetEffectiveParallelism() }, compareAction);
					}
					else {
						for (int i = 0; i < videoEntries.Count; i++)
							compareAction(i);
					}
				}
				catch (OperationCanceledException) { }
			}

			try {
				CompareImages();

				if (lshIndex != null) {
					CompareVideosLSH();
				}
				else if (videoEntries.Count < bucketActivationThreshold) {
					CompareVideosLinear();
				}
				else {
					var smallBuckets = videoBuckets.Where(kvp => kvp.Value.Count < largeBucketThreshold).ToList();
					var largeBuckets = videoBuckets.Where(kvp => kvp.Value.Count >= largeBucketThreshold).ToList();

					Parallel.ForEach(smallBuckets, new ParallelOptions { CancellationToken = cancelationTokenSource.Token, MaxDegreeOfParallelism = Settings.GetEffectiveParallelism() }, bucket => {
						foreach (var entry in bucket.Value) {
							int entryIndex = entry.compareIndex;
							double durationSeconds = entry.mediaInfo!.Duration.TotalSeconds;
							double maxDiffSeconds = GetDurationToleranceSeconds(durationSeconds);
							double minDuration = Math.Max(0d, durationSeconds - maxDiffSeconds);
							double maxDuration = durationSeconds + maxDiffSeconds;
							int minKey = (int)Math.Floor(minDuration / bucketSizeSeconds);
							int maxKey = (int)Math.Floor(maxDuration / bucketSizeSeconds);
							CompareEntry(entry, entryIndex, Enumerable.Range(minKey, maxKey - minKey + 1));
						}
					});

					foreach (var bucket in largeBuckets) {
						Parallel.For(0, bucket.Value.Count, new ParallelOptions { CancellationToken = cancelationTokenSource.Token, MaxDegreeOfParallelism = Settings.GetEffectiveParallelism() }, i => {
							var entry = bucket.Value[i];
							int entryIndex = entry.compareIndex;
							double durationSeconds = entry.mediaInfo!.Duration.TotalSeconds;
							double maxDiffSeconds = GetDurationToleranceSeconds(durationSeconds);
							double minDuration = Math.Max(0d, durationSeconds - maxDiffSeconds);
							double maxDuration = durationSeconds + maxDiffSeconds;
							int minKey = (int)Math.Floor(minDuration / bucketSizeSeconds);
							int maxKey = (int)Math.Floor(maxDuration / bucketSizeSeconds);
							CompareEntry(entry, entryIndex, Enumerable.Range(minKey, maxKey - minKey + 1));
						});
					}
				}
			}
			catch (OperationCanceledException) { }
			if (mergesBlocked > 0)
				Logger.Instance.Info($"Group merge validation: blocked {mergesBlocked} merge(s) where group representatives were not similar");
			if (missingPHashFiles.Count > 0)
				Logger.Instance.Info($"pHash comparison: {missingPHashFiles.Count} file(s) had missing pHash data and were skipped in pHash comparisons. Delete the database (or rescan with 'Always retry failed sampling') to recompute.");
			Duplicates = new HashSet<DuplicateItem>(duplicateDict.Values);
			SplitDaisyChainGroups();

			foreach (FileEntry entry in ScanList) {
				entry.compareGray = null;
				entry.comparePHash = null;
				entry.comparePHashes = null;
				entry.compareFlippedGray = null;
				entry.compareFlippedPHashes = null;
			}

			foreach (byte[] buf in rentedBuffers)
				System.Buffers.ArrayPool<byte>.Shared.Return(buf);
		}

		static bool SameFolderAtDepth(ReadOnlySpan<char> a, ReadOnlySpan<char> b, int depth) {
			for (int i = 0; i < depth; i++) {
				while (a.Length > 0 && (a[^1] == Path.DirectorySeparatorChar || a[^1] == Path.AltDirectorySeparatorChar))
					a = a[..^1];
				while (b.Length > 0 && (b[^1] == Path.DirectorySeparatorChar || b[^1] == Path.AltDirectorySeparatorChar))
					b = b[..^1];

				int sepA = a.LastIndexOf(Path.DirectorySeparatorChar);
				if (Path.DirectorySeparatorChar != Path.AltDirectorySeparatorChar) {
					int alt = a.LastIndexOf(Path.AltDirectorySeparatorChar);
					if (alt > sepA) sepA = alt;
				}
				int sepB = b.LastIndexOf(Path.DirectorySeparatorChar);
				if (Path.DirectorySeparatorChar != Path.AltDirectorySeparatorChar) {
					int alt = b.LastIndexOf(Path.AltDirectorySeparatorChar);
					if (alt > sepB) sepB = alt;
				}

				var segA = sepA >= 0 ? a[(sepA + 1)..] : a;
				var segB = sepB >= 0 ? b[(sepB + 1)..] : b;

				if (!segA.Equals(segB, StringComparison.OrdinalIgnoreCase))
					return false;

				a = sepA >= 0 ? a[..sepA] : ReadOnlySpan<char>.Empty;
				b = sepB >= 0 ? b[..sepB] : ReadOnlySpan<char>.Empty;
			}
			return true;
		}

		void LogMissingPHash(string path) {
			if (missingPHashFiles.TryAdd(path, 0))
				Logger.Instance.Info($"Missing pHash data for '{path}' — file will be skipped in pHash comparisons. Re-scan to repopulate.");
		}

		void LogGroupStatistics() {
			var groupSizes = Duplicates
				.GroupBy(d => d.GroupId)
				.Select(g => g.Count())
				.ToList();
			if (groupSizes.Count == 0) return;
			int totalItems = groupSizes.Sum();
			int maxSize = groupSizes.Max();
			double avgSize = groupSizes.Average();
			int groupsOver5 = groupSizes.Count(s => s > 5);
			int groupsOver10 = groupSizes.Count(s => s > 10);
			Logger.Instance.Info($"Group statistics: {groupSizes.Count} groups, {totalItems} items, " +
				$"avg size {avgSize:F1}, max size {maxSize}, " +
				$"groups with >5 items: {groupsOver5}, >10 items: {groupsOver10}");
		}
	}
}
