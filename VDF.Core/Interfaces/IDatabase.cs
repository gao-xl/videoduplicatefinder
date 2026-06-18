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

using System.Text.Json;

namespace VDF.Core.Interfaces {
	public interface IDatabase {
		Task<bool> LoadAsync();
		void Save();
		void Remove(FileEntry entry);
		void UpdateFilePath(string newPath, FileEntry entry);
		bool TryGet(string path, out FileEntry? entry);
		void BlacklistFileEntry(string filePath);
		void Cleanup();
		void Clear();
		bool ExportToJson(string jsonFile, JsonSerializerOptions options);
		bool ImportFromJson(string jsonFile, JsonSerializerOptions options);
	}
}
