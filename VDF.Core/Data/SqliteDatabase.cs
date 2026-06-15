using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using MemoryPack;
using VDF.Core.Utils;
using VDF.Core.ViewModels;

namespace VDF.Core.Data {

	sealed class SqliteDatabase : IDisposable {
		SqliteConnection? _connection;
		bool _disposed;

		public void Open(string dbPath) {
			ObjectDisposedException.ThrowIf(_disposed, this);
			if (_connection != null) return;

			string? dir = Path.GetDirectoryName(dbPath);
			if (!string.IsNullOrEmpty(dir))
				Directory.CreateDirectory(dir);

			var builder = new SqliteConnectionStringBuilder {
				DataSource = dbPath,
				Mode = SqliteOpenMode.ReadWriteCreate,
			};
			_connection = new SqliteConnection(builder.ConnectionString);
			_connection.Open();

			using var pragmas = _connection.CreateCommand();
			pragmas.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;";
			pragmas.ExecuteNonQuery();

			CreateSchema();
		}

		void CreateSchema() {
			using var cmd = _connection!.CreateCommand();
			cmd.CommandText = """
				CREATE TABLE IF NOT EXISTS FileEntries (
					Path TEXT PRIMARY KEY,
					Folder TEXT NOT NULL,
					FileSize INTEGER NOT NULL,
					DateCreated TEXT NOT NULL,
					DateModified TEXT NOT NULL,
					Flags INTEGER NOT NULL DEFAULT 0,
					GrayBytes BLOB,
					PHashes BLOB,
					MediaInfo BLOB,
					AudioFingerprint BLOB,
					IsImage INTEGER NOT NULL DEFAULT 0
				);
				CREATE INDEX IF NOT EXISTS idx_fileentries_folder ON FileEntries(Folder);
				""";
			cmd.ExecuteNonQuery();
		}

		// ---- FileEntry CRUD ----

		public void SaveFileEntry(FileEntry entry) {
			ObjectDisposedException.ThrowIf(_disposed, this);
			ArgumentNullException.ThrowIfNull(entry);

			using var cmd = _connection!.CreateCommand();
			cmd.CommandText = """
				INSERT OR REPLACE INTO FileEntries
					(Path, Folder, FileSize, DateCreated, DateModified, Flags,
					 GrayBytes, PHashes, MediaInfo, AudioFingerprint, IsImage)
				VALUES
					(@Path, @Folder, @FileSize, @DateCreated, @DateModified, @Flags,
					 @GrayBytes, @PHashes, @MediaInfo, @AudioFingerprint, @IsImage)
				""";
			AddFileEntryParameters(cmd, entry);
			cmd.ExecuteNonQuery();
		}

