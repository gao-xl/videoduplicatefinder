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

using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Xml;
using System.Xml.Linq;
using ReactiveUI;
using VDF.Core;
using VDF.Core.Utils;
using VDF.GUI.ViewModels;

namespace VDF.GUI.Data {
	public enum ThumbnailDoubleClickAction { OpenFile, OpenThumbnailComparer }

	public class SettingsFile : ReactiveObject {
		private static SettingsFile? instance;
		private static string? settingsPath;

		[JsonIgnore]
		public static SettingsFile Instance => instance ??= new SettingsFile();

		/// <summary>
		/// The canonical Core settings, serialized as a nested <c>"core"</c> object.
		/// Adding a new field to <see cref="VDF.Core.Settings"/> requires ZERO changes
		/// here — it is automatically serialized as part of this nested object.
		/// Forwarding properties below delegate to this instance so existing call sites
		/// (e.g. <c>SettingsFile.Instance.Percent</c>) continue to work.
		/// </summary>
		Settings _Core;
		[JsonPropertyName("core")]
		public VDF.Core.Settings Core {
			get => _Core;
			set => this.RaiseAndSetIfChanged(ref _Core, value);
		}

		public SettingsFile() {
			// GUI-specific defaults that differ from Core's defaults.
			_Core = new VDF.Core.Settings {
				GeneratePreviewThumbnails = true,
				Percent = 95f,
				MaxDegreeOfParallelism = -1, // -1 = auto (<= 0 means auto in Core)
				HardwareAccelerationMode = VDF.Core.FFTools.FFHardwareAccelerationMode.auto,
				MaximumFileSize = 999999999,
				LanguageCode = ResolveDefaultLanguageCode(),
			};
		}


		public static void SetSettingsPath(string? path) {
			settingsPath = string.IsNullOrWhiteSpace(path) ? null : path;
		}

		static string ResolveSettingsPath(string? path) {
			if (!string.IsNullOrWhiteSpace(path))
				return path;
			if (!string.IsNullOrWhiteSpace(settingsPath))
				return settingsPath;

			return FileUtils.SafePathCombine(CoreUtils.SettingsFolder, "Settings.json");
		}
		public class CustomActionCommands {
			public string OpenItemInFolder { get; set; } = string.Empty;
			public string OpenMultipleInFolder { get; set; } = string.Empty;
			public string OpenItem { get; set; } = string.Empty;
			public string OpenMultiple { get; set; } = string.Empty;
		}

		[JsonPropertyName("Includes")]
		public ObservableCollection<string> Includes { get; set; } = new();
		[JsonPropertyName("Blacklists")]
		public ObservableCollection<string> Blacklists { get; set; } = new();
		string _LastCustomSelectExpression = string.Empty;
		[JsonPropertyName("LastCustomSelectExpression")]
		public string LastCustomSelectExpression {
			get => _LastCustomSelectExpression;
			set => this.RaiseAndSetIfChanged(ref _LastCustomSelectExpression, value);
		}

		ObservableCollection<string> _ExpressionHistory = new();
		[JsonPropertyName("ExpressionHistory")]
		public ObservableCollection<string> ExpressionHistory {
			get => _ExpressionHistory;
			set => this.RaiseAndSetIfChanged(ref _ExpressionHistory, value);
		}

		ObservableCollection<ExpressionPreset> _ExpressionPresets = new();
		[JsonPropertyName("ExpressionPresets")]
		public ObservableCollection<ExpressionPreset> ExpressionPresets {
			get => _ExpressionPresets;
			set => this.RaiseAndSetIfChanged(ref _ExpressionPresets, value);
		}

		ObservableCollection<CustomSelectionPreset> _CustomSelectionPresets = new();
		[JsonPropertyName("CustomSelectionPresets")]
		public ObservableCollection<CustomSelectionPreset> CustomSelectionPresets {
			get => _CustomSelectionPresets;
			set => this.RaiseAndSetIfChanged(ref _CustomSelectionPresets, value);
		}

		bool _AutoApplySelectionPresetEnabled;
		[JsonPropertyName("AutoApplySelectionPresetEnabled")]
		public bool AutoApplySelectionPresetEnabled {
			get => _AutoApplySelectionPresetEnabled;
			set => this.RaiseAndSetIfChanged(ref _AutoApplySelectionPresetEnabled, value);
		}
		string _AutoApplySelectionPreset = string.Empty;
		/// <summary>Name of the custom-selection preset applied automatically after every scan.</summary>
		[JsonPropertyName("AutoApplySelectionPreset")]
		public string AutoApplySelectionPreset {
			get => _AutoApplySelectionPreset;
			set => this.RaiseAndSetIfChanged(ref _AutoApplySelectionPreset, value);
		}

