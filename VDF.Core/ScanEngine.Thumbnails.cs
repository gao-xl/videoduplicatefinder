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
using VDF.Core.FFTools;
using VDF.Core.Utils;
using VDF.Core.ViewModels;

namespace VDF.Core {
	public sealed partial class ScanEngine {

		static bool IsImageExtension(string ext) =>
			FileUtils.ImageExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);

		internal static bool ShouldRetryThumbnails(DuplicateItem item, byte[]? placeholder, int requiredWidth = 0) {
			if (item.ImageList == null || item.ImageList.Count == 0) return true;
			if (placeholder != null && item.ImageList.Count == 1 && ReferenceEquals(item.ImageList[0], placeholder)) return true;
			if (requiredWidth > 0 && item.ThumbnailWidth > 0 && item.ThumbnailWidth < requiredWidth) return true;
			return false;
		}

		internal void EnsureThumbnailPositions() {
			if (positionList.Count > 0) return;
			float positionCounter = 0f;
			for (int i = 0; i < Settings.ThumbnailCount; i++) {
				positionCounter += 1.0F / (Settings.ThumbnailCount + 1);
				positionList.Add(positionCounter);
			}
		}

		public async Task RetrieveThumbnailsForItems(IEnumerable<DuplicateItem> items) {
			int requiredWidth = Settings.ThumbnailMaxWidth > 0 ? Settings.ThumbnailMaxWidth : 100;
			var dupList = items.Where(d => ShouldRetryThumbnails(d, NoThumbnailImage, requiredWidth)).ToList();
			if (dupList.Count == 0) {
				Logger.Instance.Info("Explicit thumbnail retry: nothing to do (all selected items already have up-to-date thumbnails).");
				return;
			}
			EnsureThumbnailPositions();
			Logger.Instance.Info($"Explicit thumbnail retry: starting for {dupList.Count} item(s).");
			int loaded = 0, placeholders = 0, skippedMissing = 0;
			try {
				await Parallel.ForEachAsync(dupList, new ParallelOptions { MaxDegreeOfParallelism = Settings.GetEffectiveParallelism() }, (entry, cancellationToken) => {
					List<byte[]>? list = null;
					bool needsThumbnails = !Settings.IncludeNonExistingFiles || File.Exists(entry.Path);
					List<TimeSpan>? timeStamps = null;
					int maxDim = Settings.ThumbnailMaxWidth > 0 ? Settings.ThumbnailMaxWidth : 100;

					if (!needsThumbnails) {
						Interlocked.Increment(ref skippedMissing);
					}
					else if (entry.IsImage) {
						timeStamps = new(0);
						list = new List<byte[]>(1);
						var b = ExtractThumbnailJpeg(entry.Path, TimeSpan.Zero, maxDim);
						if (b == null || b.Length == 0) {
							Logger.Instance.Info($"Failed loading image from file: '{entry.Path}'.");
							return ValueTask.CompletedTask;
						}
						list.Add(b);
						entry.ThumbnailWidth = maxDim;
						Interlocked.Increment(ref loaded);
					}
					else {
						list = new List<byte[]>(positionList.Count);
						timeStamps = new List<TimeSpan>(positionList.Count);
						int failedPositions = 0;
						for (int j = 0; j < positionList.Count; j++) {
							var timestamp = TimeSpan.FromSeconds(entry.Duration.TotalSeconds * positionList[j]);
							var b = FfmpegEngine.ExtractThumbnailJpeg(entry.Path, timestamp, maxDim, Settings.ExtendedFFToolsLogging);
							if (b == null || b.Length == 0) {
								failedPositions++;
								Logger.Instance.Info($"Failed extracting thumbnail at {timestamp} for '{entry.Path}', skipping that position.");
								continue;
							}
							list.Add(b);
							timeStamps.Add(timestamp);
						}
						if (list.Count == 0 && NoThumbnailImage != null) {
							list.Add(NoThumbnailImage);
							timeStamps.Add(TimeSpan.Zero);
							entry.ThumbnailWidth = 0;
							Logger.Instance.Info($"Using placeholder for '{entry.Path}' — all {positionList.Count} sample position(s) failed.");
							Interlocked.Increment(ref placeholders);
						}
						else if (list.Count > 0 && failedPositions > 0) {
							entry.ThumbnailWidth = maxDim;
							Logger.Instance.Info($"Loaded {list.Count}/{positionList.Count} thumbnail(s) for '{entry.Path}' ({failedPositions} position(s) failed).");
							Interlocked.Increment(ref loaded);
						}
						else if (list.Count > 0) {
							entry.ThumbnailWidth = maxDim;
							Interlocked.Increment(ref loaded);
						}
					}
					Debug.Assert(timeStamps != null);
					entry.SetThumbnails(list ?? (NoThumbnailImage != null ? new() { NoThumbnailImage } : new()), timeStamps!);

					return ValueTask.CompletedTask;
				});
			}
			catch (OperationCanceledException) { }
			Logger.Instance.Info($"Explicit thumbnail retry complete: {loaded} fully loaded, {placeholders} placeholder, {skippedMissing} skipped (missing on disk).");
		}

