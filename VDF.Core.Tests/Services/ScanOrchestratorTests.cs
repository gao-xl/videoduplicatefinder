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

using System.Reflection;
using VDF.Core.Services;

namespace VDF.Core.Tests.Services;

/// <summary>
/// Tests for <see cref="ScanOrchestrator"/>'s state machine, cancellation,
/// pause/resume, progress throttling, and error handling.
///
/// The orchestrator wraps <see cref="ScanEngine"/> which requires FFmpeg and
/// real files to run a scan. To test the state machine in isolation, these
/// tests simulate engine events (Progress, ScanDone, ScanAborted) by invoking
/// the engine's field-like event backing delegates via reflection. This avoids
/// the need for FFmpeg while still exercising the orchestrator's real event
/// handlers.
/// </summary>
public class ScanOrchestratorTests {

	// ── Helpers: raise ScanEngine events via reflection ──────────────────────
	// ScanEngine's events are field-like (no explicit add/remove), so each has
	// a private backing delegate field with the same name.

	static void RaiseProgress(ScanEngine engine, int current, int max, string file = "") {
		var field = typeof(ScanEngine).GetField("Progress",
			BindingFlags.NonPublic | BindingFlags.Instance);
		var handler = (EventHandler<ScanProgressChangedEventArgs>?)field?.GetValue(engine);
		handler?.Invoke(engine, new ScanProgressChangedEventArgs {
			CurrentPosition = current,
			MaxPosition = max,
			CurrentFile = file,
		});
	}

	static void RaiseScanDone(ScanEngine engine) {
		var field = typeof(ScanEngine).GetField("ScanDone",
			BindingFlags.NonPublic | BindingFlags.Instance);
		var handler = (EventHandler?)field?.GetValue(engine);
		handler?.Invoke(engine, EventArgs.Empty);
	}

	static void RaiseScanAborted(ScanEngine engine) {
		var field = typeof(ScanEngine).GetField("ScanAborted",
			BindingFlags.NonPublic | BindingFlags.Instance);
		var handler = (EventHandler?)field?.GetValue(engine);
		handler?.Invoke(engine, EventArgs.Empty);
	}

	// ── Helpers: set orchestrator internal state via reflection ──────────────

	static void SetState(ScanOrchestrator orchestrator, ScanState state) {
		// State has a private setter; reflection bypasses it for test setup.
		typeof(ScanOrchestrator)
			.GetProperty("State", BindingFlags.Public | BindingFlags.Instance)!
			.GetSetMethod(nonPublic: true)!
			.Invoke(orchestrator, new object[] { state });
	}

	static void SetTcs(ScanOrchestrator orchestrator, TaskCompletionSource tcs) {
		var field = typeof(ScanOrchestrator).GetField("_tcs",
			BindingFlags.NonPublic | BindingFlags.Instance);
		field?.SetValue(orchestrator, tcs);
	}

	// ── Normal completion ────────────────────────────────────────────────────

	[Fact]
	public void NormalCompletion_ScanDone_StateTransitionsToDone() {
		using var orchestrator = new ScanOrchestrator();
		ScanCompletedEventArgs? completedArgs = null;
		orchestrator.Completed += (_, e) => completedArgs = e;

		RaiseScanDone(orchestrator.Engine);

		Assert.Equal(ScanState.Done, orchestrator.State);
		Assert.NotNull(completedArgs);
		Assert.Equal(ScanState.Done, completedArgs!.State);
		Assert.Null(completedArgs.ErrorMessage);
	}

	// ── Cancel mid-scan ──────────────────────────────────────────────────────

