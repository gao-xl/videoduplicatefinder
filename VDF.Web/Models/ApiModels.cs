using VDF.Web.Services;

namespace VDF.Web.Models;

// ── Scan ──────────────────────────────────────────────────────────────────────

public sealed class ScanStartRequest {
	public bool IncludeSubDirectories { get; set; } = true;
}

public sealed class ScanProgressResponse {
	public string State { get; set; } = "Idle";
	public int FilesHashed { get; set; }
	public string CurrentFile { get; set; } = string.Empty;
	public int Current { get; set; }
	public int Max { get; set; }
	public double ElapsedSeconds { get; set; }
	public double RemainingSeconds { get; set; }
	public string CurrentStage { get; set; } = string.Empty;
	public int StageCurrent { get; set; }
	public int StageMax { get; set; }
	public string? ErrorMessage { get; set; }
}

public sealed class ScanStateResponse {
	public string State { get; set; } = "Idle";
	public string? ErrorMessage { get; set; }
}

// ── Results ───────────────────────────────────────────────────────────────────

public sealed class ResultsResponse {
	public List<DuplicateGroupDto> Groups { get; set; } = new();
	public int TotalGroups { get; set; }
	public int TotalFiles { get; set; }
	public long TotalSizeBytes { get; set; }
	public long PotentialSavingsBytes { get; set; }
}

public sealed class ResultsPageResponse {
	public List<DuplicateGroupDto> Groups { get; set; } = new();
	public int TotalGroups { get; set; }
	public int Page { get; set; }
	public int PageSize { get; set; }
	public int TotalFiles { get; set; }
	public long TotalSizeBytes { get; set; }
	public long PotentialSavingsBytes { get; set; }
}

public sealed class DuplicateGroupDto {
	public Guid GroupId { get; set; }
	public List<DuplicateItemDto> Items { get; set; } = new();
}

public sealed class DuplicateItemDto {
	public string Path { get; set; } = string.Empty;
	public string Folder { get; set; } = string.Empty;
	public long SizeBytes { get; set; }
	public double DurationSeconds { get; set; }
	public string? FrameSize { get; set; }
	public float Fps { get; set; }
	public decimal BitRateKbs { get; set; }
	public string? Format { get; set; }
	public string? AudioFormat { get; set; }
	public string? AudioChannel { get; set; }
	public int AudioSampleRate { get; set; }
	public decimal AudioBitRateKbs { get; set; }
	public float Similarity { get; set; }
	public DateTime DateCreated { get; set; }
	public bool IsImage { get; set; }
	public string HdrFormat { get; set; } = string.Empty;
	public string Flags { get; set; } = "None";
	public double PartialClipOffsetSeconds { get; set; }
	public Guid GroupId { get; set; }
}

public sealed class DeleteItemsRequest {
	public List<string> Paths { get; set; } = new();
	public bool Permanent { get; set; }
}

public sealed class MoveItemsRequest {
	public List<string> Paths { get; set; } = new();
	public string Destination { get; set; } = string.Empty;
}

public sealed class CreateLinksRequest {
	public List<string> Paths { get; set; } = new();
	public bool Hardlink { get; set; }
}

public sealed class RemoveItemsRequest {
	public List<string> Paths { get; set; } = new();
}

public sealed class AutoSelectRequest {
	/// <summary>"lowestQuality" | "smallestFile" | "oldest" | "newest" | "hundredPercentEqual"</summary>
	public string Mode { get; set; } = "lowestQuality";
}

public sealed class KeepBestRequest {
	public Guid GroupId { get; set; }
}

public sealed class FileOpResultDto {
	public int Done { get; set; }
	public int Failed { get; set; }
	public long FreedBytes { get; set; }
	public List<string> Errors { get; set; } = new();
}

// ── Auth ──────────────────────────────────────────────────────────────────────

public sealed class LoginRequest {
	public string Password { get; set; } = string.Empty;
	public bool Remember { get; set; }
}

public sealed class LoginResponse {
	public string Access_token { get; set; } = string.Empty;
	public string Refresh_token { get; set; } = string.Empty;
	public int Expires_in { get; set; }
}

public sealed class RefreshRequest {
	public string Refresh_token { get; set; } = string.Empty;
}

public sealed class RefreshResponse {
	public string Access_token { get; set; } = string.Empty;
	public int Expires_in { get; set; }
}

public sealed class AuthStatusResponse {
	public bool Authenticated { get; set; }
	public bool AuthEnabled { get; set; }
}

// ── Settings ──────────────────────────────────────────────────────────────────

public sealed class WebSettingsDto {
	public bool AutoLoadThumbnails { get; set; } = true;
	public int ThumbnailWidth { get; set; } = 480;
	public int ThumbnailJpegQuality { get; set; } = 85;
}

public sealed class DatabaseCleanResponse {
	public int Removed { get; set; }
	public int Remaining { get; set; }
}

public sealed class DatabaseClearResponse {
	public bool Success { get; set; }
}

// ── Health ────────────────────────────────────────────────────────────────────

public sealed class HealthResponse {
	public string Status { get; set; } = "Unhealthy";
	public bool Ffmpeg { get; set; }
	public bool Database { get; set; }
	public string Timestamp { get; set; } = DateTime.UtcNow.ToString("o");
}
