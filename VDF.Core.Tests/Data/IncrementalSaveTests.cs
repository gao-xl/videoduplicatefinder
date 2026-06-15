using VDF.Core.Data;

namespace VDF.Core.Tests.Data;

public class IncrementalSaveTests : IDisposable {
	readonly string _dbPath;
	readonly string _tempDir;

	public IncrementalSaveTests() {
		_tempDir = Path.Combine(Path.GetTempPath(), $"vdf-incremental-tests-{Guid.NewGuid():N}");
		Directory.CreateDirectory(_tempDir);
		_dbPath = Path.Combine(_tempDir, "test.db");
	}

	public void Dispose() {
		try { Directory.Delete(_tempDir, recursive: true); } catch { }
	}

	[Fact]
	public void FileEntry_DirtyFlag_DefaultFalse() {
		var entry = new FileEntry();
		Assert.False(entry.dirty);
	}

	[Fact]
	public void FileEntry_DirtyFlag_SetTrue() {
		var entry = new FileEntry();
		entry.dirty = true;
		Assert.True(entry.dirty);
	}

	[Fact]
	public void FileEntry_DirtyFlag_NotSerialized() {
		// dirty flag should not affect MemoryPack serialization
		var entry = new FileEntry { _Path = "/test.mp4", Folder = "/", dirty = true };
		var bytes = MemoryPack.MemoryPackSerializer.Serialize(entry);
		var deserialized = MemoryPack.MemoryPackSerializer.Deserialize<FileEntry>(bytes);
		Assert.False(deserialized!.dirty); // dirty is not persisted
		Assert.Equal("/test.mp4", deserialized.Path);
	}

	[Fact]
	public void SaveDirtyFileEntries_OnlySavesDirtyEntries() {
		using var db = new SqliteDatabase();
		db.Open(_dbPath);

		var dirtyEntry = new FileEntry {
			_Path = @"C:\dirty.mp4",
			Folder = @"C:\",
			FileSize = 100,
			DateCreated = DateTime.Now,
			DateModified = DateTime.Now,
		};
		dirtyEntry.dirty = true;

		var cleanEntry = new FileEntry {
			_Path = @"C:\clean.mp4",
			Folder = @"C:\",
			FileSize = 200,
			DateCreated = DateTime.Now,
			DateModified = DateTime.Now,
		};
		// cleanEntry.dirty is false by default

		// Save the clean entry first via full save
		db.SaveFileEntry(cleanEntry);

		// Now save dirty entries — only the dirty one should be saved
		int saved = db.SaveDirtyFileEntries(new[] { dirtyEntry, cleanEntry });
		Assert.Equal(1, saved);

		// Both entries should be in the database
		var loaded = db.LoadFileEntries();
		Assert.Equal(2, loaded.Count);

		// Verify the dirty entry was saved correctly
		var loadedDirty = loaded.FirstOrDefault(e => e.Path == @"C:\dirty.mp4");
		Assert.NotNull(loadedDirty);
		Assert.Equal(100, loadedDirty.FileSize);
	}

	[Fact]
	public void SaveDirtyFileEntries_NoDirtyEntries_SavesNone() {
		using var db = new SqliteDatabase();
		db.Open(_dbPath);

		var entry1 = new FileEntry {
			_Path = @"C:\a.mp4",
			Folder = @"C:\",
			FileSize = 100,
			DateCreated = DateTime.Now,
			DateModified = DateTime.Now,
		};
		var entry2 = new FileEntry {
			_Path = @"C:\b.mp4",
			Folder = @"C:\",
			FileSize = 200,
			DateCreated = DateTime.Now,
			DateModified = DateTime.Now,
		};

		// Both clean — nothing should be saved
		int saved = db.SaveDirtyFileEntries(new[] { entry1, entry2 });
		Assert.Equal(0, saved);
		Assert.Equal(0, db.EntryCount);
	}

	[Fact]
	public void SaveDirtyFileEntries_UpdatesExistingEntry() {
		using var db = new SqliteDatabase();
		db.Open(_dbPath);

		var entry = new FileEntry {
			_Path = @"C:\update.mp4",
			Folder = @"C:\",
			FileSize = 100,
			DateCreated = DateTime.Now,
			DateModified = DateTime.Now,
		};

		// Initial save
		db.SaveFileEntry(entry);
		Assert.Equal(1, db.EntryCount);

		// Modify and mark dirty
		entry.FileSize = 999;
		entry.dirty = true;

		int saved = db.SaveDirtyFileEntries(new[] { entry });
		Assert.Equal(1, saved);

		// Verify the update
		var loaded = db.LoadFileEntries();
		Assert.Single(loaded);
		Assert.Equal(999, loaded[0].FileSize);
	}

	[Fact]
	public void SaveDirtyFileEntries_WithHeavyFields() {
		using var db = new SqliteDatabase();
		db.Open(_dbPath);

		var entry = new FileEntry {
			_Path = @"C:\heavy.mp4",
			Folder = @"C:\",
			FileSize = 500,
			DateCreated = DateTime.Now,
			DateModified = DateTime.Now,
			mediaInfo = new MediaInfo {
				Duration = TimeSpan.FromSeconds(120),
				Streams = new[] { new MediaInfo.StreamInfo { Width = 1920, Height = 1080 } }
			},
		};
		entry.grayBytes.TryAdd(0.5, new byte[1024]);
		entry.PHashes.TryAdd(0.5, 0xABCDEF0123456789UL);
		entry.AudioFingerprint = new uint[] { 1, 2, 3, 4, 5 };
		entry.dirty = true;

		int saved = db.SaveDirtyFileEntries(new[] { entry });
		Assert.Equal(1, saved);

		var loaded = db.LoadFileEntries();
		Assert.Single(loaded);
		Assert.Equal(500, loaded[0].FileSize);
		Assert.NotNull(loaded[0].mediaInfo);
		Assert.NotEmpty(loaded[0].grayBytes);
		Assert.NotEmpty(loaded[0].PHashes);
		Assert.NotEmpty(loaded[0].AudioFingerprint!);
	}
}
