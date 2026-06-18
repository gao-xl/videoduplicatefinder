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

using System.CommandLine;
using System.Text.Json;
using VDF.CLI.Actions;
using VDF.Core.Services;
using VDF.Core.ViewModels;

namespace VDF.CLI.Commands {
	internal static class MarkCommand {
		internal static Command Build() {
			var cmd = new Command("mark",
				"Read a JSON results file and mark files for deletion based on a strategy.\n\n" +
				"WARNING: Automatic deletion is not recommended. Always use --dry-run first to review what would be deleted.");

			var inputOpt = new Option<FileInfo>("--input", "-i") {
				Description = "Path to a JSON results file produced by scan-and-compare --format json.",
				Required = true
			};
			var strategyOpt = new Option<Strategy>("--strategy") {
				Description = "Selection strategy: lowest-quality, smallest-file, shortest-duration, worst-resolution, 100-percent-only.",
				Required = true
			};
			var dryRunOpt = new Option<bool>("--dry-run") {
				Description = "Print which files would be deleted without deleting anything (default).",
				DefaultValueFactory = _ => true
			};
			var deleteOpt = new Option<bool>("--delete") {
				Description = "Move files to the system recycle bin / trash."
			};
			var deletePermanentOpt = new Option<bool>("--delete-permanent") {
				Description = "Permanently delete files. WARNING: irreversible."
			};

			cmd.Options.Add(inputOpt);
			cmd.Options.Add(strategyOpt);
			cmd.Options.Add(dryRunOpt);
			cmd.Options.Add(deleteOpt);
			cmd.Options.Add(deletePermanentOpt);

			cmd.SetAction(async (parseResult, ct) => {
				var inputFile = parseResult.GetValue(inputOpt)!;
				if (!inputFile.Exists) {
					Console.Error.WriteLine($"Error: input file not found: {inputFile.FullName}");
					return;
				}

				List<DuplicateItem>? duplicates;
				try {
					await using var stream = inputFile.OpenRead();
					// The JSON is an array of groups, each with an Items array
					var groups = await JsonSerializer.DeserializeAsync(stream, Output.CliJsonContext.Default.ListDuplicateGroup, ct);
					duplicates = groups?.SelectMany(g => g.Items).ToList();
				}
				catch (Exception ex) {
					Console.Error.WriteLine($"Error reading results file: {ex.Message}");
					return;
				}

				if (duplicates == null || duplicates.Count == 0) {
					Console.Error.WriteLine("No duplicates found in the results file.");
					return;
				}

				var strategy = parseResult.GetValue(strategyOpt);
				var marked = DeletionStrategy.SelectForDeletion(duplicates, strategy);

				bool doPermanent = parseResult.GetValue(deletePermanentOpt);
				bool doDelete = parseResult.GetValue(deleteOpt) || doPermanent;
				bool dryRun = !doDelete || parseResult.GetValue(dryRunOpt);

				await ExecuteDeletion(marked, dryRun, doPermanent);
			});

			return cmd;
		}

		internal static async Task ExecuteDeletion(IReadOnlyList<DuplicateItem> marked, bool dryRun, bool permanent) {
			if (marked.Count == 0) {
				Console.Error.WriteLine("No files selected for deletion by the chosen strategy.");
				return;
			}

			if (dryRun) {
				foreach (var item in marked)
					Console.WriteLine($"[dry-run] would delete: {item.Path}");
				Console.Error.WriteLine($"[dry-run] {marked.Count} file(s) would be deleted. Use --delete or --delete-permanent to proceed.");
				return;
			}

			Console.Error.WriteLine();
			Console.Error.WriteLine("WARNING: Automatic deletion is not recommended.");
			Console.Error.WriteLine($"         {marked.Count} file(s) will be {(permanent ? "permanently deleted" : "moved to trash")}.");
			Console.Error.WriteLine("         This action cannot be undone. Proceeding in 3 seconds... (Ctrl+C to abort)");
			await Task.Delay(3000);

			// Delegate to the unified Core service. CLI has no ScanEngine, so a
			// null engine is passed — the service performs file I/O only (Windows
			// batched SHFileOperation recycle, Linux/macOS trash with permanent-delete
			// fallback) and the caller owns any database sync.
			var fileOps = new FileOperationsService(null);
			var result = await fileOps.DeleteAsync(
				marked.Select(m => m.Path),
				!permanent,
				CancellationToken.None);

			foreach (var err in result.Errors)
				Console.Error.WriteLine($"Failed: {err}");
			foreach (var warn in result.Warnings)
				Console.Error.WriteLine($"Warning: {warn}");
			foreach (var path in result.SucceededPaths)
				Console.Error.WriteLine($"Deleted: {path}");

			Console.Error.WriteLine($"Done. {result.Done} deleted, {result.Failed} failed.");
		}

	}
}
