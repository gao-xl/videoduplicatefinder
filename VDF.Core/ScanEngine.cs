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

global using System;
global using System.Collections.Concurrent;
global using System.Collections.Generic;
global using System.IO;
global using System.Threading;
global using System.Threading.Tasks;
global using Size = System.Drawing.Size;
using System.Diagnostics;
using System.Linq;
using VDF.Core.FFTools;
using VDF.Core.Utils;
using VDF.Core.ViewModels;

namespace VDF.Core {
	public sealed partial class ScanEngine {
		public HashSet<DuplicateItem> Duplicates { get; set; } = new HashSet<DuplicateItem>();
		public Settings Settings { get; set; } = new Settings();
		public event EventHandler<ScanProgressChangedEventArgs>? Progress;
		public event EventHandler? BuildingHashesDone;
		public event EventHandler? ScanDone;
		public event EventHandler? ScanAborted;
		public event EventHandler? ThumbnailsRetrieved;
		public event Action<int, int>? ThumbnailProgress;
		public event EventHandler? FilesEnumerated;
		public event EventHandler? DatabaseCleaned;

		/// <summary>Encoded placeholder image (PNG/JPEG bytes) shown when thumbnail extraction fails.</summary>
		public byte[]? NoThumbnailImage;

		PauseTokenSource pauseTokenSource = new();
		CancellationTokenSource cancelationTokenSource = new();
		PathMatcher? _includeMatcher;
		readonly List<float> positionList = new();
		FileEnumerator? _fileEnumerator;
		MediaAnalyzer? _mediaAnalyzer;

		bool isScanning;
		int scanProgressMaxValue;
		readonly Stopwatch SearchTimer = new();
		public Stopwatch ElapsedTimer = new();
		int processedFiles;
		DateTime startTime = DateTime.Now;
		DateTime lastProgressUpdate = DateTime.MinValue;
		static readonly TimeSpan progressUpdateIntervall = TimeSpan.FromMilliseconds(300);
		const int maxExcludedLogsPerReason = 5;
		readonly ConcurrentDictionary<string, int> excludedReasonCounts = new();
		readonly ConcurrentDictionary<string, int> excludedReasonLoggedCounts = new();
		readonly ConcurrentDictionary<string, byte> missingPHashFiles = new(
			CoreUtils.IsWindows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
		DateTime lastCheckpointTime = DateTime.MinValue;
		readonly object checkpointLock = new();

		string T(string key, params object[] args) =>
			LanguageService.Instance.Get(Settings.LanguageCode, key, args);

		void InitProgress(int count) {
			startTime = DateTime.UtcNow;
			scanProgressMaxValue = count;
			Interlocked.Exchange(ref processedFiles, 0);
			lastProgressUpdate = DateTime.MinValue;
			lastCheckpointTime = DateTime.UtcNow;
		}
		void ResetExcludedLogging() {
			excludedReasonCounts.Clear();
			excludedReasonLoggedCounts.Clear();
		}
		void LogExcludedFile(FileEntry entry, string reason) {
			if (!Settings.LogExcludedFiles)
				return;
			var totalCount = excludedReasonCounts.AddOrUpdate(reason, 1, (_, count) => count + 1);
			var loggedCount = excludedReasonLoggedCounts.GetOrAdd(reason, 0);
			if (loggedCount >= maxExcludedLogsPerReason)
				return;
			loggedCount = excludedReasonLoggedCounts.AddOrUpdate(reason, 1, (_, count) => count + 1);
			if (loggedCount <= maxExcludedLogsPerReason)
				Logger.Instance.Info(T("Log.ExcludedFile", entry.Path, reason, totalCount));
		}
		void LogExcludedSummary() {
			if (!Settings.LogExcludedFiles || excludedReasonCounts.IsEmpty)
				return;
			Logger.Instance.Info(T("Log.ExcludedFilesSummary"));
			foreach (var reason in excludedReasonCounts.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)) {
				var loggedCount = excludedReasonLoggedCounts.TryGetValue(reason.Key, out var value) ? value : 0;
				var suppressedCount = Math.Max(0, reason.Value - loggedCount);
				var suppressionText = suppressedCount > 0 ? T("Log.ExcludedFilesSuppressed", suppressedCount) : string.Empty;
				Logger.Instance.Info(T("Log.ExcludedFilesSummaryItem", reason.Key, reason.Value, suppressionText));
			}
		}
		void IncrementProgress(string path) {
			Interlocked.Increment(ref processedFiles);
			var pushUpdate = processedFiles == scanProgressMaxValue ||
								lastProgressUpdate + progressUpdateIntervall < DateTime.UtcNow;
			if (!pushUpdate) return;
			lastProgressUpdate = DateTime.UtcNow;
			var timeRemaining = TimeSpan.FromTicks(DateTime.UtcNow.Subtract(startTime).Ticks *
									(scanProgressMaxValue - (processedFiles + 1)) / (processedFiles + 1));
			Progress?.Invoke(this,
							new ScanProgressChangedEventArgs {
								CurrentPosition = processedFiles,
								CurrentFile = path,
								Elapsed = ElapsedTimer.Elapsed,
								Remaining = timeRemaining,
								MaxPosition = scanProgressMaxValue,
								CurrentStage = string.Empty,
							});
			TryDatabaseCheckpoint();
		}

