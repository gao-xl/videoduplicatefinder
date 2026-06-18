using VDF.Core;
using VDF.Web.Models;
using VDF.Web.Services;

namespace VDF.Web.Endpoints;

static class SettingsEndpoints {
	public static WebApplication MapSettingsApi(this WebApplication app) {
		var group = app.MapGroup("/api/settings");
		group.RequireAuthorization();

		// GET /api/settings — get current settings (returns Dto with nested Core)
		group.MapGet("/", (ScanService scan, WebSettingsService settingsService) => {
			return Results.Ok(new WebSettingsService.Dto {
				Core = scan.Settings,
				AutoLoadThumbnails = settingsService.AutoLoadThumbnails,
				ThumbnailWidth = settingsService.ThumbnailWidth,
				ThumbnailJpegQuality = settingsService.ThumbnailJpegQuality,
			});
		});

		// PUT /api/settings — update settings (receives Dto with nested Core)
		group.MapPut("/", (ScanService scan, WebSettingsService settingsService, WebSettingsService.Dto dto) => {
			// Unified validation — same clamping rules as WebSettingsService.Load
			if (dto.Core != null)
				SettingsValidator.Validate(dto.Core);
			scan.Settings = dto.Core;
			// WebUI-only settings
			settingsService.AutoLoadThumbnails = dto.AutoLoadThumbnails;
			settingsService.ThumbnailWidth = Math.Clamp(dto.ThumbnailWidth, 48, 960);
			settingsService.ThumbnailJpegQuality = Math.Clamp(dto.ThumbnailJpegQuality, 10, 95);
			return Results.Ok(new { updated = true });
		});

		// POST /api/settings/save — save settings to disk
		group.MapPost("/save", (ScanService scan) => {
			bool ok = scan.SaveSettings();
			return ok ? Results.Ok(new { saved = true }) : Results.Json(new { error = "save_failed" }, statusCode: 500);
		});

		// POST /api/settings/database/clean — clean database
		group.MapPost("/database/clean", async (ScanService scan) => {
			int removed = await scan.CleanDatabaseAsync();
			return Results.Ok(new DatabaseCleanResponse {
				Removed = removed,
				Remaining = scan.DatabaseEntryCount,
			});
		});

		// POST /api/settings/database/clear — clear entire database
		group.MapPost("/database/clear", async (ScanService scan) => {
			await scan.ClearDatabaseAsync();
			return Results.Ok(new DatabaseClearResponse { Success = true });
		});

		// GET /api/settings/web — get web-specific settings
		group.MapGet("/web", (WebSettingsService ws) => {
			return Results.Ok(new WebSettingsDto {
				AutoLoadThumbnails = ws.AutoLoadThumbnails,
				ThumbnailWidth = ws.ThumbnailWidth,
				ThumbnailJpegQuality = ws.ThumbnailJpegQuality,
			});
		});

		// PUT /api/settings/web — update web settings
		group.MapPut("/web", (WebSettingsService ws, WebSettingsDto dto) => {
			ws.AutoLoadThumbnails = dto.AutoLoadThumbnails;
			ws.ThumbnailWidth = Math.Clamp(dto.ThumbnailWidth, 48, 960);
			ws.ThumbnailJpegQuality = Math.Clamp(dto.ThumbnailJpegQuality, 10, 95);
			return Results.Ok(new WebSettingsDto {
				AutoLoadThumbnails = ws.AutoLoadThumbnails,
				ThumbnailWidth = ws.ThumbnailWidth,
				ThumbnailJpegQuality = ws.ThumbnailJpegQuality,
			});
		});

		// GET /api/settings/presets — list available presets
		group.MapGet("/presets", () => {
			var presets = Enum.GetValues<ScanPreset>()
				.Select(p => new { name = p.ToString(), value = (int)p })
				.ToList();
			return Results.Ok(presets);
		});

		// POST /api/settings/presets/apply — apply a preset
		group.MapPost("/presets/apply", (ScanService scan, PresetRequest req) => {
			if (!Enum.TryParse<ScanPreset>(req.Preset, true, out var preset))
				return Results.BadRequest(new { error = "invalid_preset" });
			scan.Settings.ApplyPreset(preset);
			return Results.Ok(new { applied = req.Preset });
		});

		return app;
	}
}