		public void SaveFileEntries(IEnumerable<FileEntry> entries) {
			ObjectDisposedException.ThrowIf(_disposed, this);

			using var tx = _connection!.BeginTransaction();
			using var cmd = _connection.CreateCommand();
			cmd.CommandText = """
				INSERT OR REPLACE INTO FileEntries
					(Path, Folder, FileSize, DateCreated, DateModified, Flags,
					 GrayBytes, PHashes, MediaInfo, AudioFingerprint, IsImage)
				VALUES
					(@Path, @Folder, @FileSize, @DateCreated, @DateModified, @Flags,
					 @GrayBytes, @PHashes, @MediaInfo, @AudioFingerprint, @IsImage)
				""";
			// Pre-create parameters once
			SqliteParameter pPath = cmd.Parameters.Add("@Path", SqliteType.Text);
			SqliteParameter pFolder = cmd.Parameters.Add("@Folder", SqliteType.Text);
			SqliteParameter pFileSize = cmd.Parameters.Add("@FileSize", SqliteType.Integer);
			SqliteParameter pDateCreated = cmd.Parameters.Add("@DateCreated", SqliteType.Text);
			SqliteParameter pDateModified = cmd.Parameters.Add("@DateModified", SqliteType.Text);
			SqliteParameter pFlags = cmd.Parameters.Add("@Flags", SqliteType.Integer);
			SqliteParameter pGrayBytes = cmd.Parameters.Add("@GrayBytes", SqliteType.Blob);
			SqliteParameter pPHashes = cmd.Parameters.Add("@PHashes", SqliteType.Blob);
			SqliteParameter pMediaInfo = cmd.Parameters.Add("@MediaInfo", SqliteType.Blob);
			SqliteParameter pAudioFingerprint = cmd.Parameters.Add("@AudioFingerprint", SqliteType.Blob);
			SqliteParameter pIsImage = cmd.Parameters.Add("@IsImage", SqliteType.Integer);

			foreach (var entry in entries) {
				pPath.Value = entry.Path;
				pFolder.Value = entry.Folder;
				pFileSize.Value = entry.FileSize;
				pDateCreated.Value = entry.DateCreated.ToString("O");
				pDateModified.Value = entry.DateModified.ToString("O");
				pFlags.Value = (int)entry.Flags;
				pGrayBytes.Value = SerializeGrayBytes(entry.grayBytes) ?? (object)DBNull.Value;
				pPHashes.Value = SerializePHashes(entry.PHashes) ?? (object)DBNull.Value;
				pMediaInfo.Value = entry.mediaInfo != null
					? MemoryPackSerializer.Serialize(entry.mediaInfo)
					: DBNull.Value;
				pAudioFingerprint.Value = entry.AudioFingerprint != null
					? MemoryPackSerializer.Serialize(entry.AudioFingerprint)
					: DBNull.Value;
				pIsImage.Value = entry.IsImage ? 1 : 0;
				cmd.ExecuteNonQuery();
			}

			tx.Commit();
		}

		public int SaveDirtyFileEntries(IEnumerable<FileEntry> entries) {
			ObjectDisposedException.ThrowIf(_disposed, this);

			int saved = 0;
			using var tx = _connection!.BeginTransaction();
			using var cmd = _connection.CreateCommand();
			cmd.CommandText = """
				INSERT OR REPLACE INTO FileEntries
					(Path, Folder, FileSize, DateCreated, DateModified, Flags,
					 GrayBytes, PHashes, MediaInfo, AudioFingerprint, IsImage)
				VALUES
					(@Path, @Folder, @FileSize, @DateCreated, @DateModified, @Flags,
					 @GrayBytes, @PHashes, @MediaInfo, @AudioFingerprint, @IsImage)
				""";
			SqliteParameter pPath = cmd.Parameters.Add("@Path", SqliteType.Text);
			SqliteParameter pFolder = cmd.Parameters.Add("@Folder", SqliteType.Text);
			SqliteParameter pFileSize = cmd.Parameters.Add("@FileSize", SqliteType.Integer);
			SqliteParameter pDateCreated = cmd.Parameters.Add("@DateCreated", SqliteType.Text);
			SqliteParameter pDateModified = cmd.Parameters.Add("@DateModified", SqliteType.Text);
			SqliteParameter pFlags = cmd.Parameters.Add("@Flags", SqliteType.Integer);
			SqliteParameter pGrayBytes = cmd.Parameters.Add("@GrayBytes", SqliteType.Blob);
			SqliteParameter pPHashes = cmd.Parameters.Add("@PHashes", SqliteType.Blob);
			SqliteParameter pMediaInfo = cmd.Parameters.Add("@MediaInfo", SqliteType.Blob);
			SqliteParameter pAudioFingerprint = cmd.Parameters.Add("@AudioFingerprint", SqliteType.Blob);
			SqliteParameter pIsImage = cmd.Parameters.Add("@IsImage", SqliteType.Integer);

			foreach (var entry in entries) {
				if (!entry.dirty) continue;
				pPath.Value = entry.Path;
				pFolder.Value = entry.Folder;
				pFileSize.Value = entry.FileSize;
				pDateCreated.Value = entry.DateCreated.ToString("O");
				pDateModified.Value = entry.DateModified.ToString("O");
				pFlags.Value = (int)entry.Flags;
				pGrayBytes.Value = SerializeGrayBytes(entry.grayBytes) ?? (object)DBNull.Value;
				pPHashes.Value = SerializePHashes(entry.PHashes) ?? (object)DBNull.Value;
				pMediaInfo.Value = entry.mediaInfo != null
					? MemoryPackSerializer.Serialize(entry.mediaInfo)
					: DBNull.Value;
				pAudioFingerprint.Value = entry.AudioFingerprint != null
					? MemoryPackSerializer.Serialize(entry.AudioFingerprint)
					: DBNull.Value;
				pIsImage.Value = entry.IsImage ? 1 : 0;
				cmd.ExecuteNonQuery();
				saved++;
			}

			tx.Commit();
			return saved;
		}

