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
using VDF.Core.Utils;
using VDF.Core.ViewModels;

namespace VDF.Core.Tests.Services;

/// <summary>
/// Integration tests for <see cref="FileOperationsService"/> covering permanent
/// delete, recycle-bin delete (with platform fallback), hardlink/symlink creation
/// via the safe temp-file + atomic-rename flow, <see cref="FileOperationsService.DropSingletonGroups"/>,
/// and database/duplicates sync when a <see cref="ScanEngine"/> is attached.
/// </summary>
public class FileOperationsServiceTests : IDisposable {

	readonly string _tempDir;
	readonly string? _origCustomDatabaseFolder;
	readonly object? _origSqliteDb;

	public FileOperationsServiceTests() {
		_tempDir = Path.Combine(Path.GetTempPath(), $"vdf-fileops-tests-{Guid.NewGuid():N}");
		Directory.CreateDirectory(_tempDir);

		// Redirect the static DatabaseUtils to a temp folder so SaveDatabase()
		// (called by DropSingletonGroups / DeleteAsync when an engine is attached)
		// never touches the user's real scan database. Reset the cached
		// SqliteDatabase so it re-opens at the temp path.
		_origCustomDatabaseFolder = DatabaseUtils.CustomDatabaseFolder;
		_origSqliteDb = typeof(DatabaseUtils)
			.GetField("_sqliteDb", BindingFlags.NonPublic | BindingFlags.Static)
			?.GetValue(null);
		DatabaseUtils.CustomDatabaseFolder = _tempDir;
		DatabaseUtils.InvalidateDatabaseFolder();
		typeof(DatabaseUtils)
			.GetField("_sqliteDb", BindingFlags.NonPublic | BindingFlags.Static)
			?.SetValue(null, null);
	}

	public void Dispose() {
		DatabaseUtils.CustomDatabaseFolder = _origCustomDatabaseFolder;
		DatabaseUtils.InvalidateDatabaseFolder();
		typeof(DatabaseUtils)
			.GetField("_sqliteDb", BindingFlags.NonPublic | BindingFlags.Static)
			?.SetValue(null, _origSqliteDb);
		try { Directory.Delete(_tempDir, recursive: true); } catch { }
	}

	string CreateTempFile(string name, byte[]? content = null) {
		string path = Path.Combine(_tempDir, name);
		File.WriteAllBytes(path, content ?? new byte[] { 1, 2, 3 });
		return path;
	}

	// ── DeleteAsync (null engine — pure file I/O, no database side effects) ──

	[Fact]
	public async Task PermanentDelete_RemovesFilesFromDisk() {
		string a = CreateTempFile("a.txt", new byte[] { 1 });
		string b = CreateTempFile("b.txt", new byte[] { 2 });
		var svc = new FileOperationsService(null);

		var result = await svc.DeleteAsync(new[] { a, b }, recycleBin: false, CancellationToken.None);

		Assert.Equal(2, result.Done);
		Assert.Equal(0, result.Failed);
		Assert.False(File.Exists(a));
		Assert.False(File.Exists(b));
		Assert.Contains(a, result.SucceededPaths);
		Assert.Contains(b, result.SucceededPaths);
	}

	[Fact]
	public async Task RecycleBinDelete_FilesRemovedFromDisk() {
		string a = CreateTempFile("recycle-a.txt", new byte[] { 1 });
		var svc = new FileOperationsService(null);

		var result = await svc.DeleteAsync(new[] { a }, recycleBin: true, CancellationToken.None);

		Assert.Equal(1, result.Done);
		Assert.Equal(0, result.Failed);
		// On all platforms the file should be gone (recycled on Windows, trashed
		// or permanently deleted on Linux/macOS).
		Assert.False(File.Exists(a));
	}

	[Fact]
	public async Task DeleteAsync_NonExistentFile_TreatedAsSucceeded() {
		string ghost = Path.Combine(_tempDir, "does-not-exist.txt");
		var svc = new FileOperationsService(null);

		var result = await svc.DeleteAsync(new[] { ghost }, recycleBin: false, CancellationToken.None);

		Assert.Equal(1, result.Done);
		Assert.Equal(0, result.Failed);
		Assert.Contains(ghost, result.SucceededPaths);
	}