		double? _MainWindowWidth;
		[JsonPropertyName("MainWindowWidth")]
		public double? MainWindowWidth {
			get => _MainWindowWidth;
			set => this.RaiseAndSetIfChanged(ref _MainWindowWidth, value);
		}
		double? _MainWindowHeight;
		[JsonPropertyName("MainWindowHeight")]
		public double? MainWindowHeight {
			get => _MainWindowHeight;
			set => this.RaiseAndSetIfChanged(ref _MainWindowHeight, value);
		}
		int? _MainWindowPositionX;
		[JsonPropertyName("MainWindowPositionX")]
		public int? MainWindowPositionX {
			get => _MainWindowPositionX;
			set => this.RaiseAndSetIfChanged(ref _MainWindowPositionX, value);
		}
		int? _MainWindowPositionY;
		[JsonPropertyName("MainWindowPositionY")]
		public int? MainWindowPositionY {
			get => _MainWindowPositionY;
			set => this.RaiseAndSetIfChanged(ref _MainWindowPositionY, value);
		}
		bool _MainWindowMaximized;
		[JsonPropertyName("MainWindowMaximized")]
		public bool MainWindowMaximized {
			get => _MainWindowMaximized;
			set => this.RaiseAndSetIfChanged(ref _MainWindowMaximized, value);
		}
		string _LastSortOrder = string.Empty;
		/// <summary>Key of the results sort order (see MainWindowVM.SortOrders), restored on startup.</summary>
		[JsonPropertyName("LastSortOrder")]
		public string LastSortOrder {
			get => _LastSortOrder;
			set => this.RaiseAndSetIfChanged(ref _LastSortOrder, value);
		}

		// ── Forwarding properties to Core ────────────────────────────────────
		// These delegate to Core so existing call sites (SettingsFile.Instance.X)
		// keep working. They are [JsonIgnore]d because Core is serialized as the
		// nested "core" object — the single source of truth. Adding a new field
		// to VDF.Core.Settings requires NO changes here.

