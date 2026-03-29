using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Homespun.Features.Observability.HealthChecks;

/// <summary>
/// Checks connectivity to the worker sidecar by calling its /api/health endpoint.
/// </summary>
public class WorkerSidecarHealthCheck(IHttpClientFactory httpClientFactory, string? sidecarUrl) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(sidecarUrl))
        {
            return HealthCheckResult.Healthy("Worker sidecar not configured (local mode)");
        }

        try
        {
            var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);

            var response = await client.GetAsync(
                $"{sidecarUrl.TrimEnd('/')}/api/health",
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return HealthCheckResult.Healthy("Worker sidecar is reachable");
            }

            return HealthCheckResult.Degraded(
                $"Worker sidecar returned {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                $"Worker sidecar unreachable: {ex.Message}");
        }
    }
}
