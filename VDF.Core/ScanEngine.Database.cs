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

using System.Linq;
using System.Text.Json;
using VDF.Core.FFTools;
using VDF.Core.Utils;
using VDF.Core.ViewModels;

namespace VDF.Core {
	public sealed partial class ScanEngine {

		public static Task<bool> LoadDatabase() => Task.Run(DatabaseUtils.LoadDatabase);
		public static void SaveDatabase() => DatabaseUtils.SaveDatabase();
		public static void RemoveFromDatabase(FileEntry dbEntry) => DatabaseUtils.Database.Remove(dbEntry);
		public static void UpdateFilePathInDatabase(string newPath, FileEntry dbEntry) => DatabaseUtils.UpdateFilePath(newPath, dbEntry);
#pragma warning disable CS8601 // Possible null reference assignment
		public static bool GetFromDatabase(string path, out FileEntry? dbEntry) {
			if (!File.Exists(path)) {
				dbEntry = null;
				return false;
			}
			return DatabaseUtils.Database.TryGetValue(new FileEntry(path), out dbEntry);
		}
#pragma warning restore CS8601 // Possible null reference assignment
		public static void BlackListFileEntry(string filePath) => DatabaseUtils.BlacklistFileEntry(filePath);

		public async Task CleanupDatabaseAsync() {
			await Task.Run(() => {
				DatabaseUtils.CleanupDatabase();
			});
			DatabaseCleaned?.Invoke(this, new EventArgs());
		}

		public static void ClearDatabase() => DatabaseUtils.ClearDatabase();
		public static bool ExportDataBaseToJson(string jsonFile, JsonSerializerOptions options) => DatabaseUtils.ExportDatabaseToJson(jsonFile, options);
		public static bool ImportDataBaseFromJson(string jsonFile, JsonSerializerOptions options) => DatabaseUtils.ImportDatabaseFromJson(jsonFile, options);
	}
}