	[Fact]
	public async Task DeleteAsync_ReportsProgress() {
		string a = CreateTempFile("prog-a.txt");
		string b = CreateTempFile("prog-b.txt");
		var svc = new FileOperationsService(null);
		var reports = new List<int>();

		var result = await svc.DeleteAsync(new[] { a, b }, recycleBin: false, CancellationToken.None,
			new SyncProgress(reports.Add));

		// Progress should have been reported at least once per file.
		Assert.NotEmpty(reports);
		Assert.Equal(2, result.Done);
	}

	[Fact]
	public async Task DeleteAsync_MultipleFiles_AllSucceed() {
		string a = CreateTempFile("multi-a.txt");
		string b = CreateTempFile("multi-b.txt");
		string c = CreateTempFile("multi-c.txt");
		var svc = new FileOperationsService(null);

		var result = await svc.DeleteAsync(new[] { a, b, c }, recycleBin: false, CancellationToken.None);

		Assert.Equal(3, result.Done);
		Assert.Equal(0, result.Failed);
		Assert.False(File.Exists(a));
		Assert.False(File.Exists(b));
		Assert.False(File.Exists(c));
	}

	// ── CreateHardLinksAsync (safe temp-file + atomic-rename flow) ──

	[Fact]
	public async Task CreateHardLinks_SafeFlow_ReplacesOriginalWithHardLink() {
		string target = CreateTempFile("target.txt", new byte[] { 10, 20, 30 });
		string linkPath = CreateTempFile("link.txt", new byte[] { 99 });
		var svc = new FileOperationsService(null);

		var result = await svc.CreateHardLinksAsync(
			new[] { (target, linkPath) }, CancellationToken.None);

		Assert.Equal(1, result.Done);
		Assert.Equal(0, result.Failed);
		Assert.True(File.Exists(linkPath));
		// The link should now point to the same inode / file record as the target.
		Assert.True(HardLinkUtils.AreSameFile(linkPath, target));
		// Content should match the target, not the original link file.
		Assert.Equal(new byte[] { 10, 20, 30 }, File.ReadAllBytes(linkPath));
	}

	[Fact]
	public async Task CreateHardLinks_NonExistentTarget_ReportsFailure() {
		string target = Path.Combine(_tempDir, "missing-target.txt");
		string linkPath = CreateTempFile("link.txt");
		var svc = new FileOperationsService(null);

		var result = await svc.CreateHardLinksAsync(
			new[] { (target, linkPath) }, CancellationToken.None);

		Assert.Equal(0, result.Done);
		Assert.Equal(1, result.Failed);
		// Original file should still be intact (safe flow leaves it on failure).
		Assert.True(File.Exists(linkPath));
	}

	// ── CreateSymbolicLinksAsync (safe temp-file + atomic-rename flow) ──

	[Fact]
	public async Task CreateSymbolicLinks_SafeFlow_ReplacesOriginalWithSymLink() {
		string target = CreateTempFile("sym-target.txt", new byte[] { 40, 50 });
		string linkPath = CreateTempFile("sym-link.txt", new byte[] { 0 });
		var svc = new FileOperationsService(null);

		var result = await svc.CreateSymbolicLinksAsync(
			new[] { (target, linkPath) }, CancellationToken.None);

		// Symlink creation may fail on Windows without developer mode / admin.
		// Skip gracefully if the platform doesn't allow it.
		if (result.Failed > 0 && result.Done == 0) {
			// Re-create the original file (the service may have deleted it before
			// the symlink call failed) so Dispose cleanup doesn't complain.
			if (!File.Exists(linkPath)) File.WriteAllBytes(linkPath, new byte[] { 0 });
			return;
		}

		Assert.Equal(1, result.Done);
		Assert.Equal(0, result.Failed);
		Assert.True(File.Exists(linkPath));
		var resolved = File.ResolveLinkTarget(linkPath, returnFinalTarget: false);
		Assert.NotNull(resolved);
		Assert.Equal(target, resolved.FullName);
	}

	// ── MoveAsync ──

	[Fact]
	public async Task MoveAsync_MovesFilesToDestination() {
		string src = CreateTempFile("move-src.txt", new byte[] { 7 });
		string dest = Path.Combine(_tempDir, "move-dest.txt");
		var svc = new FileOperationsService(null);

		var result = await svc.MoveAsync(
			new[] { (src, dest) }, CancellationToken.None);

		Assert.Equal(1, result.Done);
		Assert.Equal(0, result.Failed);
		Assert.False(File.Exists(src));
		Assert.True(File.Exists(dest));
		Assert.Equal(new byte[] { 7 }, File.ReadAllBytes(dest));
	}

