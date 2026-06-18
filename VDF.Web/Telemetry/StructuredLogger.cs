namespace VDF.Web.Telemetry;

public static class StructuredLogger {
	public static void LogScanStarted(ILogger logger, string? userId = null, string? sessionId = null) {
		logger.LogInformation("Scan started {UserId} {SessionId}", userId ?? "anonymous", sessionId ?? "");
	}

	public static void LogScanCompleted(ILogger logger, int duplicatesFound, double durationSeconds,
		int filesProcessed, string? userId = null) {
		logger.LogInformation(
			"Scan completed: {DuplicatesFound} duplicates found in {DurationSeconds:F2}s, {FilesProcessed} files processed by {UserId}",
			duplicatesFound, durationSeconds, filesProcessed, userId ?? "anonymous");
	}

	public static void LogScanFailed(ILogger logger, string error, double durationSeconds, string? userId = null) {
		logger.LogError("Scan failed after {DurationSeconds:F2}s: {Error} by {UserId}",
			durationSeconds, error, userId ?? "anonymous");
	}

	public static void LogUserAction(ILogger logger, string action, string? username = null,
		string? ipAddress = null, bool success = true) {
		if (success)
			logger.LogInformation("User action: {Action} by {Username} from {IpAddress}",
				action, username ?? "anonymous", ipAddress ?? "local");
		else
			logger.LogWarning("User action failed: {Action} by {Username} from {IpAddress}",
				action, username ?? "anonymous", ipAddress ?? "local");
	}

	public static void LogFileOperation(ILogger logger, string operation, int fileCount,
		long bytesFreed, string? username = null) {
		logger.LogInformation(
			"File operation: {Operation} - {FileCount} files, {BytesFreed} bytes freed by {Username}",
			operation, fileCount, bytesFreed, username ?? "anonymous");
	}

	public static void LogWebhookDispatched(ILogger logger, string webhookId, string eventName,
		int statusCode, bool success) {
		logger.LogInformation(
			"Webhook dispatched: {WebhookId} - {EventName} - Status: {StatusCode} - Success: {Success}",
			webhookId, eventName, statusCode, success);
	}

	public static void LogApiRequest(ILogger logger, string method, string path,
		int statusCode, double durationMs) {
		logger.LogInformation(
			"API request: {Method} {Path} - {StatusCode} - {DurationMs:F2}ms",
			method, path, statusCode, durationMs);
	}

	public static void LogSecurityEvent(ILogger logger, string eventType, string? username = null,
		string? ipAddress = null, string? details = null) {
		logger.LogWarning(
			"Security event: {EventType} - User: {Username} - IP: {IpAddress} - {Details}",
			eventType, username ?? "unknown", ipAddress ?? "unknown", details ?? "");
	}
}
