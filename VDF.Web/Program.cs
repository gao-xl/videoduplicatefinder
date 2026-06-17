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

using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using VDF.Core;
using VDF.Web.Endpoints;
using VDF.Web.Hubs;
using VDF.Web.Middleware;
using VDF.Web.Services;
using VDF.Web.Utils;

var builder = WebApplication.CreateBuilder(args);

// ── Load config.json for early configuration (CORS, TLS, base path, port) ──
// This mirrors the logic in WebConfigService so it can be used before DI is built.
static string GetEarlyConfigPath() {
	var explicitPath = Environment.GetEnvironmentVariable("VDF_CONFIG_PATH");
	if (!string.IsNullOrWhiteSpace(explicitPath)) return explicitPath;
	return Path.Combine(AppContext.BaseDirectory, "config.json");
}

string? configPassword = null;
List<string>? configCorsOrigins = null;
string? configTlsCert = null;
string? configTlsKey = null;
string? configBasePath = null;
int? configPort = null;

var configPath = GetEarlyConfigPath();
if (File.Exists(configPath)) {
	try {
		var json = File.ReadAllText(configPath);
		using var doc = System.Text.Json.JsonDocument.Parse(json);
		if (doc.RootElement.TryGetProperty("web", out var web)) {
			if (web.TryGetProperty("password", out var pw) && pw.ValueKind == System.Text.Json.JsonValueKind.String)
				configPassword = pw.GetString();
			if (web.TryGetProperty("basePath", out var bp) && bp.ValueKind == System.Text.Json.JsonValueKind.String)
				configBasePath = bp.GetString();
			if (web.TryGetProperty("tlsCert", out var tc) && tc.ValueKind == System.Text.Json.JsonValueKind.String)
				configTlsCert = tc.GetString();
			if (web.TryGetProperty("tlsKey", out var tk) && tk.ValueKind == System.Text.Json.JsonValueKind.String)
				configTlsKey = tk.GetString();
			if (web.TryGetProperty("port", out var pt) && pt.ValueKind == System.Text.Json.JsonValueKind.Number)
				configPort = pt.GetInt32();
			if (web.TryGetProperty("corsOrigins", out var co) && co.ValueKind == System.Text.Json.JsonValueKind.Array) {
				configCorsOrigins = [];
				foreach (var item in co.EnumerateArray())
					if (item.ValueKind == System.Text.Json.JsonValueKind.String)
						configCorsOrigins.Add(item.GetString()!);
			}
		}
	}
	catch (Exception ex) {
		Console.WriteLine($"Warning: Failed to parse config.json at {configPath}: {ex.Message}");
	}
}

// ── VDF_BASE_PATH: serve the app under a sub-path when behind a reverse proxy ──
var basePath = configBasePath?.Trim('/') ?? Environment.GetEnvironmentVariable("VDF_BASE_PATH")?.Trim('/');
if (!string.IsNullOrEmpty(basePath)) {
	basePath = "/" + basePath;
	builder.Configuration["BasePath"] = basePath;
}

// React SPA - static file serving only (no Blazor Server)
builder.Services.AddAntiforgery();

builder.Services.AddHttpContextAccessor();

// Register WebConfigService first so AuthService and other services can use it
builder.Services.AddSingleton<WebConfigService>();

// Create JwtService early so it can be used for both DI and JWT Bearer configuration
// Use NullLogger to avoid leaking disposable LoggerFactory resources
var jwtService = new JwtService(Microsoft.Extensions.Logging.Abstractions.NullLogger<JwtService>.Instance);
builder.Services.AddSingleton(jwtService);
builder.Services.AddSingleton<AuthService>();
builder.Services.AddSingleton<WebSettingsService>();
// ScanService is a singleton — one scan at a time, shared across all connections.
builder.Services.AddSingleton<ScanService>();
builder.Services.AddSingleton<FFmpegSetupService>();
builder.Services.AddSingleton<HealthCheckService>();

// --- SignalR ---
builder.Services.AddSignalR();