		void ReportStage(string path, string stage, int stageCurrent = 0, int stageMax = 0) {
			if (lastProgressUpdate + progressUpdateIntervall > DateTime.UtcNow) return;
			lastProgressUpdate = DateTime.UtcNow;
			var timeRemaining = TimeSpan.FromTicks(DateTime.UtcNow.Subtract(startTime).Ticks *
									(scanProgressMaxValue - (processedFiles + 1)) / (processedFiles + 1));
			Progress?.Invoke(this,
							new ScanProgressChangedEventArgs {
								CurrentPosition = processedFiles,
								CurrentFile = path,
								Elapsed = ElapsedTimer.Elapsed,
								Remaining = timeRemaining,
								MaxPosition = scanProgressMaxValue,
								CurrentStage = stage,
								StageCurrent = stageCurrent,
								StageMax = stageMax,
							});
		}

		void TryDatabaseCheckpoint() {
			if (Settings.DatabaseCheckpointIntervalMinutes <= 0) return;
			var interval = TimeSpan.FromMinutes(Settings.DatabaseCheckpointIntervalMinutes);
			if (DateTime.UtcNow - lastCheckpointTime < interval) return;
			lock (checkpointLock) {
				if (DateTime.UtcNow - lastCheckpointTime < interval) return;
				lastCheckpointTime = DateTime.UtcNow;
				DatabaseUtils.SaveDatabaseIncremental();
				Logger.Instance.Info(T("Log.DatabaseCheckpoint", DatabaseUtils.Database.Count));
			}
		}

		public static bool FFmpegExists => !string.IsNullOrEmpty(FfmpegEngine.FFmpegPath);
		public static bool FFprobeExists => !string.IsNullOrEmpty(FFProbeEngine.FFprobePath);
		public static bool NativeFFmpegExists => FFTools.FFmpegNative.FFmpegHelper.DoFFmpegLibraryFilesExist;

		public async Task StartSearch() {
			PrepareSearch();
			SearchTimer.Start();
			ElapsedTimer.Start();
			Logger.Instance.InsertSeparator('-');
			Logger.Instance.Info(T("Log.BuildingFileList"));
			await BuildFileList(cancelationTokenSource.Token);
			Logger.Instance.Info(T("Log.FinishedBuildingFileList", SearchTimer.StopGetElapsedAndRestart()));
			FilesEnumerated?.Invoke(this, new EventArgs());
			Logger.Instance.Info(T("Log.GatheringMediaInfo"));
			if (!cancelationTokenSource.IsCancellationRequested)
				await GatherInfos();
			Logger.Instance.Info(T("Log.FinishedGatheringHashes", SearchTimer.StopGetElapsedAndRestart()));
			DatabaseUtils.SaveDatabase();
			BuildingHashesDone?.Invoke(this, new EventArgs());
			if (!cancelationTokenSource.IsCancellationRequested) {
				await StartCompare();
			}
			else {
				ScanAborted?.Invoke(this, new EventArgs());
				Logger.Instance.Info(T("Log.ScanAborted"));
				isScanning = false;
			}
		}

