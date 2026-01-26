using Homespun.Features.PullRequests.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Homespun.Api.Tests;

/// <summary>
/// Custom WebApplicationFactory for API integration tests.
/// </summary>
public class HomespunWebApplicationFactory : WebApplicationFactory<Program>
{
    public TestDataStore TestDataStore { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove the real IDataStore registration
            services.RemoveAll<IDataStore>();

            // Add our test data store as singleton
            services.AddSingleton<IDataStore>(TestDataStore);
        });

        builder.UseEnvironment("Testing");
    }
}