		public List<FileEntry> LoadFileEntries(bool lightweight = false) {
			ObjectDisposedException.ThrowIf(_disposed, this);

			var result = new List<FileEntry>();
			using var cmd = _connection!.CreateCommand();

			if (lightweight) {
				cmd.CommandText = "SELECT Path, Folder, FileSize, DateCreated, DateModified, Flags, IsImage FROM FileEntries";
				using var reader = cmd.ExecuteReader();
				while (reader.Read()) {
					var entry = new FileEntry {
						_Path = reader.GetString(0),
						Folder = reader.GetString(1),
						FileSize = reader.GetInt64(2),
						DateCreated = DateTime.Parse(reader.GetString(3), null, System.Globalization.DateTimeStyles.RoundtripKind),
						DateModified = DateTime.Parse(reader.GetString(4), null, System.Globalization.DateTimeStyles.RoundtripKind),
						Flags = (EntryFlags)reader.GetInt32(5),
					};
					if (reader.GetInt32(6) == 1)
						entry.Flags.Set(EntryFlags.IsImage, true);
					entry.grayBytes = new Dictionary<double, byte[]?>();
					entry.PHashes = new Dictionary<double, ulong?>();
					entry._heavyFieldsLoaded = false;
					result.Add(entry);
				}
			}
			else {
				cmd.CommandText = "SELECT Path, Folder, FileSize, DateCreated, DateModified, Flags, GrayBytes, PHashes, MediaInfo, AudioFingerprint, IsImage FROM FileEntries";
				using var reader = cmd.ExecuteReader();
				while (reader.Read()) {
					var entry = new FileEntry {
						_Path = reader.GetString(0),
						Folder = reader.GetString(1),
						FileSize = reader.GetInt64(2),
						DateCreated = DateTime.Parse(reader.GetString(3), null, System.Globalization.DateTimeStyles.RoundtripKind),
						DateModified = DateTime.Parse(reader.GetString(4), null, System.Globalization.DateTimeStyles.RoundtripKind),
						Flags = (EntryFlags)reader.GetInt32(5),
					};
					if (!reader.IsDBNull(6))
						entry.grayBytes = DeserializeGrayBytes((byte[])reader.GetValue(6));
					else
						entry.grayBytes = new Dictionary<double, byte[]?>();

					if (!reader.IsDBNull(7))
						entry.PHashes = DeserializePHashes((byte[])reader.GetValue(7));
					else
						entry.PHashes = new Dictionary<double, ulong?>();

					if (!reader.IsDBNull(8))
						entry.mediaInfo = MemoryPackSerializer.Deserialize<MediaInfo>((byte[])reader.GetValue(8));

					if (!reader.IsDBNull(9))
						entry.AudioFingerprint = MemoryPackSerializer.Deserialize<uint[]>((byte[])reader.GetValue(9));

					result.Add(entry);
				}
			}
			return result;
		}

