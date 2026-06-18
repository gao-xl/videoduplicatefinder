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

using System.Text.Json;
using System.Text.Json.Serialization;

namespace VDF.Web.Services;

/// <summary>
/// Unified configuration service that reads settings from a config.json file
/// in the application directory. Falls back to environment variables if the
/// config file is absent or a specific key is not set.
///
/// Also exposes the early-boot configuration (<see cref="EarlyBootConfig"/>)
/// used by Program.cs before DI is built — this replaces the duplicated
/// config.json parsing that previously existed in Program.cs.
/// </summary>
public sealed class WebConfigService {
	/// <summary>Early boot configuration loaded before DI is built.</summary>
	public sealed class EarlyBootConfig {
		public string? BasePath { get; set; }
		public string? TlsCert { get; set; }
		public string? TlsKey { get; set; }
		public string? Password { get; set; }
		public int? Port { get; set; }
		public List<string>? CorsOrigins { get; set; }
		public bool LoadedFromFile { get; set; }
		public string ConfigPath { get; set; } = string.Empty;
	}

	public sealed class WebConfig {
		[JsonPropertyName("password")]
		public string? Password { get; set; }

		[JsonPropertyName("authEnabled")]
		public bool? AuthEnabled { get; set; }

		[JsonPropertyName("apiKeys")]
		public List<string>? ApiKeys { get; set; }

		[JsonPropertyName("basePath")]
		public string? BasePath { get; set; }

		[JsonPropertyName("corsOrigins")]
		public List<string>? CorsOrigins { get; set; }

		[JsonPropertyName("tlsCert")]
		public string? TlsCert { get; set; }

		[JsonPropertyName("tlsKey")]
		public string? TlsKey { get; set; }

		[JsonPropertyName("port")]
		public int? Port { get; set; }
	}

	// Wrapper class to support {"web": {...}} structure in config.json
	public sealed class WebConfigWrapper {
		[JsonPropertyName("web")]
		public WebConfig? Web { get; set; }
	}

	static readonly JsonSerializerOptions JsonOptions = new() {
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true,
	};

	/// <summary>
	/// Returns the resolved path to config.json, honouring VDF_CONFIG_PATH if set.
	/// </summary>
	public static string ResolveConfigPath() {
		var explicitPath = Environment.GetEnvironmentVariable("VDF_CONFIG_PATH");
		if (!string.IsNullOrWhiteSpace(explicitPath))
			return explicitPath;
		// Default: config.json in the application base directory
		return Path.Combine(AppContext.BaseDirectory, "config.json");
	}

	/// <summary>
	/// Loads the minimal configuration fields needed before DI is built. Returns
	/// a <see cref="EarlyBootConfig"/>; EarlyBootConfig"/>. Returns an empty instance
	/// (with <c>LoadedFromFile = false</c>) when no file exists.
	/// </summary>
	public static EarlyBootConfig LoadEarlyBootConfig() {
		string configPath = ResolveConfigPath();
		var result = new EarlyBootConfig { ConfigPath = configPath };
		if (!File.Exists(configPath))
			return result;
		try {
			var json = File.ReadAllText(configPath);
			using var doc = JsonDocument.Parse(json);
			if (doc.RootElement.TryGetProperty("web", out var web)) {
				if (web.TryGetProperty("password", out var pw) && pw.ValueKind == JsonValueKind.String)
					result.Password = pw.GetString();
				if (web.TryGetProperty("basePath", out var bp) && bp.ValueKind == JsonValueKind.String)
					result.BasePath = bp.GetString();
				if (web.TryGetProperty("tlsCert", out var tc) && tc.ValueKind == JsonValueKind.String)
					result.TlsCert = tc.GetString();
				if (web.TryGetProperty("tlsKey", out var tk) && tk.ValueKind == JsonValueKind.String)
					result.TlsKey = tk.GetString();
				if (web.TryGetProperty("port", out var pt) && pt.ValueKind == JsonValueKind.Number)
					result.Port = pt.GetInt32();
				if (web.TryGetProperty("corsOrigins", out var co) && co.ValueKind == JsonValueKind.Array) {
					result.CorsOrigins = new List<string>();
					foreach (var item in co.EnumerateArray())
						if (item.ValueKind == JsonValueKind.String)
							result.CorsOrigins.Add(item.GetString()!);
				}
				result.LoadedFromFile = true;
			}
		}
		catch {
			// Swallow — caller will fall back to env vars
		}
		return result;
	}

