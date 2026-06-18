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

using Microsoft.AspNetCore.SignalR;
using VDF.Core;
using VDF.Core.Services;
using VDF.Core.Utils;
using VDF.Core.ViewModels;
using VDF.Web.Hubs;
using VDF.Web.Models;

namespace VDF.Web.Services {

	/// <summary>Outcome of a batch file operation (delete / move / link).</summary>
	public sealed class FileOpResult {
		public int Done;
		public int Failed;
		public long FreedBytes;
		public List<string> Errors { get; } = new();
		public List<string> Warnings { get; } = new();
	}

	/// <summary>
	/// Singleton service that owns the <see cref="ScanEngine"/> instance (via
	/// <see cref="ScanOrchestrator"/>) and exposes scan lifecycle operations to
	/// Blazor components and REST/SSE endpoints via events and state.
	/// </summary>
	public sealed class ScanService : IDisposable {
		readonly ScanEngine _engine = new();
		readonly ScanOrchestrator _orchestrator;
		readonly FileOperationsService _fileOps;
		readonly ResultsStore _resultsStore = new();
		readonly WebSettingsService _settingsService;
		readonly IHubContext<ScanHub>? _hubContext;
		CancellationTokenSource _cts = new();

		/// <summary>Current scan state, delegated to the orchestrator.</summary>
		public ScanState State => _orchestrator.State;
		/// <summary>Last throttled progress payload from the orchestrator.</summary>
		public ScanProgressArgs? LastProgress => _orchestrator.LastProgress;
		/// <summary>Error message when <see cref="State"/> is <see cref="ScanState.Error"/>.</summary>
		public string? ErrorMessage => _orchestrator.ErrorMessage;
		/// <summary>Total files hashed (captured when BuildingHashesDone fires).</summary>
		public int FilesHashed => _orchestrator.FilesHashed;
		public IReadOnlyCollection<DuplicateItem> Duplicates => _engine.Duplicates;
		/// <summary>Gets or sets the scan engine's settings.  The setter allows the
		/// settings endpoints to replace the entire object after a PUT.</summary>
		public Settings Settings {
			get => _engine.Settings;
			set {
				_engine.Settings = value;
				ThumbnailService.SetAllowedRoots(value.IncludeList.ToList());
			}
		}

		/// <summary>
		/// Two-level thumbnail cache (in-memory LRU + persistent pack). Replaces the previous
		/// HqThumbCache/FullThumbCache pair. Web stores the pack in its data dir so thumbnails
		/// survive restarts.
		/// </summary>
		public ThumbnailService ThumbnailService { get; }

		void ClearThumbnailCaches() {
			ThumbnailService.ClearMemoryCache();
		}

		public event Action? StateChanged;

		public ScanService(WebSettingsService settingsService, IHubContext<ScanHub>? hubContext = null) {
			_settingsService = settingsService;
			_hubContext = hubContext;
			_orchestrator = new ScanOrchestrator(_engine);
			_fileOps = new FileOperationsService(_engine);
			// Load returns a fully-populated, validated Core Settings instance —
			// assign it directly so new Core fields need zero sync code.
			var loaded = settingsService.Load();
			if (loaded != null)
				_engine.Settings = loaded;

			// Two-level thumbnail cache: in-memory LRU + persistent pack in the data dir.
			// The pack survives restarts so Web users don't lose all thumbnails on restart.
			var thumbPackFolder = Path.Combine(CoreUtils.StateFolder, "thumbnails");
			ThumbnailService = new ThumbnailService(_engine, new ThumbnailServiceOptions {
				PackFolder = thumbPackFolder,
				AllowedRoots = _engine.Settings.IncludeList.ToList(),
			});

			// The orchestrator handles state transitions, progress throttling, and
			// completion. ScanService just forwards them to SignalR/SSE subscribers.
			_orchestrator.StateChanged += (_, _) => Notify();
			_orchestrator.ProgressChanged += (_, _) => Notify();
			_orchestrator.Completed += OnOrchestratorCompleted;
			_engine.FilesEnumerated += (_, _) => Notify();

			TryRestoreResults();
		}

		void OnOrchestratorCompleted(object? sender, ScanCompletedEventArgs e) {
			Notify();
			if (e.State == ScanState.Done)
				_ = PersistResultsAsync();
		}

		void TryRestoreResults() {
			try {
				var path = ResultsStore.DefaultStateBackupPath();
				if (!File.Exists(path)) return;
				var loaded = _resultsStore.LoadAsync(path).GetAwaiter().GetResult();
				_engine.Duplicates.Clear();
				foreach (var entry in loaded.Items)
					_engine.Duplicates.Add(entry.Item);
			}
			catch {
				// Corrupt backup — start fresh; user can re-scan.
			}
		}