		[JsonIgnore]
		public string LanguageCode {
			get => Core.LanguageCode;
			set {
				var resolved = ResolveLanguageCode(value);
				if (Core.LanguageCode != resolved) {
					Core.LanguageCode = resolved;
					this.RaisePropertyChanged(nameof(LanguageCode));
				}
			}
		}
		[JsonIgnore]
		public bool IgnoreReadOnlyFolders {
			get => Core.IgnoreReadOnlyFolders;
			set { if (Core.IgnoreReadOnlyFolders != value) { Core.IgnoreReadOnlyFolders = value; this.RaisePropertyChanged(nameof(IgnoreReadOnlyFolders)); } }
		}
		[JsonIgnore]
		public bool ExcludeHardLinks {
			get => Core.ExcludeHardLinks;
			set { if (Core.ExcludeHardLinks != value) { Core.ExcludeHardLinks = value; this.RaisePropertyChanged(nameof(ExcludeHardLinks)); } }
		}
		[JsonIgnore]
		public bool IgnoreReparsePoints {
			get => Core.IgnoreReparsePoints;
			set { if (Core.IgnoreReparsePoints != value) { Core.IgnoreReparsePoints = value; this.RaisePropertyChanged(nameof(IgnoreReparsePoints)); } }
		}
		[JsonIgnore]
		public bool IgnoreBlackPixels {
			get => Core.IgnoreBlackPixels;
			set { if (Core.IgnoreBlackPixels != value) { Core.IgnoreBlackPixels = value; this.RaisePropertyChanged(nameof(IgnoreBlackPixels)); } }
		}
		[JsonIgnore]
		public bool IgnoreWhitePixels {
			get => Core.IgnoreWhitePixels;
			set { if (Core.IgnoreWhitePixels != value) { Core.IgnoreWhitePixels = value; this.RaisePropertyChanged(nameof(IgnoreWhitePixels)); } }
		}
		[JsonIgnore]
		public int MaxDegreeOfParallelism {
			get => Core.MaxDegreeOfParallelism;
			set { if (Core.MaxDegreeOfParallelism != value) { Core.MaxDegreeOfParallelism = value; this.RaisePropertyChanged(nameof(MaxDegreeOfParallelism)); } }
		}
		[JsonIgnore]
		public VDF.Core.FFTools.FFHardwareAccelerationMode HardwareAccelerationMode {
			get => Core.HardwareAccelerationMode;
			set { if (Core.HardwareAccelerationMode != value) { Core.HardwareAccelerationMode = value; this.RaisePropertyChanged(nameof(HardwareAccelerationMode)); } }
		}
		[JsonIgnore]
		public bool CompareHorizontallyFlipped {
			get => Core.CompareHorizontallyFlipped;
			set { if (Core.CompareHorizontallyFlipped != value) { Core.CompareHorizontallyFlipped = value; this.RaisePropertyChanged(nameof(CompareHorizontallyFlipped)); } }
		}
		[JsonIgnore]
		public bool IncludeSubDirectories {
			get => Core.IncludeSubDirectories;
			set { if (Core.IncludeSubDirectories != value) { Core.IncludeSubDirectories = value; this.RaisePropertyChanged(nameof(IncludeSubDirectories)); } }
		}
		[JsonIgnore]
		public bool IncludeImages {
			get => Core.IncludeImages;
			set { if (Core.IncludeImages != value) { Core.IncludeImages = value; this.RaisePropertyChanged(nameof(IncludeImages)); } }
		}
		[JsonIgnore]
		public bool GeneratePreviewThumbnails {
			get => Core.GeneratePreviewThumbnails;
			set { if (Core.GeneratePreviewThumbnails != value) { Core.GeneratePreviewThumbnails = value; this.RaisePropertyChanged(nameof(GeneratePreviewThumbnails)); } }
		}
		[JsonIgnore]
		public int ThumbnailMaxWidth {
			get => Core.ThumbnailMaxWidth;
			set {
				var clamped = Math.Clamp(value, 48, 960);
				if (Core.ThumbnailMaxWidth != clamped) { Core.ThumbnailMaxWidth = clamped; this.RaisePropertyChanged(nameof(ThumbnailMaxWidth)); }
			}
		}
		[JsonIgnore]
		public bool ExtendedFFToolsLogging {
			get => Core.ExtendedFFToolsLogging;
			set { if (Core.ExtendedFFToolsLogging != value) { Core.ExtendedFFToolsLogging = value; this.RaisePropertyChanged(nameof(ExtendedFFToolsLogging)); } }
		}
		[JsonIgnore]
		public bool LogExcludedFiles {
			get => Core.LogExcludedFiles;
			set { if (Core.LogExcludedFiles != value) { Core.LogExcludedFiles = value; this.RaisePropertyChanged(nameof(LogExcludedFiles)); } }
		}
		[JsonIgnore]
		public bool AlwaysRetryFailedSampling {
			get => Core.AlwaysRetryFailedSampling;
			set { if (Core.AlwaysRetryFailedSampling != value) { Core.AlwaysRetryFailedSampling = value; this.RaisePropertyChanged(nameof(AlwaysRetryFailedSampling)); } }
		}
		[JsonIgnore]
		public bool UseNativeFfmpegBinding {
			get => Core.UseNativeFfmpegBinding;
			set { if (Core.UseNativeFfmpegBinding != value) { Core.UseNativeFfmpegBinding = value; this.RaisePropertyChanged(nameof(UseNativeFfmpegBinding)); } }
		}
		[JsonIgnore]
		public string CustomFFArguments {
			get => Core.CustomFFArguments;
			set { if (Core.CustomFFArguments != value) { Core.CustomFFArguments = value; this.RaisePropertyChanged(nameof(CustomFFArguments)); } }
		}
		[JsonIgnore]
		public bool BackupAfterListChanged {
			get => _BackupAfterListChanged;
			set => this.RaiseAndSetIfChanged(ref _BackupAfterListChanged, value);
		}
		bool _BackupAfterListChanged = true;
		[JsonIgnore]
		public bool AskToSaveResultsOnExit {
			get => _AskToSaveResultsOnExit;
			set => this.RaiseAndSetIfChanged(ref _AskToSaveResultsOnExit, value);
		}
		bool _AskToSaveResultsOnExit = true;
		[JsonIgnore]
		public bool IncludeNonExistingFiles {
			get => Core.IncludeNonExistingFiles;
			set { if (Core.IncludeNonExistingFiles != value) { Core.IncludeNonExistingFiles = value; this.RaisePropertyChanged(nameof(IncludeNonExistingFiles)); } }
		}
		[JsonIgnore]
		public bool ScanAgainstEntireDatabase {
			get => Core.ScanAgainstEntireDatabase;
			set { if (Core.ScanAgainstEntireDatabase != value) { Core.ScanAgainstEntireDatabase = value; this.RaisePropertyChanged(nameof(ScanAgainstEntireDatabase)); } }
		}
		[JsonIgnore]
		public VDF.Core.FolderMatchMode FolderMatchMode {
			get => Core.FolderMatchMode;
			set {
				if (Core.FolderMatchMode != value) {
					Core.FolderMatchMode = value;
					this.RaisePropertyChanged(nameof(FolderMatchMode));
					this.RaisePropertyChanged(nameof(IsFolderMatchModeActive));
				}
			}
		}
		public bool IsFolderMatchModeActive => FolderMatchMode != VDF.Core.FolderMatchMode.None;
		[JsonIgnore]
		public int SameFolderDepth {
			get => Core.SameFolderDepth;
			set { if (Core.SameFolderDepth != value) { Core.SameFolderDepth = value; this.RaisePropertyChanged(nameof(SameFolderDepth)); } }
		}
		[JsonIgnore]
		public bool UsePHash {
			get => Core.UsePHashing;
			set { if (Core.UsePHashing != value) { Core.UsePHashing = value; this.RaisePropertyChanged(nameof(UsePHash)); } }
		}
		[JsonIgnore]
		public bool UseExifCreationDate {
			get => Core.UseExifCreationDate;
			set { if (Core.UseExifCreationDate != value) { Core.UseExifCreationDate = value; this.RaisePropertyChanged(nameof(UseExifCreationDate)); } }
		}
		[JsonIgnore]
		public float Percent {
			get => Core.Percent;
			set { if (Core.Percent != value) { Core.Percent = value; this.RaisePropertyChanged(nameof(Percent)); } }
		}
		[JsonIgnore]
		public double PercentDurationDifference {
			get => Core.PercentDurationDifference;
			set { if (Core.PercentDurationDifference != value) { Core.PercentDurationDifference = value; this.RaisePropertyChanged(nameof(PercentDurationDifference)); } }
		}
		[JsonIgnore]
		public int DurationDifferenceMinSeconds {
			get => (int)Core.DurationDifferenceMinSeconds;
			set { if ((int)Core.DurationDifferenceMinSeconds != value) { Core.DurationDifferenceMinSeconds = value; this.RaisePropertyChanged(nameof(DurationDifferenceMinSeconds)); } }
		}
		[JsonIgnore]
		public int DurationDifferenceMaxSeconds {
			get => (int)Core.DurationDifferenceMaxSeconds;
			set { if ((int)Core.DurationDifferenceMaxSeconds != value) { Core.DurationDifferenceMaxSeconds = value; this.RaisePropertyChanged(nameof(DurationDifferenceMaxSeconds)); } }
		}
		[JsonIgnore]
		public int MaxSamplingDurationSeconds {
			get => (int)Core.MaxSamplingDurationSeconds;
			set { if ((int)Core.MaxSamplingDurationSeconds != value) { Core.MaxSamplingDurationSeconds = value; this.RaisePropertyChanged(nameof(MaxSamplingDurationSeconds)); } }
		}
		[JsonIgnore]
		public int Thumbnails {
			get => Core.ThumbnailCount;
			set { if (Core.ThumbnailCount != value) { Core.ThumbnailCount = value; this.RaisePropertyChanged(nameof(Thumbnails)); } }
		}
		[JsonPropertyName("CustomCommands")]
		public CustomActionCommands CustomCommands { get; set; } = new();
		[JsonIgnore]
		public string CustomDatabaseFolder {
			get => Core.CustomDatabaseFolder;
			set { if (Core.CustomDatabaseFolder != value) { Core.CustomDatabaseFolder = value; this.RaisePropertyChanged(nameof(CustomDatabaseFolder)); } }
		}
		[JsonIgnore]
		public int DatabaseCheckpointIntervalMinutes {
			get => Core.DatabaseCheckpointIntervalMinutes;
			set {
				var clamped = Math.Max(0, value);
				if (Core.DatabaseCheckpointIntervalMinutes != clamped) { Core.DatabaseCheckpointIntervalMinutes = clamped; this.RaisePropertyChanged(nameof(DatabaseCheckpointIntervalMinutes)); }
			}
		}

