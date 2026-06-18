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
using VDF.Core;
using VDF.Core.FFTools;
using VDF.Web.Services;

namespace VDF.Web.Tests;

public class WebSettingsServiceTests {
	// ── Dto round-trip (composition) ───────────────────────────────────────

	[Fact]
	public void Dto_RoundTrip_PreservesCoreAndWebSpecificFields() {
		var dto = new WebSettingsService.Dto {
			Core = new Settings {
				Percent = 92f,
				ThumbnailCount = 7,
				MaxDegreeOfParallelism = 4,
				HardwareAccelerationMode = FFHardwareAccelerationMode.cuda,
				CustomFFArguments = "-preset slow",
				LanguageCode = "de",
				TestAutoSerializeField = "web-roundtrip",
			},
			AutoLoadThumbnails = false,
			ThumbnailWidth = 320,
			ThumbnailJpegQuality = 70,
		};

		var json = JsonSerializer.Serialize(dto, WebJsonContext.Default.Dto);
		var restored = JsonSerializer.Deserialize(json, WebJsonContext.Default.Dto)!;

		Assert.Equal(92f, restored.Core.Percent);
		Assert.Equal(7, restored.Core.ThumbnailCount);
		Assert.Equal(4, restored.Core.MaxDegreeOfParallelism);
		Assert.Equal(FFHardwareAccelerationMode.cuda, restored.Core.HardwareAccelerationMode);
		Assert.Equal("-preset slow", restored.Core.CustomFFArguments);
		Assert.Equal("de", restored.Core.LanguageCode);
		Assert.Equal("web-roundtrip", restored.Core.TestAutoSerializeField);
		Assert.False(restored.AutoLoadThumbnails);
		Assert.Equal(320, restored.ThumbnailWidth);
		Assert.Equal(70, restored.ThumbnailJpegQuality);
	}

	/// <summary>
	/// Adding a new field to <see cref="Settings"/> requires zero changes in the Web
	/// layer — it is automatically serialized as part of the nested Core object.
	/// </summary>
	[Fact]
	public void Dto_NewCoreField_AutoSerialized() {
		var dto = new WebSettingsService.Dto {
			Core = new Settings { TestAutoSerializeField = "auto-via-web" },
		};
		var json = JsonSerializer.Serialize(dto, WebJsonContext.Default.Dto);
		Assert.Contains("TestAutoSerializeField", json);
		Assert.Contains("auto-via-web", json);

		var restored = JsonSerializer.Deserialize(json, WebJsonContext.Default.Dto)!;
		Assert.Equal("auto-via-web", restored.Core.TestAutoSerializeField);
	}

	// ── Legacy flat-JSON migration ─────────────────────────────────────────

	[Fact]
	public void MigrateLegacyJson_NoCoreKey_MovesAllNonWebSpecificFields() {
		// Simulates an old web-settings.json with flat Core fields at the top level
		// alongside the three WebUI-specific keys.
		var legacy = new JsonObject {
			["Percent"] = 88,
			["ThumbnailCount"] = 3,
			["MaxDegreeOfParallelism"] = 2,
			["HardwareAccelerationMode"] = "cuda",
			["CustomFFArguments"] = "-preset fast",
			["LanguageCode"] = "es",
			["AutoLoadThumbnails"] = false,
			["ThumbnailWidth"] = 240,
			["ThumbnailJpegQuality"] = 60,
		};
		var json = legacy.ToJsonString();

		var migrated = WebSettingsService.MigrateLegacyJson(json);
		var root = JsonNode.Parse(migrated)!.AsObject();

		// The "Core" object must exist
		Assert.True(root.ContainsKey("Core"));
		var core = root["Core"]!.AsObject();

		// Core fields moved into the nested object
		Assert.Equal(88, core["Percent"]!.GetValue<int>());
		Assert.Equal(3, core["ThumbnailCount"]!.GetValue<int>());
		Assert.Equal(2, core["MaxDegreeOfParallelism"]!.GetValue<int>());
		Assert.Equal("cuda", core["HardwareAccelerationMode"]!.GetValue<string>());
		Assert.Equal("-preset fast", core["CustomFFArguments"]!.GetValue<string>());
		Assert.Equal("es", core["LanguageCode"]!.GetValue<string>());

		// WebUI-specific keys stay at the top level (NOT moved into Core)
		Assert.False(core.ContainsKey("AutoLoadThumbnails"));
		Assert.False(core.ContainsKey("ThumbnailWidth"));
		Assert.False(core.ContainsKey("ThumbnailJpegQuality"));
		Assert.Equal(false, root["AutoLoadThumbnails"]!.GetValue<bool>());
		Assert.Equal(240, root["ThumbnailWidth"]!.GetValue<int>());
		Assert.Equal(60, root["ThumbnailJpegQuality"]!.GetValue<int>());
	}

