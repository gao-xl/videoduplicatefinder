using System.Text.Json.Serialization;

namespace VDF.Web.ApiModels;

public sealed class ApiResponse<T> {
	[JsonPropertyName("success")]
	public bool Success { get; init; }

	[JsonPropertyName("data")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public T? Data { get; init; }

	[JsonPropertyName("error")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Error { get; init; }

	[JsonPropertyName("errorCode")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? ErrorCode { get; init; }

	[JsonPropertyName("validationErrors")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public Dictionary<string, string[]>? ValidationErrors { get; init; }

	public static ApiResponse<T> Ok(T data) => new() { Success = true, Data = data };

	public static ApiResponse<T> Fail(string error, string? errorCode = null) => new() {
		Success = false,
		Error = error,
		ErrorCode = errorCode
	};

	public static ApiResponse<T> ValidationFail(Dictionary<string, string[]> errors) => new() {
		Success = false,
		Error = "Validation failed",
		ErrorCode = "validation_error",
		ValidationErrors = errors
	};
}

public static class ApiResponse {
	public static ApiResponse<T> Ok<T>(T data) => ApiResponse<T>.Ok(data);

	public static ApiResponse<object?> Fail(string error, string? errorCode = null) =>
		ApiResponse<object?>.Fail(error, errorCode);

	public static ApiResponse<object?> ValidationFail(Dictionary<string, string[]> errors) =>
		ApiResponse<object?>.ValidationFail(errors);
}
