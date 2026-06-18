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
using VDF.Core.ViewModels;

namespace VDF.Core.Services {

	/// <summary>
	/// Scan lifecycle state. Replaces the per-frontend state machines in GUI/CLI/Web.
	/// </summary>
	public enum ScanState { Idle, Scanning, Comparing, Done, Aborted, Error }

	/// <summary>Coarse-grained phase reported with each progress event.</summary>
	public enum ScanStage { Scan, Compare }

	/// <summary>
	/// Unified progress payload. Replaces GUI's <c>ScanProgressText</c>/
	/// <c>RemainingTime</c>/<c>ScanProgressValue</c> mapping and Web's
	/// <c>ScanProgressResponse</c> DTO mapping.
	/// </summary>
	public sealed record ScanProgressArgs {
		public ScanStage Stage { get; init; }
		public int Percent { get; init; }
		public string CurrentFile { get; init; } = string.Empty;
		public int FilesProcessed { get; init; }
		public int FilesTotal { get; init; }
		public TimeSpan RemainingTime { get; init; }
		public TimeSpan Elapsed { get; init; }
		/// <summary>Short label describing what's happening to <see cref="CurrentFile"/> (e.g. "probing", "sampling frames").</summary>
		public string Message { get; init; } = string.Empty;
		public int StageCurrent { get; init; }
		public int StageMax { get; init; }
	}

	public sealed class ScanCompletedEventArgs : EventArgs {
		public ScanState State { get; init; }
		public string? ErrorMessage { get; init; }
	}

	/// <summary>
	/// Wraps <see cref="ScanEngine"/> and provides a unified scan lifecycle API:
	/// state machine (<see cref="ScanState"/>), cancellation, pause/resume, and
	/// progress throttling. Used by GUI, CLI, and Web to avoid three separate
	/// hand-rolled state machines.
	///
	/// The orchestrator does NOT depend on Avalonia, ASP.NET Core, or any UI
	/// framework — it is pure C# in VDF.Core.
	/// </summary>
	public sealed class ScanOrchestrator : IDisposable {

		/// <summary>
		/// Progress throttle: at most one <see cref="ProgressChanged"/> event per
		/// 100ms, OR every 1% progress change, whichever comes first. This prevents
		/// UI thread overload while still reporting meaningful jumps promptly.
		/// </summary>
		static readonly TimeSpan ProgressThrottleInterval = TimeSpan.FromMilliseconds(100);
		const int ProgressThrottlePercentDelta = 1;

		readonly ScanEngine _engine;
		CancellationTokenSource _internalCts = new();
		CancellationTokenSource? _linkedCts;
		CancellationTokenRegistration _externalRegistration;
		TaskCompletionSource? _tcs;

		// Progress throttle state — Progress fires from Parallel.ForEachAsync threads.
		readonly object _progressLock = new();
		long _lastProgressEmitTicks;      // DateTime.UtcNow ticks; 0 = never emitted
		int _lastProgressPercent = -1;

		public ScanEngine Engine => _engine;
		public ScanState State { get; private set; } = ScanState.Idle;
		public bool IsPaused { get; private set; }
		public ScanProgressArgs? LastProgress { get; private set; }
		public string? ErrorMessage { get; private set; }
		/// <summary>Total files hashed (captured when BuildingHashesDone fires).</summary>
		public int FilesHashed { get; private set; }

		public event EventHandler<ScanProgressArgs>? ProgressChanged;
		public event EventHandler<ScanState>? StateChanged;
		public event EventHandler<ScanCompletedEventArgs>? Completed;

		public ScanOrchestrator() : this(new ScanEngine()) { }

		public ScanOrchestrator(ScanEngine engine) {
			_engine = engine ?? throw new ArgumentNullException(nameof(engine));
			_engine.Progress += OnEngineProgress;
			_engine.BuildingHashesDone += OnEngineBuildingHashesDone;
			_engine.ScanDone += OnEngineScanDone;
			_engine.ScanAborted += OnEngineScanAborted;
		}

