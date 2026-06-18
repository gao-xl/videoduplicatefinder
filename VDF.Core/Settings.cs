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
//


namespace VDF.Core {
	public enum FolderMatchMode { None, SameFolderOnly, DifferentFolderOnly }

	public sealed class Settings {
		// Settable so System.Text.Json can populate these from --settings JSON; without
		// a setter STJ silently leaves them empty even with IncludeFields=true (read-only
		// collection properties aren't repopulated by the default object converter).
		public HashSet<string> IncludeList { get; set; } = new HashSet<string>();
		public HashSet<string> BlackList { get; set; } = new HashSet<string>();

		public bool IgnoreReadOnlyFolders;
		public bool IgnoreReparsePoints;
		public bool ExcludeHardLinks;
		public bool GeneratePreviewThumbnails;
		public bool UseNativeFfmpegBinding;
		public bool IncludeSubDirectories = true;
		public bool IncludeImages = true;
		public bool ExtendedFFToolsLogging;
		public bool LogExcludedFiles;
		public bool AlwaysRetryFailedSampling;
		public bool IgnoreBlackPixels;
		public bool IgnoreWhitePixels;
		public bool CompareHorizontallyFlipped;
		public bool IncludeNonExistingFiles;
		public bool ScanAgainstEntireDatabase;
		public FolderMatchMode FolderMatchMode;
		public int SameFolderDepth = 1;
		public bool UsePHashing;
		public bool UseExifCreationDate;
		public string LanguageCode = "zh-Hans";
		public bool ShowWelcomeGuide = true;

		public FFTools.FFHardwareAccelerationMode HardwareAccelerationMode;

		/// <summary>
		/// When true and <see cref="UseNativeFfmpegBinding"/> is true, automatically detect
		/// and use the best available hardware acceleration method at runtime. When the
		/// <see cref="HardwareAccelerationMode"/> is set to a specific value other than
		/// <see cref="FFTools.FFHardwareAccelerationMode.none"/> or
		/// <see cref="FFTools.FFHardwareAccelerationMode.auto"/>, that explicit choice
		/// takes precedence over auto-detection.
		/// </summary>
		public bool AutoDetectHardwareAcceleration = true;

		public byte Threshhold = 5;
		public float Percent = 96f;
		public double PercentDurationDifference = 20d;
		public double DurationDifferenceMinSeconds;
		public double DurationDifferenceMaxSeconds;
		public double MaxSamplingDurationSeconds;

		public int ThumbnailCount = 1;
		/// <summary>Maximum width in pixels for display thumbnails (0 = original resolution).</summary>
		public int ThumbnailMaxWidth = 100;
		/// <summary>
		/// Maximum degree of parallelism for scanning operations.
		/// Use 0 or negative values for automatic (based on CPU count).
		/// </summary>
		public int MaxDegreeOfParallelism = 1;

		/// <summary>
		/// Gets the effective parallelism for scanning operations.
		/// Returns the configured value or falls back to CPU count.
		/// </summary>
		public int GetEffectiveParallelism() {
			if (MaxDegreeOfParallelism <= 0)
				return Math.Max(1, Environment.ProcessorCount);
			return MaxDegreeOfParallelism;
		}

		public string CustomFFArguments = string.Empty;
		public string CustomDatabaseFolder = string.Empty;

		public bool FilterByFilePathContains;
		public List<string> FilePathContainsTexts = new();
		public bool FilterByFilePathNotContains;
		public List<string> FilePathNotContainsTexts = new();
		public bool FilterByFileSize;
		public int MaximumFileSize;
		public int MinimumFileSize;

		/// <summary>
		/// When non-zero, files whose size differs by more than this percentage are
		/// skipped during comparison. E.g. 50 means files must be within ±50% of each
		/// other's size. 0 = disabled (default, backward compatible).
		/// </summary>
		public double FileSizeTolerancePercent;

		/// <summary>
		/// When true, files whose resolution (width × height) differs significantly
		/// are skipped during comparison. Two files are considered resolution-compatible
		/// if the smaller resolution is at least 50% of the larger one's pixel count.
		/// Default true.
		/// </summary>
		public bool EnableResolutionPreFilter = true;

		// ── Partial clip detection ──────────────────────────────────────────────
		/// <summary>Enable audio-fingerprint-based partial clip detection.</summary>
		public bool EnablePartialClipDetection;
		/// <summary>
		/// Minimum ratio of clip-duration / source-duration for a pair to be a candidate.
		/// Default 0.10 (clip must be at least 10% of the longer video).
		/// </summary>
		public double PartialClipMinRatio = 0.10;
		/// <summary>
		/// Minimum average Hamming similarity (0–1) for a sliding-window match to be
		/// accepted as a partial clip.  Default 0.80.
		/// </summary>
		public double PartialClipSimilarityThreshold = 0.80;
		/// <summary>
		/// When true, partial clip matches must also pass a visual frame check at the
		/// matched offset. Suppresses false positives from videos sharing the same audio
		/// (e.g. TikToks reusing a song) but with different visual content.
		/// </summary>
		public bool PartialClipRequireVisualMatch = true;
		/// <summary>
		/// Minimum visual similarity (0–1) for the on-demand frame check used by
		/// <see cref="PartialClipRequireVisualMatch"/>.  Default 0.85.
		/// Compared via pHash when <see cref="UsePHashing"/> is enabled, otherwise via
		/// 32×32 grayscale percentage difference.
		/// </summary>
		public double PartialClipVisualThreshold = 0.85;

