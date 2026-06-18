using VDF.Web.ApiModels;
using VDF.Web.Webhooks;

namespace VDF.Web.Endpoints;

static class WebhookEndpoints {
	public static WebApplication MapWebhookApi(this WebApplication app) {
		var group = app.MapGroup("/api/webhooks");
		group.RequireAuthorization();

		group.MapGet("/", (WebhookService webhookService) => {
			var webhooks = webhookService.GetAllWebhooks();
			return Results.Ok(ApiResponse.Ok(webhooks));
		});

		group.MapPost("/", (WebhookService webhookService, CreateWebhookRequest req) => {
			if (string.IsNullOrWhiteSpace(req.Url))
				return Results.Json(ApiResponse.Fail("url_required", "validation_error"), statusCode: 400);

			if (!Uri.TryCreate(req.Url, UriKind.Absolute, out _))
				return Results.Json(ApiResponse.Fail("invalid_url", "validation_error"), statusCode: 400);

			if (req.Events == null || req.Events.Count == 0)
				return Results.Json(ApiResponse.Fail("events_required", "validation_error"), statusCode: 400);

			var events = req.Events.Select(e => Enum.Parse<WebhookService.WebhookEvent>(e, true)).ToList();
			var webhook = webhookService.CreateWebhook(req.Url, events, req.Secret);
			return Results.Ok(ApiResponse.Ok(webhook));
		});

		group.MapDelete("/{id}", (WebhookService webhookService, string id) => {
			if (!webhookService.DeleteWebhook(id))
				return Results.NotFound(ApiResponse.Fail("webhook_not_found", "not_found"));
			return Results.Ok(ApiResponse.Ok(new { deleted = true }));
		});

		return app;
	}
}

public sealed class CreateWebhookRequest {
	public string Url { get; set; } = string.Empty;
	public List<string> Events { get; set; } = new();
	public string? Secret { get; set; }
}