		async Task PersistResultsAsync() {
			if (_engine.Duplicates.Count == 0) return;
			try {
				var entries = _engine.Duplicates.Select(d => new ScanResultEntry { Item = d }).ToList();
				await _resultsStore.SaveJsonAsync(ResultsStore.DefaultStateBackupPath(), entries);
			}
			catch {
				// Best-effort persistence; scan results remain in memory.
			}
		}

		public void StartScanAndCompare() {
			if (State == ScanState.Scanning || State == ScanState.Comparing) return;
			_cts?.Cancel();
			_cts?.Dispose();
			_cts = new CancellationTokenSource();
			ClearThumbnailCaches();
			// Fire-and-forget: the orchestrator drives the scan to completion and
			// fires StateChanged/ProgressChanged/Completed events that Notify()
			// forwards to SignalR/SSE subscribers.
			_ = RunScanViaOrchestrator(_cts.Token);
		}

		/// <summary>
		/// Drives the scan through <see cref="ScanOrchestrator.StartAsync"/>. The
		/// orchestrator handles state transitions, cancellation, progress throttling,
		/// and error handling — replacing the previous RunSearchWithAsyncErrorHandling.
		/// </summary>
		async Task RunScanViaOrchestrator(CancellationToken ct) {
			try {
				await _orchestrator.StartAsync(_engine.Settings, ct);
			}
			catch (Exception ex) {
				// Orchestrator.SetError is normally called internally, but guard
				// against exceptions thrown before the orchestrator's catch block.
				_orchestrator.SetError(ex);
				Notify();
			}
		}

		/// <summary>Called from global exception handlers to surface post-await async void exceptions.</summary>
		public void SetError(Exception ex) => _orchestrator.SetError(ex);

		public void Pause() => _orchestrator.PauseAsync();
		public void Resume() => _orchestrator.ResumeAsync();

		public void Stop() {
			_cts.Cancel();
			_ = _orchestrator.CancelAsync();
		}

		public bool SaveSettings() => _settingsService.Save(_engine.Settings);

		public void Reset() {
			if (State == ScanState.Scanning || State == ScanState.Comparing) return;
			_orchestrator.Reset();
			ClearThumbnailCaches();
			TryDeleteBackup();
			// Keep IncludeList/BlackList — resetting scan results should not
			// throw away the paths the user configured.
			Notify();
		}

		void TryDeleteBackup() {
			try {
				var path = ResultsStore.DefaultStateBackupPath();
				if (File.Exists(path)) File.Delete(path);
			}
			catch { /* ignore */ }
		}

		/// <summary>Removes items from the results list without touching the files on disk.</summary>
		public void RemoveFromResults(IEnumerable<DuplicateItem> items) {
			foreach (var item in items.ToList())
				_engine.Duplicates.Remove(item);
			DropSingletonGroups();
			_ = PersistResultsAsync();
			Notify();
		}

		/// <summary>Drops groups that have shrunk to a single item — a group of one is not a duplicate. Delegates to <see cref="FileOperationsService.DropSingletonGroups"/>.</summary>
		void DropSingletonGroups() => _fileOps.DropSingletonGroups();

		// === Batch file operations (delete / move / link) ===

		/// <summary>True while a delete/move/link batch is running.</summary>
		public bool FileOpRunning { get; private set; }
		public string FileOpVerb { get; private set; } = string.Empty;
		public int FileOpCurrent { get; private set; }
		public int FileOpMax { get; private set; }

		bool TryBeginFileOp(string verb, int max) {
			if (FileOpRunning || max == 0) return false;
			FileOpRunning = true;
			FileOpVerb = verb;
			FileOpCurrent = 0;
			FileOpMax = max;
			Notify();
			return true;
		}

		void EndFileOp() {
			FileOpRunning = false;
			FileOpVerb = string.Empty;
			Notify();
		}

