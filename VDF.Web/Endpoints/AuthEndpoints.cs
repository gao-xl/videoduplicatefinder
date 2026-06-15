using System.Text;
using VDF.Web.Models;
using VDF.Web.Services;

namespace VDF.Web.Endpoints;

static class AuthEndpoints {
	public static WebApplication MapAuthApi(this WebApplication app) {
		var group = app.MapGroup("/api/auth");

		// POST /api/auth/login — login (no auth required)
		group.MapPost("/login", async (HttpContext ctx, AuthService auth, LoginRequest? body) => {
			// Support both JSON body and form-encoded (browser) requests
			string? password;
			bool remember;

			if (body != null && !string.IsNullOrEmpty(body.Password)) {
				password = body.Password;
				remember = body.Remember;
			}
			else {
				var contentType = ctx.Request.ContentType ?? "";
				if (contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase)) {
					using var reader = new StreamReader(ctx.Request.Body, Encoding.UTF8);
					var jsonBody = await reader.ReadToEndAsync();
					var json = System.Text.Json.JsonDocument.Parse(jsonBody);
					password = json.RootElement.TryGetProperty("password", out var pwEl) ? pwEl.GetString() : null;
					remember = json.RootElement.TryGetProperty("remember", out var remEl) && remEl.GetBoolean();
				}
				else {
					var form = await ctx.Request.ReadFormAsync();
					password = form["password"].ToString();
					remember = form["remember"] == "true";
				}
			}

			if (auth.ValidatePassword(password ?? string.Empty)) {
				var accessToken = auth.GenerateAccessToken();
				var refreshToken = auth.GenerateRefreshToken();

				// Also set cookie for backward compatibility with browser UI
				auth.SetAuthCookie(ctx, refreshToken, remember);

				return Results.Ok(new LoginResponse {
					Access_token = accessToken,
					Refresh_token = refreshToken,
					Expires_in = 900,
				});
			}

			return Results.Json(new { error = "invalid_credentials" }, statusCode: 401);
		});

		// POST /api/auth/refresh — refresh access token (no auth required)
		group.MapPost("/refresh", (RefreshRequest req, AuthService auth) => {
			var newAccessToken = auth.RefreshAccessToken(req.Refresh_token);
			if (newAccessToken == null)
				return Results.Json(new { error = "invalid_refresh_token" }, statusCode: 401);

			return Results.Ok(new RefreshResponse {
				Access_token = newAccessToken,
				Expires_in = 900,
			});
		});

		// POST /api/auth/logout — logout (invalidate refresh token)
		group.MapPost("/logout", (HttpContext ctx, AuthService auth) => {
			// Read refresh token from body if provided
			string? refreshToken = null;
			if (ctx.Request.HasJsonContentType()) {
				using var reader = new StreamReader(ctx.Request.Body, Encoding.UTF8);
				var body = reader.ReadToEnd();
				if (!string.IsNullOrEmpty(body)) {
					try {
						var json = System.Text.Json.JsonDocument.Parse(body);
						refreshToken = json.RootElement.TryGetProperty("refresh_token", out var rtEl) ? rtEl.GetString() : null;
					}
					catch { /* ignore parse errors */ }
				}
			}

			// Clear the auth cookie
			ctx.Response.Cookies.Delete("vdf_auth");

			return Results.Ok(new { logged_out = true });
		});

		// GET /api/auth/status — check auth status
		group.MapGet("/status", (HttpContext ctx, AuthService auth) => {
			return Results.Ok(new AuthStatusResponse {
				Authenticated = auth.IsAuthenticated(ctx),
				AuthEnabled = auth.AuthEnabled,
			});
		});

		return app;
	}
}
