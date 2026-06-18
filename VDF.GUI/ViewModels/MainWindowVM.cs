// /*
//     Copyright (C) 2026 0x90d
//     This file is part of VideoDuplicateFinder
//     VideoDuplicateFinder is free software: you can redistribute it and/or modify
//     it under the terms of the GPLv3 as published by
//     the Free Software Foundation, either version 3 of the License, or
//     (at your option) any later version.
//     VideoDuplicateFinder is distributed in the hope that it will be useful,
//     but WITHOUT ANY WARRANTY without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//     GNU General Public License for more details.
//     You should have received a copy of the GNU General Public License
//     along with VideoDuplicateFinder.  If not, see <http://www.gnu.org/licenses/>.
// */
//

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FFmpeg.AutoGen;
using ReactiveUI;
using VDF.Core;
using VDF.Core.Services;
using VDF.Core.Utils;
using VDF.Core.ViewModels;
using VDF.GUI.Data;
using VDF.GUI.Views;

namespace VDF.GUI.ViewModels {
	public partial class MainWindowVM : ReactiveObject {
		public ScanEngine Scanner { get; } = new();
		/// <summary>
		/// Wraps <see cref="Scanner"/> with a unified state machine, cancellation,
		/// pause/resume, and progress throttling. GUI binds IsScanning/IsBusy etc.
		/// to orchestrator events instead of maintaining its own state machine.
		/// </summary>
		readonly ScanOrchestrator _orchestrator;
		/// <summary>
		/// Unified batch file operations (delete / move / hardlink / symlink /
		/// recycle-bin). Attached to <see cref="Scanner"/> so the service handles
		/// <c>RemoveFromDatabase</c>/<c>UpdateFilePathInDatabase</c>/
		/// <c>SaveDatabase</c> and <c>Scanner.Duplicates</c> cleanup automatically.
		/// For blacklist mode (where GUI calls <c>BlackListFileEntry</c> itself)
		/// a null-engine instance is used so the service only performs file I/O.
		/// </summary>
		readonly FileOperationsService _fileOps;
		readonly ResultsStore _resultsStore = new();
		public ScanOrchestrator Orchestrator => _orchestrator;
		public ObservableCollection<string> LogItems { get; } = new();
		List<HashSet<string>> GroupBlacklist = new();
		public string BackupScanResultsFile =>
			ResultsStore.DefaultBackupPath(SettingsFile.Instance.CustomDatabaseFolder);
		public string BlacklistedGroupsFile =>
			Path.Combine(CoreUtils.ResolveDatabaseFolder(SettingsFile.Instance.CustomDatabaseFolder), "BlacklistedGroups.json");

		// Older builds wrote BlacklistedGroups.json next to the executable; current
		// code keeps it alongside the rest of the per-user database state. Move
		// the file once if a legacy copy is present and the new location is empty.
		void MigrateLegacyBlacklistLocation() {
			try {
				string legacy = Path.Combine(CoreUtils.CurrentFolder, "BlacklistedGroups.json");
				string current = BlacklistedGroupsFile;
				var cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
				if (string.Equals(Path.GetFullPath(legacy), Path.GetFullPath(current), cmp)) return;
				if (File.Exists(legacy) && !File.Exists(current)) {
					Directory.CreateDirectory(Path.GetDirectoryName(current)!);
					File.Move(legacy, current);
					Logger.Instance.Info($"Migrated BlacklistedGroups.json from {legacy} to {current}");
				}
			}
			catch (Exception ex) {
				Logger.Instance.Info($"Failed to migrate BlacklistedGroups.json: {ex.Message}");
			}
		}

		public AvaloniaList<DuplicateItemVM> Duplicates { get; } = new();

		private TempDir? TempDirectory;

		static readonly string[] BusyMessages =
		{
			App.Lang["BusyMessages1"],
			App.Lang["BusyMessages2"],
			App.Lang["BusyMessages3"],
			App.Lang["BusyMessages4"],
			App.Lang["BusyMessages5"],
			App.Lang["BusyMessages6"],
			App.Lang["BusyMessages7"],
			App.Lang["BusyMessages8"],
			App.Lang["BusyMessages9"],
			App.Lang["BusyMessages10"],
			App.Lang["BusyMessages11"],
			App.Lang["BusyMessages12"],
			App.Lang["BusyMessages13"],
			App.Lang["BusyMessages14"],
			App.Lang["BusyMessages15"],
			App.Lang["BusyMessages16"],
			App.Lang["BusyMessages17"],
			App.Lang["BusyMessages18"],
			App.Lang["BusyMessages19"],
			App.Lang["BusyMessages20"]
		};
		private void ChangeIsBusyMessage() => IsBusyOverlayText = BusyMessages[Random.Shared.Next(BusyMessages.Length)];

		readonly DispatcherTimer scheduledScanTimer = new();
		DateTime lastScheduledScanDate = DateTime.MinValue;
		bool scheduledScanInProgress;
		bool scheduleTimeInvalidNotified;

		bool _IsScanning;
		public bool IsScanning {
			get => _IsScanning;
			set => this.RaiseAndSetIfChanged(ref _IsScanning, value);
		}
		string _IsBusyOverlayText = string.Empty;
		public string IsBusyOverlayText {
			get => _IsBusyOverlayText;
			set => this.RaiseAndSetIfChanged(ref _IsBusyOverlayText, value);
		}
		bool _IsReadyToCompare;
		public bool IsReadyToCompare {
			get => _IsReadyToCompare;
			set => this.RaiseAndSetIfChanged(ref _IsReadyToCompare, value);
		}
		bool _IsGathered;
		public bool IsGathered {
			get => _IsGathered;
			set => this.RaiseAndSetIfChanged(ref _IsGathered, value);
		}
		bool _IsPaused;
		public bool IsPaused {
			get => _IsPaused;
			set => this.RaiseAndSetIfChanged(ref _IsPaused, value);
		}
		bool _ShowThumbnailRetrievalProgressBar;
		public bool ShowThumbnailRetrievalProgressBar {
			get => _ShowThumbnailRetrievalProgressBar;
			set => this.RaiseAndSetIfChanged(ref _ShowThumbnailRetrievalProgressBar, value);
		}
		public bool ShowThumbnailRetrievalProgress => !string.IsNullOrEmpty(ThumbnailRetrievalProgressText);
		string _ThumbnailRetrievalProgressText = string.Empty;
		public string ThumbnailRetrievalProgressText {
			get => _ThumbnailRetrievalProgressText;
			set {
				this.RaiseAndSetIfChanged(ref _ThumbnailRetrievalProgressText, value);
				this.RaisePropertyChanged(nameof(ShowThumbnailRetrievalProgress));
			}
		}
		string _ScanProgressText = string.Empty;
		public string ScanProgressText {
			get => _ScanProgressText;
			set => this.RaiseAndSetIfChanged(ref _ScanProgressText, value);
		}
		string _RemainingTime = string.Empty;
		public string RemainingTime {
			get => _RemainingTime;
			set => this.RaiseAndSetIfChanged(ref _RemainingTime, value);
		}

		string _TimeElapsed = string.Empty;
		public string TimeElapsed {
			get => _TimeElapsed;
			set => this.RaiseAndSetIfChanged(ref _TimeElapsed, value);
		}
		int _ScanProgressValue;
		public int ScanProgressValue {
			get => _ScanProgressValue;
			set => this.RaiseAndSetIfChanged(ref _ScanProgressValue, value);
		}
		bool _IsBusy;
		public bool IsBusy {
			get => _IsBusy;
			set => this.RaiseAndSetIfChanged(ref _IsBusy, value);
		}
		int _ScanProgressMaxValue = 100;
		public int ScanProgressMaxValue {
			get => _ScanProgressMaxValue;
			set => this.RaiseAndSetIfChanged(ref _ScanProgressMaxValue, value);
		}
		int _TotalDuplicates;
		public int TotalDuplicates {
			get => _TotalDuplicates;
			set => this.RaiseAndSetIfChanged(ref _TotalDuplicates, value);
		}
		int _TotalDuplicateGroups;
		public int TotalDuplicateGroups {
			get => _TotalDuplicateGroups;
			set => this.RaiseAndSetIfChanged(ref _TotalDuplicateGroups, value);
		}
		string _TotalDuplicatesSize = string.Empty;
		public string TotalDuplicatesSize {
			get => _TotalDuplicatesSize;
			set => this.RaiseAndSetIfChanged(ref _TotalDuplicatesSize, value);
		}
		string _PotentialSavings = string.Empty;
		/// <summary>Space freed if only the largest file of every group were kept.</summary>
		public string PotentialSavings {
			get => _PotentialSavings;
			set => this.RaiseAndSetIfChanged(ref _PotentialSavings, value);
		}
		long _TotalSizeRemovedInternal;
		long TotalSizeRemovedInternal {
			get => _TotalSizeRemovedInternal;
			set {
				_TotalSizeRemovedInternal = value;
				this.RaisePropertyChanged(nameof(TotalSizeRemoved));
			}
		}
		int _DuplicatesCheckedCounter;
		public int DuplicatesCheckedCounter {
			get => _DuplicatesCheckedCounter;
			set => this.RaiseAndSetIfChanged(ref _DuplicatesCheckedCounter, value);
		}
		long _DuplicatesCheckedSizeInternal;
		long DuplicatesCheckedSizeInternal {
			get => _DuplicatesCheckedSizeInternal;
			set {
				_DuplicatesCheckedSizeInternal = value;
				this.RaisePropertyChanged(nameof(DuplicatesCheckedSize));
			}
		}
		public string DuplicatesCheckedSize => DuplicatesCheckedSizeInternal.BytesToString();
		// SizeLong is -1 when the file no longer exists on disk; don't let those
		// items distort the checked-size total.
		static long CheckedSizeOf(DuplicateItemVM item) => Math.Max(0, item.ItemInfo.SizeLong);

		// ---- Selection undo (Ctrl+Z) ----
		// Each step holds the items whose Checked state changed in one gesture and
		// the value to restore. Bulk selection commands group their changes via
		// BeginSelectionUndoBatch(); ungrouped changes (single checkbox clicks)
		// become one step each.
		const int MaxSelectionUndoSteps = 200;
		readonly List<List<(DuplicateItemVM Item, bool OldChecked)>> selectionUndoStack = new();
		List<(DuplicateItemVM Item, bool OldChecked)>? activeSelectionUndoBatch;
		bool suppressSelectionUndoRecording;

		internal IDisposable BeginSelectionUndoBatch() {
			// Nested scopes (e.g. a preset auto-applied from another command) fold
			// into the outer step so one Ctrl+Z reverts the whole gesture.
			if (activeSelectionUndoBatch != null)
				return System.Reactive.Disposables.Disposable.Empty;
			var batch = activeSelectionUndoBatch = new();
			return System.Reactive.Disposables.Disposable.Create(() => {
				activeSelectionUndoBatch = null;
				if (batch.Count > 0)
					PushSelectionUndoStep(batch);
			});
		}

