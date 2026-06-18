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
//

using System.IO.Enumeration;
using System.Linq;
using VDF.Core.Utils;

namespace VDF.Core;

/// <summary>
/// Handles file enumeration, filtering, and database population for scanning.
/// Extracted from ScanEngine to improve separation of concerns.
/// </summary>
internal sealed class FileEnumerator {
	readonly Settings _settings;
	readonly ScanEngine.PathMatcher? _includeMatcher;

	public FileEnumerator(Settings settings) {
		_settings = settings;
		_includeMatcher = new ScanEngine.PathMatcher(settings.IncludeList);
	}

	/// <summary>
	/// Enumerates files from configured directories and populates the database.
	/// Handles network paths with timeout and retry logic.
	/// </summary>
	public async Task BuildFileList(CancellationToken cancellationToken) {
		await Task.Run(() => {
			DatabaseUtils.LoadDatabase();
			if (DatabaseUtils.DbVersion < 2)
				_settings.UsePHashing = false;

			int oldFileCount = DatabaseUtils.Database.Count;

			foreach (string path in _settings.IncludeList) {
				if (cancellationToken.IsCancellationRequested)
					return;
				if (!Directory.Exists(path)) {
					Logger.Instance.Info($"WARNING: Search directory not found or inaccessible, skipping: '{path}'. If this is a network drive, make sure it is connected (or use the \\\\server\\share UNC path instead of a drive letter).");
					continue;
				}

				bool isNetworkPath = IsNetworkPath(path);
				int networkTimeoutMs = isNetworkPath ? _settings.NetworkPathTimeoutSeconds * 1000 : 0;

				List<FileInfo> files;
				if (isNetworkPath && networkTimeoutMs > 0) {
					var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
					cts.CancelAfter(networkTimeoutMs);
					try {
						files = FileUtils.GetFilesRecursive(path, _settings.IgnoreReadOnlyFolders, _settings.IgnoreReparsePoints,
							_settings.IncludeSubDirectories, _settings.IncludeImages, _settings.BlackList.ToList(), cts.Token);
					}
					catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
						Logger.Instance.Info($"WARNING: Network path enumeration timed out after {_settings.NetworkPathTimeoutSeconds}s, skipping: '{path}'. Increase NetworkPathTimeoutSeconds in settings if the share is slow.");
						continue;
					}
				}
				else {
					files = FileUtils.GetFilesRecursive(path, _settings.IgnoreReadOnlyFolders, _settings.IgnoreReparsePoints,
						_settings.IncludeSubDirectories, _settings.IncludeImages, _settings.BlackList.ToList(), cancellationToken);
				}

				foreach (FileInfo file in files) {
					if (cancellationToken.IsCancellationRequested)
						return;
					FileEntry? fEntry = null;
					try {
						if (IsNetworkPath(file.FullName) && _settings.NetworkRetryCount > 0) {
							for (int attempt = 0; attempt <= _settings.NetworkRetryCount; attempt++) {
								try {
									fEntry = new(file);
									break;
								}
								catch (UnauthorizedAccessException ex) when (attempt < _settings.NetworkRetryCount) {
									int delayMs = (int)Math.Pow(2, attempt) * 1000;
									Logger.Instance.Info($"Network access error creating entry for '{file}' (attempt {attempt + 1}/{_settings.NetworkRetryCount + 1}), retrying in {delayMs}ms: {ex.Message}");
									Thread.Sleep(delayMs);
								}
								catch (IOException ex) when (attempt < _settings.NetworkRetryCount) {
									int delayMs = (int)Math.Pow(2, attempt) * 1000;
									Logger.Instance.Info($"Network I/O error creating entry for '{file}' (attempt {attempt + 1}/{_settings.NetworkRetryCount + 1}), retrying in {delayMs}ms: {ex.Message}");
									Thread.Sleep(delayMs);
								}
							}
						}
						else {
							fEntry = new(file);
						}
					}
					catch (UnauthorizedAccessException ex) {
						Logger.Instance.Info($"Skipped file '{file}' because of access denied: {ex.Message}");
						continue;
					}
					catch (IOException ex) when (IsNetworkPath(file.FullName)) {
						Logger.Instance.Info($"Skipped file '{file}' because of network I/O error after retries: {ex.Message}");
						continue;
					}
					catch (Exception e) {
						Logger.Instance.Info($"Skipped file '{file}' because of {e}");
						continue;
					}
					if (fEntry == null) continue;
					if (!DatabaseUtils.Database.TryGetValue(fEntry, out var dbEntry))
						DatabaseUtils.Database.Add(fEntry);
					else if (fEntry.DateCreated != dbEntry.DateCreated ||
							fEntry.DateModified != dbEntry.DateModified ||
							fEntry.FileSize != dbEntry.FileSize) {
						DatabaseUtils.Database.Remove(dbEntry);
						DatabaseUtils.Database.Add(fEntry);
					}
				}
			}

			Logger.Instance.Info($"Files in database: {DatabaseUtils.Database.Count:N0} ({DatabaseUtils.Database.Count - oldFileCount:N0} files added)");
		});
	}

	/// <summary>
	/// Checks if a file entry should be excluded from the scan.
	/// Returns true if the entry is invalid (should be excluded).
	/// </summary>
	public bool InvalidEntry(FileEntry entry, out bool reportProgress, out string? reason) {
		reportProgress = true;
		reason = null;

		if (_settings.IncludeImages == false && entry.IsImage) {
			reason = "image files are disabled";
			return true;
		}
		if (_settings.BlackList.Any(f => IsBlackListed(entry.Folder, f))) {
			reason = "path is in the excluded directories list";
			return true;
		}

		if (!_settings.ScanAgainstEntireDatabase) {
			if (_settings.IncludeSubDirectories == false) {
				if (!_settings.IncludeList.Contains(entry.Folder)) {
					reportProgress = false;
					reason = "path is not in the included directories list";
					return true;
				}
			}
			else if (_includeMatcher != null && !_includeMatcher.IsIncluded(entry.Folder)) {
				reportProgress = false;
				reason = "path is not in the included directories list";
				return true;
			}
			else if (_includeMatcher == null && !_settings.IncludeList.Any(f => {
				if (!entry.Folder.StartsWith(f))
					return false;
				if (entry.Folder.Length == f.Length)
					return true;
				string relativePath = Path.GetRelativePath(f, entry.Folder);
				return !relativePath.StartsWith('.') && !Path.IsPathRooted(relativePath);
			})) {
				reportProgress = false;
				reason = "path is not in the included directories list";
				return true;
			}
		}

		if (entry.Flags.Has(EntryFlags.ManuallyExcluded)) {
			reason = "file has been manually excluded";
			return true;
		}

		if (entry.Flags.Has(EntryFlags.MetadataError) && !_settings.AlwaysRetryFailedSampling) {
			reason = "metadata extraction previously failed";
			return true;
		}

		if (entry.Flags.Has(EntryFlags.ThumbnailError) && !_settings.AlwaysRetryFailedSampling) {
			reason = "thumbnail sampling previously failed";
			return true;
		}

		return false;
	}

	/// <summary>
	/// Determines whether a path is likely a network path (SMB/NFS share).
	/// On Windows: UNC paths (\\server\share) or drive letters mapped to network shares.
	/// On Linux/macOS: paths starting with /mnt/, /media/, /srv/, or NFS mounts.
	/// </summary>
	public static bool IsNetworkPath(string path) {
		if (string.IsNullOrEmpty(path)) return false;
		if (CoreUtils.IsWindows) {
			// UNC path: \\server\share
			if (path.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase))
				return true;
			// Check if a drive root is a network drive (e.g. Z:\)
			if (path.Length >= 2 && path[1] == ':') {
				try {
					var driveRoot = path.Substring(0, 2) + Path.DirectorySeparatorChar;
					var driveInfo = new DriveInfo(driveRoot);
					if (driveInfo.DriveType == DriveType.Network)
						return true;
				}
				catch { }
			}
			return false;
		}
		// Linux/macOS heuristics
		if (path.StartsWith("/mnt/", StringComparison.OrdinalIgnoreCase) ||
			path.StartsWith("/media/", StringComparison.OrdinalIgnoreCase) ||
			path.StartsWith("/srv/", StringComparison.OrdinalIgnoreCase) ||
			path.StartsWith("/nas/", StringComparison.OrdinalIgnoreCase))
			return true;
		return false;
	}

	/// <summary>
	/// Returns true if folderPath is covered by blacklistEntry.
	/// Supports wildcard patterns (*, ?) in blacklistEntry — see https://github.com/0x90d/videoduplicatefinder/issues/582
	/// </summary>
	public static bool IsBlackListed(string folderPath, string blacklistEntry) {
		bool hasWildcard = blacklistEntry.IndexOfAny(['*', '?']) >= 0;
		if (!hasWildcard) {
			if (!folderPath.StartsWith(blacklistEntry, StringComparison.OrdinalIgnoreCase))
				return false;
			if (folderPath.Length == blacklistEntry.Length)
				return true;
			// Reason: https://github.com/0x90d/videoduplicatefinder/issues/249
			string relativePath = Path.GetRelativePath(blacklistEntry, folderPath);
			return !relativePath.StartsWith('.') && !Path.IsPathRooted(relativePath);
		}
		// Wildcard pattern without path separators: match against each individual segment of folderPath
		bool hasSeparator = blacklistEntry.Contains(Path.DirectorySeparatorChar) ||
		                    blacklistEntry.Contains(Path.AltDirectorySeparatorChar);
		if (!hasSeparator) {
			string[] segments = folderPath.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
				StringSplitOptions.RemoveEmptyEntries);
			return segments.Any(s => System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(blacklistEntry, s));
		}
		// Wildcard pattern with path separators: match against the full path
		return System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(blacklistEntry, folderPath);
	}
}