	// ── DropSingletonGroups (requires an attached ScanEngine) ──

	[Fact]
	public void DropSingletonGroups_RemovesGroupsWithSingleMember() {
		var engine = new ScanEngine();
		var groupA = Guid.NewGuid();
		var groupB = Guid.NewGuid();
		engine.Duplicates.Add(new DuplicateItem { Path = Path.Combine(_tempDir, "a1.txt"), GroupId = groupA });
		engine.Duplicates.Add(new DuplicateItem { Path = Path.Combine(_tempDir, "b1.txt"), GroupId = groupB });
		engine.Duplicates.Add(new DuplicateItem { Path = Path.Combine(_tempDir, "b2.txt"), GroupId = groupB });
		var svc = new FileOperationsService(engine);

		svc.DropSingletonGroups();

		// Group A had only one member → removed. Group B had two → kept.
		Assert.DoesNotContain(engine.Duplicates, d => d.GroupId == groupA);
		Assert.Equal(2, engine.Duplicates.Count);
		Assert.All(engine.Duplicates, d => Assert.Equal(groupB, d.GroupId));
	}

	[Fact]
	public void DropSingletonGroups_KeepsGroupsWithMultipleMembers() {
		var engine = new ScanEngine();
		var group = Guid.NewGuid();
		engine.Duplicates.Add(new DuplicateItem { Path = Path.Combine(_tempDir, "x1.txt"), GroupId = group });
		engine.Duplicates.Add(new DuplicateItem { Path = Path.Combine(_tempDir, "x2.txt"), GroupId = group });
		engine.Duplicates.Add(new DuplicateItem { Path = Path.Combine(_tempDir, "x3.txt"), GroupId = group });
		var svc = new FileOperationsService(engine);

		svc.DropSingletonGroups();

		Assert.Equal(3, engine.Duplicates.Count);
	}

	[Fact]
	public void DropSingletonGroups_NoEngine_IsNoOp() {
		var svc = new FileOperationsService(null);
		// Should not throw.
		svc.DropSingletonGroups();
	}

	// ── DeleteAsync with engine — Duplicates sync ──

	[Fact]
	public async Task DeleteAsync_WithEngine_RemovesFromDuplicates() {
		var engine = new ScanEngine();
		var group = Guid.NewGuid();
		string a = CreateTempFile("dup-a.txt");
		string b = CreateTempFile("dup-b.txt");
		engine.Duplicates.Add(new DuplicateItem { Path = a, GroupId = group, SizeLong = 100 });
		engine.Duplicates.Add(new DuplicateItem { Path = b, GroupId = group, SizeLong = 100 });
		var svc = new FileOperationsService(engine);

		var result = await svc.DeleteAsync(new[] { a }, recycleBin: false, CancellationToken.None);

		Assert.Equal(1, result.Done);
		Assert.False(File.Exists(a));
		// The deleted item should have been removed from the engine's Duplicates.
		Assert.DoesNotContain(engine.Duplicates, d => d.Path == a);
		Assert.Single(engine.Duplicates);
		Assert.Equal(b, engine.Duplicates.First().Path);
	}

	// ── CreateHardLinksAsync with engine — Duplicates sync ──

	[Fact]
	public async Task CreateHardLinks_WithEngine_RemovesFromDuplicates() {
		var engine = new ScanEngine();
		var group = Guid.NewGuid();
		string target = CreateTempFile("keep.txt", new byte[] { 1, 2 });
		string linkPath = CreateTempFile("replace.txt", new byte[] { 9 });
		engine.Duplicates.Add(new DuplicateItem { Path = target, GroupId = group, SizeLong = 2 });
		engine.Duplicates.Add(new DuplicateItem { Path = linkPath, GroupId = group, SizeLong = 2 });
		var svc = new FileOperationsService(engine);

		var result = await svc.CreateHardLinksAsync(
			new[] { (target, linkPath) }, CancellationToken.None);

		Assert.Equal(1, result.Done);
		// The replaced item should have been removed from Duplicates.
		Assert.DoesNotContain(engine.Duplicates, d => d.Path == linkPath);
		Assert.Single(engine.Duplicates);
		Assert.Equal(target, engine.Duplicates.First().Path);
	}

	// ── Helpers ──

	sealed class SyncProgress : IProgress<int> {
		readonly Action<int> _action;
		public SyncProgress(Action<int> action) => _action = action;
		public void Report(int value) => _action(value);
	}
}