		public static void SaveSettings(string? path = null) {
			path = ResolveSettingsPath(path);
			File.WriteAllText(path, JsonSerializer.Serialize(instance, GuiJsonContext.Default.SettingsFile));
		}

		bool _UseMica = false;
		[JsonPropertyName("UseMica")]
		public bool UseMica {
			get => _UseMica;
			set => this.RaiseAndSetIfChanged(ref _UseMica, value);
		}
		bool _DarkMode = true;
		[JsonPropertyName("DarkMode")]
		public bool DarkMode {
			get => _DarkMode;
			set => this.RaiseAndSetIfChanged(ref _DarkMode, value);
		}
		double? _ThumbnailComparerWindowWidth;
		[JsonPropertyName("ThumbnailComparerWindowWidth")]
		public double? ThumbnailComparerWindowWidth {
			get => _ThumbnailComparerWindowWidth;
			set => this.RaiseAndSetIfChanged(ref _ThumbnailComparerWindowWidth, value);
		}
		double? _ThumbnailComparerWindowHeight;
		[JsonPropertyName("ThumbnailComparerWindowHeight")]
		public double? ThumbnailComparerWindowHeight {
			get => _ThumbnailComparerWindowHeight;
			set => this.RaiseAndSetIfChanged(ref _ThumbnailComparerWindowHeight, value);
		}
		double? _ThumbnailComparerWindowPositionX;
		[JsonPropertyName("ThumbnailComparerWindowPositionX")]
		public double? ThumbnailComparerWindowPositionX {
			get => _ThumbnailComparerWindowPositionX;
			set => this.RaiseAndSetIfChanged(ref _ThumbnailComparerWindowPositionX, value);
		}
		double? _ThumbnailComparerWindowPositionY;
		[JsonPropertyName("ThumbnailComparerWindowPositionY")]
		public double? ThumbnailComparerWindowPositionY {
			get => _ThumbnailComparerWindowPositionY;
			set => this.RaiseAndSetIfChanged(ref _ThumbnailComparerWindowPositionY, value);
		}
		int? _ThumbnailComparerWindowScreenIndex;
		[JsonPropertyName("ThumbnailComparerWindowScreenIndex")]
		public int? ThumbnailComparerWindowScreenIndex {
			get => _ThumbnailComparerWindowScreenIndex;
			set => this.RaiseAndSetIfChanged(ref _ThumbnailComparerWindowScreenIndex, value);
		}
		CompareMode _ThumbnailComparerMode = CompareMode.Swipe;
		[JsonPropertyName("ThumbnailComparerMode")]
		public CompareMode ThumbnailComparerMode {
			get => _ThumbnailComparerMode;
			set => this.RaiseAndSetIfChanged(ref _ThumbnailComparerMode, value);
		}
		bool _ShowThumbnailColumn = true;
		[JsonPropertyName("ShowThumbnailColumn")]
		public bool ShowThumbnailColumn {
			get => _ShowThumbnailColumn;
			set => this.RaiseAndSetIfChanged(ref _ShowThumbnailColumn, value);
		}
		bool _ShowPathColumn = true;
		[JsonPropertyName("ShowPathColumn")]
		public bool ShowPathColumn {
			get => _ShowPathColumn;
			set => this.RaiseAndSetIfChanged(ref _ShowPathColumn, value);
		}
		bool _ShowDurationColumn = true;
		[JsonPropertyName("ShowDurationColumn")]
		public bool ShowDurationColumn {
			get => _ShowDurationColumn;
			set => this.RaiseAndSetIfChanged(ref _ShowDurationColumn, value);
		}
		bool _ShowFormatColumn = true;
		[JsonPropertyName("ShowFormatColumn")]
		public bool ShowFormatColumn {
			get => _ShowFormatColumn;
			set => this.RaiseAndSetIfChanged(ref _ShowFormatColumn, value);
		}
		bool _ShowAudioColumn = true;
		[JsonPropertyName("ShowAudioColumn")]
		public bool ShowAudioColumn {
			get => _ShowAudioColumn;
			set => this.RaiseAndSetIfChanged(ref _ShowAudioColumn, value);
		}
		bool _ShowSimilarityColumn = true;
		[JsonPropertyName("ShowSimilarityColumn")]
		public bool ShowSimilarityColumn {
			get => _ShowSimilarityColumn;
			set => this.RaiseAndSetIfChanged(ref _ShowSimilarityColumn, value);
		}
		ThumbnailDoubleClickAction _ThumbnailDoubleClickAction = ThumbnailDoubleClickAction.OpenFile;
		[JsonPropertyName("ThumbnailDoubleClickAction")]
		public ThumbnailDoubleClickAction ThumbnailDoubleClickAction {
			get => _ThumbnailDoubleClickAction;
			set => this.RaiseAndSetIfChanged(ref _ThumbnailDoubleClickAction, value);
		}
		[JsonIgnore]
		public bool FilterByFilePathContains {
			get => Core.FilterByFilePathContains;
			set { if (Core.FilterByFilePathContains != value) { Core.FilterByFilePathContains = value; this.RaisePropertyChanged(nameof(FilterByFilePathContains)); } }
		}
		ObservableCollection<string> _FilePathContainsTexts = new();
		[JsonPropertyName("FilePathContainsTexts")]
		public ObservableCollection<string> FilePathContainsTexts {
			get => _FilePathContainsTexts;
			set => this.RaiseAndSetIfChanged(ref _FilePathContainsTexts, value);
		}
		[JsonIgnore]
		public bool FilterByFilePathNotContains {
			get => Core.FilterByFilePathNotContains;
			set { if (Core.FilterByFilePathNotContains != value) { Core.FilterByFilePathNotContains = value; this.RaisePropertyChanged(nameof(FilterByFilePathNotContains)); } }
		}
		ObservableCollection<string> _FilePathNotContainsTexts = new();
		[JsonPropertyName("FilePathNotContainsTexts")]
		public ObservableCollection<string> FilePathNotContainsTexts {
			get => _FilePathNotContainsTexts;
			set => this.RaiseAndSetIfChanged(ref _FilePathNotContainsTexts, value);
		}
		[JsonIgnore]
		public bool FilterByFileSize {
			get => Core.FilterByFileSize;
			set { if (Core.FilterByFileSize != value) { Core.FilterByFileSize = value; this.RaisePropertyChanged(nameof(FilterByFileSize)); } }
		}
		[JsonIgnore]
		public int MaximumFileSize {
			get => Core.MaximumFileSize;
			set { if (Core.MaximumFileSize != value) { Core.MaximumFileSize = value; this.RaisePropertyChanged(nameof(MaximumFileSize)); } }
		}
		[JsonIgnore]
		public int MinimumFileSize {
			get => Core.MinimumFileSize;
			set { if (Core.MinimumFileSize != value) { Core.MinimumFileSize = value; this.RaisePropertyChanged(nameof(MinimumFileSize)); } }
		}