		/// <summary>
		/// Loads the heavy fields (grayBytes, PHashes, mediaInfo, AudioFingerprint)
		/// for a single entry identified by path. Returns null if the entry is not found.
		/// </summary>
		public FileEntry? LoadFileEntryHeavy(string path) {
			ObjectDisposedException.ThrowIf(_disposed, this);

			using var cmd = _connection!.CreateCommand();
			cmd.CommandText = """
				SELECT GrayBytes, PHashes, MediaInfo, AudioFingerprint
				FROM FileEntries WHERE Path = @Path
				""";
			cmd.Parameters.AddWithValue("@Path", path);
			using var reader = cmd.ExecuteReader();
			if (!reader.Read()) return null;

			var entry = new FileEntry();
			if (!reader.IsDBNull(0))
				entry.grayBytes = DeserializeGrayBytes((byte[])reader.GetValue(0));
			else
				entry.grayBytes = new Dictionary<double, byte[]?>();

			if (!reader.IsDBNull(1))
				entry.PHashes = DeserializePHashes((byte[])reader.GetValue(1));
			else
				entry.PHashes = new Dictionary<double, ulong?>();

			if (!reader.IsDBNull(2))
				entry.mediaInfo = MemoryPackSerializer.Deserialize<MediaInfo>((byte[])reader.GetValue(2));

			if (!reader.IsDBNull(3))
				entry.AudioFingerprint = MemoryPackSerializer.Deserialize<uint[]>((byte[])reader.GetValue(3));

			return entry;
		}

		public void RemoveFileEntry(string path) {
			ObjectDisposedException.ThrowIf(_disposed, this);

			using var cmd = _connection!.CreateCommand();
			cmd.CommandText = "DELETE FROM FileEntries WHERE Path = @Path";
			cmd.Parameters.AddWithValue("@Path", path);
			cmd.ExecuteNonQuery();
		}

		public void ClearAllEntries() {
			ObjectDisposedException.ThrowIf(_disposed, this);

			using var cmd = _connection!.CreateCommand();
			cmd.CommandText = "DELETE FROM FileEntries";
			cmd.ExecuteNonQuery();
		}

		public int CleanDatabase() {
			ObjectDisposedException.ThrowIf(_disposed, this);

			var toRemove = new List<string>();
			using var cmd = _connection!.CreateCommand();
			cmd.CommandText = "SELECT Path, Flags FROM FileEntries";
			using var reader = cmd.ExecuteReader();
			while (reader.Read()) {
				string path = reader.GetString(0);
				var flags = (EntryFlags)reader.GetInt32(1);
				if (!File.Exists(path) || flags.Any(EntryFlags.MetadataError | EntryFlags.ThumbnailError))
					toRemove.Add(path);
			}

			if (toRemove.Count > 0) {
				using var tx = _connection.BeginTransaction();
				using var delCmd = _connection.CreateCommand();
				delCmd.CommandText = "DELETE FROM FileEntries WHERE Path = @Path";
				var pPath = delCmd.Parameters.Add("@Path", SqliteType.Text);
				foreach (var path in toRemove) {
					pPath.Value = path;
					delCmd.ExecuteNonQuery();
				}
				tx.Commit();
			}

			return toRemove.Count;
		}

		public int EntryCount {
			get {
				ObjectDisposedException.ThrowIf(_disposed, this);
				using var cmd = _connection!.CreateCommand();
				cmd.CommandText = "SELECT COUNT(*) FROM FileEntries";
				return (int)(long)cmd.ExecuteScalar()!;
			}
		}

		// ---- DuplicateItems (stored as JSON BLOB for simplicity) ----