// --- Swagger / OpenAPI ---
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// --- JWT Bearer Authentication ---
builder.Services.AddAuthentication(options => {
	options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
	options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options => {
	options.TokenValidationParameters = new TokenValidationParameters {
		ValidateIssuer = true,
		ValidIssuer = "VDF",
		ValidateAudience = true,
		ValidAudience = "VDF",
		ValidateLifetime = true,
		ValidateIssuerSigningKey = true,
		IssuerSigningKey = jwtService.GetSigningKey(),
		ClockSkew = TimeSpan.FromSeconds(30),
	};
	// Also read token from query string for SignalR / download scenarios
	options.Events = new JwtBearerEvents {
		OnMessageReceived = context => {
			var accessToken = context.Request.Query["access_token"];
			if (!string.IsNullOrEmpty(accessToken))
				context.Token = accessToken;
			return Task.CompletedTask;
		},
	};
});

builder.Services.AddAuthorization();

// --- Rate Limiting ---
builder.Services.AddRateLimiter(options => {
	options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
		RateLimitPartition.GetFixedWindowLimiter(
			context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
			_ => new FixedWindowRateLimiterOptions {
				PermitLimit = 100,
				Window = TimeSpan.FromMinutes(1),
				QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
				QueueLimit = 0,
			}));
	options.AddPolicy("login", context =>
		RateLimitPartition.GetFixedWindowLimiter(
			context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
			_ => new FixedWindowRateLimiterOptions {
				PermitLimit = 5,
				Window = TimeSpan.FromMinutes(1),
				QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
				QueueLimit = 0,
			}));
	options.AddPolicy("scan", context =>
		RateLimitPartition.GetFixedWindowLimiter(
			context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
			_ => new FixedWindowRateLimiterOptions {
				PermitLimit = 3,
				Window = TimeSpan.FromMinutes(1),
				QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
				QueueLimit = 0,
			}));
	options.OnRejected = async (context, ct) => {
		context.HttpContext.Response.StatusCode = 429;
		await context.HttpContext.Response.WriteAsJsonAsync(new { error = "too_many_requests" }, ct);
	};
});

// --- CORS ---
// Prefer config.json, fall back to VDF_CORS_ORIGINS env var
var corsOrigins = configCorsOrigins?.Count > 0
	? string.Join(",", configCorsOrigins)
	: Environment.GetEnvironmentVariable("VDF_CORS_ORIGINS");
if (!string.IsNullOrEmpty(corsOrigins)) {
	builder.Services.AddCors(options => {
		options.AddDefaultPolicy(policy => {
			var origins = corsOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
			policy.WithOrigins(origins)
				.AllowAnyHeader()
				.AllowAnyMethod()
				.AllowCredentials();
		});
	});
}
else {
	// Restrictive default: no cross-origin requests allowed
	builder.Services.AddCors(options => {
		options.AddDefaultPolicy(policy => {
			// No AllowAnyOrigin — only same-origin requests are permitted
		});
	});
}

// --- HTTPS / TLS ---
var tlsCert = configTlsCert ?? Environment.GetEnvironmentVariable("VDF_TLS_CERT");
var tlsKey = configTlsKey ?? Environment.GetEnvironmentVariable("VDF_TLS_KEY");
if (!string.IsNullOrEmpty(tlsCert) && !string.IsNullOrEmpty(tlsKey)) {
	builder.WebHost.ConfigureKestrel(options => {
		options.ConfigureEndpointDefaults(listenOptions => {
			listenOptions.UseHttps(httpsOptions => {
				httpsOptions.ServerCertificate = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadCertificateFromFile(tlsCert!);
			});
		});
	});
}

// --- HTTP Port ---
if (configPort.HasValue && configPort > 0) {
	builder.WebHost.UseUrls($"http://0.0.0.0:{configPort}");
}

// --- ForwardedHeaders for reverse proxy support ---
builder.Services.Configure<ForwardedHeadersOptions>(options => {
	options.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
		| Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
	options.KnownIPNetworks.Clear();
	options.KnownProxies.Clear();
});

var app = builder.Build();

// Global exception handling middleware (must be first in pipeline)
app.UseMiddleware<ExceptionMiddleware>();

if (string.IsNullOrEmpty(corsOrigins)) {
	app.Logger.LogWarning("CORS policy is restrictive by default — only same-origin requests are permitted. " +
		"Set 'corsOrigins' in config.json or VDF_CORS_ORIGINS environment variable to allow cross-origin requests.");
}

// Apply path base before any other middleware so all route matching and link
// generation use the correct prefix. When VDF_BASE_PATH is not set, this is a
// no-op and everything works as before.
if (!string.IsNullOrEmpty(basePath)) {
	app.UsePathBase(basePath);
}

// Route unhandled exceptions from ScanEngine's async void methods (post-await) to ScanService
// so they appear in the UI instead of crashing the process silently.
var scanService = app.Services.GetRequiredService<ScanService>();

AppDomain.CurrentDomain.UnhandledException += (_, e) => {
	var ex = e.ExceptionObject as Exception
		?? new Exception(e.ExceptionObject?.ToString() ?? "Unknown error");
	app.Logger.LogError(ex, "Unhandled exception in background thread");
	scanService.SetError(ex);
};

TaskScheduler.UnobservedTaskException += (_, e) => {
	app.Logger.LogError(e.Exception, "Unobserved task exception");
	scanService.SetError(e.Exception);
	e.SetObserved();
};

// ForwardedHeaders middleware — must be before other middleware
app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment()) {
	app.UseExceptionHandler("/Error");
}

// Swagger UI (development only)
if (app.Environment.IsDevelopment()) {
	app.UseSwagger();
	app.UseSwaggerUI();
}

app.UseStaticFiles();