		[JsonIgnore]
		public bool EnablePartialClipDetection {
			get => Core.EnablePartialClipDetection;
			set { if (Core.EnablePartialClipDetection != value) { Core.EnablePartialClipDetection = value; this.RaisePropertyChanged(nameof(EnablePartialClipDetection)); } }
		}
		/// <summary>Forwarding property: GUI displays 0–100, Core stores 0.0–1.0 ratio.</summary>
		[JsonIgnore]
		public double PartialClipMinRatioPercent {
			get => Core.PartialClipMinRatio * 100.0;
			set {
				var ratio = value / 100.0;
				if (Core.PartialClipMinRatio != ratio) { Core.PartialClipMinRatio = ratio; this.RaisePropertyChanged(nameof(PartialClipMinRatioPercent)); }
			}
		}
		/// <summary>Forwarding property: GUI displays 0–100, Core stores 0.0–1.0 ratio.</summary>
		[JsonIgnore]
		public double PartialClipSimilarityThresholdPercent {
			get => Core.PartialClipSimilarityThreshold * 100.0;
			set {
				var ratio = value / 100.0;
				if (Core.PartialClipSimilarityThreshold != ratio) { Core.PartialClipSimilarityThreshold = ratio; this.RaisePropertyChanged(nameof(PartialClipSimilarityThresholdPercent)); }
			}
		}
		[JsonIgnore]
		public bool PartialClipRequireVisualMatch {
			get => Core.PartialClipRequireVisualMatch;
			set { if (Core.PartialClipRequireVisualMatch != value) { Core.PartialClipRequireVisualMatch = value; this.RaisePropertyChanged(nameof(PartialClipRequireVisualMatch)); } }
		}
		/// <summary>Forwarding property: GUI displays 0–100, Core stores 0.0–1.0 ratio.</summary>
		[JsonIgnore]
		public double PartialClipVisualThresholdPercent {
			get => Core.PartialClipVisualThreshold * 100.0;
			set {
				var ratio = value / 100.0;
				if (Core.PartialClipVisualThreshold != ratio) { Core.PartialClipVisualThreshold = ratio; this.RaisePropertyChanged(nameof(PartialClipVisualThresholdPercent)); }
			}
		}