		void RecordCheckedChangeForUndo(DuplicateItemVM item) {
			if (suppressSelectionUndoRecording) return;
			var entry = (item, !item.Checked); // PropertyChanged fires after the change
			if (activeSelectionUndoBatch != null) {
				activeSelectionUndoBatch.Add(entry);
				return;
			}
			PushSelectionUndoStep(new() { entry });
		}

		void PushSelectionUndoStep(List<(DuplicateItemVM Item, bool OldChecked)> step) {
			selectionUndoStack.Add(step);
			if (selectionUndoStack.Count > MaxSelectionUndoSteps)
				selectionUndoStack.RemoveAt(0);
		}

		public ReactiveCommand<Unit, Unit> UndoSelectionCommand => ReactiveCommand.Create(() => {
			if (selectionUndoStack.Count == 0) return;
			var step = selectionUndoStack[^1];
			selectionUndoStack.RemoveAt(selectionUndoStack.Count - 1);
			suppressSelectionUndoRecording = true;
			try {
				// Restore in reverse so items touched twice in one step end up
				// at their original state.
				for (int i = step.Count - 1; i >= 0; i--)
					step[i].Item.Checked = step[i].OldChecked;
			}
			finally {
				suppressSelectionUndoRecording = false;
			}
		});

		// Live count of checked items per group, maintained alongside the checked
		// counters. The "groups with checked items" filter and sort previously
		// rescanned the whole duplicates list per row, which is quadratic on
		// large result sets.
		readonly Dictionary<Guid, int> checkedCountByGroup = new();
		internal bool GroupHasCheckedItems(Guid groupId) => checkedCountByGroup.ContainsKey(groupId);
		void AdjustCheckedGroupIndex(DuplicateItemVM item, int delta) {
			Guid groupId = item.ItemInfo.GroupId;
			checkedCountByGroup.TryGetValue(groupId, out int count);
			count += delta;
			if (count <= 0)
				checkedCountByGroup.Remove(groupId);
			else
				checkedCountByGroup[groupId] = count;
		}
		public bool IsMultiOpenSupported => !string.IsNullOrEmpty(SettingsFile.Instance.CustomCommands.OpenMultiple);
		public bool IsMultiOpenInFolderSupported => !string.IsNullOrEmpty(SettingsFile.Instance.CustomCommands.OpenMultipleInFolder);

		public bool IsWindows => CoreUtils.IsWindows;

		public string TotalSizeRemoved => TotalSizeRemovedInternal.BytesToString();
#if DEBUG
		public static bool IsDebug => true;
#else
		public static bool IsDebug => false;
#endif

		// ILLink flags every WhenAnyValue call (IL2026): it reflects over the member
		// chain in the expression. All chains here target view-model properties that
		// are also statically referenced (compiled bindings and code), so they are
		// preserved and the warning is a false positive — suppressed per call site.
		internal const string WhenAnyValueTrimJustification =
			"WhenAnyValue reflects over view-model properties that are also statically referenced (compiled bindings and code), so they are preserved.";

		[System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026", Justification = WhenAnyValueTrimJustification)]
		public MainWindowVM() {
			MigrateLegacyBlacklistLocation();
			GroupBlacklist = BlacklistStore.Load(BlacklistedGroupsFile, msg => Logger.Instance.Info(msg));
			_FileType = TypeFilters[0];
			_orchestrator = new ScanOrchestrator(Scanner);
			_fileOps = new FileOperationsService(Scanner);
			_orchestrator.ProgressChanged += Orchestrator_ProgressChanged;
			_orchestrator.Completed += Orchestrator_Completed;
			Scanner.ThumbnailProgress += Scanner_ThumbnailProgress;
			Scanner.ThumbnailsRetrieved += Scanner_ThumbnailsRetrieved;
			Scanner.DatabaseCleaned += Scanner_DatabaseCleaned;
			Scanner.FilesEnumerated += Scanner_FilesEnumerated;
			using (var assetStream = AssetLoader.Open(new Uri("avares://VDF.GUI/Assets/icon.png")))
			using (var assetMs = new MemoryStream()) {
				assetStream.CopyTo(assetMs);
				Scanner.NoThumbnailImage = assetMs.ToArray();
			}

			try {
				TempDirectory = TempExtractionManager.Register(new("VDF-"));
				// Invalidate thumbnail cache if the configured width has changed
				Utils.ThumbCacheHelpers.InvalidateIfWidthChanged(TempDirectory.Path, SettingsFile.Instance.ThumbnailMaxWidth);
				Utils.ThumbCacheHelpers.Provider = new ThumbnailService(Scanner, new ThumbnailServiceOptions { PackFolder = TempDirectory.Path });
			}
			catch { Utils.ThumbCacheHelpers.Provider = null; }

			try {
				File.Delete(Path.Combine(CoreUtils.CurrentFolder, "log.txt"));
			}
			catch { }
			Logger.Instance.LogItemAdded += Instance_LogItemAdded;
			//Ensure items added before GUI was ready will be shown
			Instance_LogItemAdded(string.Empty);

			Duplicates.CollectionChanged += Duplicates_CollectionChanged;

			scheduledScanTimer.Interval = TimeSpan.FromMinutes(1);
			scheduledScanTimer.Tick += (_, __) => CheckScheduledScan();
			scheduledScanTimer.Start();
			CheckScheduledScan();

			SortOrders = new SortOrderOption[] {
				new SortOrderOption("None", null),
				new SortOrderOption("Size Ascending",
				DataGridSortDescription.FromPath($"{nameof(DuplicateItemVM.ItemInfo)}.{nameof(DuplicateItem.SizeLong)}", ListSortDirection.Ascending)),
				new SortOrderOption("Size Descending",
				DataGridSortDescription.FromPath($"{nameof(DuplicateItemVM.ItemInfo)}.{nameof(DuplicateItem.SizeLong)}", ListSortDirection.Descending)),
				new SortOrderOption("Resolution Ascending",
				DataGridSortDescription.FromPath($"{nameof(DuplicateItemVM.ItemInfo)}.{nameof(DuplicateItem.FrameSizeInt)}", ListSortDirection.Ascending)),
				new SortOrderOption("Resolution Descending",
				DataGridSortDescription.FromPath($"{nameof(DuplicateItemVM.ItemInfo)}.{nameof(DuplicateItem.FrameSizeInt)}", ListSortDirection.Descending)),
				new SortOrderOption("Duration Ascending",
				DataGridSortDescription.FromPath($"{nameof(DuplicateItemVM.ItemInfo)}.{nameof(DuplicateItem.Duration)}", ListSortDirection.Ascending)),
				new SortOrderOption("Duration Descending",
				DataGridSortDescription.FromPath($"{nameof(DuplicateItemVM.ItemInfo)}.{nameof(DuplicateItem.Duration)}", ListSortDirection.Descending)),
				new SortOrderOption("Date Created Ascending",
				DataGridSortDescription.FromPath($"{nameof(DuplicateItemVM.ItemInfo)}.{nameof(DuplicateItem.DateCreated)}", ListSortDirection.Ascending)),
				new SortOrderOption("Date Created Descending",
				DataGridSortDescription.FromPath($"{nameof(DuplicateItemVM.ItemInfo)}.{nameof(DuplicateItem.DateCreated)}", ListSortDirection.Descending)),
				new SortOrderOption("Similarity Ascending",
				DataGridSortDescription.FromPath($"{nameof(DuplicateItemVM.ItemInfo)}.{nameof(DuplicateItem.Similarity)}", ListSortDirection.Ascending)),
				new SortOrderOption("Similarity Descending",
				DataGridSortDescription.FromPath($"{nameof(DuplicateItemVM.ItemInfo)}.{nameof(DuplicateItem.Similarity)}", ListSortDirection.Descending)),
				new SortOrderOption("Group Has Selected Items Ascending",
				DataGridSortDescription.FromComparer(new CheckedGroupsComparer(this), ListSortDirection.Ascending)),
				new SortOrderOption("Group Has Selected Items Descending",
				DataGridSortDescription.FromComparer(new CheckedGroupsComparer(this), ListSortDirection.Descending)),
				new SortOrderOption("Group Size Ascending",
				DataGridSortDescription.FromComparer(new GroupSizeComparer(this), ListSortDirection.Ascending)),
				new SortOrderOption("Group Size Descending",
				DataGridSortDescription.FromComparer(new GroupSizeComparer(this), ListSortDirection.Descending)),
				new SortOrderOption("Group Total Size Ascending",
				DataGridSortDescription.FromComparer(new GroupTotalSizeComparer(this), ListSortDirection.Ascending)),
				new SortOrderOption("Group Total Size Descending",
				DataGridSortDescription.FromComparer(new GroupTotalSizeComparer(this), ListSortDirection.Descending)),
			};
			_SortOrder = SortOrders[0];
			if (!string.IsNullOrEmpty(SettingsFile.Instance.LastSortOrder)) {
				foreach (var order in SortOrders)
					if (order.Name == SettingsFile.Instance.LastSortOrder) {
						_SortOrder = order;
						break;
					}
			}

			this.WhenAnyValue(vm => vm.FilterByPath)
					.Throttle(TimeSpan.FromMilliseconds(500), RxSchedulers.MainThreadScheduler)
						.Subscribe(_ => { RebuildSearchPathIndex(); view?.Refresh(); });
		}

		void Duplicates_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) {
			if (e.OldItems != null) {
				foreach (INotifyPropertyChanged item in e.OldItems) {
					item.PropertyChanged -= DuplicateItemVM_PropertyChanged;
					if (((DuplicateItemVM)item).Checked) {
						DuplicatesCheckedCounter--;
						DuplicatesCheckedSizeInternal -= CheckedSizeOf((DuplicateItemVM)item);
						AdjustCheckedGroupIndex((DuplicateItemVM)item, -1);
					}
				}
			}
			if (e.NewItems != null) {
				foreach (INotifyPropertyChanged item in e.NewItems) {
					item.PropertyChanged += DuplicateItemVM_PropertyChanged;
					// Items can arrive already checked (restored backups); count them
					// so the counters and the per-group index stay accurate.
					if (((DuplicateItemVM)item).Checked) {
						DuplicatesCheckedCounter++;
						DuplicatesCheckedSizeInternal += CheckedSizeOf((DuplicateItemVM)item);
						AdjustCheckedGroupIndex((DuplicateItemVM)item, +1);
					}
				}
			}
			if (e.Action == NotifyCollectionChangedAction.Reset) {
				DuplicatesCheckedCounter = 0;
				DuplicatesCheckedSizeInternal = 0;
				checkedCountByGroup.Clear();
				selectionUndoStack.Clear();
			}
		}

		void DuplicateItemVM_PropertyChanged(object? sender, PropertyChangedEventArgs e) {
			if (e.PropertyName != nameof(DuplicateItemVM.Checked) || sender == null) return;
			RecordCheckedChangeForUndo((DuplicateItemVM)sender);
			if (((DuplicateItemVM)sender).Checked) {
				DuplicatesCheckedCounter++;
				DuplicatesCheckedSizeInternal += CheckedSizeOf((DuplicateItemVM)sender);
				AdjustCheckedGroupIndex((DuplicateItemVM)sender, +1);
			}
			else {
				DuplicatesCheckedCounter--;
				DuplicatesCheckedSizeInternal -= CheckedSizeOf((DuplicateItemVM)sender);
				AdjustCheckedGroupIndex((DuplicateItemVM)sender, -1);
			}
		}

