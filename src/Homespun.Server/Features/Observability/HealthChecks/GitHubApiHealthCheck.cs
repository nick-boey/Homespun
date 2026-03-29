using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Homespun.Features.Observability.HealthChecks;

/// <summary>
/// Optional, non-critical health check for GitHub API reachability.
/// Returns Degraded (not Unhealthy) on failure since GitHub access is not required for core functionality.
/// </summary>
public class GitHubApiHealthCheck(IHttpClientFactory httpClientFactory) : IHealthCheck
{
    private const string GitHubApiUrl = "https://api.github.com/";

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Homespun-HealthCheck");

            var response = await client.GetAsync(GitHubApiUrl, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return HealthCheckResult.Healthy("GitHub API is reachable");
            }

            return HealthCheckResult.Degraded(
                $"GitHub API returned {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Degraded(
                $"GitHub API unreachable: {ex.Message}");
        }
    }
}