		List<string> _QualityCriteriaOrder = ["Duration", "Resolution", "FPS", "Bitrate", "Audio Bitrate", "Size"];
		[JsonPropertyName("QualityCriteriaOrder")]
		public List<string> QualityCriteriaOrder {
			get => _QualityCriteriaOrder;
			set => this.RaiseAndSetIfChanged(ref _QualityCriteriaOrder, value);
		}

		bool _EnableScheduledScan;
		[JsonPropertyName("EnableScheduledScan")]
		public bool EnableScheduledScan {
			get => _EnableScheduledScan;
			set => this.RaiseAndSetIfChanged(ref _EnableScheduledScan, value);
		}
		string _ScheduledScanTime = "02:00";
		[JsonPropertyName("ScheduledScanTime")]
		public string ScheduledScanTime {
			get => _ScheduledScanTime;
			set => this.RaiseAndSetIfChanged(ref _ScheduledScanTime, value);
		}
		bool _NotifyOnScheduledScanComplete = true;
		[JsonPropertyName("NotifyOnScheduledScanComplete")]
		public bool NotifyOnScheduledScanComplete {
			get => _NotifyOnScheduledScanComplete;
			set => this.RaiseAndSetIfChanged(ref _NotifyOnScheduledScanComplete, value);
		}
		bool _NotifyOnScanComplete;
		[JsonPropertyName("NotifyOnScanComplete")]
		public bool NotifyOnScanComplete {
			get => _NotifyOnScanComplete;
			set => this.RaiseAndSetIfChanged(ref _NotifyOnScanComplete, value);
		}

