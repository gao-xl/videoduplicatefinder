using System.Net;
using System.Text.Json;

namespace VDF.Web.Middleware;

/// <summary>
/// Global exception handling middleware that catches unhandled exceptions
/// and returns structured error responses.
/// </summary>
public sealed class ExceptionMiddleware {
	private readonly RequestDelegate _next;
	private readonly ILogger<ExceptionMiddleware> _logger;
	private readonly IHostEnvironment _env;

	public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger, IHostEnvironment env) {
		_next = next;
		_logger = logger;
		_env = env;
	}

	public async Task InvokeAsync(HttpContext context) {
		try {
			await _next(context);
		}
		catch (Exception ex) {
			_logger.LogError(ex, "Unhandled exception on {Method} {Path}", context.Request.Method, context.Request.Path);
			await HandleExceptionAsync(context, ex);
		}
	}

	private async Task HandleExceptionAsync(HttpContext context, Exception exception) {
		context.Response.ContentType = "application/json";

		var statusCode = exception switch
		{
			ArgumentException => (int)HttpStatusCode.BadRequest,
			UnauthorizedAccessException => (int)HttpStatusCode.Unauthorized,
			InvalidOperationException => (int)HttpStatusCode.BadRequest,
			TimeoutException => (int)HttpStatusCode.RequestTimeout,
			OperationCanceledException => (int)HttpStatusCode.RequestTimeout,
			_ => (int)HttpStatusCode.InternalServerError
		};

		var response = new {
			error = new {
				code = statusCode,
				message = _env.IsDevelopment()
					? exception.Message
					: "An internal server error occurred.",
				details = _env.IsDevelopment() ? exception.StackTrace : null
			}
		};

		context.Response.StatusCode = statusCode;
		await context.Response.WriteAsync(JsonSerializer.Serialize(response, new JsonSerializerOptions {
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase
		}));
	}
}
