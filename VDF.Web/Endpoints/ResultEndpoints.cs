using System.Globalization;
using System.Text;
using VDF.Core.ViewModels;
using VDF.Web.Models;
using VDF.Web.Services;

namespace VDF.Web.Endpoints;

static class ResultEndpoints {
	public static WebApplication MapResultApi(this WebApplication app) {
		var group = app.MapGroup("/api/results");
		group.RequireAuthorization();

		// GET /api/results — list duplicate groups (with pagination)
		group.MapGet("/", (ScanService scan, int? page, int? pageSize, string? search) => {
			int p = Math.Max(1, page ?? 1);
			int ps = Math.Clamp(pageSize ?? 50, 1, 200);

			var allGroups = scan.Duplicates
				.GroupBy(d => d.GroupId)
				.Where(g => g.Count() > 1)
				.ToList();

			// Filter by search term if provided
			if (!string.IsNullOrWhiteSpace(search)) {
				var term = search.Trim();
				allGroups = allGroups
					.Where(g => g.Any(item =>
						item.Path.Contains(term, StringComparison.OrdinalIgnoreCase) ||
						item.Folder.Contains(term, StringComparison.OrdinalIgnoreCase)))
					.ToList();
			}

			int totalGroups = allGroups.Count;
			int totalFiles = allGroups.Sum(g => g.Count());
			long totalSize = allGroups.Sum(g => g.Sum(item => Math.Max(0, item.SizeLong)));

			// Potential savings: sum of all items except the largest in each group
			long potentialSavings = allGroups.Sum(g => {
				var items = g.ToList();
				long maxSize = items.Max(i => Math.Max(0, i.SizeLong));
				return items.Sum(i => Math.Max(0, i.SizeLong)) - maxSize;
			});

			var pagedGroups = allGroups
				.Skip((p - 1) * ps)
				.Take(ps)
				.Select(g => new DuplicateGroupDto {
					GroupId = g.Key,
					Items = g.Select(MapItem).ToList(),
				})
				.ToList();

			return Results.Ok(new ResultsPageResponse {
				Groups = pagedGroups,
				TotalGroups = totalGroups,
				Page = p,
				PageSize = ps,
				TotalFiles = totalFiles,
				TotalSizeBytes = totalSize,
				PotentialSavingsBytes = potentialSavings,
			});
		});

		// DELETE /api/results/items — delete selected items from disk
		group.MapDelete("/items", async (ScanService scan, [FromBody] DeleteItemsRequest req) => {
			if (scan.FileOpRunning)
				return Results.Json(new { error = "file_op_in_progress" }, statusCode: 409);
			var items = FindItems(scan, req.Paths);
			if (items.Count == 0)
				return Results.NotFound(new { error = "no_matching_items" });
			var result = await scan.DeleteItemsAsync(items, req.Permanent);
			return Results.Ok(MapFileOpResult(result));
		});

		// POST /api/results/move — move selected items
		group.MapPost("/move", async (ScanService scan, MoveItemsRequest req) => {
			if (scan.FileOpRunning)
				return Results.Json(new { error = "file_op_in_progress" }, statusCode: 409);
			if (string.IsNullOrWhiteSpace(req.Destination))
				return Results.Json(new { error = "destination_required" }, statusCode: 400);
			var items = FindItems(scan, req.Paths);
			if (items.Count == 0)
				return Results.NotFound(new { error = "no_matching_items" });
			var result = await scan.MoveItemsAsync(items, req.Destination);
			return Results.Ok(MapFileOpResult(result));
		});

		// POST /api/results/links — replace with links
		group.MapPost("/links", async (ScanService scan, CreateLinksRequest req) => {
			if (scan.FileOpRunning)
				return Results.Json(new { error = "file_op_in_progress" }, statusCode: 409);
			var items = FindItems(scan, req.Paths);
			if (items.Count == 0)
				return Results.NotFound(new { error = "no_matching_items" });
			var result = await scan.CreateLinksAsync(items, req.Hardlink);
			return Results.Ok(MapFileOpResult(result));
		});

		// DELETE /api/results/remove — remove items from results list (no disk change)
		group.MapDelete("/remove", (ScanService scan, [FromBody] RemoveItemsRequest req) => {
			var items = FindItems(scan, req.Paths);
			if (items.Count == 0)
				return Results.NotFound(new { error = "no_matching_items" });
			scan.RemoveFromResults(items);
			return Results.Ok(new { removed = items.Count });
		});

		// GET /api/results/export/csv — export results as CSV
		group.MapGet("/export/csv", (ScanService scan) => {
			static string Escape(string? s) {
				s ??= string.Empty;
				return s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r')
					? "\"" + s.Replace("\"", "\"\"") + "\""
					: s;
			}
			var inv = CultureInfo.InvariantCulture;
			var sb = new StringBuilder();
			sb.AppendLine("GroupId,Path,SizeBytes,Duration,Resolution,Fps,BitrateKbs,AudioFormat,AudioSampleRate,Similarity,DateCreated,IsImage");
			foreach (var g in scan.Duplicates.GroupBy(i => i.GroupId))
				foreach (var item in g)
					sb.AppendLine(string.Join(',',
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
						item.IsImage.ToString()));
			var utf8 = Encoding.UTF8;
			byte[] bytes = [.. utf8.GetPreamble(), .. utf8.GetBytes(sb.ToString())];
			return Results.File(bytes, "text/csv", "vdf-results.csv");
		});

		// POST /api/results/autoselect — auto-select items based on mode
		group.MapPost("/autoselect", (ScanService scan, AutoSelectRequest req) => {
			var allGroups = scan.Duplicates
				.GroupBy(d => d.GroupId)
				.Where(g => g.Count() > 1)
				.ToList();

			var selected = new List<DuplicateItem>();
			foreach (var g in allGroups) {
				var items = g.ToList();
				DuplicateItem? pick = req.Mode switch {
					"lowestQuality" => items.OrderBy(i => i.FrameSizeInt)
						.ThenBy(i => i.BitRateKbs).FirstOrDefault(),
					"smallestFile" => items.OrderBy(i => i.SizeLong).FirstOrDefault(),
					"oldest" => items.OrderBy(i => i.DateCreated).FirstOrDefault(),
					"newest" => items.OrderByDescending(i => i.DateCreated).FirstOrDefault(),
					"hundredPercentEqual" => items.FirstOrDefault(i => Math.Abs(i.Similarity - 100f) < 0.01f),
					_ => null,
				};
				if (pick != null)
					selected.Add(pick);
			}

			return Results.Ok(new {
				selectedPaths = selected.Select(i => i.Path).ToList(),
				count = selected.Count,
			});
		});

		// POST /api/results/keepbest — select all except best in group
		group.MapPost("/keepbest", (ScanService scan, KeepBestRequest req) => {
			var groupItems = scan.Duplicates
				.Where(d => d.GroupId == req.GroupId)
				.ToList();

			if (groupItems.Count == 0)
				return Results.NotFound(new { error = "group_not_found" });

			// Use QualityRanker to find the best item
			var criteria = new List<VDF.Core.Utils.QualityRanker.Criterion<DuplicateItem>> {
				new("FrameSizeInt", d => d.FrameSizeInt, videoOnly: true),
				new("BitRateKbs", d => d.BitRateKbs, videoOnly: true),
				new("AudioBitRateKbs", d => d.AudioBitRateKbs, videoOnly: false),
				new("AudioSampleRate", d => d.AudioSampleRate, videoOnly: false),
				new("Duration", d => d.Duration, videoOnly: false),
				new("Fps", d => d.Fps, videoOnly: true),
				new("HdrFormatRank", d => d.HdrFormatRank, videoOnly: true),
				new("SizeLong", d => d.SizeLong, videoOnly: false, ascending: true),
			};
			var keeper = VDF.Core.Utils.QualityRanker.PickKeeper(
				groupItems, criteria, d => d.IsImage);

			var toSelect = groupItems.Where(d => d != keeper).ToList();
			return Results.Ok(new {
				keeperPath = keeper.Path,
				selectedPaths = toSelect.Select(i => i.Path).ToList(),
				count = toSelect.Count,
			});
		});

		return app;
	}