	[Fact]
	public async Task CancelMidScan_StateTransitionsToAborted() {
		using var orchestrator = new ScanOrchestrator();
		SetState(orchestrator, ScanState.Scanning);
		SetTcs(orchestrator, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

		// CancelAsync calls _engine.Stop() then awaits _tcs.Task.
		// Since the engine isn't actually scanning, Stop() is a no-op and the
		// tcs won't be completed by the engine. We simulate the engine's
		// ScanAborted event (which the real engine fires when Stop() takes effect)
		// to complete the tcs and transition state.
		var cancelTask = orchestrator.CancelAsync();

		// Simulate the engine aborting in response to Stop()
		RaiseScanAborted(orchestrator.Engine);

		await cancelTask;

		Assert.Equal(ScanState.Aborted, orchestrator.State);
	}

	// ── Pause / Resume ───────────────────────────────────────────────────────

	[Fact]
	public async Task Pause_WhenScanning_SetsIsPaused() {
		using var orchestrator = new ScanOrchestrator();
		SetState(orchestrator, ScanState.Scanning);

		await orchestrator.PauseAsync();

		Assert.True(orchestrator.IsPaused);
	}

	[Fact]
	public async Task Resume_AfterPause_ClearsIsPaused() {
		using var orchestrator = new ScanOrchestrator();
		SetState(orchestrator, ScanState.Scanning);

		await orchestrator.PauseAsync();
		Assert.True(orchestrator.IsPaused);

		await orchestrator.ResumeAsync();
		Assert.False(orchestrator.IsPaused);
	}

	[Fact]
	public async Task Pause_WhenIdle_IsNoOp() {
		using var orchestrator = new ScanOrchestrator();
		// State is Idle by default

		await orchestrator.PauseAsync();

		Assert.False(orchestrator.IsPaused);
	}

	[Fact]
	public async Task Resume_WhenNotPaused_IsNoOp() {
		using var orchestrator = new ScanOrchestrator();
		// State is Idle, IsPaused is false

		await orchestrator.ResumeAsync();

		Assert.False(orchestrator.IsPaused);
	}

	// ── Progress throttling ──────────────────────────────────────────────────

	[Fact]
	public void ProgressThrottling_RapidSamePercentEvents_Coalesced() {
		using var orchestrator = new ScanOrchestrator();
		int progressCount = 0;
		orchestrator.ProgressChanged += (_, _) => progressCount++;

		// Fire 200 rapid events all at 50% — only the first should pass the throttle
		// (subsequent events have 0% delta and < 100ms elapsed).
		for (int i = 0; i < 200; i++) {
			RaiseProgress(orchestrator.Engine, 50, 100, "file.mp4");
		}

		// At minimum the first event is emitted; the rest are throttled away.
		Assert.True(progressCount >= 1, $"Expected at least 1 event, got {progressCount}");
		Assert.True(progressCount < 200, $"Expected throttling to coalesce events, but got {progressCount}/200");
	}

	[Fact]
	public void ProgressThrottling_OnePercentDelta_EmitsImmediately() {
		using var orchestrator = new ScanOrchestrator();
		int progressCount = 0;
		orchestrator.ProgressChanged += (_, _) => progressCount++;

		// Fire events with 1% increments — each satisfies the percent-delta threshold
		// (ProgressThrottlePercentDelta = 1) and should be emitted immediately,
		// regardless of elapsed time.
		for (int i = 0; i <= 100; i++) {
			RaiseProgress(orchestrator.Engine, i, 100, "file.mp4");
		}

		Assert.Equal(101, progressCount);
	}

	[Fact]
	public void ProgressThrottling_FirstEventAlwaysEmitted() {
		using var orchestrator = new ScanOrchestrator();
		int progressCount = 0;
		orchestrator.ProgressChanged += (_, _) => progressCount++;

		RaiseProgress(orchestrator.Engine, 0, 100, "first.mp4");

		Assert.Equal(1, progressCount);
		Assert.NotNull(orchestrator.LastProgress);
		Assert.Equal("first.mp4", orchestrator.LastProgress!.CurrentFile);
	}

	[Fact]
	public void ProgressThrottling_LastProgressUpdatedOnEmit() {
		using var orchestrator = new ScanOrchestrator();

		RaiseProgress(orchestrator.Engine, 25, 100, "file_a.mp4");

		Assert.NotNull(orchestrator.LastProgress);
		Assert.Equal(25, orchestrator.LastProgress!.Percent);
		Assert.Equal(25, orchestrator.LastProgress.FilesProcessed);
		Assert.Equal(100, orchestrator.LastProgress.FilesTotal);
		Assert.Equal("file_a.mp4", orchestrator.LastProgress.CurrentFile);
	}

	// ── Error state ──────────────────────────────────────────────────────────

	[Fact]
	public void SetError_StateTransitionsToError() {
		using var orchestrator = new ScanOrchestrator();
		ScanCompletedEventArgs? completedArgs = null;
		orchestrator.Completed += (_, e) => completedArgs = e;

		orchestrator.SetError(new InvalidOperationException("test error"));

		Assert.Equal(ScanState.Error, orchestrator.State);
		Assert.Equal("test error", orchestrator.ErrorMessage);
		Assert.NotNull(completedArgs);
		Assert.Equal(ScanState.Error, completedArgs!.State);
		Assert.Equal("test error", completedArgs.ErrorMessage);
	}

	[Fact]
	public void SetError_ClearsLastProgress() {
		using var orchestrator = new ScanOrchestrator();

		// Set up some progress first
		RaiseProgress(orchestrator.Engine, 50, 100, "file.mp4");
		Assert.NotNull(orchestrator.LastProgress);

		orchestrator.SetError(new InvalidOperationException("boom"));

		Assert.Null(orchestrator.LastProgress);
	}

	// ── Reset ────────────────────────────────────────────────────────────────

	[Fact]
	public void Reset_FromDone_ReturnsToIdle() {
		using var orchestrator = new ScanOrchestrator();
		SetState(orchestrator, ScanState.Done);

		orchestrator.Reset();

		Assert.Equal(ScanState.Idle, orchestrator.State);
		Assert.Null(orchestrator.ErrorMessage);
		Assert.Null(orchestrator.LastProgress);
	}

	[Fact]
	public void Reset_WhileScanning_IsNoOp() {
		using var orchestrator = new ScanOrchestrator();
		SetState(orchestrator, ScanState.Scanning);

		orchestrator.Reset();

		Assert.Equal(ScanState.Scanning, orchestrator.State);
	}

	// ── Abort via engine event ───────────────────────────────────────────────

	[Fact]
	public void EngineScanAborted_StateTransitionsToAborted() {
		using var orchestrator = new ScanOrchestrator();
		ScanCompletedEventArgs? completedArgs = null;
		orchestrator.Completed += (_, e) => completedArgs = e;

		RaiseScanAborted(orchestrator.Engine);

		Assert.Equal(ScanState.Aborted, orchestrator.State);
		Assert.NotNull(completedArgs);
		Assert.Equal(ScanState.Aborted, completedArgs!.State);
	}

	// ── StateChanged event ───────────────────────────────────────────────────

	[Fact]
	public void StateChanged_FiresOnStateTransition() {
		using var orchestrator = new ScanOrchestrator();
		ScanState? lastNotifiedState = null;
		orchestrator.StateChanged += (_, state) => lastNotifiedState = state;

		orchestrator.SetError(new InvalidOperationException("test"));

		Assert.Equal(ScanState.Error, lastNotifiedState);
	}
}
