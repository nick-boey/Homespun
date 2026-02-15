using Homespun.Features.PullRequests.Data;
using Homespun.Features.Testing;
using Homespun.Tests.Helpers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Homespun.Api.Tests;

/// <summary>
/// Custom WebApplicationFactory for API integration tests.
/// Uses the shared MockDataStore from the main project.
/// </summary>
public class HomespunWebApplicationFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// The mock data store for test data manipulation.
    /// Uses the shared MockDataStore from Homespun.Features.Testing.
    /// </summary>
    public MockDataStore MockDataStore { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove any existing IDataStore registrations (from production or mock mode)
            services.RemoveAll<IDataStore>();
            services.RemoveAll<MockDataStore>();

            // Add our test data store as singleton
            services.AddSingleton(MockDataStore);
            services.AddSingleton<IDataStore>(MockDataStore);
        });

        // Redirect logging to file instead of console for better test performance
        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.SetMinimumLevel(LogLevel.Warning);

            var testResultsDir = Path.Combine(Directory.GetCurrentDirectory(), "TestResults", "ApiTests");
            var logFilePath = Path.Combine(testResultsDir, $"api-test-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            logging.AddProvider(new TestFileLoggerProvider(logFilePath));
        });

        builder.UseEnvironment("Testing");
    }
}