		/// <summary>
		/// Starts the full scan pipeline (enumerate → hash → compare) via
		/// <see cref="ScanEngine.StartSearch"/>. Manages state transitions
		/// Idle → Scanning → Comparing → Done/Aborted/Error.
		/// </summary>
		public async Task StartAsync(Settings settings, CancellationToken cancellationToken = default) {
			if (State == ScanState.Scanning || State == ScanState.Comparing)
				throw new InvalidOperationException("A scan is already in progress.");
			if (settings == null) throw new ArgumentNullException(nameof(settings));

			_engine.Settings = settings;
			BeginRun(cancellationToken);

			try {
				await _engine.StartSearch();
				if (_tcs != null) await _tcs.Task;
			}
			catch (Exception ex) {
				SetError(ex);
			}
			finally {
				EndRun();
			}
		}

		/// <summary>
		/// Runs compare-only via <see cref="ScanEngine.StartCompare"/> (assumes the
		/// database was populated by a prior scan). State: Idle → Comparing → Done/Aborted/Error.
		/// </summary>
		public async Task<HashSet<DuplicateItem>> CompareAsync(CancellationToken cancellationToken = default) {
			if (State == ScanState.Scanning || State == ScanState.Comparing)
				throw new InvalidOperationException("A scan is already in progress.");

			BeginRun(cancellationToken);
			// Compare-only starts directly in Comparing — BuildingHashesDone won't fire.
			SetState(ScanState.Comparing);

			try {
				await _engine.StartCompare();
				if (_tcs != null) await _tcs.Task;
			}
			catch (Exception ex) {
				SetError(ex);
			}
			finally {
				EndRun();
			}
			return _engine.Duplicates;
		}

		/// <summary>
		/// Cancels the in-progress scan via the internal CancellationTokenSource
		/// and calls <see cref="ScanEngine.Stop"/>. Awaits the scan's actual
		/// shutdown (state transitions to <see cref="ScanState.Aborted"/>).
		/// </summary>
		public async Task CancelAsync() {
			if (State != ScanState.Scanning && State != ScanState.Comparing) return;
			try { _internalCts.Cancel(); } catch { /* already disposed */ }
			try { _engine.Stop(); } catch { /* engine may already be stopped */ }
			if (_tcs != null) {
				try { await _tcs.Task; } catch { /* state already set by events */ }
			}
		}

		public Task PauseAsync() {
			if (State != ScanState.Scanning && State != ScanState.Comparing) return Task.CompletedTask;
			if (IsPaused) return Task.CompletedTask;
			_engine.Pause();
			IsPaused = true;
			StateChanged?.Invoke(this, State);
			return Task.CompletedTask;
		}

		public Task ResumeAsync() {
			if (!IsPaused) return Task.CompletedTask;
			_engine.Resume();
			IsPaused = false;
			StateChanged?.Invoke(this, State);
			return Task.CompletedTask;
		}

		/// <summary>Resets to Idle, clearing results and error state. No-op while scanning.</summary>
		public void Reset() {
			if (State == ScanState.Scanning || State == ScanState.Comparing) return;
			SetState(ScanState.Idle);
			ErrorMessage = null;
			LastProgress = null;
			FilesHashed = 0;
			_engine.Duplicates.Clear();
		}

		void BeginRun(CancellationToken externalToken) {
			_internalCts = new CancellationTokenSource();
			_linkedCts = CancellationTokenSource.CreateLinkedTokenSource(externalToken, _internalCts.Token);
			_externalRegistration = _linkedCts.Token.Register(() => {
				try { _engine.Stop(); } catch { /* engine may already be stopped */ }
			});
			_tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			ErrorMessage = null;
			LastProgress = null;
			FilesHashed = 0;
			IsPaused = false;
			ResetProgressThrottle();
			_engine.Duplicates.Clear();
			SetState(ScanState.Scanning);
		}

		void EndRun() {
			_externalRegistration.Dispose();
			_linkedCts?.Dispose();
			_linkedCts = null;
		}