		public async void Thumbnails_ValueChanged(object? sender, NumericUpDownValueChangedEventArgs e) {
			bool isReadyToCompare = IsGathered;
			isReadyToCompare &= Scanner.Settings.ThumbnailCount == e.NewValue;
			if (!isReadyToCompare && ApplicationHelpers.MainWindowDataContext.IsReadyToCompare)
				await MessageBoxService.Show($"Number of thumbnails can't be changed between quick rescans. Full scan will be required.");
			ApplicationHelpers.MainWindowDataContext.IsReadyToCompare = isReadyToCompare;
		}

		private void Scanner_ThumbnailProgress(int arg1, int arg2) => Dispatcher.UIThread.Post(() => {
			ThumbnailRetrievalProgressText = $"Retrieving thumbnails for preview: {arg1}/{arg2}";
		});

		void Scanner_ThumbnailsRetrieved(object? sender, EventArgs e) {
			//Reset properties
			ScanProgressText = string.Empty;
			RemainingTime = new TimeSpan().Format();
			ScanProgressValue = 0;
			ScanProgressMaxValue = 100;
			ThumbnailRetrievalProgressText = string.Empty;
			ShowThumbnailRetrievalProgressBar = false;
#pragma warning disable CS4014
			if (SettingsFile.Instance.BackupAfterListChanged)
				ExportScanResults(BackupScanResultsFile);
#pragma warning restore CS4014
		}

		void Scanner_FilesEnumerated(object? sender, EventArgs e) => ChangeIsBusyMessage();

		async void Scanner_DatabaseCleaned(object? sender, EventArgs e) {
			IsBusy = false;
			await MessageBoxService.Show(App.Lang["Message.DatabaseCleaned"]);
		}

		public async Task<bool> SaveScanResults() {
			if (Duplicates.Count == 0 || !SettingsFile.Instance.AskToSaveResultsOnExit) {
				try { Utils.ThumbCacheHelpers.Provider?.FlushIndex(); } catch { }
				return true;
			}
			MessageBoxButtons? result = await MessageBoxService.Show(App.Lang["Message.SaveResultsPrompt"],
				MessageBoxButtons.Yes | MessageBoxButtons.No | MessageBoxButtons.Cancel);
			if (result == null || result == MessageBoxButtons.Cancel) {
				return false;
			}
			if (result != MessageBoxButtons.Yes) {
				return true;
			}
			await ExportScanResults(BackupScanResultsFile);
			return true;
		}

		public void RestoreBackupScanResults() {
			if (File.Exists(BackupScanResultsFile))
				ImportScanResultsIncludingThumbnails(BackupScanResultsFile);
		}

		public async void LoadDatabase() {
			IsBusy = true;
			IsBusyOverlayText = "Loading database...";
			bool success = await ScanEngine.LoadDatabase();
			IsBusy = false;
			if (!success) {
				await MessageBoxService.Show(App.Lang["Message.LoadDatabaseFailed"]);
				Environment.Exit(-1);
			}
		}

		void CheckScheduledScan() {
			if (!SettingsFile.Instance.EnableScheduledScan || IsScanning || scheduledScanInProgress)
				return;
			if (!TryParseScheduledTime(SettingsFile.Instance.ScheduledScanTime, out var scheduledTime)) {
				if (!scheduleTimeInvalidNotified) {
					Logger.Instance.Info(App.Lang["Log.InvalidScheduledScanTime"]);
					scheduleTimeInvalidNotified = true;
				}
				return;
			}
			scheduleTimeInvalidNotified = false;
			var now = DateTime.Now;
			if (lastScheduledScanDate.Date == now.Date)
				return;
			if (now.TimeOfDay < scheduledTime)
				return;
			TryStartScheduledScan();
		}

		static bool TryParseScheduledTime(string value, out TimeSpan time) {
			time = default;
			if (string.IsNullOrWhiteSpace(value)) return false;
			if (TimeSpan.TryParse(value, out time))
				return true;
			return TimeSpan.TryParseExact(value, "hh\\:mm", null, out time);
		}

		void TryStartScheduledScan() {
			if (IsScanning || IsBusy) return;
			if (!string.IsNullOrEmpty(SettingsFile.Instance.CustomDatabaseFolder) && !Directory.Exists(SettingsFile.Instance.CustomDatabaseFolder)) {
				Logger.Instance.Info(App.Lang["Log.ScheduledScanSkippedMissingDatabaseFolder"]);
				return;
			}
			if (Duplicates.Count > 0) {
				Logger.Instance.Info(App.Lang["Log.ScheduledScanSkippedWithResults"]);
				return;
			}
			if ((SettingsFile.Instance.UseNativeFfmpegBinding && !ScanEngine.NativeFFmpegExists) ||
				(!SettingsFile.Instance.UseNativeFfmpegBinding && !ScanEngine.FFmpegExists)) {
				Logger.Instance.Info(App.Lang["Log.ScheduledScanSkippedMissingFfmpeg"]);
				return;
			}
			if (!ScanEngine.FFprobeExists) {
				Logger.Instance.Info(App.Lang["Log.ScheduledScanSkippedMissingFfprobe"]);
				return;
			}
			if (SettingsFile.Instance.Includes.Count == 0) {
				Logger.Instance.Info(App.Lang["Log.ScheduledScanSkippedNoFolders"]);
				return;
			}
			scheduledScanInProgress = true;
			lastScheduledScanDate = DateTime.Now.Date;
			Dispatcher.UIThread.Post(() => StartScanCommand.Execute("FullScan").Subscribe());
		}

		void Orchestrator_ProgressChanged(object? sender, ScanProgressArgs e) =>
			Dispatcher.UIThread.InvokeAsync(() => {
				// Stage goes at the END — the status bar's TextBlock uses PrefixCharacterEllipsis,
				// so trimming happens at the start (leading directory path) and the filename + stage stay visible.
				string stageSuffix = string.IsNullOrEmpty(e.Message)
					? string.Empty
					: e.StageMax > 0 ? $"  [{e.Message} {e.StageCurrent}/{e.StageMax}]" : $"  [{e.Message}]";
				ScanProgressText = e.CurrentFile + stageSuffix;
				RemainingTime = e.RemainingTime.Format();
				ScanProgressValue = e.FilesProcessed;
				TimeElapsed = e.Elapsed.Format();
				ScanProgressMaxValue = e.FilesTotal;
			});

		void Orchestrator_Completed(object? sender, ScanCompletedEventArgs e) {
			if (e.State == ScanState.Done)
				Dispatcher.UIThread.InvokeAsync(OnScanDone);
			else
				Dispatcher.UIThread.InvokeAsync(() => OnScanAborted(e.ErrorMessage));
		}

		void OnScanAborted(string? errorMessage) {
			if (!string.IsNullOrEmpty(errorMessage))
				Logger.Instance.Info($"Scan error: {errorMessage}");
			IsScanning = false;
			IsBusy = false;
			IsReadyToCompare = false;
			IsGathered = false;
			scheduledScanInProgress = false;
		}

		void OnScanDone() {
			IsScanning = false;
			IsBusy = false;
			IsReadyToCompare = true;
			IsGathered = true;
			ScanProgressText = string.Empty;
			RemainingTime = TimeSpan.Zero.Format();
			ScanProgressValue = 0;
			var completedScheduledScan = scheduledScanInProgress;
			scheduledScanInProgress = false;

			var blacklistedGids = ComputeBlacklistedGroupIds(
				Scanner.Duplicates.Select(d => (d.GroupId, d.Path)));
			if (blacklistedGids.Count > 0)
				Scanner.Duplicates.RemoveWhere(d => blacklistedGids.Contains(d.GroupId));

			foreach (var item in Scanner.Duplicates)
				Duplicates.Add(new DuplicateItemVM(item));

			if (SettingsFile.Instance.GeneratePreviewThumbnails) {
				ShowThumbnailRetrievalProgressBar = true;
				ThumbnailRetrievalProgressText = "Starting to retrieve thumbnails for preview";
				Scanner.RetrieveThumbnails();
			}

			BuildDuplicatesView();
			RebuildSearchPathIndex();
			RefreshGroupStats();

			if (SettingsFile.Instance.AutoApplySelectionPresetEnabled &&
				!string.IsNullOrEmpty(SettingsFile.Instance.AutoApplySelectionPreset)) {
				var preset = SettingsFile.Instance.CustomSelectionPresets
					.FirstOrDefault(p => p.Name == SettingsFile.Instance.AutoApplySelectionPreset);
				if (preset != null) {
					RunCustomSelection(preset.Data);
					Logger.Instance.Info(string.Format(App.Lang["Log.AutoAppliedPreset"], preset.Name));
				}
				else {
					Logger.Instance.Info(string.Format(App.Lang["Log.AutoApplyPresetMissing"], SettingsFile.Instance.AutoApplySelectionPreset));
				}
			}

			if (completedScheduledScan && SettingsFile.Instance.NotifyOnScheduledScanComplete) {
				_ = MessageBoxService.Show(App.Lang["Message.ScheduledScanCompleted"]);
			}

			if (SettingsFile.Instance.NotifyOnScanComplete) {
				VDF.GUI.Utils.DesktopNotificationHelper.Notify(
					App.Lang["Notification.ScanComplete.Title"],
					string.Format(App.Lang["Notification.ScanComplete.Message"], TotalDuplicateGroups));
			}
		}

		void BuildDuplicatesView() {
			view = new DataGridCollectionView(Duplicates);
			view.GroupDescriptions.Add(new DataGridPathGroupDescription($"{nameof(DuplicateItemVM.ItemInfo)}.{nameof(DuplicateItem.GroupId)}"));
			// Rebuilding the view (rescan, import) previously dropped the active sort
			// while the sort ComboBox kept displaying it.
			if (_SortOrder.Sort != null)
				view.SortDescriptions.Add(_SortOrder.Sort);
			view.Filter += DuplicatesFilter;
			GetDataGrid.ItemsSource = view;
			TotalSizeRemovedInternal = 0;
		}

		static DataGrid GetDataGrid => ApplicationHelpers.MainWindow.FindControl<DataGrid>("dataGridGrouping")!;

		void RefreshGroupStats() {
			TotalDuplicates = Duplicates.Count;
			int groupCount = 0;
			long totalSize = 0;
			long savings = 0;
			foreach (var group in Duplicates.GroupBy(x => x.ItemInfo.GroupId)) {
				groupCount++;
				long groupTotal = 0;
				long largest = 0;
				foreach (var item in group) {
					long size = Math.Max(0, item.ItemInfo.SizeLong);
					groupTotal += size;
					if (size > largest) largest = size;
				}
				totalSize += groupTotal;
				savings += groupTotal - largest;
			}
			TotalDuplicatesSize = totalSize.BytesToString();
			TotalDuplicateGroups = groupCount;
			PotentialSavings = savings.BytesToString();
		}