	static DuplicateItemDto MapItem(DuplicateItem item) => new() {
		Path = item.Path,
		Folder = item.Folder,
		SizeBytes = item.SizeLong,
		DurationSeconds = item.Duration.TotalSeconds,
		FrameSize = item.FrameSize,
		Fps = item.Fps,
		BitRateKbs = item.BitRateKbs,
		Format = item.Format,
		AudioFormat = item.AudioFormat,
		AudioChannel = item.AudioChannel,
		AudioSampleRate = item.AudioSampleRate,
		AudioBitRateKbs = item.AudioBitRateKbs,
		Similarity = item.Similarity,
		DateCreated = item.DateCreated,
		IsImage = item.IsImage,
		HdrFormat = item.HdrFormat,
		Flags = item.Flags.ToString(),
		PartialClipOffsetSeconds = item.PartialClipOffset.TotalSeconds,
		GroupId = item.GroupId,
	};

	static List<DuplicateItem> FindItems(ScanService scan, List<string> paths) {
		var pathSet = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
		return scan.Duplicates.Where(d => pathSet.Contains(d.Path)).ToList();
	}

	static FileOpResultDto MapFileOpResult(FileOpResult result) => new() {
		Done = result.Done,
		Failed = result.Failed,
		FreedBytes = result.FreedBytes,
		Errors = result.Errors,
		Warnings = result.Warnings,
	};
}
