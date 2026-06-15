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

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VDF.Web.Services {
	/// <summary>
	/// Manages authentication for the WebUI.  On first launch a random password is
	/// generated and printed to the console.  Users can override it via the
	/// VDF_WEB_PASSWORD environment variable or disable auth entirely with VDF_WEB_AUTH=false.
	/// Supports JWT access tokens, refresh tokens, and API key authentication.
	/// </summary>
	public sealed class AuthService {
		internal sealed class StoredCredentials {
			[JsonPropertyName("password")]
			public string? Password { get; set; }
		}

		private record RefreshTokenEntry(DateTime CreatedAt, DateTime LastUsedAt);

		const string CookieName = "vdf_auth";
		const int TokenExpirationDays = 30;
		static readonly TimeSpan CookieMaxAge = TimeSpan.FromDays(TokenExpirationDays);
		static readonly TimeSpan AccessTokenExpiry = TimeSpan.FromMinutes(15);
		static readonly TimeSpan RefreshTokenTtl = TimeSpan.FromDays(7);
		const int MaxSessionsPerUser = 5;

		readonly string _password;
		readonly bool _authEnabled;
		readonly ConcurrentDictionary<string, RefreshTokenEntry> _validTokens = new(StringComparer.Ordinal);
		readonly HashSet<string> _apiKeys = new(StringComparer.Ordinal);
		readonly string _credentialsPath;
		readonly ILogger<AuthService> _logger;
		readonly JwtService _jwtService;

		public bool AuthEnabled => _authEnabled;

		public AuthService(ILogger<AuthService> logger, JwtService jwtService, WebConfigService config) {
			_logger = logger;
			_jwtService = jwtService;

			// Allow disabling auth entirely via config.json or env var
			var authEnv = Environment.GetEnvironmentVariable("VDF_WEB_AUTH");
			if (!string.Equals(authEnv, "false", StringComparison.OrdinalIgnoreCase) && config.AuthEnabled)
				_authEnabled = true;
			else
				_authEnabled = false;

			if (!_authEnabled) {
				_password = string.Empty;
				_credentialsPath = string.Empty;
			}
			else {
				_credentialsPath = GetCredentialsPath();

				// Priority: config.json > env var > saved file > generate new
				var envPassword = Environment.GetEnvironmentVariable("VDF_WEB_PASSWORD");
				var configPassword = config.Password;
				if (!string.IsNullOrWhiteSpace(configPassword))
					_password = configPassword;
				else if (!string.IsNullOrWhiteSpace(envPassword))
					_password = envPassword;
				else
					_password = LoadOrGeneratePassword();

				// Load API keys from config.json or env var
				var keys = config.ApiKeys;
				if (keys.Count == 0) {
					var apiKeysEnv = Environment.GetEnvironmentVariable("VDF_API_KEYS");
					if (!string.IsNullOrWhiteSpace(apiKeysEnv)) {
						foreach (var key in apiKeysEnv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
							_apiKeys.Add(key);
					}
				}
				else {
					foreach (var key in keys)
						_apiKeys.Add(key);
				}
				if (_apiKeys.Count > 0)
					_logger.LogInformation("Loaded {Count} API key(s)", _apiKeys.Count);

				PrintPasswordBanner();
			}
		}

		public bool ValidatePassword(string password) {
			// Hash both sides so the comparison is constant-time and leaks neither
			// content nor length differences.
			byte[] expected = SHA256.HashData(Encoding.UTF8.GetBytes(_password));
			byte[] actual = SHA256.HashData(Encoding.UTF8.GetBytes(password ?? string.Empty));
			return _authEnabled && CryptographicOperations.FixedTimeEquals(expected, actual);
		}

		public bool ValidateApiKey(string key) {
			if (string.IsNullOrEmpty(key)) return false;
			lock (_apiKeys)
				return _apiKeys.Contains(key);
		}

		/// <summary>
		/// Generates a JWT access token with 15-minute expiry containing role claim.
		/// </summary>
		public string GenerateAccessToken() {
			return _jwtService.GenerateToken("vdf-user", "admin", AccessTokenExpiry);
		}

		/// <summary>
		/// Generates a random refresh token, stored with creation and last-used timestamps.
		/// Enforces max session limit by evicting the oldest entry.
		/// </summary>
		public string GenerateRefreshToken() {
			var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
			var now = DateTime.UtcNow;
			_validTokens[token] = new RefreshTokenEntry(now, now);

			// Enforce max sessions: remove oldest entry if over limit
			if (_validTokens.Count > MaxSessionsPerUser) {
				var oldest = _validTokens.OrderBy(kvp => kvp.Value.CreatedAt).First();
				_validTokens.TryRemove(oldest.Key, out _);
			}

			return token;
		}

		/// <summary>
		/// Validates whether a refresh token exists and has not expired.
		/// Updates LastUsedAt on successful validation.
		/// </summary>
		public bool ValidateRefreshToken(string token) {
			if (string.IsNullOrEmpty(token)) return false;
			if (!_validTokens.TryGetValue(token, out var entry)) return false;

			if (DateTime.UtcNow - entry.LastUsedAt > RefreshTokenTtl) {
				_validTokens.TryRemove(token, out _);
				return false;
			}

			// Update last used timestamp
			_validTokens[token] = entry with { LastUsedAt = DateTime.UtcNow };
			return true;
		}

		/// <summary>
		/// Validates a refresh token and issues a new access token.
		/// Returns null if the refresh token is invalid.
		/// Performs expired token cleanup on each call.
		/// </summary>
		public string? RefreshAccessToken(string refreshToken) {
			CleanupExpiredTokens();
			if (!ValidateRefreshToken(refreshToken))
				return null;
			return GenerateAccessToken();
		}

		// --- Legacy cookie-based auth methods (kept for backward compatibility) ---

		public string IssueToken() {
			var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
			var now = DateTime.UtcNow;
			_validTokens[token] = new RefreshTokenEntry(now, now);
			return token;
		}

		public bool ValidateToken(string? token) {
			if (string.IsNullOrEmpty(token)) return false;
			if (!_validTokens.TryGetValue(token, out var entry)) return false;

			if (DateTime.UtcNow - entry.LastUsedAt > RefreshTokenTtl) {
				_validTokens.TryRemove(token, out _);
				return false;
			}

			// Update last used timestamp
			_validTokens[token] = entry with { LastUsedAt = DateTime.UtcNow };
			return true;
		}

		/// <summary>
		/// Revokes a refresh token, removing it from the valid token collection.
		/// </summary>
		public void RevokeRefreshToken(string token) {
			if (!string.IsNullOrEmpty(token))
				_validTokens.TryRemove(token, out _);
		}

		/// <summary>
		/// Removes all expired refresh tokens from the collection.
		/// </summary>
		public void CleanupExpiredTokens() {
			var now = DateTime.UtcNow;
			foreach (var kvp in _validTokens) {
				if (now - kvp.Value.LastUsedAt > RefreshTokenTtl)
					_validTokens.TryRemove(kvp.Key, out _);
			}
		}

		public bool IsAuthenticated(HttpContext ctx) {
			if (!_authEnabled) return true;
			// Check JWT Bearer token first
			var user = ctx.User;
			if (user?.Identity?.IsAuthenticated == true)
				return true;
			// Fall back to cookie
			return ctx.Request.Cookies.TryGetValue(CookieName, out var token) && ValidateToken(token);
		}

		public void SetAuthCookie(HttpContext ctx, string token, bool persistent = true) {
			var isHttps = ctx.Request.IsHttps;
			if (!isHttps)
				_logger.LogWarning("Auth cookie is being set over an insecure (HTTP) connection. " +
					"Enable HTTPS to ensure cookies are transmitted securely.");

			ctx.Response.Cookies.Append(CookieName, token, new CookieOptions {
				HttpOnly = true,
				Secure = isHttps,
				SameSite = SameSiteMode.Strict,
				MaxAge = persistent ? CookieMaxAge : null,
				IsEssential = true,
			});
		}

		string LoadOrGeneratePassword() {
			// Try loading saved password
			if (File.Exists(_credentialsPath)) {
				try {
					var saved = JsonSerializer.Deserialize(File.ReadAllText(_credentialsPath), WebJsonContext.Default.StoredCredentials);
					if (!string.IsNullOrWhiteSpace(saved?.Password))
						return saved.Password;
				}
				catch (Exception ex) { _logger.LogWarning(ex, "Failed to load credentials file, generating new password"); }
			}

			// Generate new password
			var password = GeneratePassword();
			SavePassword(password);
			return password;
		}

		void SavePassword(string password) {
			try {
				Directory.CreateDirectory(Path.GetDirectoryName(_credentialsPath)!);
				File.WriteAllText(_credentialsPath,
					JsonSerializer.Serialize(new StoredCredentials { Password = password }, WebJsonContext.Default.StoredCredentials));
			}
			catch (Exception ex) { _logger.LogError(ex, "Failed to save credentials file"); }
		}

		void PrintPasswordBanner() {
			_logger.LogInformation("Web UI password is ready.");

			var configSource = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VDF_CONFIG_PATH"))
				? "VDF_CONFIG_PATH/config.json"
				: "config.json in app directory";
			_logger.LogInformation("  Password loaded from {Source} or VDF_WEB_PASSWORD env var.", configSource);
			_logger.LogInformation("  Set 'authEnabled: false' in config.json or VDF_WEB_AUTH=false to disable authentication.");
		}

		static string GeneratePassword() {
			const string chars = "abcdefghjkmnpqrstuvwxyzABCDEFGHJKMNPQRSTUVWXYZ23456789";
			return string.Create(10, chars, (span, c) => {
				Span<byte> random = stackalloc byte[span.Length];
				RandomNumberGenerator.Fill(random);
				for (int i = 0; i < span.Length; i++)
					span[i] = c[random[i] % c.Length];
			});
		}

		static string GetCredentialsPath() {
			string folder;
			if (OperatingSystem.IsWindows())
				folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VDF");
			else if (OperatingSystem.IsMacOS())
				folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Preferences", "VDF");
			else
				folder = Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
					?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"), "VDF");
			return Path.Combine(folder, "web-credentials.json");
		}
	}
}
