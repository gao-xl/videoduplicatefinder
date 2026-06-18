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

using System.Text.Json;
using VDF.Core;
using VDF.Core.Services;
using VDF.Core.ViewModels;

namespace VDF.CLI.Commands {
	/// <summary>
	/// Drives scan operations via <see cref="ScanOrchestrator"/> and adapts the
	/// orchestrator's events to the CLI's awaitable Task + Console.Error progress model.
	/// </summary>
	internal static class ScanRunner {
		/// <summary>Runs StartSearch() then StartCompare() (the full pipeline).</summary>
		internal static async Task<HashSet<DuplicateItem>> RunScanAndCompareAsync(ScanEngine engine, CancellationToken ct) {
			using var orchestrator = new ScanOrchestrator(engine);
			WireProgress(orchestrator);
			await orchestrator.StartAsync(engine.Settings, ct);
			if (orchestrator.State == ScanState.Aborted)
				throw new OperationCanceledException(ct);
			return engine.Duplicates;
		}

		/// <summary>Runs StartSearch() only (enumerate files and build hashes).</summary>
		internal static async Task RunSearchAsync(ScanEngine engine, CancellationToken ct) {
			using var orchestrator = new ScanOrchestrator(engine);
			WireProgress(orchestrator);
			await orchestrator.StartAsync(engine.Settings, ct);
			if (orchestrator.State == ScanState.Aborted)
				throw new OperationCanceledException(ct);
		}

		/// <summary>Runs StartCompare() only (assumes database already populated by a prior scan).</summary>
		internal static async Task<HashSet<DuplicateItem>> RunCompareAsync(ScanEngine engine, CancellationToken ct) {
			using var orchestrator = new ScanOrchestrator(engine);
			WireProgress(orchestrator);
			await orchestrator.CompareAsync(ct);
			if (orchestrator.State == ScanState.Aborted)
				throw new OperationCanceledException(ct);
			return engine.Duplicates;
		}

		/// <summary>
		/// Subscribes to the orchestrator's events and writes progress to Console.Error.
		/// Adapts the previous WireProgress(ScanEngine) to the orchestrator's throttled
		/// ProgressChanged event, preserving the \r progress bar in interactive terminals.
		/// </summary>
		internal static void WireProgress(ScanOrchestrator orchestrator) {
			bool isTerminal = !Console.IsErrorRedirected;

			orchestrator.Engine.FilesEnumerated += (_, _) =>
				Console.Error.WriteLine("[scan] File enumeration complete.");

			orchestrator.Engine.BuildingHashesDone += (_, _) =>
				Console.Error.WriteLine("[scan] Hashing complete.");

			orchestrator.ProgressChanged += (_, e) => {
				int pct = e.Percent;
				string eta = e.RemainingTime == TimeSpan.Zero ? "..." : e.RemainingTime.ToString(@"m\mss\s");
				string stage = string.IsNullOrEmpty(e.Message)
					? string.Empty
					: e.StageMax > 0 ? $"  ({e.Message} {e.StageCurrent}/{e.StageMax})" : $"  ({e.Message})";
				if (isTerminal)
					Console.Error.Write($"\r[{pct,3}%] {e.FilesProcessed}/{e.FilesTotal}  ETA {eta}  {TruncatePath(e.CurrentFile, 60)}{stage}    ");
				else
					Console.Error.WriteLine($"[{pct,3}%] {e.FilesProcessed}/{e.FilesTotal}  ETA {eta}  {TruncatePath(e.CurrentFile, 60)}{stage}");
			};

			orchestrator.Completed += (_, e) => {
				if (e.State == ScanState.Done) {
					if (isTerminal) Console.Error.WriteLine();
					Console.Error.WriteLine("[scan] Comparison complete.");
				}
				else if (e.State == ScanState.Aborted) {
					if (isTerminal) Console.Error.WriteLine();
					Console.Error.WriteLine("[scan] Aborted.");
				}
				else if (e.State == ScanState.Error && e.ErrorMessage != null) {
					if (isTerminal) Console.Error.WriteLine();
					Console.Error.WriteLine($"[scan] Error: {e.ErrorMessage}");
				}
			};
		}

		internal static Settings LoadOrCreateSettings(FileInfo? settingsFile) {
			if (settingsFile == null || !settingsFile.Exists)
				return new Settings();

			try {
				var json = File.ReadAllText(settingsFile.FullName);
				return JsonSerializer.Deserialize(json, VDF.Core.Utils.CoreJsonContext.Default.Settings) ?? new Settings();
			}
			catch (Exception ex) {
				Console.Error.WriteLine($"Warning: could not load settings file '{settingsFile.FullName}': {ex.Message}");
				return new Settings();
			}
		}

		static string TruncatePath(string path, int maxLen) {
			if (path.Length <= maxLen) return path;
			return "..." + path[^(maxLen - 3)..];
		}
	}
}