		private DuplicateItemVM? GetSelectedDuplicateItem() {
			return GetDataGrid.SelectedItem as DuplicateItemVM;
		}

		private List<DuplicateItemVM> GetSelectedDuplicates() {
			return GetDataGrid.SelectedItems?.Cast<DuplicateItemVM>().ToList() ?? new();
		}

		public static ReactiveCommand<Unit, Unit> LatestReleaseCommand => ReactiveCommand.CreateFromTask(async () => {
			try {
				Process.Start(new ProcessStartInfo {
					FileName = "https://github.com/0x90d/videoduplicatefinder/releases",
					UseShellExecute = true
				});
			}
			catch {
				await MessageBoxService.Show(App.Lang["Message.OpenReleaseFailed"]);
			}
		});

		public static ReactiveCommand<Unit, Unit> OpenOwnFolderCommand => ReactiveCommand.Create(() => {
			Process.Start(new ProcessStartInfo {
				FileName = CoreUtils.CurrentFolder,
				UseShellExecute = true,
			});
		});

		public ReactiveCommand<Unit, Unit> CleanDatabaseCommand => ReactiveCommand.Create(() => {
			IsBusy = true;
			IsBusyOverlayText = App.Lang["Busy.CleaningDatabase"];
			_ = Scanner.CleanupDatabaseAsync();
		});

		public ReactiveCommand<Unit, Unit> ClearDatabaseCommand => ReactiveCommand.CreateFromTask(async () => {
			MessageBoxButtons? dlgResult = await MessageBoxService.Show(
				App.Lang["Message.ClearDatabaseWarning"],
				MessageBoxButtons.Yes | MessageBoxButtons.No);
			if (dlgResult != MessageBoxButtons.Yes) return;
			ScanEngine.ClearDatabase();
			await MessageBoxService.Show(App.Lang["Message.Done"]);
		});

		public static ReactiveCommand<Unit, Unit> EditDataBaseCommand => ReactiveCommand.CreateFromTask(async () => {
			DatabaseViewer dlg = new();
			bool res = await dlg.ShowDialog<bool>(ApplicationHelpers.MainWindow);
		});
		public static ReactiveCommand<Unit, Unit> ImportDataBaseFromJsonCommand => ReactiveCommand.CreateFromTask(async () => {
			var result = await Utils.PickerDialogUtils.OpenFilePicker(new FilePickerOpenOptions() {
				SuggestedStartLocation = await ApplicationHelpers.MainWindow.StorageProvider.TryGetFolderFromPathAsync(CoreUtils.CurrentFolder),
				FileTypeFilter = [new FilePickerFileType("Json File") { Patterns = ["*.json"] }]
			});
			if (string.IsNullOrEmpty(result)) return;

			bool success = ScanEngine.ImportDataBaseFromJson(result, new JsonSerializerOptions {
				IncludeFields = true,
			});
			if (!success)
				await MessageBoxService.Show(App.Lang["Message.ImportDatabaseFailed"]);
			else
				ScanEngine.SaveDatabase();
		});

		public static ReactiveCommand<Unit, Unit> ExportDataBaseToJsonCommand => ReactiveCommand.Create(() => {
			ExportDbToJson(new JsonSerializerOptions {
				IncludeFields = true,
			});
		});

		public static ReactiveCommand<Unit, Unit> ExportDataBaseToJsonPrettyCommand => ReactiveCommand.Create(() => {
			ExportDbToJson(new JsonSerializerOptions {
				IncludeFields = true,
				WriteIndented = true,
			});
		});

		async static void ExportDbToJson(JsonSerializerOptions options) {
			var result = await Utils.PickerDialogUtils.SaveFilePicker(new FilePickerSaveOptions() {
				DefaultExtension = ".json",
				FileTypeChoices = [new FilePickerFileType("Json Files") { Patterns = ["*.json"] }]
			});
			if (string.IsNullOrEmpty(result)) return;

			if (!ScanEngine.ExportDataBaseToJson(result, options))
				await MessageBoxService.Show(App.Lang["Message.ExportDatabaseFailed"]);
		}

		public ReactiveCommand<Unit, Unit> ExportScanResultsCommand => ReactiveCommand.CreateFromTask(async () => {
			await ExportScanResults();
		});

		public ReactiveCommand<Unit, Unit> ExportScanResultsPrettyCommand => ReactiveCommand.CreateFromTask(async () => {
			await ExportScanResults(indented: true);
		});

		public ReactiveCommand<Unit, Unit> ExportScanResultsToFileCommand => ReactiveCommand.CreateFromTask(async () => {
			await ExportScanResults();
		});

		public ReactiveCommand<Unit, Unit> ExportScanResultsToCsvCommand => ReactiveCommand.CreateFromTask(async () => {
			if (Duplicates.Count == 0) return;
			var path = await Utils.PickerDialogUtils.SaveFilePicker(new FilePickerSaveOptions() {
				DefaultExtension = ".csv",
				FileTypeChoices = [new FilePickerFileType("CSV") { Patterns = ["*.csv"] }]
			});
			if (string.IsNullOrEmpty(path)) return;
			try {
				var snapshot = Duplicates.ToList();
				var checkedPaths = snapshot.Where(i => i.Checked).Select(i => i.ItemInfo.Path)
					.ToHashSet(StringComparer.OrdinalIgnoreCase);
				await Task.Run(() => ResultsCsvExporter.ExportToFile(
					path,
					snapshot.Select(i => i.ItemInfo),
					checkedPaths,
					includeCheckedColumn: true));
			}
			catch (Exception ex) {
				string error = string.Format(App.Lang["Message.ExportScanResultsFailed"], ex);
				Logger.Instance.Info(error);
				await MessageBoxService.Show(error);
			}
		});

		static List<ScanResultEntry> ToScanResultEntries(IEnumerable<DuplicateItemVM> items) =>
			items.Select(vm => new ScanResultEntry {
				Item = vm.ItemInfo,
				Checked = vm.Checked,
				ThumbnailKey = vm.ThumbnailKey,
			}).ToList();

		async Task ExportScanResults(string? path = null, bool includeThumbnails = true, bool indented = false) {
			path ??= await Utils.PickerDialogUtils.SaveFilePicker(new FilePickerSaveOptions() {
				SuggestedStartLocation = await ApplicationHelpers.MainWindow.StorageProvider.TryGetFolderFromPathAsync(CoreUtils.CurrentFolder),
				DefaultExtension = includeThumbnails ? ".zip" : ".json",
				FileTypeChoices = [new FilePickerFileType("Scan Results") { Patterns = [includeThumbnails ? "*.zip" : ".scanresults"] }]
			});

			if (string.IsNullOrEmpty(path)) return;

			IsBusy = true;
			IsBusyOverlayText = App.Lang["Busy.SavingScanResults"];

			try {
				var entries = ToScanResultEntries(Duplicates);
				if (!includeThumbnails)
					await _resultsStore.SaveJsonAsync(path, entries, indented);
				else
					await _resultsStore.SaveZipAsync(path, entries, Utils.ThumbCacheHelpers.Provider);
			}
			catch (Exception ex) {
				string error = string.Format(App.Lang["Message.ExportScanResultsFailed"], ex);
				Logger.Instance.Info(error);
				await MessageBoxService.Show(error);
			}
			finally {
				IsBusy = false;
			}
		}

		public ReactiveCommand<Unit, Unit> ImportScanResultsFromFileCommand => ReactiveCommand.CreateFromTask(async () => {
			var result = await Utils.PickerDialogUtils.OpenFilePicker(new FilePickerOpenOptions {
				SuggestedStartLocation = await ApplicationHelpers.MainWindow.StorageProvider.TryGetFolderFromPathAsync(CoreUtils.CurrentFolder),
				FileTypeFilter = [new FilePickerFileType("Scan Results") { Patterns = ["*.zip"] }]
			});
			if (string.IsNullOrEmpty(result)) return;
			ImportScanResultsIncludingThumbnails(result);
		});

		async void ImportScanResultsIncludingThumbnails(string? path = null) {
			if (Duplicates.Count > 0) {
				MessageBoxButtons? result = await MessageBoxService.Show(App.Lang["Message.ImportScanResultsClearConfirm"], MessageBoxButtons.Yes | MessageBoxButtons.No);
				if (result != MessageBoxButtons.Yes) return;
			}

			if (path == null) {
				path = await Utils.PickerDialogUtils.OpenFilePicker(new FilePickerOpenOptions() {
					SuggestedStartLocation = await ApplicationHelpers.MainWindow.StorageProvider.TryGetFolderFromPathAsync(CoreUtils.CurrentFolder),
					FileTypeFilter = [new FilePickerFileType("Scan Results") { Patterns = ["*.zip", "*.scanresults", "*.json"] }]
				});
			}
			if (string.IsNullOrEmpty(path)) return;

			try {
				IsBusy = true;
				IsBusyOverlayText = App.Lang["Busy.ImportScanResults"];

				var loaded = await Task.Run(() => _resultsStore.LoadAsync(path));

				var entries = loaded.Items;
				var importBlacklistedGids = ComputeBlacklistedGroupIds(
					entries.Select(i => (i.Item.GroupId, i.Item.Path)));
				if (importBlacklistedGids.Count > 0) {
					int removed = entries.RemoveAll(i => importBlacklistedGids.Contains(i.Item.GroupId));
					if (removed > 0)
						Logger.Instance.Info($"Filtered out {removed} items in {importBlacklistedGids.Count} blacklisted groups during import");
				}

				if (loaded.ThumbnailPackFolder != null)
					TempDirectory = TempExtractionManager.Register(new TempDir(loaded.ThumbnailPackFolder, wrapExisting: true));
				else
					TempDirectory = TempExtractionManager.Register(new("VDF-"));

				Utils.ThumbCacheHelpers.SetActiveProvider(Scanner, TempDirectory.Path);

				Duplicates.Clear();
				foreach (var entry in entries) {
					var vm = new DuplicateItemVM(entry.Item) {
						Checked = entry.Checked,
						ThumbnailKey = entry.ThumbnailKey ?? string.Empty,
					};
					Duplicates.Add(vm);
				}

				BuildDuplicatesView();
				RefreshGroupStats();
				IsBusy = false;

				await PromptLoadMissingThumbnails();
			}
			catch (JsonException) {
				IsBusy = false;
				string error = App.Lang["Message.ImportScanResultsCorrupt"];
				Logger.Instance.Info(error);
				await MessageBoxService.Show(error);
			}
			catch (Exception ex) {
				IsBusy = false;
				string error = string.Format(App.Lang["Message.ImportScanResultsFailed"], ex);
				Logger.Instance.Info(error);
				await MessageBoxService.Show(error);
			}
		}

		/// <summary>
		/// A restored item is considered missing its preview when no usable thumbnail is cached
		/// for it — either it never got a key (thumbnails hadn't loaded before the backup was
		/// saved) or the pack has no non-empty entry for that key.
		/// </summary>
		static bool IsThumbnailMissing(DuplicateItemVM item) {
			if (string.IsNullOrEmpty(item.ThumbnailKey)) return true;
			var provider = Utils.ThumbCacheHelpers.Provider;
			if (provider == null) return true;
			return !provider.TryGetPackEntry(item.ThumbnailKey, out _, out var len) || len <= 0;
		}