		public void SaveDuplicateItems(HashSet<DuplicateItem> items) {
			ObjectDisposedException.ThrowIf(_disposed, this);

			using var tx = _connection!.BeginTransaction();

			// Ensure table exists
			using var createCmd = _connection.CreateCommand();
			createCmd.CommandText = """
				CREATE TABLE IF NOT EXISTS DuplicateItems (
					Id INTEGER PRIMARY KEY AUTOINCREMENT,
					Data TEXT NOT NULL
				);
				DELETE FROM DuplicateItems;
				""";
			createCmd.ExecuteNonQuery();

			// Serialize the entire set as a single JSON row for atomicity
			using var insertCmd = _connection.CreateCommand();
			insertCmd.CommandText = "INSERT INTO DuplicateItems (Data) VALUES (@Data)";
			insertCmd.Parameters.AddWithValue("@Data",
				JsonSerializer.Serialize(items, CoreJsonContext.Default.HashSetDuplicateItem));
			insertCmd.ExecuteNonQuery();

			tx.Commit();
		}

		public HashSet<DuplicateItem> LoadDuplicateItems() {
			ObjectDisposedException.ThrowIf(_disposed, this);

			// Check if table exists
			using var checkCmd = _connection!.CreateCommand();
			checkCmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='DuplicateItems'";
			var tableName = checkCmd.ExecuteScalar();
			if (tableName == null)
				return new HashSet<DuplicateItem>();

			using var cmd = _connection.CreateCommand();
			cmd.CommandText = "SELECT Data FROM DuplicateItems ORDER BY Id DESC LIMIT 1";
			var data = cmd.ExecuteScalar();
			if (data == null || data == DBNull.Value)
				return new HashSet<DuplicateItem>();

			return JsonSerializer.Deserialize((string)data, CoreJsonContext.Default.HashSetDuplicateItem)
				?? new HashSet<DuplicateItem>();
		}

		// ---- Serialization helpers for Dictionary<double, byte[]?> ----

		static byte[]? SerializeGrayBytes(Dictionary<double, byte[]?> grayBytes) {
			if (grayBytes.Count == 0) return null;
			return MemoryPackSerializer.Serialize(grayBytes);
		}

		static Dictionary<double, byte[]?> DeserializeGrayBytes(byte[] data) {
			return MemoryPackSerializer.Deserialize<Dictionary<double, byte[]?>>(data)
				?? new Dictionary<double, byte[]?>();
		}

		static byte[]? SerializePHashes(Dictionary<double, ulong?> pHashes) {
			if (pHashes.Count == 0) return null;
			return MemoryPackSerializer.Serialize(pHashes);
		}

		static Dictionary<double, ulong?> DeserializePHashes(byte[] data) {
			return MemoryPackSerializer.Deserialize<Dictionary<double, ulong?>>(data)
				?? new Dictionary<double, ulong?>();
		}

		static void AddFileEntryParameters(SqliteCommand cmd, FileEntry entry) {
			cmd.Parameters.AddWithValue("@Path", entry.Path);
			cmd.Parameters.AddWithValue("@Folder", entry.Folder);
			cmd.Parameters.AddWithValue("@FileSize", entry.FileSize);
			cmd.Parameters.AddWithValue("@DateCreated", entry.DateCreated.ToString("O"));
			cmd.Parameters.AddWithValue("@DateModified", entry.DateModified.ToString("O"));
			cmd.Parameters.AddWithValue("@Flags", (int)entry.Flags);
			cmd.Parameters.AddWithValue("@GrayBytes",
				(object?)SerializeGrayBytes(entry.grayBytes) ?? DBNull.Value);
			cmd.Parameters.AddWithValue("@PHashes",
				(object?)SerializePHashes(entry.PHashes) ?? DBNull.Value);
			cmd.Parameters.AddWithValue("@MediaInfo",
				entry.mediaInfo != null ? MemoryPackSerializer.Serialize(entry.mediaInfo) : DBNull.Value);
			cmd.Parameters.AddWithValue("@AudioFingerprint",
				entry.AudioFingerprint != null ? MemoryPackSerializer.Serialize(entry.AudioFingerprint) : DBNull.Value);
			cmd.Parameters.AddWithValue("@IsImage", entry.IsImage ? 1 : 0);
		}

		public void Dispose() {
			if (_disposed) return;
			_disposed = true;
			_connection?.Close();
			_connection?.Dispose();
			_connection = null;
		}
	}
}
