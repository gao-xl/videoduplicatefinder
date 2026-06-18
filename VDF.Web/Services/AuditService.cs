using System.Collections.Concurrent;
using System.Text.Json;

namespace VDF.Web.Services;

public sealed class AuditService {
	public enum AuditAction {
		UserLogin,
		UserLogout,
		UserCreated,
		UserDeleted,
		RoleChanged,
		ScanStarted,
		ScanStopped,
		ScanPaused,
		ScanResumed,
		FileDeleted,
		FileMoved,
		FileLinked,
		SettingsChanged,
		ConfigChanged,
		ApiKeyCreated,
		ApiKeyRevoked,
	}

	private sealed class AuditEntry {
		public DateTime Timestamp { get; set; } = DateTime.UtcNow;
		public string? UserId { get; set; }
		public string? Username { get; set; }
		public AuditAction Action { get; set; }
		public string? Details { get; set; }
		public string? IpAddress { get; set; }
		public bool Success { get; set; } = true;
	}

	private readonly ConcurrentBag<AuditEntry> _entries = new();
	private readonly string _logPath;
	private readonly ILogger<AuditService> _logger;

	public AuditService(ILogger<AuditService> logger) {
		_logger = logger;
		_logPath = GetLogPath();
	}

	public void Log(AuditAction action, string? username = null, string? details = null,
		string? ipAddress = null, bool success = true, string? userId = null) {
		var entry = new AuditEntry {
			UserId = userId,
			Username = username,
			Action = action,
			Details = details,
			IpAddress = ipAddress,
			Success = success,
		};

		_entries.Add(entry);
		_logger.LogInformation("Audit: {Action} by {Username} from {Ip} - {Details}",
			action, username ?? "system", ipAddress ?? "local", details ?? "");

		TryPersist(entry);
	}

	public IReadOnlyList<AuditEntryDto> GetRecent(int count = 100) {
		return _entries
			.OrderByDescending(e => e.Timestamp)
			.Take(count)
			.Select(e => new AuditEntryDto {
				Timestamp = e.Timestamp,
				Username = e.Username,
				Action = e.Action.ToString(),
				Details = e.Details,
				IpAddress = e.IpAddress,
				Success = e.Success,
			})
			.ToList();
	}

	private void TryPersist(AuditEntry entry) {
		try {
			var dir = Path.GetDirectoryName(_logPath);
			if (!string.IsNullOrEmpty(dir))
				Directory.CreateDirectory(dir);

			var json = JsonSerializer.Serialize(entry);
			File.AppendAllText(_logPath, json + Environment.NewLine);
		}
		catch (Exception ex) {
			_logger.LogError(ex, "Failed to persist audit entry");
		}
	}

	private static string GetLogPath() {
		string folder;
		if (OperatingSystem.IsWindows())
			folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VDF");
		else if (OperatingSystem.IsMacOS())
			folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Preferences", "VDF");
		else
			folder = Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
				?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"), "VDF");
		return Path.Combine(folder, "audit.log");
	}
}

public sealed class AuditEntryDto {
	public DateTime Timestamp { get; set; }
	public string? Username { get; set; }
	public string Action { get; set; } = string.Empty;
	public string? Details { get; set; }
	public string? IpAddress { get; set; }
	public bool Success { get; set; }
}
