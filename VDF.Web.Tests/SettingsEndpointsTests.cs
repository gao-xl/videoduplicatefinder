using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace VDF.Web.Tests;

public class SettingsEndpointsTests : IClassFixture<VdfWebFactory> {
	readonly HttpClient _client;
	readonly VdfWebFactory _factory;

	public SettingsEndpointsTests(VdfWebFactory factory) {
		_factory = factory;
		_client = factory.CreateClient();
	}

	[Fact]
	public async Task GetSettings_ReturnsSettingsObject() {
		using var request = new HttpRequestMessage(HttpMethod.Get, "/api/settings");
		request.Headers.Add("X-API-Key", VdfWebFactory.TestApiKey);

		var response = await _client.SendAsync(request);
		response.EnsureSuccessStatusCode();

		var body = await response.Content.ReadFromJsonAsync<JsonElement>();
		Assert.True(body.TryGetProperty("Threshhold", out _), "Settings should contain Threshhold");
		Assert.True(body.TryGetProperty("IncludeList", out _), "Settings should contain IncludeList");
		Assert.True(body.TryGetProperty("Percent", out _), "Settings should contain Percent");
	}

	[Fact]
	public async Task PutSettings_UpdatesSettings() {
		var dto = new Dictionary<string, object?> {
			["IncludeList"] = new List<string>(),
			["BlackList"] = new List<string>(),
			["Threshhold"] = 3,
			["Percent"] = 95f,
			["PercentDurationDifference"] = 20.0,
			["MaxDegreeOfParallelism"] = 1,
			["ThumbnailCount"] = 1,
			["IncludeSubDirectories"] = true,
			["IncludeImages"] = true,
		};

		using var putReq = new HttpRequestMessage(HttpMethod.Put, "/api/settings") {
			Content = JsonContent.Create(dto),
		};
		putReq.Headers.Add("X-API-Key", VdfWebFactory.TestApiKey);

		var putResp = await _client.SendAsync(putReq);
		Assert.Equal(HttpStatusCode.OK, putResp.StatusCode);

		var putBody = await putResp.Content.ReadFromJsonAsync<JsonElement>();
		Assert.True(putBody.TryGetProperty("updated", out var updated));
		Assert.True(updated.GetBoolean());
	}
}