		/// <summary>Deletes files from disk and removes them from results and the scan database.</summary>
		public async Task<FileOpResult> DeleteItemsAsync(IEnumerable<DuplicateItem> items, bool permanent) {
			var list = items.ToList();
			var result = new FileOpResult();
			if (!TryBeginFileOp(permanent ? "Deleting" : "Moving to trash", list.Count))
				return result;
			try {
				var sw = System.Diagnostics.Stopwatch.StartNew();
				var progress = new SyncProgress(count => {
					FileOpCurrent = count;
					if (sw.ElapsedMilliseconds >= 100) { sw.Restart(); Notify(); }
				});
				// recycleBin = !permanent: permanent deletes bypass the recycle bin.
				var core = await _fileOps.DeleteAsync(list.Select(i => i.Path), !permanent, CancellationToken.None, progress);
				MapCoreResult(core, result, list);
				_fileOps.DropSingletonGroups();
				_ = PersistResultsAsync();
			}
			finally { EndFileOp(); }
			return result;
		}

		/// <summary>Moves files to a destination folder and updates the scan database paths.</summary>
		public async Task<FileOpResult> MoveItemsAsync(IEnumerable<DuplicateItem> items, string destinationFolder) {
			var list = items.ToList();
			var result = new FileOpResult();
			try { Directory.CreateDirectory(destinationFolder); }
			catch (Exception ex) {
				result.Errors.Add($"Cannot create destination folder: {ex.Message}");
				return result;
			}
			if (!TryBeginFileOp("Moving", list.Count))
				return result;
			try {
				// Pre-compute destination paths with collision avoidance against both
				// existing files and earlier items in this same batch.
				var moves = new List<(string source, string dest)>();
				var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				foreach (var item in list) {
					string dest = Path.Combine(destinationFolder, Path.GetFileName(item.Path));
					int n = 1;
					string ext = Path.GetExtension(dest);
					string nameNoExt = Path.GetFileNameWithoutExtension(dest);
					while (File.Exists(dest) || taken.Contains(dest))
						dest = Path.Combine(destinationFolder, $"{nameNoExt}_{n++}{ext}");
					taken.Add(dest);
					moves.Add((item.Path, dest));
				}
				var sw = System.Diagnostics.Stopwatch.StartNew();
				var progress = new SyncProgress(count => {
					FileOpCurrent = count;
					if (sw.ElapsedMilliseconds >= 100) { sw.Restart(); Notify(); }
				});
				var core = await _fileOps.MoveAsync(moves, CancellationToken.None, progress);
				result.Done = core.Done;
				result.Failed = core.Failed;
				result.Errors.AddRange(core.Errors);
				result.Warnings.AddRange(core.Warnings);
				_fileOps.DropSingletonGroups();
				_ = PersistResultsAsync();
			}
			finally { EndFileOp(); }
			return result;
		}

		/// <summary>
		/// Replaces each selected file with a hardlink or symlink to the kept file of its
		/// group (the highest-similarity unselected member that still exists on disk).
		/// </summary>
		public async Task<FileOpResult> CreateLinksAsync(IEnumerable<DuplicateItem> items, bool hardLinks) {
			var list = items.ToList();
			var result = new FileOpResult();
			if (!TryBeginFileOp(hardLinks ? "Creating hardlinks" : "Creating symlinks", list.Count))
				return result;
			try {
				// Keeper per group: highest-similarity unselected member that still exists.
				var selected = list.ToHashSet();
				var keeperByGroup = _engine.Duplicates
					.Where(d => !selected.Contains(d))
					.GroupBy(d => d.GroupId)
					.ToDictionary(g => g.Key, g => g.OrderByDescending(d => d.Similarity).FirstOrDefault(d => File.Exists(d.Path)));

				// Items with no keeper are reported as failures here (preserving the
				// original error message); the rest are delegated to the Core service.
				int preFailed = 0;
				var links = new List<(string target, string linkPath)>();
				foreach (var item in list) {
					keeperByGroup.TryGetValue(item.GroupId, out var keeper);
					if (keeper == null) {
						result.Errors.Add($"{Path.GetFileName(item.Path)}: no unselected file is left in this group to link to");
						result.Failed++;
						preFailed++;
						continue;
					}
					links.Add((keeper.Path, item.Path));
				}

				if (links.Count > 0) {
					var sw = System.Diagnostics.Stopwatch.StartNew();
					var progress = new SyncProgress(count => {
						FileOpCurrent = preFailed + count;
						if (sw.ElapsedMilliseconds >= 100) { sw.Restart(); Notify(); }
					});
					var core = hardLinks
						? await _fileOps.CreateHardLinksAsync(links, CancellationToken.None, progress)
						: await _fileOps.CreateSymbolicLinksAsync(links, CancellationToken.None, progress);
					result.Done = core.Done;
					result.Failed += core.Failed;
					result.Errors.AddRange(core.Errors);
					result.Warnings.AddRange(core.Warnings);
					var succeeded = core.SucceededPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
					foreach (var item in list)
						if (succeeded.Contains(item.Path))
							result.FreedBytes += Math.Max(0, item.SizeLong);
				}
				_fileOps.DropSingletonGroups();
				_ = PersistResultsAsync();
			}
			finally { EndFileOp(); }
			return result;
		}

