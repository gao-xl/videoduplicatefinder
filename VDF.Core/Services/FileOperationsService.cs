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
//     along with VideoDuplicateFinder. If not, see <http://www.gnu.org/licenses/>.
// */

using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using VDF.Core.Utils;
using VDF.Core.ViewModels;

namespace VDF.Core.Services {

	/// <summary>
	/// Outcome of a batch file operation (delete / move / link). Mirrors the
	/// shape of <c>VDF.Web.Services.FileOpResult</c> so the Web layer can map
	/// fields directly. <see cref="FreedBytes"/> is left at zero by the service
	/// (it operates on paths, not sized items); callers that know item sizes
	/// fill it in from <see cref="SucceededPaths"/>.
	/// </summary>
	public sealed class FileOperationResult {
		public int Done;
		public int Failed;
		public long FreedBytes;
		public List<string> Errors { get; } = new();
		public List<string> Warnings { get; } = new();
		/// <summary>Paths that were successfully processed (deleted / moved / linked).</summary>
		public List<string> SucceededPaths { get; } = new();
	}

	/// <summary>
	/// Unified batch file operations: delete (with Windows recycle-bin batching),
	/// move, hardlink/symlink creation (temp-file + atomic-rename safe flow),
	/// and singleton-group cleanup. Replaces the three divergent implementations
	/// previously in GUI <c>MainWindowVM.DeleteInternal</c>, Web
	/// <c>ScanService.DeleteItemsAsync</c>/<c>MoveItemsAsync</c>/<c>CreateLinksAsync</c>,
	/// and CLI <c>MarkCommand.ExecuteDeletion</c>.
	///
	/// The service is pure C# in VDF.Core — no Avalonia or ASP.NET Core dependency.
	/// P/Invoke for <c>SHFileOperation</c> is Windows-only and guarded with
	/// <see cref="OperatingSystem.IsWindows"/>.
	///
	/// When constructed with a non-null <see cref="ScanEngine"/>, the service also
	/// removes processed items from <see cref="ScanEngine.Duplicates"/> and syncs
	/// the scan database (<c>RemoveFromDatabase</c>/<c>UpdateFilePathInDatabase</c>/
	/// <c>SaveDatabase</c>). When constructed with a null engine (CLI, or GUI which
	/// manages its own VM collection and blacklist mode), only file I/O is performed
	/// and the caller owns database sync.
	/// </summary>
	public sealed class FileOperationsService {

		readonly ScanEngine? _engine;

		public FileOperationsService(ScanEngine? engine = null) {
			_engine = engine;
		}

		/// <summary>
		/// Batch-deletes files. When <paramref name="recycleBin"/> is true on
		/// Windows, the whole batch is sent to the recycle bin in a single
		/// <c>SHFileOperation</c> call (one shell round-trip instead of N). On
		/// non-Windows, each file is moved to the system trash with a permanent
		/// delete fallback. When <paramref name="recycleBin"/> is false, files
		/// are permanently deleted with <see cref="File.Delete(string)"/>.
		///
		/// Files that no longer exist on disk are treated as successfully
		/// deleted so their database/list entries are still cleaned up.
		/// </summary>
		public Task<FileOperationResult> DeleteAsync(IEnumerable<string> paths, bool recycleBin, CancellationToken ct, IProgress<int>? progress = null) =>
			Task.Run(() => DeleteCore(paths, recycleBin, ct, progress));

