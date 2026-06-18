using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VDF.Web.Webhooks;

public sealed class WebhookService {
	public enum WebhookEvent {
		ScanCompleted,
		ScanFailed,
		DuplicatesFound,
		FilesDeleted,
	}

	private sealed class StoredWebhook {
		public string Id { get; set; } = Guid.NewGuid().ToString("N");
		public string Url { get; set; } = string.Empty;
		public string? Secret { get; set; }
		public List<string> Events { get; set; } = new();
		public bool IsActive { get; set; } = true;
		public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
	}

	private readonly ConcurrentDictionary<string, StoredWebhook> _webhooks = new();
	private readonly HttpClient _httpClient;
	private readonly ILogger<WebhookService> _logger;
	private readonly string _dbPath;

	public WebhookService(IHttpClientFactory httpClientFactory, ILogger<WebhookService> logger) {
		_httpClient = httpClientFactory.CreateClient();
		_logger = logger;
		_dbPath = GetDatabasePath();
		Load();
	}

	public WebhookInfo CreateWebhook(string url, IEnumerable<WebhookEvent> events, string? secret = null) {
		var webhook = new StoredWebhook {
			Url = url,
			Events = events.Select(e => e.ToString()).ToList(),
			Secret = secret,
			IsActive = true,
			CreatedAt = DateTime.UtcNow,
		};

		_webhooks[webhook.Id] = webhook;
		Save();

		_logger.LogInformation("Created webhook {Id} for {Url}", webhook.Id, url);
		return MapToInfo(webhook);
	}

	public bool DeleteWebhook(string id) {
		if (!_webhooks.TryRemove(id, out _))
			return false;

		Save();
		_logger.LogInformation("Deleted webhook {Id}", id);
		return true;
	}

	public IReadOnlyList<WebhookInfo> GetAllWebhooks() {
		return _webhooks.Values.Select(MapToInfo).ToList();
	}

	public async Task DispatchAsync(WebhookEvent webhookEvent, object payload) {
		var eventStr = webhookEvent.ToString();
		var relevantWebhooks = _webhooks.Values
			.Where(w => w.IsActive && w.Events.Contains(eventStr))
			.ToList();

		if (relevantWebhooks.Count == 0)
			return;

		var payloadJson = JsonSerializer.Serialize(payload);
		var tasks = relevantWebhooks.Select(w => SendWebhookAsync(w, eventStr, payloadJson));
		await Task.WhenAll(tasks);
	}

	private async Task SendWebhookAsync(StoredWebhook webhook, string eventName, string payloadJson) {
		try {
			var content = new StringContent(payloadJson, Encoding.UTF8, "application/json");

			if (!string.IsNullOrEmpty(webhook.Secret)) {
				var signature = ComputeHmacSignature(webhook.Secret, payloadJson);
				content.Headers.Add("X-Webhook-Signature", signature);
			}

			content.Headers.Add("X-Webhook-Event", eventName);
			content.Headers.Add("X-Webhook-Id", webhook.Id);

			var response = await _httpClient.PostAsync(webhook.Url, content);

			if (!response.IsSuccessStatusCode) {
				_logger.LogWarning("Webhook {Id} returned {StatusCode}", webhook.Id, response.StatusCode);
			}
		}
		catch (Exception ex) {
			_logger.LogError(ex, "Failed to send webhook {Id} to {Url}", webhook.Id, webhook.Url);
		}
	}

	private static string ComputeHmacSignature(string secret, string payload) {
		using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
		var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
		return Convert.ToHexString(hash).ToLowerInvariant();
	}

	private WebhookInfo MapToInfo(StoredWebhook webhook) => new() {
		Id = webhook.Id,
		Url = webhook.Url,
		Events = webhook.Events.Select(e => Enum.Parse<WebhookEvent>(e)).ToList(),
		IsActive = webhook.IsActive,
		CreatedAt = webhook.CreatedAt,
	};

	private void Save() {
		try {
			var dir = Path.GetDirectoryName(_dbPath);
			if (!string.IsNullOrEmpty(dir))
				Directory.CreateDirectory(dir);
			var json = JsonSerializer.Serialize(_webhooks.Values.ToList(), new JsonSerializerOptions { WriteIndented = true });
			File.WriteAllText(_dbPath, json);
		}
		catch (Exception ex) {
			_logger.LogError(ex, "Failed to save webhooks");
		}
	}

	private void Load() {
		if (!File.Exists(_dbPath))
			return;

		try {
			var json = File.ReadAllText(_dbPath);
			var webhooks = JsonSerializer.Deserialize<List<StoredWebhook>>(json);
			if (webhooks != null) {
				foreach (var webhook in webhooks)
					_webhooks[webhook.Id] = webhook;
			}
		}
		catch (Exception ex) {
			_logger.LogError(ex, "Failed to load webhooks");
		}
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
		return Path.Combine(folder, "webhooks.json");
	}
}

public sealed class WebhookInfo {
	public string Id { get; set; } = string.Empty;
	public string Url { get; set; } = string.Empty;
	public List<WebhookService.WebhookEvent> Events { get; set; } = new();
	public bool IsActive { get; set; }
	public DateTime CreatedAt { get; set; }
}
