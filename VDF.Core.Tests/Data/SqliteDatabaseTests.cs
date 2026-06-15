using Microsoft.Data.Sqlite;
using VDF.Core.Data;
using VDF.Core.ViewModels;

namespace VDF.Core.Tests.Data;

public class SqliteDatabaseTests : IDisposable {
	readonly string _dbPath;
	readonly string _tempDir;

	public SqliteDatabaseTests() {
		_tempDir = Path.Combine(Path.GetTempPath(), $"vdf-sqlite-tests-{Guid.NewGuid():N}");
		Directory.CreateDirectory(_tempDir);
		_dbPath = Path.Combine(_tempDir, "test.db");
	}

	public void Dispose() {
		try { Directory.Delete(_tempDir, recursive: true); } catch { }
	}

	[Fact]
	public void Open_CreatesDatabaseAndSchema() {
		using var db = new SqliteDatabase();
		db.Open(_dbPath);

		Assert.True(File.Exists(_dbPath));
		Assert.Equal(0, db.EntryCount);
	}

	[Fact]
	public void SaveFileEntry_And_LoadFileEntries_RoundTrip() {
		using var db = new SqliteDatabase();
		db.Open(_dbPath);

		var entry = new FileEntry {
			_Path = @"C:\test\video.mp4",
			Folder = @"C:\test",
			FileSize = 123456,
			DateCreated = new DateTime(2024, 1, 15, 10, 30, 0),
			DateModified = new DateTime(2024, 6, 1, 8, 0, 0),
			Flags = EntryFlags.NoAudioTrack,
		};

		db.SaveFileEntry(entry);
		Assert.Equal(1, db.EntryCount);

		var loaded = db.LoadFileEntries();
		Assert.Single(loaded);
		Assert.Equal(entry.Path, loaded[0].Path);
		Assert.Equal(entry.Folder, loaded[0].Folder);
		Assert.Equal(entry.FileSize, loaded[0].FileSize);
		Assert.Equal(entry.DateCreated, loaded[0].DateCreated);
		Assert.Equal(entry.DateModified, loaded[0].DateModified);
		Assert.Equal(entry.Flags, loaded[0].Flags);
	}