		public async Task StartCompare() {
			PrepareCompare();
			SearchTimer.Start();
			ElapsedTimer.Start();
			Logger.Instance.Info(T("Log.ScanForDuplicates"));
			if (!cancelationTokenSource.IsCancellationRequested)
				await Task.Run(ScanForDuplicates, cancelationTokenSource.Token);
			if (!cancelationTokenSource.IsCancellationRequested && Settings.EnablePartialClipDetection)
				await Task.Run(ScanForPartialDuplicates, cancelationTokenSource.Token);
			SearchTimer.Stop();
			ElapsedTimer.Stop();
			Logger.Instance.Info(T("Log.FinishedScanForDuplicates", SearchTimer.Elapsed));
			LogGroupStatistics();
			Logger.Instance.Info(T("Log.HighlightingBestResults"));
			HighlightBestMatches();
			DatabaseUtils.SaveDatabase();
			isScanning = false;
			ScanDone?.Invoke(this, new EventArgs());
			Logger.Instance.Info(T("Log.ScanDone"));
		}

		void PrepareSearch() {
			ResetExcludedLogging();
			if (!Settings.UseNativeFfmpegBinding && !FFmpegExists)
				throw new FFNotFoundException("Cannot find FFmpeg");
			if (!FFprobeExists)
				throw new FFNotFoundException("Cannot find FFprobe");
			if (Settings.UseNativeFfmpegBinding && !FFTools.FFmpegNative.FFmpegHelper.DoFFmpegLibraryFilesExist)
				throw new FFNotFoundException($"Cannot find FFmpeg libraries. {FFTools.FFmpegNative.FFmpegHelper.DescribeExpectedLibraries()}");

			CancelAllTasks();

			FfmpegEngine.HardwareAccelerationMode = Settings.HardwareAccelerationMode;
			FfmpegEngine.CustomFFArguments = Settings.CustomFFArguments;
			FfmpegEngine.UseNativeBinding = Settings.UseNativeFfmpegBinding;
			DatabaseUtils.CustomDatabaseFolder = Settings.CustomDatabaseFolder;
			DatabaseUtils.InvalidateDatabaseFolder();
			Duplicates.Clear();
			positionList.Clear();
			ElapsedTimer.Reset();
			SearchTimer.Reset();

			BuildPositionList();
			NormalizeScanPaths();
			_includeMatcher = new PathMatcher(Settings.IncludeList);

			isScanning = true;
		}

		void BuildPositionList() {
			positionList.Clear();
			float positionCounter = 0f;
			for (int i = 0; i < Settings.ThumbnailCount; i++) {
				positionCounter += 1.0F / (Settings.ThumbnailCount + 1);
				positionList.Add(positionCounter);
			}
		}

		void NormalizeScanPaths() {
			static HashSet<string> Normalize(HashSet<string> paths) {
				var result = new HashSet<string>();
				foreach (var path in paths) {
					string normalized = path;
					try {
						normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
					}
					catch { }
					result.Add(normalized);
				}
				return result;
			}
			Settings.IncludeList = Normalize(Settings.IncludeList);
			Settings.BlackList = Normalize(Settings.BlackList);
		}

		void CancelAllTasks() {
			if (!cancelationTokenSource.IsCancellationRequested)
				cancelationTokenSource.Cancel();
			cancelationTokenSource = new CancellationTokenSource();
			pauseTokenSource = new PauseTokenSource();
			isScanning = false;
		}

		static bool IsNetworkPath(string path) => FileEnumerator.IsNetworkPath(path);

		async Task BuildFileList(CancellationToken cancellationToken) {
			_fileEnumerator ??= new FileEnumerator(Settings);
			await _fileEnumerator.BuildFileList(cancellationToken);
		}