		/// <summary>Copies Done/Failed/Errors/Warnings and computes FreedBytes from succeeded paths.</summary>
		static void MapCoreResult(FileOperationResult core, FileOpResult web, List<DuplicateItem> list) {
			web.Done = core.Done;
			web.Failed = core.Failed;
			web.Errors.AddRange(core.Errors);
			web.Warnings.AddRange(core.Warnings);
			var succeeded = core.SucceededPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
			foreach (var item in list)
				if (succeeded.Contains(item.Path))
					web.FreedBytes += Math.Max(0, item.SizeLong);
		}

		/// <summary>
		/// Synchronous <see cref="IProgress{T}"/> — invokes the callback directly on the
		/// reporting thread (the Core service's worker), matching the pre-refactor
		/// behavior where FileOpCurrent was incremented inline inside the Task.Run loop.
		/// </summary>
		sealed class SyncProgress : IProgress<int> {
			readonly Action<int> _action;
			public SyncProgress(Action<int> action) => _action = action;
			public void Report(int value) => _action(value);
		}


		/// <summary>Removes database entries for files that no longer exist or have errors.</summary>
		public async Task<int> CleanDatabaseAsync() {
			await ScanEngine.LoadDatabase();
			int before = DatabaseEntryCount;
			await _engine.CleanupDatabaseAsync();
			return before - DatabaseEntryCount;
		}

		/// <summary>Wipes all entries from the scan database.</summary>
		public async Task ClearDatabaseAsync() {
			await ScanEngine.LoadDatabase();
			ScanEngine.ClearDatabase();
			_engine.Duplicates.Clear();
			Notify();
		}

		/// <summary>Number of file entries currently stored in the scan database.</summary>
		public int DatabaseEntryCount => VDF.Core.Utils.DatabaseUtils.Database.Count;

		/// <summary>
		/// Runs the single-pair detection diagnostic with the current settings and
		/// returns the step-by-step report. See <see cref="ScanEngine.TestFilePairAsync"/>.
		/// </summary>
		public Task<string> TestFilePairAsync(string fileA, string fileB) {
			if (State == ScanState.Scanning || State == ScanState.Comparing)
				return Task.FromResult("A scan is currently running. Wait for it to finish before running the file pair test.");
			return _engine.TestFilePairAsync(fileA, fileB);
		}

		/// <summary>
	/// Creates a <see cref="ScanProgressResponse"/> from the current scan state
	/// and optional progress event. Centralises the 3 separate construction sites
	/// that previously existed (ScanService.Notify, ScanEndpoints, SseEndpoints).
	/// </summary>
	public ScanProgressResponse BuildProgressResponse() {
		var p = LastProgress;
		string? thumbnailPath = null;
		if (p != null && !string.IsNullOrEmpty(p.CurrentFile) && File.Exists(p.CurrentFile))
			thumbnailPath = p.CurrentFile;
		return new ScanProgressResponse {
			State = State.ToString(),
			FilesHashed = FilesHashed,
			CurrentFile = p?.CurrentFile ?? string.Empty,
			Current = p?.FilesProcessed ?? 0,
			Max = p?.FilesTotal ?? 0,
			ElapsedSeconds = p?.Elapsed.TotalSeconds ?? 0,
			RemainingSeconds = p?.RemainingTime.TotalSeconds ?? 0,
			CurrentStage = p?.Message ?? string.Empty,
			StageCurrent = p?.StageCurrent ?? 0,
			StageMax = p?.StageMax ?? 0,
			ErrorMessage = ErrorMessage,
			CurrentThumbnailPath = thumbnailPath,
		};
	}

	void Notify() {
		StateChanged?.Invoke();
		// Broadcast via SignalR
		if (_hubContext != null) {
			_ = _hubContext.Clients.All.SendAsync("StateChanged", State.ToString());
			if (LastProgress != null) {
				var payload = BuildProgressResponse();
				_ = _hubContext.Clients.All.SendAsync("ProgressUpdate", payload);
			}
			if (FileOpRunning) {
				_ = _hubContext.Clients.All.SendAsync("FileOpProgress", FileOpCurrent, FileOpMax, FileOpVerb);
			}
		}
	}

		public void Dispose() {
			_orchestrator.Dispose();
			_cts.Dispose();
		}
	}
}
