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

		const string CookieName = "vdf_auth";
		const int TokenExpirationDays = 30;
		static readonly TimeSpan CookieMaxAge = TimeSpan.FromDays(TokenExpirationDays);
		static readonly TimeSpan AccessTokenExpiry = TimeSpan.FromMinutes(15);
		static readonly TimeSpan RefreshTokenExpiry = TimeSpan.FromDays(30);

		readonly string _password;
		readonly bool _authEnabled;
		readonly HashSet<string> _validTokens = new();
		readonly HashSet<string> _apiKeys = new(StringComparer.Ordinal);
		readonly string _credentialsPath;
		readonly ILogger<AuthService> _logger;
		readonly JwtService _jwtService;

		public bool AuthEnabled => _authEnabled;

		public AuthService(ILogger<AuthService> logger, JwtService jwtService) {
			_logger = logger;
			_jwtService = jwtService;

			// Allow disabling auth entirely
			var authEnv = Environment.GetEnvironmentVariable("VDF_WEB_AUTH");
			if (string.Equals(authEnv, "false", StringComparison.OrdinalIgnoreCase)) {
				_authEnabled = false;
				_password = string.Empty;
				_credentialsPath = string.Empty;
			}
			else {
				_authEnabled = true;
				_credentialsPath = GetCredentialsPath();

				// Priority: env var > saved file > generate new
				var envPassword = Environment.GetEnvironmentVariable("VDF_WEB_PASSWORD");
				if (!string.IsNullOrWhiteSpace(envPassword))
					_password = envPassword;
				else
					_password = LoadOrGeneratePassword();

				PrintPasswordBanner();
			}

			// Load API keys from environment variable
			var apiKeysEnv = Environment.GetEnvironmentVariable("VDF_API_KEYS");
			if (!string.IsNullOrWhiteSpace(apiKeysEnv)) {
				foreach (var key in apiKeysEnv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
					_apiKeys.Add(key);
				}
				if (_apiKeys.Count > 0)
					_logger.LogInformation("Loaded {Count} API key(s) from VDF_API_KEYS", _apiKeys.Count);
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
		/// Generates a random refresh token (30-day expiry), stored in the valid tokens set.
		/// </summary>
		public string GenerateRefreshToken() {
			var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
			lock (_validTokens)
				_validTokens.Add(token);
			return token;
		}

		/// <summary>
		/// Validates whether a refresh token is in the valid tokens set.
		/// </summary>
		public bool ValidateRefreshToken(string token) {
			if (string.IsNullOrEmpty(token)) return false;
			lock (_validTokens)
				return _validTokens.Contains(token);
		}

		/// <summary>
		/// Validates a refresh token and issues a new access token.
		/// Returns null if the refresh token is invalid.
		/// </summary>
		public string? RefreshAccessToken(string refreshToken) {
			if (!ValidateRefreshToken(refreshToken))
				return null;
			return GenerateAccessToken();
		}

		// --- Legacy cookie-based auth methods (kept for backward compatibility) ---

		public string IssueToken() {
			var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
			lock (_validTokens)
				_validTokens.Add(token);
			return token;
		}

		public bool ValidateToken(string? token) {
			if (string.IsNullOrEmpty(token)) return false;
			lock (_validTokens)
				return _validTokens.Contains(token);
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
			ctx.Response.Cookies.Append(CookieName, token, new CookieOptions {
				HttpOnly = true,
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
				catch { }
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
			catch { }
		}

		void PrintPasswordBanner() {
			// Log via ILogger so it shows up in VS Code Debug Console / structured logging
			_logger.LogInformation("============================================");
			_logger.LogInformation("  Web UI password:  {Password}", _password);
			_logger.LogInformation("============================================");

			var envOverride = Environment.GetEnvironmentVariable("VDF_WEB_PASSWORD");
			if (!string.IsNullOrWhiteSpace(envOverride))
				_logger.LogInformation("  (using password from VDF_WEB_PASSWORD environment variable)");
			else
				_logger.LogInformation("  Tip: Set VDF_WEB_PASSWORD environment variable to use your own password.");

			_logger.LogInformation("  Set VDF_WEB_AUTH=false to disable authentication entirely.");

			// Also write to stdout for Docker users (docker logs)
			Console.WriteLine();
			Console.WriteLine("============================================");
			Console.WriteLine($"  Web UI password:  {_password}");
			Console.WriteLine("============================================");
			Console.WriteLine();
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
