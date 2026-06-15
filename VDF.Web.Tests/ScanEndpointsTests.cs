using System.Net;
using System.Net.Http.Json;
using VDF.Web.Models;

namespace VDF.Web.Tests;

public class ScanEndpointsTests : IClassFixture<VdfWebFactory> {
	readonly HttpClient _client;
 readonly VdfWebFactory _factory;

	public ScanEndpointsTests(VdfWebFactory factory) {
		_factory = factory;
		_client = factory.CreateClient();
	}

	[Fact]
	public async Task GetState_ReturnsCurrentState() {
		// /api/scan/state requires auth
		using var request = new HttpRequestMessage(HttpMethod.Get, "/api/scan/state");
		request.Headers.Add("X-API-Key", VdfWebFactory.TestApiKey);

		var response = await _client.SendAsync(request);
		response.EnsureSuccessStatusCode();

		var body = await response.Content.ReadFromJsonAsync<ScanStateResponse>();
		Assert.NotNull(body);
		Assert.False(string.IsNullOrEmpty(body.State));
	}

	[Fact]
	public async Task Start_WithoutAuth_Returns401() {
		// No auth header at all
		using var client = _factory.CreateClient();
		var response = await client.PostAsJsonAsync("/api/scan/start", new { });

		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
	}

	[Fact]
	public async Task Start_WithAuth_Returns202() {
		using var request = new HttpRequestMessage(HttpMethod.Post, "/api/scan/start");
		request.Headers.Add("X-API-Key", VdfWebFactory.TestApiKey);
		request.Content = JsonContent.Create(new { });

		var response = await _client.SendAsync(request);

		// 202 Accepted or 409 Conflict if a scan is already running
		Assert.True(
			response.StatusCode == HttpStatusCode.Accepted ||
			response.StatusCode == HttpStatusCode.Conflict,
			$"Expected 202 or 409, got {response.StatusCode}");
	}
}