		void SetState(ScanState newState) {
			if (State == newState) return;
			State = newState;
			StateChanged?.Invoke(this, newState);
		}

		/// <summary>
		/// Transitions the orchestrator to the <see cref="ScanState.Error"/> state and
		/// fires <see cref="Completed"/>. Used internally by <see cref="StartAsync"/>/
		/// <see cref="CompareAsync"/> catch blocks, and externally by host-level error
		/// handlers (e.g., ASP.NET Core unhandled exception handlers) to surface errors
		/// that occur outside the scan pipeline.
		/// </summary>
		public void SetError(Exception ex) {
			ErrorMessage = ex.Message;
			LastProgress = null;
			SetState(ScanState.Error);
			_tcs?.TrySetResult();
			Completed?.Invoke(this, new ScanCompletedEventArgs { State = ScanState.Error, ErrorMessage = ex.Message });
		}

		void OnEngineBuildingHashesDone(object? sender, EventArgs e) {
			FilesHashed = LastProgress?.FilesTotal ?? 0;
			SetState(ScanState.Comparing);
		}

		void OnEngineScanDone(object? sender, EventArgs e) {
			LastProgress = null;
			SetState(ScanState.Done);
			_tcs?.TrySetResult();
			Completed?.Invoke(this, new ScanCompletedEventArgs { State = ScanState.Done });
		}

		void OnEngineScanAborted(object? sender, EventArgs e) {
			LastProgress = null;
			SetState(ScanState.Aborted);
			_tcs?.TrySetResult();
			Completed?.Invoke(this, new ScanCompletedEventArgs { State = ScanState.Aborted });
		}

		void OnEngineProgress(object? sender, ScanProgressChangedEventArgs e) {
			int percent = e.MaxPosition > 0 ? (int)(100L * e.CurrentPosition / e.MaxPosition) : 0;
			var stage = State == ScanState.Comparing ? ScanStage.Compare : ScanStage.Scan;
			var args = new ScanProgressArgs {
				Stage = stage,
				Percent = percent,
				CurrentFile = e.CurrentFile,
				FilesProcessed = e.CurrentPosition,
				FilesTotal = e.MaxPosition,
				RemainingTime = e.Remaining,
				Elapsed = e.Elapsed,
				Message = e.CurrentStage ?? string.Empty,
				StageCurrent = e.StageCurrent,
				StageMax = e.StageMax,
			};

			if (!ShouldEmitProgress(percent)) return;
			LastProgress = args;
			ProgressChanged?.Invoke(this, args);
		}

		bool ShouldEmitProgress(int currentPercent) {
			lock (_progressLock) {
				long nowTicks = DateTime.UtcNow.Ticks;
				if (_lastProgressEmitTicks == 0) {
					_lastProgressEmitTicks = nowTicks;
					_lastProgressPercent = currentPercent;
					return true;
				}
				long elapsedMs = (nowTicks - _lastProgressEmitTicks) / TimeSpan.TicksPerMillisecond;
				int percentDelta = Math.Abs(currentPercent - _lastProgressPercent);
				bool shouldEmit = elapsedMs >= ProgressThrottleInterval.TotalMilliseconds
								  || percentDelta >= ProgressThrottlePercentDelta;
				if (shouldEmit) {
					_lastProgressEmitTicks = nowTicks;
					_lastProgressPercent = currentPercent;
				}
				return shouldEmit;
			}
		}

		void ResetProgressThrottle() {
			lock (_progressLock) {
				_lastProgressEmitTicks = 0;
				_lastProgressPercent = -1;
			}
		}

		public void Dispose() {
			_externalRegistration.Dispose();
			_linkedCts?.Dispose();
			_internalCts.Dispose();
			_engine.Progress -= OnEngineProgress;
			_engine.BuildingHashesDone -= OnEngineBuildingHashesDone;
			_engine.ScanDone -= OnEngineScanDone;
			_engine.ScanAborted -= OnEngineScanAborted;
		}
	}
}
