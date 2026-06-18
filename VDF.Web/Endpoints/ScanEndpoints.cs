using Microsoft.AspNetCore.SignalR;
using VDF.Core.Services;
using VDF.Web.Hubs;
using VDF.Web.Models;
using VDF.Web.Services;

namespace VDF.Web.Endpoints;

static class ScanEndpoints {
	public static WebApplication MapScanApi(this WebApplication app) {
		var group = app.MapGroup("/api/scan");
		group.RequireAuthorization();

		// POST /api/scan/start — start scan and compare
		group.MapPost("/start", (ScanService scan, ScanStartRequest? _) => {
			if (scan.State == ScanState.Scanning || scan.State == ScanState.Comparing)
				return Results.Json(new { error = "scan_already_running" }, statusCode: 409);
			scan.StartScanAndCompare();
			return Results.Json(new { scanId = Guid.NewGuid().ToString("N")[..8] }, statusCode: 202);
		}).RequireRateLimiting("scan");

		// POST /api/scan/stop — stop current scan
		group.MapPost("/stop", (ScanService scan) => {
			if (scan.State != ScanState.Scanning && scan.State != ScanState.Comparing)
				return Results.Json(new { error = "no_scan_running" }, statusCode: 400);
			scan.Stop();
			return Results.Ok(new { status = "stopping" });
		});

		// POST /api/scan/pause — pause current scan
		group.MapPost("/pause", (ScanService scan) => {
			if (scan.State != ScanState.Scanning && scan.State != ScanState.Comparing)
				return Results.Json(new { error = "no_scan_running" }, statusCode: 400);
			scan.Pause();
			return Results.Ok(new { status = "paused" });
		});

		// POST /api/scan/resume — resume current scan
		group.MapPost("/resume", (ScanService scan) => {
			if (scan.State != ScanState.Scanning && scan.State != ScanState.Comparing)
				return Results.Json(new { error = "no_scan_running" }, statusCode: 400);
			scan.Resume();
			return Results.Ok(new { status = "resumed" });
		});

		// GET /api/scan/progress — get current scan progress
		group.MapGet("/progress", (ScanService scan) => {
			return Results.Ok(scan.BuildProgressResponse());
		});

		// GET /api/scan/state — get current scan state only
		group.MapGet("/state", (ScanService scan) => {
			return Results.Ok(new ScanStateResponse {
				State = scan.State.ToString(),
				ErrorMessage = scan.ErrorMessage,
			});
		});

		// POST /api/scan/reset — reset scan state and results
		group.MapPost("/reset", (ScanService scan) => {
			if (scan.State == ScanState.Scanning || scan.State == ScanState.Comparing)
				return Results.Json(new { error = "scan_running" }, statusCode: 400);
			scan.Reset();
			return Results.Ok(new { status = "reset" });
		});

		// POST /api/scan/clear-database — clear the entire scan database
		group.MapPost("/clear-database", async (ScanService scan) => {
			if (scan.State == ScanState.Scanning || scan.State == ScanState.Comparing)
				return Results.Json(new { error = "scan_running" }, statusCode: 400);
			await scan.ClearDatabaseAsync();
			return Results.Ok(new { status = "database_cleared" });
		});

		return app;
	}
}