		/// <summary>
		/// After restoring a saved scan that was interrupted before all thumbnails loaded, offer
		/// to generate the missing previews in one pass instead of leaving rows blank (issue #775).
		/// </summary>
		async Task PromptLoadMissingThumbnails() {
			var missing = Duplicates.Where(IsThumbnailMissing).ToList();
			if (missing.Count == 0) return;

			MessageBoxButtons? result = await MessageBoxService.Show(
				string.Format(App.Lang["Message.LoadMissingThumbnailsPrompt"], missing.Count),
				MessageBoxButtons.Yes | MessageBoxButtons.No);
			if (result != MessageBoxButtons.Yes) return;

			IsBusy = true;
			IsBusyOverlayText = App.Lang["Busy.LoadMissingThumbnails"];
			try {
				SyncCoreSettings();
				await Scanner.RetrieveThumbnailsForItems(missing.Select(d => d.ItemInfo));
			}
			finally {
				IsBusy = false;
			}
		}

		public ReactiveCommand<DuplicateItemVM, Unit> OpenItemCommand => ReactiveCommand.Create<DuplicateItemVM>(currentItem => {
			OpenItems();
		});

		public ReactiveCommand<Unit, Unit> OpenItemsByColIdCommand => ReactiveCommand.Create(() => {
			var tag = GetDataGrid.CurrentColumn?.Tag as string;
			if (tag == "Thumbnail")
				OpenItems();
			else if (tag == "Path")
				OpenItemsInFolder();
		});

		public ReactiveCommand<Unit, Unit> ThumbnailDoubleClickCommand => ReactiveCommand.Create(() => {
			if (SettingsFile.Instance.ThumbnailDoubleClickAction == Data.ThumbnailDoubleClickAction.OpenThumbnailComparer)
				ShowGroupInThumbnailComparerCommand.Execute().Subscribe();
			else
				OpenItems();
		});

		public Data.ThumbnailDoubleClickOption[] ThumbnailDoubleClickOptions { get; } = {
			new(App.Lang["MainWindow.Settings.ThumbnailDoubleClick.OpenFile"], Data.ThumbnailDoubleClickAction.OpenFile),
			new(App.Lang["MainWindow.Settings.ThumbnailDoubleClick.OpenThumbnailComparer"], Data.ThumbnailDoubleClickAction.OpenThumbnailComparer),
		};

		public ReactiveCommand<Unit, Unit> OpenItemInFolderCommand => ReactiveCommand.Create(OpenItemsInFolder);

		public ReactiveCommand<string, Unit> OpenGroupCommand => ReactiveCommand.Create<string>(openInFolder => {
			if (GetSelectedDuplicateItem() is DuplicateItemVM currentItem) {
				List<DuplicateItemVM> items = Duplicates.Where(d => d.ItemInfo.GroupId == currentItem.ItemInfo.GroupId).ToList();
				if (openInFolder == "0")
					AlternativeOpen(String.Empty, SettingsFile.Instance.CustomCommands.OpenMultiple, items);
				else
					AlternativeOpen(String.Empty, SettingsFile.Instance.CustomCommands.OpenMultipleInFolder, items);
			}
		});

		public async void OpenItems() {
			if (AlternativeOpen(SettingsFile.Instance.CustomCommands.OpenItem,
								SettingsFile.Instance.CustomCommands.OpenMultiple))
				return;

			if (GetSelectedDuplicateItem() is not DuplicateItemVM currentItem) return;
			try {
				if (CoreUtils.IsWindows) {
					Process.Start(new ProcessStartInfo {
						FileName = currentItem.ItemInfo.Path,
						UseShellExecute = true
					});
				}
				else {
					Process.Start(new ProcessStartInfo {
						FileName = currentItem.ItemInfo.Path,
						UseShellExecute = true,
						Verb = "open"
					});
				}
			}
			catch (Exception ex) {
				await MessageBoxService.Show(string.Format(App.Lang["Message.OpenFilesFailed"], ex.Message));
				return;
			}
		}

		public async void OpenItemsInFolder() {
			if (AlternativeOpen(SettingsFile.Instance.CustomCommands.OpenItemInFolder,
								SettingsFile.Instance.CustomCommands.OpenMultipleInFolder))
				return;

			if (GetSelectedDuplicateItem() is not DuplicateItemVM currentItem) return;
			try {
				if (OperatingSystem.IsWindows()) {
					try {
						Utils.ShellUtils.ShowInExplorer(currentItem.ItemInfo.Path);
					}
					catch {
						// Fallback to explorer.exe if shell API fails (Notepad++/Electron pattern)
						var psi = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
						psi.ArgumentList.Add($"/select,{currentItem.ItemInfo.Path}");
						Process.Start(psi);
					}
				}
				else if (OperatingSystem.IsMacOS()) {
					var psi = new ProcessStartInfo("open") { UseShellExecute = false };
					psi.ArgumentList.Add("-R");
					psi.ArgumentList.Add(currentItem.ItemInfo.Path);
					Process.Start(psi);
				}
				else {
					Process.Start(new ProcessStartInfo {
						FileName = currentItem.ItemInfo.Folder,
						UseShellExecute = true,
						Verb = "open"
					});
				}
			}
			catch (Exception ex) {
				await MessageBoxService.Show(string.Format(App.Lang["Message.OpenFilesFailed"], ex.Message));
				return;
			}
		}

		private bool AlternativeOpen(string cmdSingle, string cmdMulti, List<DuplicateItemVM>? items = null) {
			if (string.IsNullOrEmpty(cmdSingle) && string.IsNullOrEmpty(cmdMulti))
				return false;

			if (items == null) {
				items = new();
				if (!string.IsNullOrEmpty(cmdMulti)) {
					foreach (var selectedItem in GetSelectedDuplicates())
						if (selectedItem is DuplicateItemVM item)
							items.Add(item);
				}
				else {
					if (GetSelectedDuplicateItem() is DuplicateItemVM duplicateItem)
						items.Add(duplicateItem);
				}
			}

			string[]? cmd = null;
			string command = string.Empty;
			if (!string.IsNullOrEmpty(cmdSingle) && (string.IsNullOrEmpty(cmdMulti) || items.Count == 1))
				command = cmdSingle;
			else if (items.Count > 1)
				command = cmdMulti;
			if (!string.IsNullOrEmpty(command)) {
				if (command[0] == '"' || command[0] == '\'') {
					cmd = command.Split(command[0] + " ", 2);
					cmd[0] += command[0];
				}
				else
					cmd = command.Split(' ', 2);
			}
			if (string.IsNullOrEmpty(cmd?[0]))
				return false;

			command = cmd[0];
			var psi = new ProcessStartInfo {
				FileName = command,
				UseShellExecute = false,
				RedirectStandardError = true,
			};

			if (cmd.Length == 2 && cmd[1].Contains("%f")) {
				// When the user has a %f placeholder, substitute file paths into the argument template.
				// Each file gets its own copy of the template as a separate argument to avoid injection.
				foreach (var item in items) {
					psi.ArgumentList.Add(cmd[1].Replace("%f", item.ItemInfo.Path));
				}
			}
			else {
				if (cmd.Length == 2)
					psi.ArgumentList.Add(cmd[1]);
				foreach (var item in items)
					psi.ArgumentList.Add(item.ItemInfo.Path);
			}

			try {
				Process.Start(psi);
			}
			catch (Exception e) {
				Logger.Instance.Info(string.Format(App.Lang["Log.CustomCommandFailed"], command,
					string.Join(" ", psi.ArgumentList), e.Message));
			}

			return true;
		}

		public ReactiveCommand<Unit, Unit> RenameFileCommand => ReactiveCommand.CreateFromTask(async () => {
			if (GetSelectedDuplicateItem() is not DuplicateItemVM currentItem) return;
			if (!File.Exists(currentItem.ItemInfo.Path)) {
				await MessageBoxService.Show(App.Lang["Message.FileNoLongerExists"]);
				return;
			}
			var fi = new FileInfo(currentItem.ItemInfo.Path);
			Debug.Assert(fi.Directory != null, "fi.Directory != null");
			string newName = await InputBoxService.Show("Enter new name", Path.GetFileNameWithoutExtension(fi.FullName), title: "Rename File", readOnlyInfo: fi.FullName);
			if (string.IsNullOrEmpty(newName)) return;
			newName = FileUtils.SafePathCombine(fi.DirectoryName!, newName + fi.Extension);
			while (File.Exists(newName) && !string.Equals(fi.FullName, newName, StringComparison.OrdinalIgnoreCase)) {
				MessageBoxButtons? result = await MessageBoxService.Show($"A file with the name '{Path.GetFileName(newName)}' already exists. Do you want to overwrite this file? Click on 'No' to enter a new name", MessageBoxButtons.Yes | MessageBoxButtons.No | MessageBoxButtons.Cancel);
				if (result == null || result == MessageBoxButtons.Cancel)
					return;
				if (result == MessageBoxButtons.Yes)
					break;
				newName = await InputBoxService.Show("Enter new name", Path.GetFileNameWithoutExtension(newName), title: "Rename File", readOnlyInfo: fi.FullName);
				if (string.IsNullOrEmpty(newName))
					return;
				newName = FileUtils.SafePathCombine(fi.DirectoryName!, newName + fi.Extension);
			}
			try {
				ScanEngine.GetFromDatabase(currentItem.ItemInfo.Path, out var dbEntry);
				fi.MoveTo(newName, true);
				ScanEngine.UpdateFilePathInDatabase(newName, dbEntry!);
				currentItem.ItemInfo.Path = newName;
				ScanEngine.SaveDatabase();
			}
			catch (Exception e) {
				await MessageBoxService.Show(e.Message);
			}
		});

		public ReactiveCommand<Unit, Unit> ToggleCheckboxCommand => ReactiveCommand.Create(() => {
			using var _ = BeginSelectionUndoBatch();
			foreach (var item in GetSelectedDuplicates()) {
				if (item is not DuplicateItemVM currentItem) return;
				currentItem.Checked = !currentItem.Checked;
			}
		});

		static int MapToFfmpegMajor(int avcodecMajor, int avformatMajor, int avutilMajor) =>
			VDF.Core.Services.FFmpegSetupService.MapToFfmpegMajor(avcodecMajor, avformatMajor, avutilMajor);

		static string ArchString(Architecture a) => a switch {
			Architecture.X64 => "x64 (64-bit)",
			Architecture.X86 => "x86 (32-bit)",
			Architecture.Arm64 => "arm64 (64-bit ARM)",
			Architecture.Arm => "arm (32-bit ARM)",
			_ => a.ToString()
		};