		// ── Network path resilience ──────────────────────────────────────────
		/// <summary>
		/// Timeout in seconds for directory enumeration on network paths (SMB/NFS).
		/// When a network path cannot be enumerated within this time, it is skipped
		/// instead of hanging the scan. Default 30 seconds.
		/// </summary>
		public int NetworkPathTimeoutSeconds = 30;
		/// <summary>
		/// Maximum number of retry attempts for transient network errors during scanning.
		/// Each retry uses exponential backoff (1s, 2s, 4s, …). Default 3.
		/// </summary>
		public int NetworkRetryCount = 3;

		// ── Database checkpoints ────────────────────────────────────────────
		/// <summary>
		/// Interval in minutes between automatic database saves during scanning.
		/// 0 = disabled (only save at phase boundaries). Default 5.
		/// </summary>
		public int DatabaseCheckpointIntervalMinutes = 5;

		/// <summary>
		/// Test-only field used by SettingsTests to verify that new public fields on
		/// <see cref="Settings"/> are automatically serialized by the composition-based
		/// GUI/Web settings files without any manual sync code.  Not read by any
		/// production logic.
		/// </summary>
		public string TestAutoSerializeField = string.Empty;

		/// <summary>
		/// Returns the allowed duration tolerance in seconds for a video of the given duration,
		/// based on <see cref="PercentDurationDifference"/>, <see cref="DurationDifferenceMinSeconds"/>,
		/// and <see cref="DurationDifferenceMaxSeconds"/>. When the percent rule is disabled (0%),
		/// the seconds bounds act as a flat tolerance so users can run a seconds-only comparison.
		/// </summary>
		internal double GetDurationToleranceSeconds(double durationSeconds) {
			if (PercentDurationDifference > 0) {
				double toleranceSeconds = durationSeconds * PercentDurationDifference / 100d;
				if (DurationDifferenceMinSeconds > 0)
					toleranceSeconds = Math.Max(toleranceSeconds, DurationDifferenceMinSeconds);
				if (DurationDifferenceMaxSeconds > 0)
					toleranceSeconds = Math.Min(toleranceSeconds, DurationDifferenceMaxSeconds);
				return Math.Max(0d, toleranceSeconds);
			}
			// Percent rule disabled: tolerance comes solely from the seconds bounds. Without a
			// percent term, Max would otherwise pin the tolerance to 0; instead take the largest
			// enabled bound so a seconds-only setup behaves like a flat tolerance.
			return Math.Max(0d, Math.Max(DurationDifferenceMinSeconds, DurationDifferenceMaxSeconds));
		}

		/// <summary>
		/// Applies a preset configuration to the current settings.
		/// </summary>
		public void ApplyPreset(ScanPreset preset) {
			switch (preset) {
			case ScanPreset.Fast:
				Threshhold = 10;
				Percent = 90f;
				PercentDurationDifference = 30d;
				MaxDegreeOfParallelism = 0; // Auto based on CPU count
				ThumbnailCount = 1;
				UsePHashing = false;
				IncludeImages = false;
				EnablePartialClipDetection = false;
				CompareHorizontallyFlipped = false;
				MaxSamplingDurationSeconds = 30;
				break;

			case ScanPreset.Balanced:
				Threshhold = 5;
				Percent = 96f;
				PercentDurationDifference = 20d;
				MaxDegreeOfParallelism = 0; // Auto based on CPU count
				ThumbnailCount = 1;
				UsePHashing = false;
				IncludeImages = true;
				EnablePartialClipDetection = false;
				CompareHorizontallyFlipped = false;
				MaxSamplingDurationSeconds = 0;
				break;

			case ScanPreset.Precise:
				Threshhold = 2;
				Percent = 99f;
				PercentDurationDifference = 10d;
				MaxDegreeOfParallelism = 1;
				ThumbnailCount = 3;
				UsePHashing = true;
				IncludeImages = true;
				EnablePartialClipDetection = true;
				PartialClipRequireVisualMatch = true;
				CompareHorizontallyFlipped = true;
				MaxSamplingDurationSeconds = 0;
				break;

			case ScanPreset.ImageOnly:
				Threshhold = 5;
				Percent = 96f;
				PercentDurationDifference = 0;
				MaxDegreeOfParallelism = 0; // Auto based on CPU count
				ThumbnailCount = 1;
				IncludeImages = true;
				EnablePartialClipDetection = false;
				CompareHorizontallyFlipped = false;
				break;

			case ScanPreset.AudioFingerprint:
				Threshhold = 5;
				Percent = 96f;
				PercentDurationDifference = 20d;
				MaxDegreeOfParallelism = 1;
				ThumbnailCount = 1;
				EnablePartialClipDetection = true;
				PartialClipRequireVisualMatch = true;
				PartialClipMinRatio = 0.10;
				PartialClipSimilarityThreshold = 0.80;
				PartialClipVisualThreshold = 0.85;
				break;
			}
		}
	}

	public enum ScanPreset {
		Fast,
		Balanced,
		Precise,
		ImageOnly,
		AudioFingerprint
	}
}
