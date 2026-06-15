using System.Net;
using System.Net.Http.Json;
using VDF.Web.Models;

namespace VDF.Web.Tests;

public class AuthEndpointsTests : IClassFixture<VdfWebFactory> {
	readonly HttpClient _client;
 readonly VdfWebFactory _factory;

	public AuthEndpointsTests(VdfWebFactory factory) {
		_factory = factory;
		_client = factory.CreateClient();
	}

	[Fact]
	public async Task Login_CorrectPassword_Returns200WithTokens() {
		var response = await _client.PostAsJsonAsync("/api/auth/login",
			new { password = VdfWebFactory.TestPassword });

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);

		var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
		Assert.NotNull(body);
		Assert.False(string.IsNullOrEmpty(body.Access_token));
		Assert.False(string.IsNullOrEmpty(body.Refresh_token));
		Assert.Equal(900, body.Expires_in);
	}

	[Fact]
	public async Task Login_WrongPassword_Returns401() {
		var response = await _client.PostAsJsonAsync("/api/auth/login",
			new { password = "wrong-password" });

		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
	}

	[Fact]
	public async Task Status_NotAuthenticated_ReturnsAuthenticatedFalse() {
		// Use a fresh client without any cookies/tokens
		using var client = _factory.CreateClient();

		var response = await client.GetAsync("/api/auth/status");
		response.EnsureSuccessStatusCode();

		var body = await response.Content.ReadFromJsonAsync<AuthStatusResponse>();
		Assert.NotNull(body);
		Assert.False(body.Authenticated);
	}

	[Fact]
	public async Task Status_Authenticated_ReturnsAuthenticatedTrue() {
		// Login first to get a token
		var loginResponse = await _client.PostAsJsonAsync("/api/auth/login",
			new { password = VdfWebFactory.TestPassword });
		loginResponse.EnsureSuccessStatusCode();

		var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
		Assert.NotNull(loginBody);

		// Use the access token to check status
		using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/status");
		request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", loginBody.Access_token);

		var response = await _client.SendAsync(request);
		response.EnsureSuccessStatusCode();

		var body = await response.Content.ReadFromJsonAsync<AuthStatusResponse>();
		Assert.NotNull(body);
		Assert.True(body.Authenticated);
	}

	[Fact]
	public async Task Refresh_ValidRefreshToken_ReturnsNewAccessToken() {
		// Login first
		var loginResponse = await _client.PostAsJsonAsync("/api/auth/login",
			new { password = VdfWebFactory.TestPassword });
		loginResponse.EnsureSuccessStatusCode();

		var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
		Assert.NotNull(loginBody);

		// Refresh the token
		var refreshResponse = await _client.PostAsJsonAsync("/api/auth/refresh",
			new { refresh_token = loginBody.Refresh_token });

		Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);

		var refreshBody = await refreshResponse.Content.ReadFromJsonAsync<RefreshResponse>();
		Assert.NotNull(refreshBody);
		Assert.False(string.IsNullOrEmpty(refreshBody.Access_token));
		Assert.Equal(900, refreshBody.Expires_in);
	}

	[Fact]
	public async Task Refresh_InvalidRefreshToken_Returns401() {
		var refreshResponse = await _client.PostAsJsonAsync("/api/auth/refresh",
			new { refresh_token = "invalid-token" });

		Assert.Equal(HttpStatusCode.Unauthorized, refreshResponse.StatusCode);
	}

	[Fact]
	public async Task ApiKeyAuthentication_ValidKey_ReturnsAuthenticated() {
		using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/status");
		request.Headers.Add("X-API-Key", VdfWebFactory.TestApiKey);

		var response = await _client.SendAsync(request);
		response.EnsureSuccessStatusCode();

		var body = await response.Content.ReadFromJsonAsync<AuthStatusResponse>();
		Assert.NotNull(body);
		Assert.True(body.Authenticated);
	}

	[Fact]
	public async Task ApiKeyAuthentication_InvalidKey_ReturnsUnauthenticated() {
		using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/status");
		request.Headers.Add("X-API-Key", "invalid-key");

		var response = await _client.SendAsync(request);
		response.EnsureSuccessStatusCode();

		var body = await response.Content.ReadFromJsonAsync<AuthStatusResponse>();
		Assert.NotNull(body);
		Assert.False(body.Authenticated);
	}
}
