using Microsoft.AspNetCore.SignalR;
using VDF.Core.Services;
using VDF.Web.ApiModels;
using VDF.Web.Hubs;
using VDF.Web.Models;
using VDF.Web.Services;

namespace VDF.Web.Endpoints;

static class ScanEndpoints {
	public static WebApplication MapScanApi(this WebApplication app) {
		var group = app.MapGroup("/api/scan");
		group.RequireAuthorization();

		group.MapPost("/start", (ScanService scan, ScanStartRequest? _) => {
			if (scan.State == ScanState.Scanning || scan.State == ScanState.Comparing)
				return Results.Json(ApiResponse.Fail("scan_already_running", "scan_in_progress"), statusCode: 409);
			scan.StartScanAndCompare();
			return Results.Json(ApiResponse.Ok(new { scanId = Guid.NewGuid().ToString("N")[..8] }), statusCode: 202);
		}).RequireRateLimiting("scan");

		group.MapPost("/stop", (ScanService scan) => {
			if (scan.State != ScanState.Scanning && scan.State != ScanState.Comparing)
				return Results.Json(ApiResponse.Fail("no_scan_running", "no_active_scan"), statusCode: 400);
			scan.Stop();
			return Results.Ok(ApiResponse.Ok(new { status = "stopping" }));
		});

		group.MapPost("/pause", (ScanService scan) => {
			if (scan.State != ScanState.Scanning && scan.State != ScanState.Comparing)
				return Results.Json(ApiResponse.Fail("no_scan_running", "no_active_scan"), statusCode: 400);
			scan.Pause();
			return Results.Ok(ApiResponse.Ok(new { status = "paused" }));
		});

		group.MapPost("/resume", (ScanService scan) => {
			if (scan.State != ScanState.Scanning && scan.State != ScanState.Comparing)
				return Results.Json(ApiResponse.Fail("no_scan_running", "no_active_scan"), statusCode: 400);
			scan.Resume();
			return Results.Ok(ApiResponse.Ok(new { status = "resumed" }));
		});

		group.MapGet("/progress", (ScanService scan) => {
			return Results.Ok(ApiResponse.Ok(scan.BuildProgressResponse()));
		});

		group.MapGet("/state", (ScanService scan) => {
			return Results.Ok(ApiResponse.Ok(new ScanStateResponse {
				State = scan.State.ToString(),
				ErrorMessage = scan.ErrorMessage,
			}));
		});

		group.MapPost("/reset", (ScanService scan) => {
			if (scan.State == ScanState.Scanning || scan.State == ScanState.Comparing)
				return Results.Json(ApiResponse.Fail("scan_running", "scan_in_progress"), statusCode: 400);
			scan.Reset();
			return Results.Ok(ApiResponse.Ok(new { status = "reset" }));
		});

		group.MapPost("/clear-database", async (ScanService scan) => {
			if (scan.State == ScanState.Scanning || scan.State == ScanState.Comparing)
				return Results.Json(ApiResponse.Fail("scan_running", "scan_in_progress"), statusCode: 400);
			await scan.ClearDatabaseAsync();
			return Results.Ok(ApiResponse.Ok(new { status = "database_cleared" }));
		});

		return app;
	}
}