		Dictionary<string, string> _KeyboardShortcuts = new();
		[JsonPropertyName("KeyboardShortcuts")]
		public Dictionary<string, string> KeyboardShortcuts {
			get => _KeyboardShortcuts;
			set => this.RaiseAndSetIfChanged(ref _KeyboardShortcuts, value);
		}

		public static void LoadSettings(string? path = null) {
			path ??= settingsPath;
			if ((path == null || path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) && LoadOldSettings(path))
				return;

			path = ResolveSettingsPath(path);
			if (!File.Exists(path)) return;
			var json = File.ReadAllText(path);
			// Migrate legacy flat JSON (pre-composition) to the nested "core" format.
			json = MigrateLegacyJson(json);
			instance = JsonSerializer.Deserialize(json, GuiJsonContext.Default.SettingsFile);
		}

		/// <summary>
		/// If the JSON has flat Core fields at the top level (old format), moves them
		/// into a nested <c>"core"</c> object so the composition-based deserializer
		/// can read them.  Handles name mapping (e.g. <c>Thumbnails</c> →
		/// <c>ThumbnailCount</c>) and unit conversion (e.g.
		/// <c>PartialClipMinRatioPercent</c> → <c>PartialClipMinRatio</c> /100).
		/// </summary>
		static string MigrateLegacyJson(string json) {
			JsonNode? root;
			try { root = JsonNode.Parse(json); }
			catch { return json; } // malformed — let the deserializer report the error
			if (root is not JsonObject obj) return json;
			if (obj.ContainsKey("core")) return json; // already new format

			var core = new JsonObject();

			// Direct name matches — move as-is from root to core.
			string[] directMoves = [
				"IgnoreReadOnlyFolders", "ExcludeHardLinks", "IgnoreReparsePoints",
				"IgnoreBlackPixels", "IgnoreWhitePixels", "MaxDegreeOfParallelism",
				"HardwareAccelerationMode", "CompareHorizontallyFlipped",
				"IncludeSubDirectories", "IncludeImages", "GeneratePreviewThumbnails",
				"ThumbnailMaxWidth", "ExtendedFFToolsLogging", "LogExcludedFiles",
				"AlwaysRetryFailedSampling", "UseNativeFfmpegBinding",
				"CustomFFArguments", "IncludeNonExistingFiles",
				"ScanAgainstEntireDatabase", "FolderMatchMode", "SameFolderDepth",
				"UseExifCreationDate", "Percent", "PercentDurationDifference",
				"DurationDifferenceMinSeconds", "DurationDifferenceMaxSeconds",
				"MaxSamplingDurationSeconds", "CustomDatabaseFolder",
				"DatabaseCheckpointIntervalMinutes", "FilterByFilePathContains",
				"FilterByFilePathNotContains", "FilterByFileSize",
				"MaximumFileSize", "MinimumFileSize",
				"EnablePartialClipDetection", "PartialClipRequireVisualMatch",
				"LanguageCode",
			];
			foreach (var key in directMoves) {
				if (obj.TryGetPropertyValue(key, out var v)) {
					obj.Remove(key); // detach parent before re-parenting under "core"
					core[key] = v;
				}
			}

			// Name-mapped moves.
			if (obj.TryGetPropertyValue("Thumbnails", out var thumbs)) {
				obj.Remove("Thumbnails");
				core["ThumbnailCount"] = thumbs;
			}
			if (obj.TryGetPropertyValue("UsePHash", out var phash)) {
				obj.Remove("UsePHash");
				core["UsePHashing"] = phash;
			}

			// Name-mapped + unit-converted (percent 0–100 → ratio 0.0–1.0).
			if (obj.TryGetPropertyValue("PartialClipMinRatioPercent", out var pct)) {
				core["PartialClipMinRatio"] = pct?.GetValue<double>() / 100.0;
				obj.Remove("PartialClipMinRatioPercent");
			}
			if (obj.TryGetPropertyValue("PartialClipSimilarityThresholdPercent", out pct)) {
				core["PartialClipSimilarityThreshold"] = pct?.GetValue<double>() / 100.0;
				obj.Remove("PartialClipSimilarityThresholdPercent");
			}
			if (obj.TryGetPropertyValue("PartialClipVisualThresholdPercent", out pct)) {
				core["PartialClipVisualThreshold"] = pct?.GetValue<double>() / 100.0;
				obj.Remove("PartialClipVisualThresholdPercent");
			}

			obj["core"] = core;
			return root.ToJsonString();
		}

