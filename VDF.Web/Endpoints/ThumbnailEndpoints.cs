using VDF.Core;
using VDF.Web.Services;

namespace VDF.Web.Endpoints;

static class ThumbnailEndpoints {
	public static WebApplication MapThumbnailApi(this WebApplication app) {
		var group = app.MapGroup("/api/thumbnail");
		group.RequireAuthorization();

		// GET /api/thumbnail/hq — high quality thumbnail
		group.MapGet("/hq", async (HttpContext ctx, ScanService scan, WebSettingsService webSettings) => {
			string? path = ctx.Request.Query["path"];
			if (string.IsNullOrEmpty(path)) { ctx.Response.StatusCode = 400; return; }

			path = Path.GetFullPath(path);
			var item = scan.Duplicates.FirstOrDefault(d => d.Path == path);
			if (item == null) { ctx.Response.StatusCode = 404; return; }

			int width = int.TryParse(ctx.Request.Query["w"], out int w) ? w : webSettings.ThumbnailWidth;
			int quality = int.TryParse(ctx.Request.Query["q"], out int q) ? q : webSettings.ThumbnailJpegQuality;
			width = Math.Clamp(width, 48, 960);
			quality = Math.Clamp(quality, 10, 95);

			var position = item.ThumbnailTimestamps.Count > 0
				? item.ThumbnailTimestamps[0]
				: TimeSpan.FromSeconds(item.Duration.TotalSeconds * 0.1);

			string cacheKey = $"{path}|{position.TotalSeconds:F2}|{width}|{quality}";

			if (!scan.HqThumbCache.TryGetValue(cacheKey, out var jpeg)) {
				jpeg = await Task.Run(() => ScanEngine.ExtractThumbnailJpeg(path, position, width, quality));
				if (jpeg == null || jpeg.Length == 0) { ctx.Response.StatusCode = 204; return; }
				if (scan.HqThumbCache.Count >= 4096)
					scan.HqThumbCache.Clear();
				scan.HqThumbCache.TryAdd(cacheKey, jpeg);
			}

			ctx.Response.ContentType = "image/jpeg";
			ctx.Response.Headers.CacheControl = "public, max-age=3600";
			await ctx.Response.Body.WriteAsync(jpeg);
		});

		// GET /api/thumbnail/full — full resolution thumbnail
		group.MapGet("/full", async (HttpContext ctx, ScanService scan) => {
			string? path = ctx.Request.Query["path"];
			if (string.IsNullOrEmpty(path)) { ctx.Response.StatusCode = 400; return; }

			path = Path.GetFullPath(path);
			var item = scan.Duplicates.FirstOrDefault(d => d.Path == path);
			if (item == null) { ctx.Response.StatusCode = 404; return; }

			var position = item.ThumbnailTimestamps.Count > 0
				? item.ThumbnailTimestamps[0]
				: TimeSpan.FromSeconds(item.Duration.TotalSeconds * 0.1);

			string cacheKey = $"{path}|{position.TotalSeconds:F2}|full";

			if (!scan.FullThumbCache.TryGetValue(cacheKey, out var jpeg)) {
				jpeg = await Task.Run(() => ScanEngine.ExtractThumbnailJpeg(path, position, 0));
				if (jpeg == null || jpeg.Length == 0) { ctx.Response.StatusCode = 204; return; }
				if (scan.FullThumbCache.Count >= 64)
					scan.FullThumbCache.Clear();
				scan.FullThumbCache.TryAdd(cacheKey, jpeg);
			}

			ctx.Response.ContentType = "image/jpeg";
			ctx.Response.Headers.CacheControl = "public, max-age=3600";
			await ctx.Response.Body.WriteAsync(jpeg);
		});

		return app;
	}
}