// Health check endpoint — lightweight, no auth required, for load balancers / orchestrators.
app.MapGet("/health", async (HealthCheckService health) => {
	var report = await health.CheckHealthAsync();
	return Results.Json(report);
});

// CORS
app.UseCors();

// Rate limiting
app.UseRateLimiter();

// Authentication & Authorization
app.UseAuthentication();
app.UseMiddleware<ApiKeyMiddleware>();
app.UseAuthorization();

// Authentication gate — redirect unauthenticated requests to /login
var authService = app.Services.GetRequiredService<AuthService>();
app.Use(async (ctx, next) => {
	var path = ctx.Request.Path.Value ?? "/";
	// Always allow: login page, auth endpoints, health check, static files, API auth endpoints
	if (!authService.AuthEnabled
		|| path.StartsWith("/login", StringComparison.OrdinalIgnoreCase)
		|| path.StartsWith("/auth/", StringComparison.OrdinalIgnoreCase)
		|| path.StartsWith("/api/auth/", StringComparison.OrdinalIgnoreCase)
		|| path.StartsWith("/health", StringComparison.OrdinalIgnoreCase)
		|| path.StartsWith("/assets/", StringComparison.OrdinalIgnoreCase)
		|| path.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
		|| path.EndsWith(".css", StringComparison.OrdinalIgnoreCase)
		|| path.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
		|| path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
		|| path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
		|| path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase)
		|| path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase)) {
		await next();
		return;
	}
	if (!authService.IsAuthenticated(ctx)) {
		// For API requests (Accept: application/json or has Authorization header), return 401
		if (ctx.Request.Headers.ContainsKey("Authorization")
			|| ctx.Request.Headers.ContainsKey("X-API-Key")
			|| ctx.Request.GetTypedHeaders().Accept?.Any(a => a.MediaType == "application/json") == true
			|| path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)) {
			ctx.Response.StatusCode = 401;
			return;
		}
		var returnUrl = Uri.EscapeDataString(RedirectHelper.SafeReturnUrl(path));
		ctx.Response.Redirect($"/login?returnUrl={returnUrl}");
		return;
	}
	await next();
});

// HQ thumbnail endpoint — extracts a fresh frame using configurable resolution and quality.
// Used by the card-based results view for crisp thumbnails.
var webSettings = app.Services.GetRequiredService<WebSettingsService>();
app.MapGet("/thumbnail/hq", async (HttpContext ctx, ScanService scan) => {
	string? path = ctx.Request.Query["path"];
	if (string.IsNullOrEmpty(path)) { ctx.Response.StatusCode = 400; return; }

	path = Path.GetFullPath(path);
	var item = scan.Duplicates.FirstOrDefault(d => d.Path == path);
	if (item == null) { ctx.Response.StatusCode = 404; return; }

	// Honor the w/q the page requested (falling back to the current settings) so
	// cached browser URLs stay consistent with the bytes they were rendered from.
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

// Full-resolution thumbnail endpoint — extracts at original resolution for the comparison modal.
app.MapGet("/thumbnail/full", async (HttpContext ctx, ScanService scan) => {
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

// CSV export of the current results — same column layout as the GUI export,
// minus the GUI-only Checked column.
app.MapGet("/export/csv", (ScanService scan) => {
	static string Escape(string? s) {
		s ??= string.Empty;
		return s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r')
			? "\"" + s.Replace("\"", "\"\"") + "\""
			: s;
	}
	var inv = System.Globalization.CultureInfo.InvariantCulture;
	var sb = new System.Text.StringBuilder();
	sb.AppendLine("GroupId,Path,SizeBytes,Duration,Resolution,Fps,BitrateKbs,AudioFormat,AudioSampleRate,Similarity,DateCreated,IsImage");
	// Keep group members on adjacent rows regardless of list order.
	foreach (var group in scan.Duplicates.GroupBy(i => i.GroupId))
		foreach (var item in group)
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
	// UTF-8 BOM so Excel detects the encoding.
	var utf8 = System.Text.Encoding.UTF8;
	byte[] bytes = [.. utf8.GetPreamble(), .. utf8.GetBytes(sb.ToString())];
	return Microsoft.AspNetCore.Http.Results.File(bytes, "text/csv", "vdf-results.csv");
});

// ── New Minimal API endpoints ──

app.MapScanApi();
app.MapBrowseApi();
app.MapResultApi();
app.MapSettingsApi();
app.MapThumbnailApi();
app.MapAuthApi();
app.MapSseApi();

// ── SignalR hub ──

app.MapHub<ScanHub>("/scanhub");

// SPA fallback — serve index.html for non-API routes (React Router)
app.MapFallbackToFile("index.html");

// Kick off FFmpeg availability check / auto-download in background
var ffmpegSetup = app.Services.GetService<FFmpegSetupService>();
if (ffmpegSetup != null)
	_ = ffmpegSetup.CheckAndSetupAsync();

app.Run();