		FileOperationResult DeleteCore(IEnumerable<string> paths, bool recycleBin, CancellationToken ct, IProgress<int>? progress) {
			var result = new FileOperationResult();
			var list = paths.ToList();
			if (list.Count == 0) return result;

			// Windows recycle-bin deletes go through a single batched shell operation
			// — one SHFileOperation per file pays the full shell round-trip each time
			// and is dramatically slower for big batches. Per-file success is
			// determined afterwards by re-checking existence.
			bool batchedRecycle = recycleBin && OperatingSystem.IsWindows();
			var batchRecycled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			if (batchedRecycle) {
				var existing = list.Where(File.Exists).ToList();
				if (existing.Count > 0) {
					var fs = new FileUtils.SHFILEOPSTRUCT {
						wFunc = FileUtils.FileOperationType.FO_DELETE,
						pFrom = string.Join('\0', existing) + "\0\0",
						fFlags = FileUtils.FileOperationFlags.FOF_ALLOWUNDO |
								 FileUtils.FileOperationFlags.FOF_NOCONFIRMATION |
								 FileUtils.FileOperationFlags.FOF_NOERRORUI |
								 FileUtils.FileOperationFlags.FOF_SILENT
					};
					int shResult = FileUtils.SHFileOperation(ref fs);
					if (shResult != 0)
						Logger.Instance.Info($"SHFileOperation returned {shResult:X} for a batch of {existing.Count} file(s); checking which files were actually recycled.");
					foreach (var p in existing)
						batchRecycled.Add(p);
				}
			}

			foreach (var path in list) {
				if (ct.IsCancellationRequested) break;
				try {
					bool exists = File.Exists(path);
					if (!exists) {
						// Already gone — still remove the entry and database record.
						// (If the batch recycled it, the caller can count freed bytes.)
					}
					else if (batchedRecycle) {
						// Batch ran but this file is still there.
						throw new IOException("the shell did not move the file to the recycle bin");
					}
					else if (recycleBin) {
						// Linux/macOS: attempt to move to system trash, fall back to
						// permanent delete (e.g. cross-filesystem files where trashing
						// means a full copy).
						if (!FileUtils.MoveToTrash(path)) {
							lock (result.Warnings) {
								if (result.Warnings.Count < 5)
									result.Warnings.Add($"File on different filesystem — will be permanently deleted instead of moved to trash: {Path.GetFileName(path)}");
								else if (result.Warnings.Count == 5)
									result.Warnings.Add($"... and more files on different filesystems");
							}
							File.Delete(path);
						}
					}
					else {
						File.Delete(path);
					}

					result.SucceededPaths.Add(path);
					result.Done++;
					RemoveFromDuplicatesAndDatabase(path);
				}
				catch (Exception ex) {
					result.Errors.Add($"{Path.GetFileName(path)}: {ex.Message}");
					result.Failed++;
				}
				progress?.Report(result.Done + result.Failed);
			}

			SaveDatabaseIfChanged(result.Done);
			return result;
		}

		/// <summary>
		/// Batch-moves files to pre-computed destination paths. The caller is
		/// responsible for destination-name collision avoidance. On success the
		/// source's database entry is updated to the new path (when an engine is
		/// attached) and the item is removed from <see cref="ScanEngine.Duplicates"/>.
		/// </summary>
		public Task<FileOperationResult> MoveAsync(IEnumerable<(string source, string dest)> moves, CancellationToken ct, IProgress<int>? progress = null) =>
			Task.Run(() => MoveCore(moves, ct, progress));

		FileOperationResult MoveCore(IEnumerable<(string source, string dest)> moves, CancellationToken ct, IProgress<int>? progress) {
			var result = new FileOperationResult();
			var list = moves.ToList();
			if (list.Count == 0) return result;

			foreach (var (source, dest) in list) {
				if (ct.IsCancellationRequested) break;
				try {
					File.Move(source, dest);
					if (_engine != null) {
						if (ScanEngine.GetFromDatabase(source, out var dbEntry) && dbEntry != null)
							ScanEngine.UpdateFilePathInDatabase(dest, dbEntry);
						RemoveFromDuplicatesByPath(source);
					}
					result.SucceededPaths.Add(source);
					result.Done++;
				}
				catch (Exception ex) {
					result.Errors.Add($"{Path.GetFileName(source)}: {ex.Message}");
					result.Failed++;
				}
				progress?.Report(result.Done + result.Failed);
			}

			SaveDatabaseIfChanged(result.Done);
			return result;
		}

