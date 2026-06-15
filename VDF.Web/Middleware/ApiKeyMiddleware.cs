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

using System.Security.Claims;
using VDF.Web.Services;

namespace VDF.Web.Middleware {
	/// <summary>
	/// Middleware that checks the X-API-Key header against configured API keys.
	/// If valid, sets an authenticated user with "api-key" authentication type.
	/// Skipped if an Authorization header is present (JWT takes priority).
	/// </summary>
	public sealed class ApiKeyMiddleware {
		readonly RequestDelegate _next;

		public ApiKeyMiddleware(RequestDelegate next) {
			_next = next;
		}

		public async Task InvokeAsync(HttpContext context, AuthService authService) {
			// Skip if already authenticated (e.g. via JWT Bearer)
			if (context.User?.Identity?.IsAuthenticated != true) {
				// Skip if Authorization header is present — let JWT handler process it
				if (!context.Request.Headers.ContainsKey("Authorization")) {
					if (context.Request.Headers.TryGetValue("X-API-Key", out var apiKeyHeader)) {
						var apiKey = apiKeyHeader.ToString();
						if (authService.ValidateApiKey(apiKey)) {
							var claims = new[] {
								new Claim(ClaimTypes.NameIdentifier, "api-key-user"),
								new Claim(ClaimTypes.Role, "admin"),
								new Claim("auth_type", "api-key"),
							};
							var identity = new ClaimsIdentity(claims, "api-key");
							context.User = new ClaimsPrincipal(identity);
						}
					}
				}
			}

			await _next(context);
		}
	}
}
