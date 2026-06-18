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
//

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using VDF.Core;
using VDF.Core.FFTools;

namespace VDF.Web.Services {
	public sealed class WebSettingsService {
		readonly ILogger<WebSettingsService> _logger;

		public WebSettingsService(ILogger<WebSettingsService> logger) {
			_logger = logger;
		}

		/// <summary>
		/// JSON-serializable settings for VDF.Web.  Composes the entire
		/// <see cref="Core"/> object so that new Core fields are automatically
		/// persisted without manual sync code.  Only WebUI-specific fields that
		/// don't belong in Core are kept at the top level.
		/// </summary>
		public sealed class Dto {
			/// <summary>The canonical Core settings — serialized as a nested "core" object.</summary>
			public Settings Core { get; set; } = new();

			// ── WebUI-only settings (not in VDF.Core Settings) ──────────────
			/// <summary>Whether to automatically load HQ thumbnails on the results page.</summary>
			public bool AutoLoadThumbnails { get; set; } = true;
			/// <summary>Thumbnail resolution width in pixels (48–960). Lower = less memory, more pixelated.</summary>
			public int ThumbnailWidth { get; set; } = 480;
			/// <summary>JPEG quality for thumbnails (10–95). Lower = smaller, more artifacts.</summary>
			public int ThumbnailJpegQuality { get; set; } = 85;
		}

		/// <summary>WebUI-only settings that don't belong in VDF.Core.Settings.</summary>
		public bool AutoLoadThumbnails { get; set; } = true;
		public int ThumbnailWidth { get; set; } = 480;
		public int ThumbnailJpegQuality { get; set; } = 85;

		static string SettingsPath {
			get {
				string folder;
				if (OperatingSystem.IsWindows())
					folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VDF");
				else if (OperatingSystem.IsMacOS())
					folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Preferences", "VDF");
				else
					folder = Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
						?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"), "VDF");
				return Path.Combine(folder, "web-settings.json");
			}
		}

		/// <summary>
		/// Loads settings from disk.  Returns a validated <see cref="Settings"/>
		/// instance (the nested Core object) or <c>null</c> if no file exists.
		/// The caller should assign the result to <c>ScanEngine.Settings</c>.
		/// </summary>
		public Settings? Load() {
			if (!File.Exists(SettingsPath)) return null;
			try {
				var json = File.ReadAllText(SettingsPath);
				json = MigrateLegacyJson(json);
				var dto = JsonSerializer.Deserialize(json, WebJsonContext.Default.Dto);
				if (dto?.Core == null) return null;
				SettingsValidator.Validate(dto.Core);
				// WebUI-only
				AutoLoadThumbnails = dto.AutoLoadThumbnails;
				ThumbnailWidth = Math.Clamp(dto.ThumbnailWidth, 48, 960);
				ThumbnailJpegQuality = Math.Clamp(dto.ThumbnailJpegQuality, 10, 95);
				return dto.Core;
			}
			catch (Exception ex) { _logger.LogWarning(ex, "Failed to load web settings file"); return null; }
		}

		public bool Save(Settings s) {
			try {
				Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
				var dto = new Dto {
					Core = s,
					AutoLoadThumbnails = AutoLoadThumbnails,
					ThumbnailWidth = ThumbnailWidth,
					ThumbnailJpegQuality = ThumbnailJpegQuality,
				};
				File.WriteAllText(SettingsPath, JsonSerializer.Serialize(dto, WebJsonContext.Default.Dto));
				return true;
			}
			catch (Exception ex) { _logger.LogError(ex, "Failed to save web settings file"); return false; }
		}

		/// <summary>
		/// If the JSON has flat Core fields at the top level (old format), moves them
		/// into a nested <c>"Core"</c> object so the composition-based deserializer
		/// can read them.  Only the three WebUI-specific keys stay at the top level.
		/// </summary>
		internal static string MigrateLegacyJson(string json) {
			JsonNode? root;
			try { root = JsonNode.Parse(json); }
			catch { return json; }
			if (root is not JsonObject obj) return json;
			if (obj.ContainsKey("Core")) return json; // already new format

			var core = new JsonObject();
			// Everything that is NOT a WebUI-specific key is a Core field — move it.
			var webSpecificKeys = new HashSet<string> {
				"AutoLoadThumbnails", "ThumbnailWidth", "ThumbnailJpegQuality"
			};
			var keysToMove = obj
				.Where(kvp => !webSpecificKeys.Contains(kvp.Key))
				.Select(kvp => kvp.Key)
				.ToList();
			foreach (var key in keysToMove) {
				var node = obj[key];
				obj.Remove(key); // detach parent before re-parenting under "Core"
				core[key] = node;
			}
			obj["Core"] = core;
			return root.ToJsonString();
		}
	}
}