	[Fact]
	public void MigrateLegacyJson_AlreadyHasCoreKey_ReturnedUnchanged() {
		// New-format JSON already has a "Core" key — migration must be a no-op.
		var nested = new JsonObject {
			["Core"] = new JsonObject {
				["Percent"] = 95,
				["ThumbnailCount"] = 5,
			},
			["AutoLoadThumbnails"] = true,
			["ThumbnailWidth"] = 480,
			["ThumbnailJpegQuality"] = 85,
		};
		var json = nested.ToJsonString();

		var migrated = WebSettingsService.MigrateLegacyJson(json);

		Assert.Equal(json, migrated);
	}

	[Fact]
	public void MigrateLegacyJson_MalformedJson_ReturnedAsIs() {
		// Malformed JSON must not throw — it's returned unchanged so the
		// deserializer can report a clean error.
		const string malformed = "{ this is not valid json";
		var migrated = WebSettingsService.MigrateLegacyJson(malformed);
		Assert.Equal(malformed, migrated);
	}

	[Fact]
	public void MigrateLegacyJson_RoundTripWithDeserializer_PreservesValues() {
		// End-to-end: legacy flat JSON → migrate → deserialize → check values.
		var legacy = new JsonObject {
			["Percent"] = 77,
			["ThumbnailCount"] = 4,
			["MaxDegreeOfParallelism"] = 6,
			["CustomFFArguments"] = "-crf 23",
			["LanguageCode"] = "fr",
			["TestAutoSerializeField"] = "migrated-value",
			["AutoLoadThumbnails"] = false,
			["ThumbnailWidth"] = 200,
			["ThumbnailJpegQuality"] = 50,
		};
		var json = legacy.ToJsonString();

		var migrated = WebSettingsService.MigrateLegacyJson(json);
		var dto = JsonSerializer.Deserialize(migrated, WebJsonContext.Default.Dto)!;

		Assert.Equal(77, dto.Core.Percent);
		Assert.Equal(4, dto.Core.ThumbnailCount);
		Assert.Equal(6, dto.Core.MaxDegreeOfParallelism);
		Assert.Equal("-crf 23", dto.Core.CustomFFArguments);
		Assert.Equal("fr", dto.Core.LanguageCode);
		Assert.Equal("migrated-value", dto.Core.TestAutoSerializeField);
		Assert.False(dto.AutoLoadThumbnails);
		Assert.Equal(200, dto.ThumbnailWidth);
		Assert.Equal(50, dto.ThumbnailJpegQuality);
	}

	[Fact]
	public void MigrateLegacyJson_OnlyWebSpecificKeys_ProducesEmptyCore() {
		// Edge case: a legacy file that contained only WebUI-specific keys
		// (no Core fields). Migration should still produce a "Core" object
		// (empty), so the deserializer can populate it with defaults.
		var legacy = new JsonObject {
			["AutoLoadThumbnails"] = true,
			["ThumbnailWidth"] = 480,
			["ThumbnailJpegQuality"] = 85,
		};
		var json = legacy.ToJsonString();

		var migrated = WebSettingsService.MigrateLegacyJson(json);
		var root = JsonNode.Parse(migrated)!.AsObject();

		Assert.True(root.ContainsKey("Core"));
		var core = root["Core"]!.AsObject();
		Assert.Empty(core);
		// WebUI-specific keys stay at top level
		Assert.Equal(true, root["AutoLoadThumbnails"]!.GetValue<bool>());
		Assert.Equal(480, root["ThumbnailWidth"]!.GetValue<int>());
		Assert.Equal(85, root["ThumbnailJpegQuality"]!.GetValue<int>());
	}
}
