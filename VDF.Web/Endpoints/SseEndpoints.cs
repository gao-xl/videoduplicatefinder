using System.Text;
using System.Text.Json;
using VDF.Web.Models;
using VDF.Web.Services;

namespace VDF.Web.Endpoints;

static class SseEndpoints {
	public static WebApplication MapSseApi(this WebApplication app) {
		var group = app.MapGroup("/api/scan");
		group.RequireAuthorization();

		// GET /api/scan/events — SSE endpoint for scan progress
		group.MapGet("/events", async (HttpContext ctx, ScanService scan, ILoggerFactory loggerFactory) => {
			var logger = loggerFactory.CreateLogger("SseEndpoints");
			ctx.Response.ContentType = "text/event-stream";
			ctx.Response.Headers.CacheControl = "no-cache";
			ctx.Response.Headers.Connection = "keep-alive";

			var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted);

			// Send initial state
			await SendEvent(ctx, "state", scan.State.ToString(), cts.Token);

			// Subscribe to ScanService events
			void OnStateChanged() {
				try {
					var state = scan.State.ToString();
					var msg = JsonSerializer.Serialize(new { state, errorMessage = scan.ErrorMessage });
					// Fire-and-forget write; if the connection is dead the cancellation
					// token will stop the loop on the next iteration.
					SendEvent(ctx, "state", msg, cts.Token).GetAwaiter().GetResult();
				}
				catch (Exception ex) { logger.LogWarning(ex, "Error sending SSE event"); }
			}

			scan.StateChanged += OnStateChanged;

			try {
				// Keep the connection open until the client disconnects
				while (!cts.Token.IsCancellationRequested) {
					// Send periodic progress updates
					if (scan.LastProgress != null) {
						var p = scan.LastProgress;
						var progress = JsonSerializer.Serialize(new ScanProgressResponse {
							State = scan.State.ToString(),
							FilesHashed = scan.FilesHashed,
							CurrentFile = p.CurrentFile,
							Current = p.Current,
							Max = p.Max,
							ElapsedSeconds = p.Elapsed.TotalSeconds,
							RemainingSeconds = p.Remaining.TotalSeconds,
							CurrentStage = p.CurrentStage,
							StageCurrent = p.StageCurrent,
							StageMax = p.StageMax,
							ErrorMessage = scan.ErrorMessage,
							CurrentThumbnailPath = p.CurrentThumbnailPath,
						});
						await SendEvent(ctx, "progress", progress, cts.Token);
					}

					// Send file op progress if active
					if (scan.FileOpRunning) {
						var fileOp = JsonSerializer.Serialize(new {
							current = scan.FileOpCurrent,
							max = scan.FileOpMax,
							verb = scan.FileOpVerb,
						});
						await SendEvent(ctx, "fileop", fileOp, cts.Token);
					}

					await Task.Delay(500, cts.Token);
				}
			}
			catch (OperationCanceledException) {
				// Client disconnected — expected
			}
			finally {
				scan.StateChanged -= OnStateChanged;
				cts.Dispose();
			}
		});

		return app;
	}

	static async Task SendEvent(HttpContext ctx, string eventType, string data, CancellationToken ct) {
		var sb = new StringBuilder();
		sb.Append("event: ").AppendLine(eventType);
		sb.Append("data: ").AppendLine(data);
		sb.AppendLine();
		await ctx.Response.WriteAsync(sb.ToString(), ct);
		await ctx.Response.Body.FlushAsync(ct);
	}
}
