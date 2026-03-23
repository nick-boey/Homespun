using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Homespun.Api.Tests;

/// <summary>
/// Custom WebApplicationFactory that configures the app in mock mode for integration testing.
/// </summary>
public class HomespunWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Use Production environment to skip UseStaticWebAssets (avoids missing wwwroot directory)
        builder.UseEnvironment("Production");
        builder.UseSetting("MockMode:Enabled", "true");
        builder.UseSetting("MockMode:SeedData", "false");
    }
}
