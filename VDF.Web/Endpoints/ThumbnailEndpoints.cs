using VDF.Core.Services;
using VDF.Core.ViewModels;
using VDF.Web.Services;

namespace VDF.Web.Endpoints;

static class ThumbnailEndpoints {
	public static WebApplication MapThumbnailApi(this WebApplication app) {
		var group = app.MapGroup("/api/thumbnail");
		group.RequireAuthorization();

		group.MapGet("/hq", async (HttpContext ctx, ScanService scan, WebSettingsService webSettings) => {
			string? path = ctx.Request.Query["path"];
			if (string.IsNullOrEmpty(path)) { ctx.Response.StatusCode = 400; return; }

			path = Path.GetFullPath(path);
			if (!scan.ThumbnailService.IsPathAllowed(path)) { ctx.Response.StatusCode = 403; return; }

			var item = scan.Duplicates.FirstOrDefault(d => d.Path == path);
			if (item == null) { ctx.Response.StatusCode = 404; return; }

			int width = int.TryParse(ctx.Request.Query["w"], out int w) ? w : webSettings.ThumbnailWidth;
			int quality = int.TryParse(ctx.Request.Query["q"], out int q) ? q : webSettings.ThumbnailJpegQuality;
			width = Math.Clamp(width, 48, 960);
			quality = Math.Clamp(quality, 10, 95);

			var jpeg = scan.ThumbnailService.GetThumbnailBytes(path, GetThumbnailPosition(item), width, quality);
			await WriteJpegResponse(ctx, jpeg);
		});

		group.MapGet("/full", async (HttpContext ctx, ScanService scan) => {
			string? path = ctx.Request.Query["path"];
			if (string.IsNullOrEmpty(path)) { ctx.Response.StatusCode = 400; return; }

			path = Path.GetFullPath(path);
			if (!scan.ThumbnailService.IsPathAllowed(path)) { ctx.Response.StatusCode = 403; return; }

			var item = scan.Duplicates.FirstOrDefault(d => d.Path == path);
			if (item == null) { ctx.Response.StatusCode = 404; return; }

			var jpeg = scan.ThumbnailService.GetThumbnailBytes(path, GetThumbnailPosition(item), 0, 85);
			await WriteJpegResponse(ctx, jpeg);
		});

		return app;
	}

	static TimeSpan GetThumbnailPosition(DuplicateItem item) =>
		item.ThumbnailTimestamps.Count > 0
			? item.ThumbnailTimestamps[0]
			: TimeSpan.FromSeconds(item.Duration.TotalSeconds * 0.1);

	static async Task WriteJpegResponse(HttpContext ctx, byte[]? jpeg) {
		if (jpeg == null || jpeg.Length == 0) { ctx.Response.StatusCode = 204; return; }
		ctx.Response.ContentType = "image/jpeg";
		ctx.Response.Headers.CacheControl = "public, max-age=3600";
		await ctx.Response.Body.WriteAsync(jpeg);
	}
}
