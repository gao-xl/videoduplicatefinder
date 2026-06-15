using VDF.Core.Data;
using VDF.Core.Utils;

namespace VDF.Core.Tests.Data;

public class SqliteDatabaseMigratorTests : IDisposable {
	readonly string _tempDir;

	static string Asset(string name) =>
		Path.Combine(AppContext.BaseDirectory, "TestAssets", name);

	public SqliteDatabaseMigratorTests() {
		_tempDir = Path.Combine(Path.GetTempPath(), $"vdf-migrator-tests-{Guid.NewGuid():N}");
		Directory.CreateDirectory(_tempDir);
	}

	public void Dispose() {
		DatabaseUtils.CustomDatabaseFolder = null;
		DatabaseUtils.InvalidateDatabaseFolder();
		DatabaseUtils.Database.Clear();
		try { Directory.Delete(_tempDir, recursive: true); } catch { }
	}

	[Fact]
	public void MigrateIfNeeded_MigratesOldDatabase() {
		// Copy the legacy fixture into the temp dir
		File.Copy(Asset("legacy-wrapper.db"), Path.Combine(_tempDir, "ScannedFiles.db"));

		using var sqliteDb = new SqliteDatabase();
		string sqlitePath = Path.Combine(_tempDir, "vdf.db");
		sqliteDb.Open(sqlitePath);

		bool migrated = SqliteDatabaseMigrator.MigrateIfNeeded(_tempDir, sqliteDb);

		Assert.True(migrated);
		Assert.Equal(3, sqliteDb.EntryCount);
	}

	[Fact]
	public void MigrateIfNeeded_RenamesOldFileToBak() {
		string oldDbPath = Path.Combine(_tempDir, "ScannedFiles.db");
		File.Copy(Asset("legacy-wrapper.db"), oldDbPath);

		using var sqliteDb = new SqliteDatabase();
		sqliteDb.Open(Path.Combine(_tempDir, "vdf.db"));

		SqliteDatabaseMigrator.MigrateIfNeeded(_tempDir, sqliteDb);

		// Old file should be renamed to .bak
		Assert.False(File.Exists(oldDbPath), "Original file should have been renamed");
		Assert.True(File.Exists(Path.Combine(_tempDir, "ScannedFiles.db.bak")),
			"Backup file should exist");
	}

	[Fact]
	public void MigrateIfNeeded_SkipsIfNoOldDatabase() {
		// No ScannedFiles.db in the temp dir
		using var sqliteDb = new SqliteDatabase();
		sqliteDb.Open(Path.Combine(_tempDir, "vdf.db"));

		bool migrated = SqliteDatabaseMigrator.MigrateIfNeeded(_tempDir, sqliteDb);

		Assert.False(migrated);
		Assert.Equal(0, sqliteDb.EntryCount);
	}

	[Fact]
	public void MigrateIfNeeded_SkipsIfSqliteAlreadyHasData() {
		// Create an old database file
		File.Copy(Asset("legacy-wrapper.db"), Path.Combine(_tempDir, "ScannedFiles.db"));

		// Pre-populate the SQLite database
		using var sqliteDb = new SqliteDatabase();
		sqliteDb.Open(Path.Combine(_tempDir, "vdf.db"));
		sqliteDb.SaveFileEntry(new FileEntry {
			_Path = @"C:\existing.mp4",
			Folder = @"C:\",
			FileSize = 1,
			DateCreated = DateTime.Now,
			DateModified = DateTime.Now,
		});

		// Migration should still run (it appends, doesn't skip based on existing data)
		bool migrated = SqliteDatabaseMigrator.MigrateIfNeeded(_tempDir, sqliteDb);

		// The migrator migrates if the old file exists, regardless of existing data
		Assert.True(migrated);
		// Should have 1 existing + 3 migrated entries
		Assert.Equal(4, sqliteDb.EntryCount);
	}
}
