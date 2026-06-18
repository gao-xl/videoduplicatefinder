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

using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using VDF.Core.Utils;
using VDF.Core.ViewModels;

namespace VDF.Core.Services {

	/// <summary>Outcome of <see cref="ResultsStore.LoadAsync"/>.</summary>
	public sealed class ResultsLoadResult {
		public List<ScanResultEntry> Items { get; init; } = new();
		/// <summary>Folder containing extracted thumbs.pack/idx when loaded from a ZIP backup.</summary>
		public string? ThumbnailPackFolder { get; init; }
	}

	/// <summary>
	/// Unified scan-result persistence for GUI, Web, and CLI. Supports:
	/// <list type="bullet">
	/// <item>JSON backup (<c>backup.scanresults</c>)</item>
	/// <item>ZIP export (<c>scan.json</c> + optional <c>thumbs.pack</c>/<c>thumbs.idx</c>)</item>
	/// <item>Legacy GUI formats (raw array or v1 envelope with DuplicateItemVM-shaped entries)</item>
	/// </list>
	/// </summary>
	public sealed class ResultsStore {

		const string ScanJsonEntry = "scan.json";
		static readonly string[] ThumbnailEntries = ["thumbs.pack", "thumbs.idx"];

		/// <summary>Default auto-backup path next to the scan database.</summary>
		public static string DefaultBackupPath(string? customDatabaseFolder) =>
			Path.Combine(CoreUtils.ResolveDatabaseFolder(customDatabaseFolder), "backup.scanresults");

		/// <summary>Web/state-folder backup path (no custom database folder).</summary>
		public static string DefaultStateBackupPath() =>
			Path.Combine(CoreUtils.StateFolder, "backup.scanresults");