		bool InvalidEntry(FileEntry entry, out bool reportProgress, out string? reason) {
			reportProgress = true;
			reason = null;

			if (Settings.IncludeImages == false && entry.IsImage) {
				reason = "image files are disabled";
				return true;
			}
			if (Settings.BlackList.Any(f => IsBlackListed(entry.Folder, f))) {
				reason = "path is in the excluded directories list";
				return true;
			}

			if (!Settings.ScanAgainstEntireDatabase) {
				if (Settings.IncludeSubDirectories == false) {
					if (!Settings.IncludeList.Contains(entry.Folder)) {
						reportProgress = false;
						reason = "path is not in the included directories list";
						return true;
					}
				}
				else if (_includeMatcher != null && !_includeMatcher.IsIncluded(entry.Folder)) {
					reportProgress = false;
					reason = "path is not in the included directories list";
					return true;
				}
				else if (_includeMatcher == null && !Settings.IncludeList.Any(f => {
					if (!entry.Folder.StartsWith(f))
						return false;
					if (entry.Folder.Length == f.Length)
						return true;
					string relativePath = Path.GetRelativePath(f, entry.Folder);
					return !relativePath.StartsWith('.') && !Path.IsPathRooted(relativePath);
				})) {
					reportProgress = false;
					reason = "path is not in the included directories list";
					return true;
				}
			}

			if (entry.Flags.Has(EntryFlags.ManuallyExcluded)) {
				reason = "file has been manually excluded";
				return true;
			}
			if (entry.Flags.Has(EntryFlags.TooDark)) {
				reason = "file is marked as too dark";
				return true;
			}
			if (!Settings.IncludeNonExistingFiles && !File.Exists(entry.Path))
			{
				reason = "file does not exist";
				return true;
			}
			if (!FileUtils.IsPathFFmpegSafe(entry.Path)) {
				entry.Flags.Set(EntryFlags.MetadataError);
				entry.dirty = true;
				reason = "path contains characters not encodable to UTF-8 (e.g. lone surrogate from a mangled emoji) — FFmpeg cannot open it";
				return true;
			}

			if (Settings.FilterByFileSize && (entry.FileSize.BytesToMegaBytes() > Settings.MaximumFileSize ||
				entry.FileSize.BytesToMegaBytes() < Settings.MinimumFileSize)) {
				reason = "file size is outside the configured range";
				return true;
			}
			if (Settings.FilterByFilePathContains) {
				bool contains = false;
				foreach (var f in Settings.FilePathContainsTexts) {
					if (System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(f, entry.Path)) {
						contains = true;
						break;
					}
				}
				if (!contains) {
					reason = "file path does not match the required patterns";
					return true;
				}
			}

			if (Settings.IgnoreReparsePoints) {
				if (!entry.Flags.Has(EntryFlags.ReparsePointChecked)) {
					try {
						FileAttributes attributes = File.GetAttributes(entry.Path);
						entry.Flags.Set(EntryFlags.ReparsePoint, (attributes & FileAttributes.ReparsePoint) != 0);
						entry.Flags.Set(EntryFlags.ReparsePointChecked);
						entry.dirty = true;
					}
					catch { }
				}
				if (entry.Flags.Has(EntryFlags.ReparsePoint)) {
					reason = "file is a reparse point";
					return true;
				}
			}
			if (Settings.FilterByFilePathNotContains) {
				bool contains = false;
				foreach (var f in Settings.FilePathNotContainsTexts) {
					if (System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(f, entry.Path)) {
						contains = true;
						break;
					}
				}
				if (contains) {
					reason = "file path matches an excluded pattern";
					return true;
				}
			}

			return false;
		}
		bool InvalidEntryForDuplicateCheck(FileEntry entry) =>
			entry.invalid || entry.mediaInfo == null || entry.Flags.Has(EntryFlags.ThumbnailError) || (!entry.IsImage && entry.grayBytes.Count < Settings.ThumbnailCount);

		static bool IsBlackListed(string folderPath, string blacklistEntry) =>
			FileEnumerator.IsBlackListed(folderPath, blacklistEntry);