	[Fact]
	public void SaveFileEntries_Batch_RoundTrips() {
		using var db = new SqliteDatabase();
		db.Open(_dbPath);

		var entries = new List<FileEntry> {
			new() { _Path = @"C:\a.mp4", Folder = @"C:\", FileSize = 100, DateCreated = DateTime.MinValue, DateModified = DateTime.MaxValue },
			new() { _Path = @"C:\b.mp4", Folder = @"C:\", FileSize = 200, DateCreated = DateTime.Now, DateModified = DateTime.Now, Flags = EntryFlags.IsImage },
			new() { _Path = @"C:\c.mp4", Folder = @"C:\sub", FileSize = 300, DateCreated = DateTime.UtcNow, DateModified = DateTime.UtcNow },
		};

		db.SaveFileEntries(entries);
		Assert.Equal(3, db.EntryCount);

		var loaded = db.LoadFileEntries();
		Assert.Equal(3, loaded.Count);
	}

	[Fact]
	public void Open_WALModeIsEnabled() {
		using var db = new SqliteDatabase();
		db.Open(_dbPath);

		// Verify WAL mode by querying the journal_mode pragma directly
		using var conn = new SqliteConnection($"Data Source={_dbPath}");
		conn.Open();
		using var cmd = conn.CreateCommand();
		cmd.CommandText = "PRAGMA journal_mode";
		var mode = cmd.ExecuteScalar()?.ToString();
		Assert.Equal("wal", mode, StringComparer.OrdinalIgnoreCase);
	}

	[Fact]
	public void SaveFileEntry_DuplicatePath_ReplacesEntry() {
		using var db = new SqliteDatabase();
		db.Open(_dbPath);

		var entry1 = new FileEntry { _Path = @"C:\dup.mp4", Folder = @"C:\", FileSize = 100, DateCreated = DateTime.Now, DateModified = DateTime.Now };
		var entry2 = new FileEntry { _Path = @"C:\dup.mp4", Folder = @"C:\", FileSize = 999, DateCreated = DateTime.Now, DateModified = DateTime.Now };

		db.SaveFileEntry(entry1);
		db.SaveFileEntry(entry2);

		Assert.Equal(1, db.EntryCount);
		var loaded = db.LoadFileEntries();
		Assert.Equal(999, loaded[0].FileSize);
	}

	[Fact]
	public void RemoveFileEntry_RemovesSpecificEntry() {
		using var db = new SqliteDatabase();
		db.Open(_dbPath);

		db.SaveFileEntry(new FileEntry { _Path = @"C:\keep.mp4", Folder = @"C:\", FileSize = 1, DateCreated = DateTime.Now, DateModified = DateTime.Now });
		db.SaveFileEntry(new FileEntry { _Path = @"C:\remove.mp4", Folder = @"C:\", FileSize = 2, DateCreated = DateTime.Now, DateModified = DateTime.Now });

		Assert.Equal(2, db.EntryCount);
		db.RemoveFileEntry(@"C:\remove.mp4");
		Assert.Equal(1, db.EntryCount);

		var loaded = db.LoadFileEntries();
		Assert.Single(loaded);
		Assert.Equal(@"C:\keep.mp4", loaded[0].Path);
	}

	[Fact]
	public void CleanDatabase_RemovesNonExistentFiles() {
		using var db = new SqliteDatabase();
		db.Open(_dbPath);

		// Create a real temp file so it "exists"
		string realFile = Path.Combine(_tempDir, "real.mp4");
		File.WriteAllText(realFile, "fake");

		// Entry for a file that exists on disk → should be kept
		db.SaveFileEntry(new FileEntry { _Path = realFile, Folder = _tempDir, FileSize = 4, DateCreated = DateTime.Now, DateModified = DateTime.Now });
		// Entry for a file that doesn't exist → should be removed
		db.SaveFileEntry(new FileEntry { _Path = @"C:\nonexistent\phantom.mp4", Folder = @"C:\nonexistent", FileSize = 0, DateCreated = DateTime.Now, DateModified = DateTime.Now });

		Assert.Equal(2, db.EntryCount);

		int removed = db.CleanDatabase();
		Assert.Equal(1, removed);
		Assert.Equal(1, db.EntryCount);
	}

	[Fact]
	public void ClearDatabase_RemovesAllEntries() {
		using var db = new SqliteDatabase();
		db.Open(_dbPath);

		db.SaveFileEntry(new FileEntry { _Path = @"C:\a.mp4", Folder = @"C:\", FileSize = 1, DateCreated = DateTime.Now, DateModified = DateTime.Now });
		db.SaveFileEntry(new FileEntry { _Path = @"C:\b.mp4", Folder = @"C:\", FileSize = 2, DateCreated = DateTime.Now, DateModified = DateTime.Now });

		Assert.Equal(2, db.EntryCount);
		db.ClearAllEntries();
		Assert.Equal(0, db.EntryCount);
	}

	[Fact]
	public void ParameterizedQueries_PreventSqlInjection() {
		using var db = new SqliteDatabase();
		db.Open(_dbPath);

		// Attempt SQL injection via a path value
		string maliciousPath = @"C:\test'; DROP TABLE FileEntries; --";
		db.SaveFileEntry(new FileEntry { _Path = maliciousPath, Folder = @"C:\test", FileSize = 1, DateCreated = DateTime.Now, DateModified = DateTime.Now });

		// Table should still exist and contain the entry
		Assert.Equal(1, db.EntryCount);
		var loaded = db.LoadFileEntries();
		Assert.Single(loaded);
		Assert.Equal(maliciousPath, loaded[0].Path);
	}

	[Fact]
	public void SaveAndLoadDuplicateItems_RoundTrip() {
		using var db = new SqliteDatabase();
		db.Open(_dbPath);

		var items = new HashSet<DuplicateItem> {
			new() { Path = @"C:\a.mp4", GroupId = Guid.NewGuid(), SizeLong = 100, Similarity = 96f },
			new() { Path = @"C:\b.mp4", GroupId = Guid.NewGuid(), SizeLong = 200, Similarity = 98f },
		};

		db.SaveDuplicateItems(items);
		var loaded = db.LoadDuplicateItems();
		Assert.Equal(2, loaded.Count);
	}
}
