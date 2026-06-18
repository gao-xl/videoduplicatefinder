using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VDF.Web.Auth;

public sealed class UserStore {
	private sealed class StoredUser {
		public string Username { get; set; } = string.Empty;
		public string PasswordHash { get; set; } = string.Empty;
		public string PasswordSalt { get; set; } = string.Empty;
		public string Role { get; set; } = "viewer";
		public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
		public bool IsActive { get; set; } = true;
	}

	private readonly ConcurrentDictionary<string, StoredUser> _users = new(StringComparer.OrdinalIgnoreCase);
	private readonly string _dbPath;
	private readonly ILogger<UserStore> _logger;

	public UserStore(ILogger<UserStore> logger) {
		_logger = logger;
		_dbPath = GetDatabasePath();
		Load();
	}

	public bool ValidateCredentials(string username, string password) {
		if (!_users.TryGetValue(username, out var user) || !user.IsActive)
			return false;

		byte[] salt = Convert.FromBase64String(user.PasswordSalt);
		byte[] hash = Convert.FromBase64String(user.PasswordHash);
		byte[] candidateHash = Rfc2898DeriveBytes.Pbkdf2(
			Encoding.UTF8.GetBytes(password),
			salt,
			100_000,
			HashAlgorithmName.SHA256,
			32);
		return CryptographicOperations.FixedTimeEquals(hash, candidateHash);
	}

	public Role GetRole(string username) {
		if (_users.TryGetValue(username, out var user) && user.IsActive)
			return RoleExtensions.FromClaimValue(user.Role);
		return Role.Viewer;
	}

	public bool CreateUser(string username, string password, Role role) {
		if (_users.ContainsKey(username))
			return false;

		byte[] salt = RandomNumberGenerator.GetBytes(16);
		byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
			Encoding.UTF8.GetBytes(password),
			salt,
			100_000,
			HashAlgorithmName.SHA256,
			32);

		var user = new StoredUser {
			Username = username,
			PasswordHash = Convert.ToBase64String(hash),
			PasswordSalt = Convert.ToBase64String(salt),
			Role = role.ToClaimValue(),
			CreatedAt = DateTime.UtcNow,
			IsActive = true,
		};

		_users[username] = user;
		Save();
		_logger.LogInformation("Created user {Username} with role {Role}", username, role);
		return true;
	}

	public bool UpdateRole(string username, Role role) {
		if (!_users.TryGetValue(username, out var user))
			return false;

		user.Role = role.ToClaimValue();
		Save();
		_logger.LogInformation("Updated {Username} role to {Role}", username, role);
		return true;
	}

	public bool SetActive(string username, bool active) {
		if (!_users.TryGetValue(username, out var user))
			return false;

		user.IsActive = active;
		Save();
		return true;
	}

	public bool DeleteUser(string username) {
		if (!_users.TryRemove(username, out _))
			return false;

		Save();
		_logger.LogInformation("Deleted user {Username}", username);
		return true;
	}

	public IReadOnlyList<UserInfo> GetAllUsers() {
		return _users.Values
			.Where(u => u.IsActive)
			.Select(u => new UserInfo {
				Username = u.Username,
				Role = RoleExtensions.FromClaimValue(u.Role),
				CreatedAt = u.CreatedAt,
			})
			.ToList();
	}

	public bool UserExists(string username) => _users.ContainsKey(username);

	private void Save() {
		try {
			var dir = Path.GetDirectoryName(_dbPath);
			if (!string.IsNullOrEmpty(dir))
				Directory.CreateDirectory(dir);
			var json = JsonSerializer.Serialize(_users.Values.ToList(), new JsonSerializerOptions { WriteIndented = true });
			File.WriteAllText(_dbPath, json);
		}
		catch (Exception ex) {
			_logger.LogError(ex, "Failed to save user database");
		}
	}

	private void Load() {
		if (!File.Exists(_dbPath)) {
			SeedDefaultAdmin();
			return;
		}

		try {
			var json = File.ReadAllText(_dbPath);
			var users = JsonSerializer.Deserialize<List<StoredUser>>(json);
			if (users != null) {
				foreach (var user in users)
					_users[user.Username] = user;
			}
			if (_users.IsEmpty)
				SeedDefaultAdmin();
		}
		catch (Exception ex) {
			_logger.LogError(ex, "Failed to load user database, seeding default admin");
			SeedDefaultAdmin();
		}
	}

	private void SeedDefaultAdmin() {
		const string defaultPassword = "admin123";
		CreateUser("admin", defaultPassword, Role.Admin);
		_logger.LogWarning("Default admin user created with password: {Password}. Change it immediately!", defaultPassword);
	}

	private static string GetDatabasePath() {
		string folder;
		if (OperatingSystem.IsWindows())
			folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VDF");
		else if (OperatingSystem.IsMacOS())
			folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Preferences", "VDF");
		else
			folder = Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
				?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"), "VDF");
		return Path.Combine(folder, "users.json");
	}
}

public sealed class UserInfo {
	public string Username { get; set; } = string.Empty;
	public Role Role { get; set; }
	public DateTime CreatedAt { get; set; }
}
