using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace VDF.Web.Tests;

public class HealthEndpointTests : IClassFixture<VdfWebFactory> {
	readonly HttpClient _client;
	readonly VdfWebFactory _factory;

	public HealthEndpointTests(VdfWebFactory factory) {
		_factory = factory;
		_client = factory.CreateClient();
	}

	[Fact]
	public async Task Health_ReturnsHealthReport() {
		var response = await _client.GetAsync("/health");
		response.EnsureSuccessStatusCode();

		var body = await response.Content.ReadFromJsonAsync<JsonElement>();
		Assert.True(body.TryGetProperty("Status", out var statusProp));
		var status = statusProp.GetString();
		Assert.NotNull(status);
		// Status should be one of: Healthy, Degraded, Unhealthy
		Assert.Contains(status, new[] { "Healthy", "Degraded", "Unhealthy" });
	}

	[Fact]
	public async Task Health_ContainsRequiredFields() {
		var response = await _client.GetAsync("/health");
		response.EnsureSuccessStatusCode();

		var body = await response.Content.ReadFromJsonAsync<JsonElement>();
		Assert.True(body.TryGetProperty("Ffmpeg", out _), "Health report should contain Ffmpeg field");
		Assert.True(body.TryGetProperty("Database", out _), "Health report should contain Database field");
		Assert.True(body.TryGetProperty("Timestamp", out _), "Health report should contain Timestamp field");
	}

	[Fact]
	public async Task Health_DoesNotRequireAuth() {
		// /health is explicitly excluded from auth
		using var client = _factory.CreateClient();
		var response = await client.GetAsync("/health");
		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
	}
}