		async Task GatherInfos() {
			try {
				InitProgress(DatabaseUtils.Database.Count);
				await Parallel.ForEachAsync(DatabaseUtils.Database, new ParallelOptions { CancellationToken = cancelationTokenSource.Token, MaxDegreeOfParallelism = Settings.GetEffectiveParallelism() }, (entry, token) => {
					pauseTokenSource.WaitWhilePaused(token);

					try {
						entry.invalid = InvalidEntry(entry, out bool reportProgress, out string? invalidReason);
						if (entry.invalid && invalidReason != null)
							LogExcludedFile(entry, invalidReason);

						bool wasInvalid = entry.invalid;
						bool skipEntry = false;
						string? skipReason = null;
						skipEntry |= entry.invalid;
						if (!skipEntry && entry.Flags.Has(EntryFlags.ThumbnailError) && !Settings.AlwaysRetryFailedSampling) {
							skipEntry = true;
							skipReason = "previous thumbnail sampling failed and retry is disabled";
						}

						if (!skipEntry && !Settings.ScanAgainstEntireDatabase) {
							if (Settings.IncludeSubDirectories == false) {
								if (!Settings.IncludeList.Contains(entry.Folder)) {
									skipEntry = true;
									skipReason = "path is not in the included directories list";
								}
							}
							else if (_includeMatcher != null && !_includeMatcher.IsIncluded(entry.Folder)) {
								skipEntry = true;
								skipReason = "path is not in the included directories list";
							}
							else if (_includeMatcher == null && !Settings.IncludeList.Any(f => {
								if (!entry.Folder.StartsWith(f))
									return false;
								if (entry.Folder.Length == f.Length)
									return true;
								string relativePath = Path.GetRelativePath(f, entry.Folder);
								return !relativePath.StartsWith('.') && !Path.IsPathRooted(relativePath);
							})) {
								skipEntry = true;
								skipReason = "path is not in the included directories list";
							}
						}

						if (skipEntry) {
							entry.invalid = true;
							if (!wasInvalid && skipReason != null)
								LogExcludedFile(entry, skipReason);
							if (reportProgress)
								IncrementProgress(entry.Path);
							return ValueTask.CompletedTask;
						}

						DatabaseUtils.EnsureHeavyFieldsLoaded(entry);

						_mediaAnalyzer ??= new MediaAnalyzer(Settings, positionList);
						bool valid = _mediaAnalyzer.GatherInfo(entry,
							(path, stage, current, max) => ReportStage(path, stage, current, max),
							cancelationTokenSource.Token);

						if (!valid) {
							IncrementProgress(entry.Path);
							return ValueTask.CompletedTask;
						}

						IncrementProgress(entry.Path);
						return ValueTask.CompletedTask;
					}
					catch (OperationCanceledException) {
						throw;
					}
					catch (Exception ex) {
						Logger.Instance.Info($"Unhandled error processing '{entry.Path}': {ex}");
						entry.invalid = true;
						entry.Flags.Set(EntryFlags.ThumbnailError);
						entry.dirty = true;
						IncrementProgress(entry.Path);
						return ValueTask.CompletedTask;
					}
				});
			}
			catch (OperationCanceledException) { }
			finally {
				LogExcludedSummary();
			}
		}

		static void ExtractAudioFingerprint(FileEntry entry, CancellationToken ct = default, Action<double>? onProgress = null) {
			MediaAnalyzer.ExtractAudioFingerprint(entry, ct, onProgress);
		}

		internal static bool IsSilentFingerprint(uint[] fp) => MediaAnalyzer.IsSilentFingerprint(fp);

		internal void HighlightBestMatches() {
			foreach (var group in Duplicates.GroupBy(d => d.GroupId)) {
				List<DuplicateItem> items = group.ToList();
				bool isImage = items[0].IsImage;

				if (!isImage) {
					TimeSpan bestDuration = items.Max(d => d.Duration);
					foreach (DuplicateItem d in items)
						if (d.Duration == bestDuration) d.IsBestDuration = true;
				}

				long bestSize = items.Min(d => d.SizeLong);
				foreach (DuplicateItem d in items)
					if (d.SizeLong == bestSize) d.IsBestSize = true;

				if (!isImage) {
					float bestFps = items.Max(d => d.Fps);
					foreach (DuplicateItem d in items)
						if (d.Fps == bestFps) d.IsBestFps = true;

					decimal bestBitRate = items.Max(d => d.BitRateKbs);
					foreach (DuplicateItem d in items)
						if (d.BitRateKbs == bestBitRate) d.IsBestBitRateKbs = true;

					int bestAudioSampleRate = items.Max(d => d.AudioSampleRate);
					foreach (DuplicateItem d in items)
						if (d.AudioSampleRate == bestAudioSampleRate) d.IsBestAudioSampleRate = true;

					decimal bestAudioBitRate = items.Max(d => d.AudioBitRateKbs);
					foreach (DuplicateItem d in items)
						if (d.AudioBitRateKbs == bestAudioBitRate) d.IsBestAudioBitRateKbs = true;

					int bestHdrRank = items.Max(d => d.HdrFormatRank);
					foreach (DuplicateItem d in items)
						if (d.HdrFormatRank == bestHdrRank) d.IsBestHdrFormat = true;
				}

				int bestFrameSize = items.Max(d => d.FrameSizeInt);
				foreach (DuplicateItem d in items)
					if (d.FrameSizeInt == bestFrameSize) d.IsBestFrameSize = true;
			}
		}

