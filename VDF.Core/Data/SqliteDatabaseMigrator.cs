using System.Diagnostics;
using MemoryPack;
using VDF.Core.Utils;

namespace VDF.Core.Data {

	static class SqliteDatabaseMigrator {

		/// <summary>
		/// Migrates data from the old MemoryPack/protobuf database (ScannedFiles.db)
		/// to the new SQLite database (vdf.db). Returns true if migration was performed.
		/// </summary>
		public static bool MigrateIfNeeded(string databaseFolder, SqliteDatabase sqliteDb) {
			string oldDbPath = FileUtils.SafePathCombine(databaseFolder, "ScannedFiles.db");
			string tempDbPath = FileUtils.SafePathCombine(databaseFolder, "ScannedFiles_new.db");
			string backupPath = FileUtils.SafePathCombine(databaseFolder, "ScannedFiles.db.bak");

			// Nothing to migrate if old database doesn't exist
			if (!File.Exists(oldDbPath) && !File.Exists(tempDbPath))
				return false;

			// Determine which file to read from
			string sourcePath = File.Exists(tempDbPath) ? tempDbPath : oldDbPath;

			Logger.Instance.Info($"Migrating old database '{sourcePath}' to SQLite format...");
			var st = Stopwatch.StartNew();

			try {
				DatabaseWrapper wrapper = LoadOldDatabase(sourcePath);
				int count = wrapper.Entries.Count;

				if (count > 0) {
					sqliteDb.SaveFileEntries(wrapper.Entries);
				}

				st.Stop();
				Logger.Instance.Info($"Migration complete: {count:N0} entries migrated in {st.Elapsed}.");

				// Rename old file to .bak
				RenameOldFile(sourcePath, backupPath);
				// Also clean up the temp file if it's different from the source
				if (sourcePath != tempDbPath && File.Exists(tempDbPath)) {
					try { File.Delete(tempDbPath); } catch { }
				}
				// Clean up the main old file if we migrated from temp
				if (sourcePath == tempDbPath && File.Exists(oldDbPath)) {
					try {
						string oldBackup = FileUtils.SafePathCombine(databaseFolder, "ScannedFiles.db.bak");
						RenameOldFile(oldDbPath, oldBackup);
					} catch { }
				}

				return true;
			}
			catch (Exception ex) {
				st.Stop();
				Logger.Instance.Info($"Database migration failed: {ex}");
				return false;
			}
		}

		static DatabaseWrapper LoadOldDatabase(string path) {
			FileInfo fi = new(path);
			if (!fi.Exists || fi.Length == 0)
				return new DatabaseWrapper();

			using var file = new FileStream(path, FileMode.Open, FileAccess.Read);
			ReadOnlySpan<byte> formatMagic = "VDFDB001"u8;

			Span<byte> header = stackalloc byte[8];
			int headerRead = file.Read(header);

			if (headerRead == formatMagic.Length && header.SequenceEqual(formatMagic)) {
				return MemoryPackSerializer.DeserializeAsync<DatabaseWrapper>(file)
					.AsTask().GetAwaiter().GetResult() ?? new DatabaseWrapper();
			}
			else {
				// Legacy protobuf-net database
				file.Position = 0;
				byte[] raw = new byte[file.Length];
				file.ReadExactly(raw);
				return LegacyDatabaseReader.Read(raw);
			}
		}

		static void RenameOldFile(string sourcePath, string backupPath) {
			try {
				if (File.Exists(sourcePath)) {
					File.Move(sourcePath, backupPath, overwrite: true);
					Logger.Instance.Info($"Old database renamed to '{backupPath}'.");
				}
			}
			catch (Exception ex) {
				Logger.Instance.Info($"Could not rename old database file: {ex}");
			}
		}
	}
}