		public static string GetRequiredFfmpegPackage(string? vdfFolderPath = null) {
			int avcodec = ffmpeg.LIBAVCODEC_VERSION_MAJOR;
			int avformat = ffmpeg.LIBAVFORMAT_VERSION_MAJOR;
			int avutil = ffmpeg.LIBAVUTIL_VERSION_MAJOR;

			int ffMajor = MapToFfmpegMajor(avcodec, avformat, avutil);

			string platform =
				OperatingSystem.IsWindows() ? "Windows" :
				OperatingSystem.IsMacOS() ? "macOS" :
				OperatingSystem.IsLinux() ? "Linux" : "Unknown";

			var osArch = RuntimeInformation.OSArchitecture;
			var procArch = RuntimeInformation.ProcessArchitecture;
			string versionPart = ffMajor == 0 ? "unknown (old headers?)" : $"{ffMajor}.x";
			string archPart = ArchString(procArch);

			var msg =
$@"FFmpeg was not found.

Which FFmpeg you need:
  • Version: FFmpeg {versionPart}
  • Architecture: {archPart}

Platform/OS:
  • {platform} · OS arch: {ArchString(osArch)} · Process arch: {ArchString(procArch)}

Notes:
  • On ARM64 systems (e.g., Windows/macOS ARM): if your app runs as x64 (emulation/Rosetta), use x64 FFmpeg.
    If it runs natively as ARM64, use ARM64 FFmpeg.";

			if (OperatingSystem.IsWindows()) {
				string target = string.IsNullOrWhiteSpace(vdfFolderPath)
					? @"<YourApp>\VDF\bin"
					: System.IO.Path.Combine(vdfFolderPath, "bin");

				msg +=
	$@"

Windows setup:
  1) Create a folder named 'bin' inside your VDF folder:
       {target}
  2) Download the FFmpeg {versionPart} shared build for {archPart}.
  3) Extract these DLLs into that 'bin' folder:
       avcodec-*.dll, avformat-*.dll, avutil-*.dll, swresample-*.dll, swscale-*.dll";
			}
			else {
				msg +=
	@"

Non-Windows setup:
  • Recommended: Check Github instructions
  • Use the shared libraries matching the required version/architecture.
  • Make sure the dynamic loader can find them (e.g., alongside your app binary,
    via rpath, or environment variables like LD_LIBRARY_PATH/DYLD_LIBRARY_PATH).
  • Typical library names:
      - Linux: libavcodec.so.*, libavformat.so.*, libavutil.so.*, libswresample.so.*, libswscale.so.*
      - macOS: libavcodec.*.dylib, libavformat.*.dylib, libavutil.*.dylib, libswresample.*.dylib, libswscale.*.dylib";
			}

			return msg;
		}

		/// <summary>
		/// Message for the specific case where 'Use native FFmpeg binding' is on and the
		/// FFmpeg/FFprobe executables were found, but the matching shared libraries were not.
		/// The native binding cannot use the executable, so "FFmpeg was not found" is wrong and
		/// confusing here — guide the user to either disable native binding or install the libs.
		/// </summary>
		public static string GetNativeLibrariesMissingMessage() {
			int ffMajor = MapToFfmpegMajor(ffmpeg.LIBAVCODEC_VERSION_MAJOR, ffmpeg.LIBAVFORMAT_VERSION_MAJOR, ffmpeg.LIBAVUTIL_VERSION_MAJOR);
			string versionPart = ffMajor == 0 ? "FFmpeg" : $"FFmpeg {ffMajor}.x";
			string archPart = ArchString(RuntimeInformation.ProcessArchitecture);

			// Library file names are platform-specific but not translatable (filenames).
			string libNames = OperatingSystem.IsWindows()
				? "avcodec-*.dll, avformat-*.dll, avutil-*.dll, swresample-*.dll, swscale-*.dll"
				: OperatingSystem.IsMacOS()
					? "libavcodec.*.dylib, libavformat.*.dylib, libavutil.*.dylib, libswresample.*.dylib, libswscale.*.dylib"
					: "libavcodec.so.*, libavformat.so.*, libavutil.so.*, libswresample.so.*, libswscale.so.*";

			return string.Format(App.Lang["Message.NativeFfmpegLibrariesMissing"], versionPart, archPart, libNames);
		}

		public ReactiveCommand<string, Unit> StartScanCommand => ReactiveCommand.CreateFromTask(async (string command) => {
			if (!string.IsNullOrEmpty(SettingsFile.Instance.CustomDatabaseFolder) && !Directory.Exists(SettingsFile.Instance.CustomDatabaseFolder)) {
				await MessageBoxService.Show(App.Lang["Message.CustomDatabaseFolderMissing"]);
				return;
			}
			if (Duplicates.Count > 0) {
				if (await MessageBoxService.Show(App.Lang["Message.DiscardResultsPrompt"], MessageBoxButtons.Yes | MessageBoxButtons.No) != MessageBoxButtons.Yes) {
					return;
				}
			}

			if ((SettingsFile.Instance.UseNativeFfmpegBinding && !ScanEngine.NativeFFmpegExists) ||
				(!SettingsFile.Instance.UseNativeFfmpegBinding && !ScanEngine.FFmpegExists) ||
				!ScanEngine.FFprobeExists) {
				await DownloadSharedFfmpegAsync();
			}
			// Native binding on, shared libraries still missing, but the ffmpeg/ffprobe
			// executables ARE present: don't claim "FFmpeg was not found" — point the user
			// at the actual distinction (native needs shared libs) and the one-click way out
			// (disable native binding to use the executable). See issue #788.
			if (SettingsFile.Instance.UseNativeFfmpegBinding && !ScanEngine.NativeFFmpegExists &&
				ScanEngine.FFmpegExists && ScanEngine.FFprobeExists) {
				await MessageBoxService.Show(GetNativeLibrariesMissingMessage());
				return;
			}
			if ((SettingsFile.Instance.UseNativeFfmpegBinding && !ScanEngine.NativeFFmpegExists) ||
				(!SettingsFile.Instance.UseNativeFfmpegBinding && !ScanEngine.FFmpegExists)) {
				await MessageBoxService.Show(GetRequiredFfmpegPackage(CoreUtils.CurrentFolder));
				return;
			}
			if (!ScanEngine.FFprobeExists) {
				if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
					await MessageBoxService.Show(App.Lang["Message.FFprobeMissingWithHint"]);
				}
				else {
					await MessageBoxService.Show(App.Lang["Message.FFprobeMissing"]);
				}
				return;
			}
			if (SettingsFile.Instance.UseNativeFfmpegBinding && SettingsFile.Instance.HardwareAccelerationMode == Core.FFTools.FFHardwareAccelerationMode.auto) {
				await MessageBoxService.Show(App.Lang["Message.NativeFfmpegAutoNotSupported"]);
				return;
			}
			if (SettingsFile.Instance.Includes.Count == 0) {
				await MessageBoxService.Show(App.Lang["Message.NoScanFolders"]);
				return;
			}
			if (SettingsFile.Instance.MaxDegreeOfParallelism == 0) {
				await MessageBoxService.Show(App.Lang["Message.MaxDegreeOfParallelismInvalid"]);
				return;
			}
			if (SettingsFile.Instance.FilterByFileSize && SettingsFile.Instance.MaximumFileSize <= SettingsFile.Instance.MinimumFileSize) {
				await MessageBoxService.Show(App.Lang["Message.FileSizeFilterInvalid"]);
				return;
			}
			bool isFreshScan = true;
			switch (command) {
			case "FullScan":
				isFreshScan = true;
				break;
			case "CompareOnly":
				isFreshScan = false;
				if (await MessageBoxService.Show(App.Lang["Message.RescanConfirm"], MessageBoxButtons.Yes | MessageBoxButtons.No) != MessageBoxButtons.Yes)
					return;
				break;
			default:
				await MessageBoxService.Show(App.Lang["Message.CommandNotImplemented"]);
				break;
			}

			Duplicates.Clear();

			TempDirectory = TempExtractionManager.Register(new("VDF-"));
			Utils.ThumbCacheHelpers.SetActiveProvider(Scanner, TempDirectory.Path);

			IsScanning = true;
			IsReadyToCompare = false;
			IsGathered = false;
			TotalDuplicateGroups = 0;
			TotalDuplicates = 0;
			TotalDuplicatesSize = string.Empty;

			SettingsFile.SaveSettings();
			SyncCoreSettings();

			ChangeIsBusyMessage();
			IsBusy = true;

			if (isFreshScan) {
				_ = RunSearchViaOrchestrator();
			}
			else {
				_ = RunCompareViaOrchestrator();
			}
		}, CanStartScan);

		/// <summary>
		/// Drives StartSearch() through the orchestrator so that state transitions,
		/// cancellation, and progress throttling are handled uniformly across GUI/CLI/Web.
		/// Exceptions are caught and logged by the orchestrator (State → Error) which
		/// fires <see cref="Orchestrator_Completed"/>.
		/// </summary>
		async Task RunSearchViaOrchestrator() {
			try {
				await _orchestrator.StartAsync(Scanner.Settings);
			}
			catch (Exception ex) {
				Logger.Instance.Info($"Unhandled error in StartSearch: {ex}");
			}
		}

		/// <summary>
		/// Drives StartCompare() through the orchestrator (compare-only, assumes the
		/// database was populated by a prior scan).
		/// </summary>
		async Task RunCompareViaOrchestrator() {
			try {
				await _orchestrator.CompareAsync();
			}
			catch (Exception ex) {
				Logger.Instance.Info($"Unhandled error in StartCompare: {ex}");
			}
		}

		IObservable<bool> CanStartScan {
			[System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026", Justification = WhenAnyValueTrimJustification)]
			get => this.WhenAnyValue(x => x.IsBusy, busy => !busy);
		}

		/// <summary>
		/// Syncs the GUI settings into the Core engine. With the composition-based
		/// SettingsFile, the Core object IS the engine's settings — we just assign
		/// the reference and sync the UI-only ObservableCollections (Includes /
		/// Blacklists / FilePathContainsTexts / FilePathNotContainsTexts) that can't
		/// be forwarded directly due to their collection type difference.
		/// Must run before any engine operation that reads
		/// <see cref="ScanEngine.Settings"/> — not just scans: explicit thumbnail
		/// loading after a backup restore otherwise runs with Core defaults (e.g.
		/// ThumbnailMaxWidth 100 instead of the configured value), producing
		/// pixelated thumbnails (issue #778).
		/// </summary>
		void SyncCoreSettings() {
			var sf = SettingsFile.Instance;
			// The nested Core object is the single source of truth — assign it
			// directly so new Core fields need zero sync code here.
			Scanner.Settings = sf.Core;

			// UI-only ObservableCollections that can't be forwarded (type mismatch
			// with Core's HashSet/List). Sync them explicitly.
			Scanner.Settings.IncludeList.Clear();
			foreach (var s in sf.Includes)
				Scanner.Settings.IncludeList.Add(s);
			Scanner.Settings.BlackList.Clear();
			foreach (var s in sf.Blacklists)
				Scanner.Settings.BlackList.Add(s);
			Scanner.Settings.FilePathContainsTexts = sf.FilePathContainsTexts.ToList();
			Scanner.Settings.FilePathNotContainsTexts = sf.FilePathNotContainsTexts.ToList();

			// Keep LanguageCode in sync with the app's current language.
			sf.LanguageCode = App.Lang.CurrentLanguage;

			// Apply the FFmpeg engine statics as well — PrepareSearch does this at scan
			// start, but thumbnail-only flows never reach PrepareSearch.
			VDF.Core.FFTools.FfmpegEngine.HardwareAccelerationMode = Scanner.Settings.HardwareAccelerationMode;
			VDF.Core.FFTools.FfmpegEngine.CustomFFArguments = Scanner.Settings.CustomFFArguments;
			VDF.Core.FFTools.FfmpegEngine.UseNativeBinding = Scanner.Settings.UseNativeFfmpegBinding;
		}

		public ReactiveCommand<Unit, Unit> PauseScanCommand => ReactiveCommand.Create(() => {
			_orchestrator.PauseAsync();
			IsPaused = true;
		}, CanPauseScan);

		IObservable<bool> CanPauseScan {
			[System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026", Justification = WhenAnyValueTrimJustification)]
			get => this.WhenAnyValue(x => x.IsScanning, x => x.IsPaused, (a, b) => a && !b);
		}

		public ReactiveCommand<Unit, Unit> ResumeScanCommand => ReactiveCommand.Create(() => {
			IsPaused = false;
			_orchestrator.ResumeAsync();
		}, CanResumeScan);

		IObservable<bool> CanResumeScan {
			[System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026", Justification = WhenAnyValueTrimJustification)]
			get => this.WhenAnyValue(x => x.IsScanning, x => x.IsPaused, (a, b) => a && b);
		}

		public ReactiveCommand<Unit, Unit> StopScanCommand => ReactiveCommand.Create(() => {
			IsPaused = false;
			IsBusy = true;
			IsBusyOverlayText = "Stopping all scan threads...";
			_ = _orchestrator.CancelAsync();
		}, CanStopScan);

		IObservable<bool> CanStopScan {
			[System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026", Justification = WhenAnyValueTrimJustification)]
			get => this.WhenAnyValue(x => x.IsScanning);
		}

		public ReactiveCommand<Unit, Unit> MarkGroupAsNotAMatchCommand => ReactiveCommand.CreateFromTask(async () => {
			try {
				if (GetSelectedDuplicateItem() is not DuplicateItemVM data) return;

				var gid = data.ItemInfo.GroupId;

				HashSet<string> blacklist = new HashSet<string>(PathComparer.ForCurrentPlatform);
				foreach (DuplicateItemVM duplicateItem in Duplicates.Where(d => d.ItemInfo.GroupId == gid))
					blacklist.Add(duplicateItem.ItemInfo.Path);
				GroupBlacklist.Add(blacklist);
				try {
					await BlacklistStore.SaveAsync(BlacklistedGroupsFile, GroupBlacklist);
				}
				catch (Exception e) {
					GroupBlacklist.Remove(blacklist);
					await MessageBoxService.Show(e.Message);
					return;
				}

				// Remove all items in this group from the list
				foreach (var path in blacklist.ToList())
					for (int i = Duplicates.Count - 1; i >= 0; i--)
						if (Duplicates[i].ItemInfo.GroupId == gid && Duplicates[i].ItemInfo.Path == path)
							Duplicates.RemoveAt(i);

				// Drop singleton groups
				DropSingletonGroups();
				RefreshGroupStats();
				view?.Refresh();

				// Mirror the deletion path: keep backup.scanresults in sync so the mark
				// survives a crash before the user gets to a clean exit.
				if (SettingsFile.Instance.BackupAfterListChanged)
					await ExportScanResults(BackupScanResultsFile);
			}
			catch (Exception ex) {
				Logger.Instance.Info($"MarkGroupAsNotAMatch failed: {ex}");
			}
		});

		private HashSet<Guid> ComputeBlacklistedGroupIds(IEnumerable<(Guid GroupId, string Path)> items) =>
			GroupBlacklistFilter.ComputeBlacklistedGroupIds(items, GroupBlacklist);

		public ReactiveCommand<Unit, Unit> OpenBlacklistManagerCommand => ReactiveCommand.Create(() => {
			var vm = new BlacklistManagerVM(GroupBlacklist,
				() => BlacklistStore.SaveAsync(BlacklistedGroupsFile, GroupBlacklist));
			new BlacklistManagerView(vm).Show();
		});

		public ReactiveCommand<Unit, Unit> ShowGroupInThumbnailComparerCommand => ReactiveCommand.Create(() => {
			if (GetSelectedDuplicateItem() is not DuplicateItemVM data) return;
			List<LargeThumbnailDuplicateItem> items = new();
			Guid? groupId = null;

			if (GetSelectedDuplicates().Count == 1) {
				foreach (DuplicateItemVM duplicateItem in Duplicates.Where(d => d.ItemInfo.GroupId == data.ItemInfo.GroupId))
					items.Add(new LargeThumbnailDuplicateItem(duplicateItem));
				groupId = data.ItemInfo.GroupId;
			}
			else {
				foreach (DuplicateItemVM duplicateItem in GetSelectedDuplicates())
					items.Add(new LargeThumbnailDuplicateItem(duplicateItem));
			}

			ThumbnailComparer thumbnailComparer = new(items, groupId, NavigateGroupForComparer);
			thumbnailComparer.Show();
		});

		public void CompareGroup(Guid groupId) {
			var items = Duplicates
				.Where(d => d.ItemInfo.GroupId == groupId)
				.Select(d => new LargeThumbnailDuplicateItem(d))
				.ToList();
			if (items.Count == 0) return;
			new ThumbnailComparer(items, groupId, NavigateGroupForComparer).Show();
		}

		public void KeepBestInGroup(Guid groupId) {
			var groupItems = Duplicates.Where(d => d.ItemInfo.GroupId == groupId).ToList();
			if (groupItems.Count < 2) return;

			var keep = VDF.Core.Utils.QualityRanker.PickKeeper(
				groupItems,
				ResolveCriteria(QualityCriteriaOrder),
				d => d.ItemInfo.IsImage);

			using var _ = BeginSelectionUndoBatch();
			keep.Checked = false;
			foreach (var item in groupItems)
				if (item.ItemInfo.Path != keep.ItemInfo.Path)
					item.Checked = true;
		}

	public ReactiveCommand<Unit, Unit> LoadThumbnailsForCheckedItemsCommand => ReactiveCommand.CreateFromTask(async () => {
		var items = Duplicates.Where(d => d.Checked).Select(vm => vm.ItemInfo).ToList();
		if (items.Count == 0) return;
		SyncCoreSettings();
		await Scanner.RetrieveThumbnailsForItems(items);
	});

	public ReactiveCommand<Unit, Unit> LoadThumbnailsForGroupCommand => ReactiveCommand.CreateFromTask(async () => {
		if (GetSelectedDuplicateItem() is not DuplicateItemVM currentItem) return;
		var items = Duplicates.Where(d => d.ItemInfo.GroupId == currentItem.ItemInfo.GroupId).Select(d => d.ItemInfo).ToList();
		SyncCoreSettings();
		await Scanner.RetrieveThumbnailsForItems(items);
	});

		List<DuplicateItemVM> CheckedItemsToDelete => ScopedDuplicates().Where(d => d.Checked && d.IsVisibleInFilter).ToList();

		async void DeleteInternal(bool fromDisk,
									List<DuplicateItemVM>? toDelete = null,
									bool blackList = false,
									bool createSymbolLinksInstead = false,
									bool permanently = false,
									bool createHardLinksInstead = false) {
			if (Duplicates.Count == 0) return;
			toDelete ??= CheckedItemsToDelete;
			if (toDelete.Count == 0) return;
			bool createLinks = createSymbolLinksInstead || createHardLinksInstead;

			long totalSizeToDelete = 0;
			foreach (var item in toDelete)
				totalSizeToDelete += CheckedSizeOf(item);

			string confirmMessage = createLinks
					? App.Lang["Message.ReplaceWithLinksConfirm"]
					: fromDisk
					? (!permanently ? App.Lang["Message.DeleteToTrashConfirm"] : App.Lang["Message.DeletePermanentlyConfirm"])
					: (blackList ? App.Lang["Message.DeleteFromListBlacklistConfirm"] : App.Lang["Message.DeleteFromListConfirm"]);
			confirmMessage += Environment.NewLine + Environment.NewLine +
				string.Format(App.Lang["Message.DeleteConfirmStats"], toDelete.Count, totalSizeToDelete.BytesToString());

			MessageBoxButtons? dlgResult = await MessageBoxService.Show(confirmMessage,
				MessageBoxButtons.Yes | MessageBoxButtons.No);
			if (dlgResult != MessageBoxButtons.Yes) return;

			var keepByGroup = Duplicates
				   .GroupBy(d => d.ItemInfo.GroupId)
				   .ToDictionary(
					   g => g.Key,
					   g => g.FirstOrDefault(x => !x.Checked)
				   );

			var actuallyDeleted = new HashSet<DuplicateItemVM>(toDelete.Count, ReferenceEqualityComparer<DuplicateItemVM>.Instance);
			long freedBytes = 0;
			int total = toDelete.Count;
			IsBusy = true;
			IsBusyOverlayText = string.Format(App.Lang["Busy.Deleting"], 0, total);

			// For blacklist mode the service must not touch the database (GUI calls
			// BlackListFileEntry itself), so use a null-engine instance. For every
			// other mode the Scanner-attached _fileOps handles RemoveFromDatabase /
			// UpdateFilePathInDatabase / SaveDatabase and Scanner.Duplicates cleanup.
			FileOperationsService svc = blackList ? new FileOperationsService(null) : _fileOps;
			FileOperationResult coreResult = new();

			try {
				// File I/O and database updates run off the UI thread; thousands of
				// deletions previously froze the window for their full duration.
				await Task.Run(async () => {
					var progressTimer = Stopwatch.StartNew();
					IProgress<int> progress = new SyncProgress(count => {
						if (progressTimer.ElapsedMilliseconds < 300) return;
						progressTimer.Restart();
						Dispatcher.UIThread.Post(() =>
							IsBusyOverlayText = string.Format(App.Lang["Busy.Deleting"], count, total));
					});

					if (createLinks) {
						// Build (target, linkPath) tuples; pre-fail items whose keeper
						// is missing. Items whose file is already gone are recorded as
						// succeeded so their list/DB entries are still cleaned up.
						var links = new List<(string target, string linkPath)>();
						foreach (var dub in toDelete) {
							if (!File.Exists(dub.ItemInfo.Path)) {
								Logger.Instance.Info($"'{dub.ItemInfo.Path}' no longer exists on disk; removing entry only.");
								coreResult.SucceededPaths.Add(dub.ItemInfo.Path);
								coreResult.Done++;
								continue;
							}
							var keeper = keepByGroup.TryGetValue(dub.ItemInfo.GroupId, out var k) ? k : null;
							if (keeper == null) {
								coreResult.Errors.Add($"{Path.GetFileName(dub.ItemInfo.Path)}: Cannot create a link because all items in this group are selected");
								coreResult.Failed++;
								continue;
							}
							if (!File.Exists(keeper.ItemInfo.Path)) {
								coreResult.Errors.Add($"{Path.GetFileName(dub.ItemInfo.Path)}: Cannot create a link because the file to keep ('{keeper.ItemInfo.Path}') does not exist");
								coreResult.Failed++;
								continue;
							}
							links.Add((keeper.ItemInfo.Path, dub.ItemInfo.Path));
						}
						if (links.Count > 0) {
							var linkResult = createHardLinksInstead
								? await svc.CreateHardLinksAsync(links, CancellationToken.None, progress)
								: await svc.CreateSymbolicLinksAsync(links, CancellationToken.None, progress);
							coreResult.Done += linkResult.Done;
							coreResult.Failed += linkResult.Failed;
							coreResult.Errors.AddRange(linkResult.Errors);
							coreResult.Warnings.AddRange(linkResult.Warnings);
							coreResult.SucceededPaths.AddRange(linkResult.SucceededPaths);
						}
					}
					else if (fromDisk) {
						// DeleteAsync handles Windows batched SHFileOperation recycle,
						// Linux/macOS trash with permanent-delete fallback, and the
						// "file already gone" case. With the Scanner engine attached it
						// also calls RemoveFromDatabase + SaveDatabase.
						var delResult = await svc.DeleteAsync(
							toDelete.Select(d => d.ItemInfo.Path),
							!permanently,
							CancellationToken.None,
							progress);
						coreResult = delResult;
					}
					else {
						// fromDisk=false, no links: no file I/O. Every item "succeeds"
						// and is removed from the list/database below.
						foreach (var dub in toDelete) {
							coreResult.SucceededPaths.Add(dub.ItemInfo.Path);
							coreResult.Done++;
						}
					}
				});
			}
			finally {
				IsBusy = false;
			}

			// Map succeeded paths back to VMs. For blacklist mode and list-only
			// removal the service didn't touch the database, so GUI does it here.
			var succeeded = coreResult.SucceededPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
			foreach (var dub in toDelete) {
				if (!succeeded.Contains(dub.ItemInfo.Path)) continue;
				actuallyDeleted.Add(dub);
				if (createLinks || fromDisk)
					freedBytes += CheckedSizeOf(dub);
				if (blackList)
					ScanEngine.BlackListFileEntry(dub.ItemInfo.Path);
				else if (!fromDisk && !createLinks)
					ScanEngine.RemoveFromDatabase(new FileEntry { Path = dub.ItemInfo.Path });
			}

			if (freedBytes > 0)
				TotalSizeRemovedInternal += freedBytes;

			int failedCount = coreResult.Failed;
			if (failedCount > 0)
				await MessageBoxService.Show(string.Format(App.Lang["Message.DeleteCompletedWithFailures"],
					coreResult.Done, toDelete.Count, failedCount));

			if (actuallyDeleted.Count == 0)
				return;

			// Remove deleted items from flat list
			foreach (var item in actuallyDeleted) {
				for (int i = Duplicates.Count - 1; i >= 0; i--)
					if (ReferenceEquals(Duplicates[i], item)) { Duplicates.RemoveAt(i); break; }
			}

			// When ExcludeHardLinks is enabled, remove items within each group
			// that are hardlinks of another remaining item in the same group.
			if (SettingsFile.Instance.ExcludeHardLinks)
				DropHardLinkDuplicates();

			// Drop groups that have only one item left (no longer duplicates)
			DropSingletonGroups();

			RefreshGroupStats();
			view?.Refresh();

			// The service already saved the database for non-blacklist file/link
			// ops (Scanner engine attached). For blacklist mode and list-only
			// removal, save here.
			if (blackList || (!fromDisk && !createLinks))
				ScanEngine.SaveDatabase();

			if (SettingsFile.Instance.BackupAfterListChanged)
				await ExportScanResults(BackupScanResultsFile);
		}

		/// <summary>
		/// Synchronous <see cref="IProgress{T}"/> — invokes the callback directly
		/// on the reporting thread (the Core service's worker), matching the
		/// pre-refactor behavior where the progress counter was incremented inline.
		/// </summary>
		sealed class SyncProgress : IProgress<int> {
			readonly Action<int> _action;
			public SyncProgress(Action<int> action) => _action = action;
			public void Report(int value) => _action(value);
		}

		private void DropSingletonGroups() {
			var singletonGroups = Duplicates
				.GroupBy(d => d.ItemInfo.GroupId)
				.Where(g => g.Count() <= 1)
				.Select(g => g.Key)
				.ToHashSet();

			if (singletonGroups.Count == 0) return;

			for (int i = Duplicates.Count - 1; i >= 0; i--)
				if (singletonGroups.Contains(Duplicates[i].ItemInfo.GroupId))
					Duplicates.RemoveAt(i);
		}

		/// <summary>
		/// After deletion, re-check remaining groups for items that are hardlinks
		/// of each other. Within each group, keep only one representative per set
		/// of hardlinked files.
		/// </summary>
		private void DropHardLinkDuplicates() {
			var toRemove = new List<DuplicateItemVM>();
			foreach (var group in Duplicates.GroupBy(d => d.ItemInfo.GroupId)) {
				var items = group.ToList();
				if (items.Count <= 1) continue;
				var kept = new List<DuplicateItemVM>();
				foreach (var item in items) {
					bool isHardLinkOfKept = false;
					foreach (var k in kept) {
						if (item.ItemInfo.SizeLong == k.ItemInfo.SizeLong &&
							HardLinkUtils.AreSameFile(item.ItemInfo.Path, k.ItemInfo.Path)) {
							isHardLinkOfKept = true;
							break;
						}
					}
					if (isHardLinkOfKept)
						toRemove.Add(item);
					else
						kept.Add(item);
				}
			}

			foreach (var item in toRemove)
				for (int i = Duplicates.Count - 1; i >= 0; i--)
					if (ReferenceEquals(Duplicates[i], item)) { Duplicates.RemoveAt(i); break; }
		}

		public ReactiveCommand<Unit, Unit> ExpandAllGroupsCommand => ReactiveCommand.Create(() => {
			if (view == null) return;
			foreach (var group in view.Groups ?? Enumerable.Empty<object>())
				if (group is DataGridCollectionViewGroup g)
					GetDataGrid.ExpandRowGroup(g, true);
		});

		public ReactiveCommand<Unit, Unit> CollapseAllGroupsCommand => ReactiveCommand.Create(() => {
			if (view == null) return;
			foreach (var group in view.Groups ?? Enumerable.Empty<object>())
				if (group is DataGridCollectionViewGroup g)
					GetDataGrid.CollapseRowGroup(g, true);
		});

		public ReactiveCommand<Unit, Unit> NavigateNextGroupCommand => ReactiveCommand.Create(() => {
			NavigateGroup(forward: true);
		});

		// Keyboard triage (Alt+K): keep the highlighted item — check everything
		// else in its group — then advance to the next group.
		public ReactiveCommand<Unit, Unit> KeepHighlightedAndAdvanceCommand => ReactiveCommand.Create(() => {
			if (GetSelectedDuplicateItem() is DuplicateItemVM keeper) {
				using var _ = BeginSelectionUndoBatch();
				keeper.Checked = false;
				foreach (var item in Duplicates)
					if (item.ItemInfo.GroupId == keeper.ItemInfo.GroupId && !ReferenceEquals(item, keeper))
						item.Checked = true;
			}
			NavigateGroup(forward: true);
		});

		public ReactiveCommand<Unit, Unit> NavigatePreviousGroupCommand => ReactiveCommand.Create(() => {
			NavigateGroup(forward: false);
		});

		Guid? NavigateGroup(bool forward, Guid? fromGroupId = null) {
			if (view?.Groups == null) return null;
			var groups = view.Groups.OfType<DataGridCollectionViewGroup>().ToList();
			if (groups.Count == 0) return null;

			var dataGrid = GetDataGrid;
			Guid? referenceGroupId = fromGroupId
				?? (dataGrid.SelectedItem as DuplicateItemVM)?.ItemInfo.GroupId;
			int currentGroupIndex = -1;

			if (referenceGroupId.HasValue) {
				for (int i = 0; i < groups.Count; i++) {
					if (groups[i].Items.OfType<DuplicateItemVM>()
						.Any(item => item.ItemInfo.GroupId == referenceGroupId.Value)) {
						currentGroupIndex = i;
						break;
					}
				}
			}

			int targetIndex = forward
				? (currentGroupIndex + 1 < groups.Count ? currentGroupIndex + 1 : 0)
				: (currentGroupIndex - 1 >= 0 ? currentGroupIndex - 1 : groups.Count - 1);

			var targetGroup = groups[targetIndex];
			dataGrid.ExpandRowGroup(targetGroup, true);
			var firstItem = targetGroup.Items.OfType<DuplicateItemVM>().FirstOrDefault();
			if (firstItem != null) {
				dataGrid.SelectedItem = firstItem;
				dataGrid.ScrollIntoView(firstItem, null);
				return firstItem.ItemInfo.GroupId;
			}
			return null;
		}

		// Used by ThumbnailComparer to walk to the sibling group without closing the dialog.
		// Also moves the main grid's selection so state stays consistent when the dialog closes.
		internal (Guid GroupId, List<LargeThumbnailDuplicateItem> Items)? NavigateGroupForComparer(Guid currentGroupId, bool forward) {
			var newGroupId = NavigateGroup(forward, currentGroupId);
			if (newGroupId is null) return null;
			var items = Duplicates
				.Where(d => d.ItemInfo.GroupId == newGroupId.Value)
				.Select(d => new LargeThumbnailDuplicateItem(d))
				.ToList();
			if (items.Count == 0) return null;
			return (newGroupId.Value, items);
		}

		public ReactiveCommand<Unit, Unit> CopyPathsToClipboardCommand => ReactiveCommand.CreateFromTask(async () => {
			StringBuilder sb = new();
			foreach (var item in GetSelectedDuplicates()) {
				if (item is not DuplicateItemVM currentItem) return;
				sb.AppendLine($"\"{currentItem.ItemInfo.Path}\"");
			}
			if (ApplicationHelpers.MainWindow.Clipboard is { } clipboard)
				await clipboard.SetTextAsync(sb.ToString().TrimEnd(new char[2] { '\r', '\n' }));
		});

		public ReactiveCommand<Unit, Unit> CopyFilenamesToClipboardCommand => ReactiveCommand.CreateFromTask(async () => {
			StringBuilder sb = new();
			foreach (var item in GetSelectedDuplicates()) {
				if (item is not DuplicateItemVM currentItem) return;
				sb.AppendLine(Path.GetFileName(currentItem.ItemInfo.Path));
			}
			if (ApplicationHelpers.MainWindow.Clipboard is { } clipboard)
				await clipboard.SetTextAsync(sb.ToString().TrimEnd(new char[2] { '\r', '\n' }));
		});

		public ReactiveCommand<Unit, Unit> RelocateDatabaseFilesCommand => ReactiveCommand.Create(() => {
			var dlg = new RelocateFilesDialog();
			dlg.ShowDialog(ApplicationHelpers.MainWindow);
		});
	}
}