		public void Pause() {
			if (!isScanning || pauseTokenSource.IsPaused) return;
			Logger.Instance.Info("Scan paused by user");
			ElapsedTimer.Stop();
			SearchTimer.Stop();
			pauseTokenSource.IsPaused = true;
		}

		public void Resume() {
			if (!isScanning || pauseTokenSource.IsPaused != true) return;
			Logger.Instance.Info("Scan resumed by user");
			ElapsedTimer.Start();
			SearchTimer.Start();
			pauseTokenSource.IsPaused = false;
		}

		public void Stop() {
			if (pauseTokenSource.IsPaused)
				Resume();
			Logger.Instance.Info("Scan stopped by user");
			if (isScanning)
				cancelationTokenSource.Cancel();
		}

		void SplitDaisyChainGroups() {
			var dbLookup = new Dictionary<string, FileEntry>(
				CoreUtils.IsWindows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
			foreach (FileEntry fe in DatabaseUtils.Database)
				dbLookup[fe.Path] = fe;

			var groups = Duplicates
				.GroupBy(d => d.GroupId)
				.Where(g => g.Count() >= 3)
				.ToList();

			if (groups.Count == 0) return;

			int groupsSplit = 0;
			int itemsRemoved = 0;

			foreach (var group in groups) {
				var members = group.ToList();
				int n = members.Count;

				var entries = new FileEntry[n];
				bool allFound = true;
				for (int i = 0; i < n; i++) {
					if (!dbLookup.TryGetValue(members[i].Path, out var fe) || fe.compareGray == null) {
						allFound = false;
						break;
					}
					entries[i] = fe;
				}
				if (!allFound) continue;

				var simCache = new Dictionary<(int, int), bool>();

				bool AreSimilar(int i, int j) {
					var key = i < j ? (i, j) : (j, i);
					if (simCache.TryGetValue(key, out bool cached))
						return cached;
					bool result = CheckIfDuplicate(entries[i], null, null, entries[j], out _);
					simCache[key] = result;
					return result;
				}

				var active = new List<int>(Enumerable.Range(0, n));
				var connectionCounts = new int[n];
				for (int ai = 0; ai < active.Count; ai++) {
					int idx = active[ai];
					for (int aj = ai + 1; aj < active.Count; aj++) {
						int jdx = active[aj];
						if (AreSimilar(idx, jdx)) {
							connectionCounts[idx]++;
							connectionCounts[jdx]++;
						}
					}
				}

				var pruned = new List<int>();

				bool changed = true;
				while (changed && active.Count >= 2) {
					changed = false;
					int worstAi = -1;
					int worstConnections = int.MaxValue;

					for (int ai = 0; ai < active.Count; ai++) {
						if (connectionCounts[active[ai]] < worstConnections) {
							worstConnections = connectionCounts[active[ai]];
							worstAi = ai;
						}
					}

					int requiredConnections = (active.Count - 1 + 1) / 2;
					if (worstConnections < requiredConnections) {
						int prunedIdx = active[worstAi];
						pruned.Add(prunedIdx);
						for (int ai = 0; ai < active.Count; ai++) {
							int otherIdx = active[ai];
							if (otherIdx != prunedIdx && AreSimilar(prunedIdx, otherIdx))
								connectionCounts[otherIdx]--;
						}
						active.RemoveAt(worstAi);
						changed = true;
					}
				}

				if (pruned.Count == 0) continue;

				groupsSplit++;

				if (active.Count >= 2) {
					var coreGroupId = Guid.NewGuid();
					foreach (int idx in active)
						members[idx].GroupId = coreGroupId;
				}
				else {
					foreach (int idx in active) {
						Duplicates.Remove(members[idx]);
						itemsRemoved++;
					}
					active.Clear();
				}

				var visited = new HashSet<int>();
				foreach (int seed in pruned) {
					if (visited.Contains(seed)) continue;
					var component = new List<int>();
					var queue = new Queue<int>();
					queue.Enqueue(seed);
					visited.Add(seed);
					while (queue.Count > 0) {
						int cur = queue.Dequeue();
						component.Add(cur);
						foreach (int other in pruned) {
							if (!visited.Contains(other) && AreSimilar(cur, other)) {
								visited.Add(other);
								queue.Enqueue(other);
							}
						}
					}

					if (component.Count >= 2) {
						var subActive = new List<int>(component);
						var subConnCounts = new int[n];
						for (int ai = 0; ai < subActive.Count; ai++) {
							int idx = subActive[ai];
							for (int aj = ai + 1; aj < subActive.Count; aj++) {
								int jdx = subActive[aj];
								if (AreSimilar(idx, jdx)) {
									subConnCounts[idx]++;
									subConnCounts[jdx]++;
								}
							}
						}

						bool subChanged = true;
						while (subChanged && subActive.Count >= 2) {
							subChanged = false;
							int subWorstAi = -1;
							int subWorstConn = int.MaxValue;
							for (int ai = 0; ai < subActive.Count; ai++) {
								if (subConnCounts[subActive[ai]] < subWorstConn) {
									subWorstConn = subConnCounts[subActive[ai]];
									subWorstAi = ai;
								}
							}
							int subRequired = (subActive.Count - 1 + 1) / 2;
							if (subWorstConn < subRequired) {
								int subPrunedIdx = subActive[subWorstAi];
								Duplicates.Remove(members[subPrunedIdx]);
								itemsRemoved++;
								for (int ai = 0; ai < subActive.Count; ai++) {
									int otherIdx = subActive[ai];
									if (otherIdx != subPrunedIdx && AreSimilar(subPrunedIdx, otherIdx))
										subConnCounts[otherIdx]--;
								}
								subActive.RemoveAt(subWorstAi);
								subChanged = true;
							}
						}

						if (subActive.Count >= 2) {
							var subGroupId = Guid.NewGuid();
							foreach (int idx in subActive)
								members[idx].GroupId = subGroupId;
						}
						else {
							foreach (int idx in subActive) {
								Duplicates.Remove(members[idx]);
								itemsRemoved++;
							}
						}
					}
					else {
						Duplicates.Remove(members[component[0]]);
						itemsRemoved++;
					}
				}
			}

			if (groupsSplit > 0)
				Logger.Instance.Info($"Daisy-chain validation: split {groupsSplit} group(s), removed {itemsRemoved} singleton item(s)");
		}

		internal sealed class PathMatcher {
			readonly string[] _sortedRoots;
			readonly bool _ignoreCase;

			public PathMatcher(HashSet<string> roots) {
				_ignoreCase = CoreUtils.IsWindows;
				_sortedRoots = roots
					.OrderBy(r => r, _ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
					.ToArray();
			}

			public bool IsIncluded(string folderPath) {
				if (_sortedRoots.Length == 0) return false;

				int idx = Array.BinarySearch(_sortedRoots, folderPath,
					_ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

				if (idx >= 0) return true;

				int insertPoint = ~idx;

				for (int i = insertPoint - 1; i >= 0; i--) {
					string root = _sortedRoots[i];
					if (!folderPath.StartsWith(root, _ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
						break;
					if (folderPath.Length == root.Length)
						return true;
					string relativePath = Path.GetRelativePath(root, folderPath);
					if (!relativePath.StartsWith('.') && !Path.IsPathRooted(relativePath))
						return true;
				}

				return false;
			}
		}
	}
}
