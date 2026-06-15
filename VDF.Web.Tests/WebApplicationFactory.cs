using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using VDF.Web.Services;

namespace VDF.Web.Tests;

/// <summary>
/// Custom WebApplicationFactory that configures the VDF.Web host for integration testing.
/// Disables authentication by default so endpoint tests can focus on HTTP behaviour.
/// </summary>
public class VdfWebFactory : WebApplicationFactory<Program> {
	public const string TestPassword = "test-password-1234";
	public const string TestApiKey = "test-api-key-abc";

	protected override void ConfigureWebHost(IWebHostBuilder builder) {
		builder.UseEnvironment("Development");

		// Set environment variables before services are built
		Environment.SetEnvironmentVariable("VDF_WEB_PASSWORD", TestPassword);
		Environment.SetEnvironmentVariable("VDF_WEB_AUTH", "true");
		Environment.SetEnvironmentVariable("VDF_API_KEYS", TestApiKey);

		builder.ConfigureServices(services => {
			// Remove the FFmpegSetupService background startup to avoid side effects
			var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(FFmpegSetupService));
			if (descriptor != null)
				services.Remove(descriptor);
			services.AddSingleton<FFmpegSetupService>(_ => null!);
		});
	}
}