		public async void RetrieveThumbnails() {
			var dupList = Duplicates.Where(d => ShouldRetryThumbnails(d, NoThumbnailImage)).ToList();
			int total = dupList.Count;
			int done = 0;
			int lastNotified = 0;
			int loaded = 0, placeholders = 0, skippedMissing = 0;
			Logger.Instance.Info($"Thumbnail loading: starting for {total} item(s).");

			var totalSw = Stopwatch.StartNew();
			var sw = Stopwatch.StartNew();
			try {
				await Parallel.ForEachAsync(dupList, new ParallelOptions { CancellationToken = cancelationTokenSource.Token, MaxDegreeOfParallelism = Settings.GetEffectiveParallelism() }, (entry, cancellationToken) => {
					List<byte[]>? list = null;
					bool needsThumbnails = !Settings.IncludeNonExistingFiles || File.Exists(entry.Path);
					List<TimeSpan>? timeStamps = null;

					int current = Interlocked.Increment(ref done);
					if (sw.ElapsedMilliseconds > 300)
						if (Interlocked.Exchange(ref lastNotified, current) < current) {
							sw.Restart();
							ThumbnailProgress?.Invoke(current, total);
						}

					int maxDim = Settings.ThumbnailMaxWidth > 0 ? Settings.ThumbnailMaxWidth : 100;

					if (!needsThumbnails) {
						Interlocked.Increment(ref skippedMissing);
					}
					else if (entry.IsImage) {
						timeStamps = new(0);
						list = new List<byte[]>(1);
						var b = ExtractThumbnailJpeg(entry.Path, TimeSpan.Zero, maxDim);
						if (b == null || b.Length == 0) {
							Logger.Instance.Info($"Failed loading image from file: '{entry.Path}'.");
							return ValueTask.CompletedTask;
						}
						list.Add(b);
						entry.ThumbnailWidth = maxDim;
						Interlocked.Increment(ref loaded);
					}
					else {
						list = new List<byte[]>(positionList.Count);
						timeStamps = new List<TimeSpan>(positionList.Count);
						int failedPositions = 0;
						for (int j = 0; j < positionList.Count; j++) {
							var timestamp = TimeSpan.FromSeconds(entry.Duration.TotalSeconds * positionList[j]);
							var b = FfmpegEngine.ExtractThumbnailJpeg(entry.Path, timestamp, maxDim, Settings.ExtendedFFToolsLogging);
							if (b == null || b.Length == 0) {
								failedPositions++;
								Logger.Instance.Info($"Failed extracting thumbnail at {timestamp} for '{entry.Path}', skipping that position.");
								continue;
							}
							list.Add(b);
							timeStamps.Add(timestamp);
						}
						if (list.Count == 0 && NoThumbnailImage != null) {
							list.Add(NoThumbnailImage);
							timeStamps.Add(TimeSpan.Zero);
							entry.ThumbnailWidth = 0;
							Logger.Instance.Info($"Using placeholder for '{entry.Path}' — all {positionList.Count} sample position(s) failed.");
							Interlocked.Increment(ref placeholders);
						}
						else if (list.Count > 0 && failedPositions > 0) {
							entry.ThumbnailWidth = maxDim;
							Logger.Instance.Info($"Loaded {list.Count}/{positionList.Count} thumbnail(s) for '{entry.Path}' ({failedPositions} position(s) failed).");
							Interlocked.Increment(ref loaded);
						}
						else if (list.Count > 0) {
							entry.ThumbnailWidth = maxDim;
							Interlocked.Increment(ref loaded);
						}
					}
					Debug.Assert(timeStamps != null);
					entry.SetThumbnails(list ?? (NoThumbnailImage != null ? new() { NoThumbnailImage } : new()), timeStamps!);

					return ValueTask.CompletedTask;
				});
			}
			catch (OperationCanceledException) { }
			Logger.Instance.Info($"Thumbnail loading complete: {loaded} fully loaded, {placeholders} placeholder, {skippedMissing} skipped (missing on disk) in {totalSw.Elapsed.TotalSeconds:F1}s.");
			ThumbnailsRetrieved?.Invoke(this, new EventArgs());
		}

		public static byte[]? ExtractThumbnailJpeg(string filePath, TimeSpan position, int maxWidth = 0, int jpegQuality = 0) {
			if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return null;

			bool isImage = IsImageExtension(Path.GetExtension(filePath));
			return FfmpegEngine.GetThumbnail(new FfmpegSettings {
				File = filePath,
				Position = isImage ? TimeSpan.Zero : position,
				GrayScale = 0,
				Fullsize = (byte)(maxWidth == 0 ? 1 : 0),
				MaxWidth = maxWidth,
				JpegQuality = jpegQuality,
				SoftwareDecodeOnly = isImage,
			}, false);
		}

		static bool GetGrayBytesFromImage(FileEntry imageFile, bool useExifIfAvailable, bool extendedLogging) =>
			MediaAnalyzer.GetGrayBytesFromImage(imageFile, useExifIfAvailable, extendedLogging);
	}
}