	readonly WebConfig? _config;
	readonly string _configPath;
	readonly ILogger<WebConfigService> _logger;

	/// <summary>Password from config file or VDF_WEB_PASSWORD env var.</summary>
	public string? Password => GetValue(() => _config?.Password, "VDF_WEB_PASSWORD");

	/// <summary>Whether auth is enabled. Default: true (unless VDF_WEB_AUTH=false).</summary>
	public bool AuthEnabled {
		get {
			if (_config?.AuthEnabled.HasValue == true) return _config.AuthEnabled.Value;
			var env = Environment.GetEnvironmentVariable("VDF_WEB_AUTH");
			return !string.Equals(env, "false", StringComparison.OrdinalIgnoreCase);
		}
	}

	/// <summary>List of API keys, from config file or VDF_API_KEYS env var.</summary>
	public List<string> ApiKeys {
		get {
			if (_config?.ApiKeys?.Count > 0) return _config.ApiKeys;
			var env = Environment.GetEnvironmentVariable("VDF_API_KEYS");
			if (string.IsNullOrWhiteSpace(env)) return [];
			return env.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
		}
	}

	/// <summary>Base path for reverse proxy (e.g. /vdf). Falls back to VDF_BASE_PATH.</summary>
	public string? BasePath {
		get {
			var configPath = _config?.BasePath?.Trim('/');
			var envPath = Environment.GetEnvironmentVariable("VDF_BASE_PATH")?.Trim('/');
			return !string.IsNullOrEmpty(configPath) ? configPath : envPath;
		}
	}

	/// <summary>CORS origins. Falls back to VDF_CORS_ORIGINS.</summary>
	public List<string> CorsOrigins {
		get {
			if (_config?.CorsOrigins?.Count > 0) return _config.CorsOrigins;
			var env = Environment.GetEnvironmentVariable("VDF_CORS_ORIGINS");
			if (string.IsNullOrWhiteSpace(env)) return [];
			return env.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
		}
	}

	/// <summary>TLS certificate path. Falls back to VDF_TLS_CERT.</summary>
	public string? TlsCert {
		get => GetValue(() => _config?.TlsCert, "VDF_TLS_CERT");
	}

	/// <summary>TLS key path. Falls back to VDF_TLS_KEY.</summary>
	public string? TlsKey {
		get => GetValue(() => _config?.TlsKey, "VDF_TLS_KEY");
	}

	/// <summary>HTTP server port. Falls back to default 5000.</summary>
	public int Port {
		get {
			if (_config?.Port > 0) return _config.Port.Value;
			if (int.TryParse(Environment.GetEnvironmentVariable("VDF_PORT"), out var envPort) && envPort > 0)
				return envPort;
			return 5000;
		}
	}

	/// <summary>True if config.json was found and loaded.</summary>
	public bool ConfigFileExists => _config != null;

	/// <summary>Path to the config file being used.</summary>
	public string ConfigFilePath => _configPath;

	string? GetValue(Func<string?> configGetter, string envVar) {
		var fromConfig = configGetter();
		if (!string.IsNullOrWhiteSpace(fromConfig)) return fromConfig;
		return Environment.GetEnvironmentVariable(envVar);
	}

	public WebConfigService(ILogger<WebConfigService> logger) {
		_logger = logger;
		_configPath = GetConfigPath();

		if (File.Exists(_configPath)) {
			try {
				var json = File.ReadAllText(_configPath);
				// Try wrapped {"web": {...}} first, fall back to flat structure for backwards compat
				var wrapper = JsonSerializer.Deserialize<WebConfigWrapper>(json, JsonOptions);
				_config = wrapper?.Web ?? JsonSerializer.Deserialize<WebConfig>(json, JsonOptions);
				_logger.LogInformation("Loaded configuration from {Path}", _configPath);
			}
			catch (Exception ex) {
				_logger.LogError(ex, "Failed to parse config.json at {Path}, using environment variables only", _configPath);
			}
		}
		else {
			_logger.LogInformation("No config.json found at {Path}. Set environment variables for configuration.", _configPath);
		}
	}

	static string GetConfigPath() => ResolveConfigPath();
}
