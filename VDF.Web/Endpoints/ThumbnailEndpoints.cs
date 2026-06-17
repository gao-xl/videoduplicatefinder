using System.Collections.Concurrent;
using System.Diagnostics;
using VDF.Core;
using VDF.Web.Services;

namespace VDF.Web.Endpoints;

/// <summary>
/// Simple LRU cache that tracks access time and evicts oldest entries when the limit is reached.
/// </summary>
public sealed class ThumbnailLruCache {
	readonly ConcurrentDictionary<string, (Lazy<byte[]> Value, long LastAccess)> _cache = new();
	readonly int _maxSize;
	readonly object _evictLock = new();

	public int Count => _cache.Count;

	public ThumbnailLruCache(int maxSize) {
		_maxSize = maxSize;
	}

	public byte[]? GetOrAdd(string key, Func<byte[]> valueFactory) {
		var entry = _cache.AddOrUpdate(
			key,
			_ => (new Lazy<byte[]>(valueFactory), Stopwatch.GetTimestamp()),
			(_, existing) => (existing.Value, Stopwatch.GetTimestamp()));

		// Check size and evict if needed
		if (_cache.Count > _maxSize) {
			lock (_evictLock) {
				if (_cache.Count > _maxSize) {
					int toEvict = Math.Max(1, _maxSize / 4);
					var oldest = _cache
						.OrderBy(kvp => kvp.Value.LastAccess)
						.Take(toEvict)
						.Select(kvp => kvp.Key)
						.ToList();
					foreach (var k in oldest) {
						_cache.TryRemove(k, out _);
					}
				}
			}
		}

		return entry.Value.Value;
	}

	public void Clear() {
		_cache.Clear();
	}
}

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

			var jpeg = scan.HqThumbCache.GetOrAdd(cacheKey,
				() => ScanEngine.ExtractThumbnailJpeg(path, position, width, quality));
			if (jpeg == null || jpeg.Length == 0) { ctx.Response.StatusCode = 204; return; }

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

			var jpeg = scan.FullThumbCache.GetOrAdd(cacheKey,
				() => ScanEngine.ExtractThumbnailJpeg(path, position, 0));
			if (jpeg == null || jpeg.Length == 0) { ctx.Response.StatusCode = 204; return; }

			ctx.Response.ContentType = "image/jpeg";
			ctx.Response.Headers.CacheControl = "public, max-age=3600";
			await ctx.Response.Body.WriteAsync(jpeg);
		});

		return app;
	}
}