		static bool LoadOldSettings(string? path) {
			path ??= FileUtils.SafePathCombine(CoreUtils.CurrentFolder, "Settings.xml");
			if (!File.Exists(path)) return false;
			var xmlSettings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit };
			using var reader = XmlReader.Create(path, xmlSettings);
			var xDoc = XDocument.Load(reader);
			foreach (var n in xDoc.Descendants("Include"))
				Instance.Includes.Add(n.Value);
			foreach (var n in xDoc.Descendants("Exclude"))
				Instance.Blacklists.Add(n.Value);
			foreach (var n in xDoc.Descendants("Percent"))
				if (int.TryParse(n.Value, out var value))
					Instance.Percent = value;
			foreach (var n in xDoc.Descendants("MaxDegreeOfParallelism"))
				if (int.TryParse(n.Value, out var value))
					Instance.MaxDegreeOfParallelism = value;
			foreach (var n in xDoc.Descendants("Thumbnails"))
				if (int.TryParse(n.Value, out var value))
					Instance.Thumbnails = value;
			foreach (var n in xDoc.Descendants("IncludeSubDirectories"))
				if (bool.TryParse(n.Value, out var value))
					Instance.IncludeSubDirectories = value;
			foreach (var n in xDoc.Descendants("IncludeImages"))
				if (bool.TryParse(n.Value, out var value))
					Instance.IncludeImages = value;
			foreach (var n in xDoc.Descendants("IgnoreReadOnlyFolders"))
				if (bool.TryParse(n.Value, out var value))
					Instance.IgnoreReadOnlyFolders = value;
			//09.03.21: UseCuda is obsolete and has been replaced with UseHardwareAcceleration.
			foreach (var n in xDoc.Descendants("UseCuda"))
				if (bool.TryParse(n.Value, out var value))
					Instance.HardwareAccelerationMode = value ? VDF.Core.FFTools.FFHardwareAccelerationMode.auto : VDF.Core.FFTools.FFHardwareAccelerationMode.none;
			foreach (var n in xDoc.Descendants("HardwareAccelerationMode"))
				if (Enum.TryParse<VDF.Core.FFTools.FFHardwareAccelerationMode>(n.Value, out var value))
					Instance.HardwareAccelerationMode = value;
			foreach (var n in xDoc.Descendants("GeneratePreviewThumbnails"))
				if (bool.TryParse(n.Value, out var value))
					Instance.GeneratePreviewThumbnails = value;
			foreach (var n in xDoc.Descendants("IgnoreHardlinks"))
				if (bool.TryParse(n.Value, out var value))
					Instance.IgnoreReparsePoints = value;
			foreach (var n in xDoc.Descendants("ExtendedFFToolsLogging"))
				if (bool.TryParse(n.Value, out var value))
					Instance.ExtendedFFToolsLogging = value;
			foreach (var n in xDoc.Descendants("AlwaysRetryFailedSampling"))
				if (bool.TryParse(n.Value, out var value))
					Instance.AlwaysRetryFailedSampling = value;
			foreach (var n in xDoc.Descendants("UseNativeFfmpegBinding"))
				if (bool.TryParse(n.Value, out var value))
					Instance.UseNativeFfmpegBinding = value;
			foreach (var n in xDoc.Descendants("BackupAfterListChanged"))
				if (bool.TryParse(n.Value, out var value))
					Instance.BackupAfterListChanged = value;
			foreach (var n in xDoc.Descendants("IgnoreBlackPixels"))
				if (bool.TryParse(n.Value, out var value))
					Instance.IgnoreBlackPixels = value;
			foreach (var n in xDoc.Descendants("IgnoreWhitePixels"))
				if (bool.TryParse(n.Value, out var value))
					Instance.IgnoreWhitePixels = value;
			foreach (var n in xDoc.Descendants("CustomFFArguments"))
				Instance.CustomFFArguments = n.Value;
			foreach (var n in xDoc.Descendants("LastCustomSelectExpression"))
				Instance.LastCustomSelectExpression = n.Value;
			foreach (var n in xDoc.Descendants("CompareHorizontallyFlipped"))
				if (bool.TryParse(n.Value, out var value))
					Instance.CompareHorizontallyFlipped = value;
			SaveSettings(Path.ChangeExtension(path, "json"));
			File.Delete(path);
			return true;
		}

		static string ResolveDefaultLanguageCode() => ResolveLanguageCode(null);

		static string ResolveLanguageCode(string? languageCode) {
			if (!string.IsNullOrWhiteSpace(languageCode))
				return languageCode;

			var culture = CultureInfo.CurrentUICulture;
			if (!string.IsNullOrWhiteSpace(culture.TwoLetterISOLanguageName))
				return culture.TwoLetterISOLanguageName;

			return "zh-Hans";
		}
	}
}
