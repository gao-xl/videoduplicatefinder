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
//     along with VideoDuplicateFinder.  If not, see <https://www.gnu.org/licenses/>.
// */

using System.Text.Json.Serialization;

namespace VDF.Core.ViewModels {

	/// <summary>
	/// One duplicate result row with optional UI selection state and thumbnail pack key.
	/// Serializes with <c>itemInfo</c> for wire compatibility with legacy GUI backups.
	/// </summary>
	public sealed class ScanResultEntry {
		[JsonPropertyName("itemInfo")]
		public DuplicateItem Item { get; set; } = null!;

		[JsonPropertyName("checked")]
		public bool Checked { get; set; }

		[JsonPropertyName("thumbnailKey")]
		public string? ThumbnailKey { get; set; }
	}

	/// <summary>
	/// Versioned envelope for scan result persistence (JSON or ZIP scan.json entry).
	/// Older builds wrote a raw <see cref="List{ScanResultEntry}"/> at the document root;
	/// <see cref="ResultsStore"/> still accepts that legacy shape.
	/// </summary>
	public sealed class ScanResultsEnvelope {
		public const int CurrentVersion = 1;

		[JsonPropertyName("version")]
		public int Version { get; set; } = CurrentVersion;

		[JsonPropertyName("items")]
		public List<ScanResultEntry> Items { get; set; } = new();
	}
}
