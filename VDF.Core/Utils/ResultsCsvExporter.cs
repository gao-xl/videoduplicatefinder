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

using System.Globalization;
using System.Linq;
using System.Text;
using VDF.Core.ViewModels;

namespace VDF.Core.Utils {

	/// <summary>Shared CSV export for scan results (GUI + Web).</summary>
	public static class ResultsCsvExporter {

		public static readonly string HeaderWithChecked =
			"GroupId,Path,SizeBytes,Duration,Resolution,Fps,BitrateKbs,AudioFormat,AudioSampleRate,Similarity,DateCreated,IsImage,Checked";

		public static readonly string HeaderWithoutChecked =
			"GroupId,Path,SizeBytes,Duration,Resolution,Fps,BitrateKbs,AudioFormat,AudioSampleRate,Similarity,DateCreated,IsImage";

		/// <summary>Export with UTF-8 BOM for Excel compatibility.</summary>
		public static byte[] ExportToUtf8Bom(
			IEnumerable<DuplicateItem> items,
			IReadOnlySet<string>? checkedPaths = null,
			bool includeCheckedColumn = true) {
			var inv = CultureInfo.InvariantCulture;
			var sb = new StringBuilder();
			sb.AppendLine(includeCheckedColumn ? HeaderWithChecked : HeaderWithoutChecked);

			foreach (var group in items.GroupBy(i => i.GroupId))
				foreach (var item in group) {
					var fields = new List<string> {
						item.GroupId.ToString(),
						Escape(item.Path),
						item.SizeLong.ToString(inv),
						item.Duration.ToString(null, inv),
						Escape(item.FrameSize),
						item.Fps.ToString(inv),
						item.BitRateKbs.ToString(inv),
						Escape(item.AudioFormat),
						item.AudioSampleRate.ToString(inv),
						item.Similarity.ToString(inv),
						item.DateCreated.ToString("yyyy-MM-dd HH:mm:ss", inv),
						item.IsImage.ToString(),
					};
					if (includeCheckedColumn) {
						bool isChecked = checkedPaths != null && checkedPaths.Contains(item.Path);
						fields.Add(isChecked.ToString());
					}
					sb.AppendLine(string.Join(',', fields));
				}

			var utf8 = Encoding.UTF8;
			return [.. utf8.GetPreamble(), .. utf8.GetBytes(sb.ToString())];
		}

		public static void ExportToFile(
			string path,
			IEnumerable<DuplicateItem> items,
			IReadOnlySet<string>? checkedPaths = null,
			bool includeCheckedColumn = true) {
			var bytes = ExportToUtf8Bom(items, checkedPaths, includeCheckedColumn);
			File.WriteAllBytes(path, bytes);
		}

		static string Escape(string? s) {
			s ??= string.Empty;
			return s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r')
				? "\"" + s.Replace("\"", "\"\"") + "\""
				: s;
		}
	}
}