		/// <summary>
		/// Save a JSON backup atomically. When <paramref name="includeThumbnails"/> is false
		/// this is the <c>backup.scanresults</c> shape used for crash recovery.
		/// </summary>
		public async Task SaveJsonAsync(
			string path,
			IEnumerable<ScanResultEntry> items,
			bool indented = false,
			CancellationToken cancellationToken = default) {
			ArgumentException.ThrowIfNullOrEmpty(path);
			ArgumentNullException.ThrowIfNull(items);

			var envelope = new ScanResultsEnvelope {
				Version = ScanResultsEnvelope.CurrentVersion,
				Items = items.ToList(),
			};

			var dir = Path.GetDirectoryName(path)!;
			Directory.CreateDirectory(dir);
			var tmp = path + ".tmp";
			var context = indented ? CoreJsonPrettyResultsContext.Default.ScanResultsEnvelope
				: CoreJsonContext.Default.ScanResultsEnvelope;

			await using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, useAsync: true)) {
				await JsonSerializer.SerializeAsync(fs, envelope, context, cancellationToken);
				await fs.FlushAsync(cancellationToken);
			}
			File.Move(tmp, path, overwrite: true);
		}

		/// <summary>
		/// Save a ZIP export with <c>scan.json</c> and optional thumbnail pack files
		/// copied from <paramref name="thumbnailService"/>.
		/// </summary>
		public async Task SaveZipAsync(
			string path,
			IEnumerable<ScanResultEntry> items,
			ThumbnailService? thumbnailService,
			CancellationToken cancellationToken = default) {
			ArgumentException.ThrowIfNullOrEmpty(path);
			ArgumentNullException.ThrowIfNull(items);

			var dir = Path.GetDirectoryName(path)!;
			Directory.CreateDirectory(dir);
			var tmp = path + ".tmp";

			await using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, useAsync: true)) {
				using var zip = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false);
				var jsonEntry = zip.CreateEntry(ScanJsonEntry, CompressionLevel.NoCompression);
				await using (var es = jsonEntry.Open()) {
					var envelope = new ScanResultsEnvelope {
						Version = ScanResultsEnvelope.CurrentVersion,
						Items = items.ToList(),
					};
					await JsonSerializer.SerializeAsync(es, envelope, CoreJsonContext.Default.ScanResultsEnvelope, cancellationToken);
					await es.FlushAsync(cancellationToken);
				}

				thumbnailService?.Flush();

				if (thumbnailService?.PackFolder is { } packFolder) {
					foreach (var name in ThumbnailEntries) {
						string src = Path.Combine(packFolder, name);
						if (!File.Exists(src)) continue;
						var entry = zip.CreateEntry(name, CompressionLevel.NoCompression);
						await using var es = entry.Open();
						await using var srcFs = File.OpenRead(src);
						await srcFs.CopyToAsync(es, cancellationToken);
					}
				}
			}
			File.Move(tmp, path, overwrite: true);
		}

		/// <summary>Load from JSON or ZIP based on file content.</summary>
		public Task<ResultsLoadResult> LoadAsync(string path, CancellationToken cancellationToken = default) {
			ArgumentException.ThrowIfNullOrEmpty(path);
			return LooksLikeZipArchive(path)
				? LoadZipAsync(path, cancellationToken)
				: LoadJsonAsync(path, cancellationToken);
		}

		static bool LooksLikeZipArchive(string path) {
			if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
				|| path.EndsWith(".scanresults", StringComparison.OrdinalIgnoreCase))
				return true;
			try {
				using var fs = File.OpenRead(path);
				Span<byte> header = stackalloc byte[2];
				return fs.Read(header) == 2 && header[0] == (byte)'P' && header[1] == (byte)'K';
			}
			catch {
				return false;
			}
		}

		public async Task<ResultsLoadResult> LoadJsonAsync(string path, CancellationToken cancellationToken = default) {
			await using var fs = File.OpenRead(path);
			using var doc = await JsonDocument.ParseAsync(fs, cancellationToken: cancellationToken);
			return new ResultsLoadResult { Items = ParseItems(doc.RootElement) };
		}

		public async Task<ResultsLoadResult> LoadZipAsync(string path, CancellationToken cancellationToken = default) {
			string extractFolder = Path.Combine(Path.GetTempPath(), "VDF-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(extractFolder);

			try {
				using var zip = ZipFile.OpenRead(path);
				var json = zip.GetEntry(ScanJsonEntry)
					?? throw new InvalidDataException($"{ScanJsonEntry} missing");

				List<ScanResultEntry> items;
				await using (var js = json.Open()) {
					using var doc = await JsonDocument.ParseAsync(js, cancellationToken: cancellationToken);
					items = ParseItems(doc.RootElement);
				}

				string? thumbFolder = null;
				foreach (var entryName in ThumbnailEntries) {
					var entry = zip.GetEntry(entryName);
					if (entry == null) continue;
					string dest = Path.GetFullPath(Path.Combine(extractFolder, entryName));
					string root = Path.GetFullPath(extractFolder);
					if (!dest.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
						&& !dest.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
						throw new InvalidOperationException($"ZIP entry '{entryName}' would extract outside target directory");
					entry.ExtractToFile(dest, overwrite: true);
					thumbFolder = extractFolder;
				}

				return new ResultsLoadResult { Items = items, ThumbnailPackFolder = thumbFolder };
			}
			catch {
				TryDeleteDirectory(extractFolder);
				throw;
			}
		}

		/// <summary>
		/// Parse items from a JSON root that may be a v1 envelope, a legacy raw array,
		/// or DuplicateItemVM-shaped objects (<c>itemInfo</c> + <c>checked</c>).
		/// </summary>
		public static List<ScanResultEntry> ParseItems(JsonElement root) {
			JsonElement itemsEl = root;
			if (root.ValueKind == JsonValueKind.Object &&
				root.TryGetProperty("items", out var nested) &&
				nested.ValueKind == JsonValueKind.Array)
				itemsEl = nested;

			if (itemsEl.ValueKind != JsonValueKind.Array)
				throw new JsonException("Unknown scan results format");

			var list = new List<ScanResultEntry>();
			foreach (var el in itemsEl.EnumerateArray()) {
				var entry = ParseEntry(el);
				if (entry != null)
					list.Add(entry);
			}
			if (list.Count == 0)
				throw new JsonException("All scan result entries were corrupt");
			return list;
		}

		static ScanResultEntry? ParseEntry(JsonElement el) {
			if (el.ValueKind != JsonValueKind.Object)
				return null;

			if (!TryGetItemElement(el, out var itemEl))
				return null;

			var item = JsonSerializer.Deserialize(itemEl, CoreJsonContext.Default.DuplicateItem);
			if (item == null || string.IsNullOrEmpty(item.Path))
				return null;

			bool isChecked = false;
			if (el.TryGetProperty("Checked", out var checkedEl) || el.TryGetProperty("checked", out checkedEl))
				isChecked = checkedEl.ValueKind == JsonValueKind.True;

			string? thumbKey = null;
			if (el.TryGetProperty("ThumbnailKey", out var tkEl) || el.TryGetProperty("thumbnailKey", out tkEl))
				thumbKey = tkEl.GetString();

			return new ScanResultEntry {
				Item = item,
				Checked = isChecked,
				ThumbnailKey = thumbKey,
			};
		}

		static bool TryGetItemElement(JsonElement el, out JsonElement itemEl) {
			if (el.TryGetProperty("ItemInfo", out itemEl) || el.TryGetProperty("itemInfo", out itemEl)
				|| el.TryGetProperty("Item", out itemEl) || el.TryGetProperty("item", out itemEl))
				return itemEl.ValueKind == JsonValueKind.Object;

			// Legacy raw DuplicateItem at array root
			if (el.TryGetProperty("Path", out _)) {
				itemEl = el;
				return true;
			}

			itemEl = default;
			return false;
		}

		static void TryDeleteDirectory(string path) {
			try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
			catch { /* ignore */ }
		}
	}
}
