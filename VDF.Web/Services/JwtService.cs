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

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using Microsoft.IdentityModel.Tokens;

namespace VDF.Web.Services {
	/// <summary>
	/// Handles JWT token generation and validation using an auto-generated
	/// HMACSHA256 signing key that is persisted to disk so tokens survive restarts.
	/// </summary>
	public sealed class JwtService {
		internal sealed class StoredSigningKey {
			[JsonPropertyName("key")]
			public string? Key { get; set; }
		}

		const string Issuer = "VDF";
		const string Audience = "VDF";

		readonly SymmetricSecurityKey _signingKey;
		readonly SigningCredentials _signingCredentials;
		readonly JwtSecurityTokenHandler _tokenHandler = new();
		readonly TokenValidationParameters _validationParameters;
		readonly ILogger<JwtService> _logger;

		public JwtService(ILogger<JwtService> logger) {
			_logger = logger;
			var keyPath = GetSigningKeyPath();
			var keyBytes = LoadOrGenerateKey(keyPath);
			_signingKey = new SymmetricSecurityKey(keyBytes);
			_signingCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256);

			_validationParameters = new TokenValidationParameters {
				ValidateIssuer = true,
				ValidIssuer = Issuer,
				ValidateAudience = true,
				ValidAudience = Audience,
				ValidateLifetime = true,
				ValidateIssuerSigningKey = true,
				IssuerSigningKey = _signingKey,
				ClockSkew = TimeSpan.FromSeconds(30),
			};
		}

		public SymmetricSecurityKey GetSigningKey() => _signingKey;

		public string GenerateToken(string userId, string role, TimeSpan expiry) {
			var claims = new[] {
				new Claim(JwtRegisteredClaimNames.Sub, userId),
				new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
				new Claim(ClaimTypes.Role, role),
				new Claim(ClaimTypes.NameIdentifier, userId),
			};

			var descriptor = new SecurityTokenDescriptor {
				Subject = new ClaimsIdentity(claims),
				Expires = DateTime.UtcNow.Add(expiry),
				Issuer = Issuer,
				Audience = Audience,
				SigningCredentials = _signingCredentials,
			};

			var token = _tokenHandler.CreateToken(descriptor);
			return _tokenHandler.WriteToken(token);
		}

		public ClaimsPrincipal? ValidateToken(string token) {
			try {
				var principal = _tokenHandler.ValidateToken(token, _validationParameters, out _);
				return principal;
			}
			catch {
				return null;
			}
		}

		byte[] LoadOrGenerateKey(string keyPath) {
			if (File.Exists(keyPath)) {
				try {
					var json = File.ReadAllText(keyPath);
					var stored = System.Text.Json.JsonSerializer.Deserialize<StoredSigningKey>(json);
					if (!string.IsNullOrEmpty(stored?.Key)) {
						var bytes = Convert.FromBase64String(stored.Key);
						if (bytes.Length >= 32)
							return bytes;
					}
				}
				catch (Exception ex) {
					_logger.LogWarning(ex, "Failed to load JWT signing key from {Path}, generating a new one", keyPath);
				}
			}

			// Generate a new 256-bit key
			var newKey = RandomNumberGenerator.GetBytes(64);
			try {
				Directory.CreateDirectory(Path.GetDirectoryName(keyPath)!);
				File.WriteAllText(keyPath,
					System.Text.Json.JsonSerializer.Serialize(new StoredSigningKey { Key = Convert.ToBase64String(newKey) }));
			}
			catch (Exception ex) {
				_logger.LogWarning(ex, "Failed to persist JWT signing key to {Path}", keyPath);
			}

			return newKey;
		}

		static string GetSigningKeyPath() {
			string folder;
			if (OperatingSystem.IsWindows())
				folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VDF");
			else if (OperatingSystem.IsMacOS())
				folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Preferences", "VDF");
			else
				folder = Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
					?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"), "VDF");
			return Path.Combine(folder, "jwt-signing-key.json");
		}
	}
}