		/// <summary>
		/// Replaces each <paramref name="linkPath"/> with a hardlink to
		/// <paramref name="target"/> using the safe temp-file + atomic-rename
		/// flow: create the link at a temporary path, delete the original, then
		/// rename the temp file into place. The original is only deleted after
		/// the link has been created, so a failure leaves the original intact.
		/// </summary>
		public Task<FileOperationResult> CreateHardLinksAsync(IEnumerable<(string target, string linkPath)> links, CancellationToken ct, IProgress<int>? progress = null) =>
			Task.Run(() => CreateLinksCore(links, hardLinks: true, ct, progress));

		/// <summary>Same safe flow as <see cref="CreateHardLinksAsync"/> but for symbolic links.</summary>
		public Task<FileOperationResult> CreateSymbolicLinksAsync(IEnumerable<(string target, string linkPath)> links, CancellationToken ct, IProgress<int>? progress = null) =>
			Task.Run(() => CreateLinksCore(links, hardLinks: false, ct, progress));

		FileOperationResult CreateLinksCore(IEnumerable<(string target, string linkPath)> links, bool hardLinks, CancellationToken ct, IProgress<int>? progress) {
			var result = new FileOperationResult();
			var list = links.ToList();
			if (list.Count == 0) return result;

			foreach (var (target, linkPath) in list) {
				if (ct.IsCancellationRequested) break;
				try {
					if (!File.Exists(linkPath)) {
						// Already gone — still remove the entry and database record.
					}
					else {
						if (!File.Exists(target))
							throw new IOException($"the file to keep ('{target}') does not exist");
						// Create link to a temporary path first, then delete original,
						// then rename. This ensures the original file is not lost if
						// link creation fails.
						string tempPath = linkPath + ".vdf_link_tmp";
						try {
							if (hardLinks)
								HardLinkUtils.CreateHardLink(tempPath, target);
							else
								File.CreateSymbolicLink(tempPath, target);
							File.Delete(linkPath);
							File.Move(tempPath, linkPath);
						}
						catch {
							try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* ignore */ }
							throw;
						}
					}

					result.SucceededPaths.Add(linkPath);
					result.Done++;
					RemoveFromDuplicatesAndDatabase(linkPath);
				}
				catch (Exception ex) {
					result.Errors.Add($"{Path.GetFileName(linkPath)}: {ex.Message}");
					result.Failed++;
				}
				progress?.Report(result.Done + result.Failed);
			}

			SaveDatabaseIfChanged(result.Done);
			return result;
		}

		/// <summary>
		/// Removes groups that have shrunk to a single remaining member — a group
		/// of one is no longer a duplicate. Operates on the attached
		/// <see cref="ScanEngine"/>'s <see cref="ScanEngine.Duplicates"/> collection
		/// and saves the database. No-op when no engine is attached.
		/// </summary>
		public void DropSingletonGroups() {
			if (_engine == null) return;

			var keep = _engine.Duplicates
				.GroupBy(d => d.GroupId)
				.Where(g => g.Count() > 1)
				.Select(g => g.Key)
				.ToHashSet();

			bool changed = false;
			foreach (var d in _engine.Duplicates.ToList())
				if (!keep.Contains(d.GroupId)) {
					_engine.Duplicates.Remove(d);
					changed = true;
				}

			if (changed)
				ScanEngine.SaveDatabase();
		}

		// ── Database / Duplicates sync helpers (only act when an engine is attached) ──

		void RemoveFromDuplicatesAndDatabase(string path) {
			if (_engine == null) return;
			// Path-only entry — FileEntry(string) stats the file and throws once it's gone.
			ScanEngine.RemoveFromDatabase(new FileEntry { Path = path });
			RemoveFromDuplicatesByPath(path);
		}

		void RemoveFromDuplicatesByPath(string path) {
			if (_engine == null) return;
			var match = _engine.Duplicates.FirstOrDefault(d =>
				string.Equals(d.Path, path, StringComparison.OrdinalIgnoreCase));
			if (match != null)
				_engine.Duplicates.Remove(match);
		}

		void SaveDatabaseIfChanged(int done) {
			if (_engine != null && done > 0)
				ScanEngine.SaveDatabase();
		}
	}
}
