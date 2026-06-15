using VDF.Core;
using VDF.Web.Models;
using VDF.Web.Services;

namespace VDF.Web.Endpoints;

static class SettingsEndpoints {
	public static WebApplication MapSettingsApi(this WebApplication app) {
		var group = app.MapGroup("/api/settings");
		group.RequireAuthorization();

		// GET /api/settings — get current settings
		group.MapGet("/", (ScanService scan) => {
			var s = scan.Settings;
			return Results.Ok(new {
				IncludeList = s.IncludeList.ToList(),
				BlackList = s.BlackList.ToList(),
				Threshhold = s.Threshhold,
				Percent = s.Percent,
				PercentDurationDifference = s.PercentDurationDifference,
				MaxDegreeOfParallelism = s.MaxDegreeOfParallelism,
				ThumbnailCount = s.ThumbnailCount,
				IncludeSubDirectories = s.IncludeSubDirectories,
				IncludeImages = s.IncludeImages,
				UsePHashing = s.UsePHashing,
				IgnoreReadOnlyFolders = s.IgnoreReadOnlyFolders,
				IgnoreReparsePoints = s.IgnoreReparsePoints,
				ExcludeHardLinks = s.ExcludeHardLinks,
				UseExifCreationDate = s.UseExifCreationDate,
				AlwaysRetryFailedSampling = s.AlwaysRetryFailedSampling,
				ExtendedFFToolsLogging = s.ExtendedFFToolsLogging,
				LogExcludedFiles = s.LogExcludedFiles,
				UseNativeFfmpegBinding = s.UseNativeFfmpegBinding,
				HardwareAccelerationMode = s.HardwareAccelerationMode.ToString(),
				CustomFFArguments = s.CustomFFArguments,
				CustomDatabaseFolder = s.CustomDatabaseFolder,
				DatabaseCheckpointIntervalMinutes = s.DatabaseCheckpointIntervalMinutes,
				CompareHorizontallyFlipped = s.CompareHorizontallyFlipped,
				IgnoreBlackPixels = s.IgnoreBlackPixels,
				IgnoreWhitePixels = s.IgnoreWhitePixels,
				IncludeNonExistingFiles = s.IncludeNonExistingFiles,
				ScanAgainstEntireDatabase = s.ScanAgainstEntireDatabase,
				FolderMatchMode = s.FolderMatchMode.ToString(),
				SameFolderDepth = s.SameFolderDepth,
				DurationDifferenceMinSeconds = s.DurationDifferenceMinSeconds,
				DurationDifferenceMaxSeconds = s.DurationDifferenceMaxSeconds,
				MaxSamplingDurationSeconds = s.MaxSamplingDurationSeconds,
				FilterByFileSize = s.FilterByFileSize,
				MinimumFileSize = s.MinimumFileSize,
				MaximumFileSize = s.MaximumFileSize,
				FilterByFilePathContains = s.FilterByFilePathContains,
				FilePathContainsTexts = s.FilePathContainsTexts,
				FilterByFilePathNotContains = s.FilterByFilePathNotContains,
				FilePathNotContainsTexts = s.FilePathNotContainsTexts,
				EnablePartialClipDetection = s.EnablePartialClipDetection,
				PartialClipMinRatio = s.PartialClipMinRatio,
				PartialClipSimilarityThreshold = s.PartialClipSimilarityThreshold,
				PartialClipRequireVisualMatch = s.PartialClipRequireVisualMatch,
				PartialClipVisualThreshold = s.PartialClipVisualThreshold,
			});
		});

		// PUT /api/settings — update settings
		group.MapPut("/", (ScanService scan, WebSettingsService settingsService, WebSettingsService.Dto dto) => {
			var s = scan.Settings;
			s.IncludeList = new HashSet<string>(dto.IncludeList, StringComparer.OrdinalIgnoreCase);
			s.BlackList = new HashSet<string>(dto.BlackList, StringComparer.OrdinalIgnoreCase);
			s.Threshhold = dto.Threshhold;
			s.Percent = dto.Percent;
			s.PercentDurationDifference = dto.PercentDurationDifference;
			s.MaxDegreeOfParallelism = dto.MaxDegreeOfParallelism;
			s.ThumbnailCount = dto.ThumbnailCount;
			s.IncludeSubDirectories = dto.IncludeSubDirectories;
			s.IncludeImages = dto.IncludeImages;
			s.UsePHashing = dto.UsePHashing;
			s.IgnoreReadOnlyFolders = dto.IgnoreReadOnlyFolders;
			s.IgnoreReparsePoints = dto.IgnoreReparsePoints;
			s.ExcludeHardLinks = dto.ExcludeHardLinks;
			s.UseExifCreationDate = dto.UseExifCreationDate;
			s.AlwaysRetryFailedSampling = dto.AlwaysRetryFailedSampling;
			s.ExtendedFFToolsLogging = dto.ExtendedFFToolsLogging;
			s.LogExcludedFiles = dto.LogExcludedFiles;
			s.UseNativeFfmpegBinding = dto.UseNativeFfmpegBinding;
			s.HardwareAccelerationMode = dto.HardwareAccelerationMode;
			s.CustomFFArguments = dto.CustomFFArguments;
			s.CustomDatabaseFolder = dto.CustomDatabaseFolder;
			s.DatabaseCheckpointIntervalMinutes = dto.DatabaseCheckpointIntervalMinutes;
			s.CompareHorizontallyFlipped = dto.CompareHorizontallyFlipped;
			s.IgnoreBlackPixels = dto.IgnoreBlackPixels;
			s.IgnoreWhitePixels = dto.IgnoreWhitePixels;
			s.IncludeNonExistingFiles = dto.IncludeNonExistingFiles;
			s.ScanAgainstEntireDatabase = dto.ScanAgainstEntireDatabase;
			s.FolderMatchMode = dto.FolderMatchMode;
			s.SameFolderDepth = dto.SameFolderDepth;
			s.DurationDifferenceMinSeconds = dto.DurationDifferenceMinSeconds;
			s.DurationDifferenceMaxSeconds = dto.DurationDifferenceMaxSeconds;
			s.MaxSamplingDurationSeconds = dto.MaxSamplingDurationSeconds;
			s.FilterByFileSize = dto.FilterByFileSize;
			s.MinimumFileSize = dto.MinimumFileSize;
			s.MaximumFileSize = dto.MaximumFileSize;
			s.FilterByFilePathContains = dto.FilterByFilePathContains;
			s.FilePathContainsTexts = dto.FilePathContainsTexts.ToList();
			s.FilterByFilePathNotContains = dto.FilterByFilePathNotContains;
			s.FilePathNotContainsTexts = dto.FilePathNotContainsTexts.ToList();
			s.EnablePartialClipDetection = dto.EnablePartialClipDetection;
			s.PartialClipMinRatio = dto.PartialClipMinRatio;
			s.PartialClipSimilarityThreshold = dto.PartialClipSimilarityThreshold;
			s.PartialClipRequireVisualMatch = dto.PartialClipRequireVisualMatch;
			s.PartialClipVisualThreshold = dto.PartialClipVisualThreshold;
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

		return app;
	}
}
