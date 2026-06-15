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

// ── VDF_BASE_PATH: serve the app under a sub-path when behind a reverse proxy ──
// e.g. VDF_BASE_PATH=/vdf  →  all routes become /vdf/..., /vdf/login, /vdf/health, etc.
var basePath = Environment.GetEnvironmentVariable("VDF_BASE_PATH")?.Trim('/');
if (!string.IsNullOrEmpty(basePath)) {
	basePath = "/" + basePath;
	builder.Configuration["BasePath"] = basePath;
}

// React SPA - static file serving only (no Blazor Server)
builder.Services.AddAntiforgery();

builder.Services.AddHttpContextAccessor();

// Create JwtService early so it can be used for both DI and JWT Bearer configuration
var jwtService = new JwtService(LoggerFactory.Create(b => { }).CreateLogger<JwtService>());
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
	options.OnRejected = async (context, ct) => {
		context.HttpContext.Response.StatusCode = 429;
		await context.HttpContext.Response.WriteAsJsonAsync(new { error = "too_many_requests" }, ct);
	};
});

// --- CORS ---
var corsOrigins = Environment.GetEnvironmentVariable("VDF_CORS_ORIGINS");
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
var tlsCert = Environment.GetEnvironmentVariable("VDF_TLS_CERT");
var tlsKey = Environment.GetEnvironmentVariable("VDF_TLS_KEY");
if (!string.IsNullOrEmpty(tlsCert) && !string.IsNullOrEmpty(tlsKey)) {
	builder.WebHost.ConfigureKestrel(options => {
		options.ConfigureEndpointDefaults(listenOptions => {
			listenOptions.UseHttps(httpsOptions => {
				httpsOptions.ServerCertificate = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadCertificateFromFile(tlsCert);
			});
		});
	});
}

// --- ForwardedHeaders for reverse proxy support ---
builder.Services.Configure<ForwardedHeadersOptions>(options => {
	options.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
		| Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
	options.KnownIPNetworks.Clear();
	options.KnownProxies.Clear();
});

var app = builder.Build();

if (string.IsNullOrEmpty(corsOrigins)) {
	app.Logger.LogWarning("VDF_CORS_ORIGINS is not set. CORS policy is restrictive by default — only same-origin requests are permitted. Set VDF_CORS_ORIGINS to allow specific origins.");
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

// ── Legacy Blazor Server endpoints (kept for backward compatibility) ──

// Login endpoint — returns JSON with access_token and refresh_token
app.MapPost("/auth/login", async (HttpContext ctx, AuthService auth) => {
	// Support both form-encoded (browser) and JSON (API) requests
	string? password;
	string? returnUrl;
	bool remember;

	var contentType = ctx.Request.ContentType ?? "";
	if (contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase)) {
		using var reader = new StreamReader(ctx.Request.Body, Encoding.UTF8);
		var body = await reader.ReadToEndAsync();
		var json = System.Text.Json.JsonDocument.Parse(body);
		password = json.RootElement.TryGetProperty("password", out var pwEl) ? pwEl.GetString() : null;
		returnUrl = json.RootElement.TryGetProperty("returnUrl", out var ruEl) ? ruEl.GetString() : null;
		remember = json.RootElement.TryGetProperty("remember", out var remEl) && remEl.GetBoolean();
	}
	else {
		var form = await ctx.Request.ReadFormAsync();
		password = form["password"].ToString();
		returnUrl = form["returnUrl"].ToString();
		remember = form["remember"] == "true";
	}

	if (auth.ValidatePassword(password ?? string.Empty)) {
		var accessToken = auth.GenerateAccessToken();
		var refreshToken = auth.GenerateRefreshToken();

		// Also set cookie for backward compatibility with browser UI
		auth.SetAuthCookie(ctx, refreshToken, remember);

		// If the request came from a browser form, redirect
		if (!contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase)) {
			ctx.Response.Redirect(RedirectHelper.SafeReturnUrl(returnUrl));
			return;
		}

		// For API clients, return JSON
		ctx.Response.ContentType = "application/json";
		await ctx.Response.WriteAsJsonAsync(new {
			access_token = accessToken,
			refresh_token = refreshToken,
			expires_in = 900, // 15 minutes in seconds
		});
	}
	else {
		if (!contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase)) {
			var qs = "?error=1";
			var safeReturn = RedirectHelper.SafeReturnUrl(returnUrl);
			if (safeReturn != "/")
				qs += $"&returnUrl={Uri.EscapeDataString(safeReturn)}";
			ctx.Response.Redirect($"/login{qs}");
			return;
		}
		ctx.Response.StatusCode = 401;
		await ctx.Response.WriteAsJsonAsync(new { error = "invalid_credentials" });
	}
}).RequireRateLimiting("login");
app.MapPost("/auth/refresh", (HttpContext ctx, AuthService auth) => {
	if (!ctx.Request.HasJsonContentType()) {
		ctx.Response.StatusCode = 400;
		return Results.Json(new { error = "invalid_request" });
	}
	// Read refresh token from JSON body
	using var reader = new StreamReader(ctx.Request.Body, Encoding.UTF8);
	var body = reader.ReadToEnd();
	var json = System.Text.Json.JsonDocument.Parse(body);
	var refreshToken = json.RootElement.TryGetProperty("refresh_token", out var rtEl) ? rtEl.GetString() : null;

	var newAccessToken = auth.RefreshAccessToken(refreshToken ?? string.Empty);
	if (newAccessToken == null) {
		return Results.Json(new { error = "invalid_refresh_token" }, statusCode: 401);
	}

	return Results.Json(new {
		access_token = newAccessToken,
		expires_in = 900,
	});
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

	var lazy = new Lazy<byte[]>(() => ScanEngine.ExtractThumbnailJpeg(path, position, width, quality));
	if (scan.HqThumbCache.TryAdd(cacheKey, lazy)) {
		if (scan.HqThumbCache.Count > 4096) {
			lock (scan.HqThumbCacheLock) {
				if (scan.HqThumbCache.Count > 4096) {
					scan.HqThumbCache.Clear();
					scan.HqThumbCache.TryAdd(cacheKey, lazy);
				}
			}
		}
	}
	var jpeg = await Task.Run(() => scan.HqThumbCache[cacheKey].Value);
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

	var lazy = new Lazy<byte[]>(() => ScanEngine.ExtractThumbnailJpeg(path, position, 0));
	if (scan.FullThumbCache.TryAdd(cacheKey, lazy)) {
		if (scan.FullThumbCache.Count > 64) {
			lock (scan.FullThumbCacheLock) {
				if (scan.FullThumbCache.Count > 64) {
					scan.FullThumbCache.Clear();
					scan.FullThumbCache.TryAdd(cacheKey, lazy);
				}
			}
		}
	}
	var jpeg = await Task.Run(() => scan.FullThumbCache[cacheKey].Value);
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
var ffmpegSetup = app.Services.GetRequiredService<FFmpegSetupService>();
_ = ffmpegSetup.CheckAndSetupAsync();

app.Run();
