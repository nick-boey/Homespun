using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Homespun.Features.Observability.HealthChecks;

/// <summary>
/// Verifies the data directory is writable.
/// </summary>
public class DataDirectoryHealthCheck(string dataDirectory) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!Directory.Exists(dataDirectory))
            {
                return Task.FromResult(HealthCheckResult.Unhealthy(
                    $"Data directory does not exist: {dataDirectory}"));
            }

            // Test writability by creating and deleting a temp file
            var testFile = Path.Combine(dataDirectory, $".health-check-{Guid.NewGuid():N}");
            File.WriteAllText(testFile, "health-check");
            File.Delete(testFile);

            return Task.FromResult(HealthCheckResult.Healthy("Data directory is writable"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"Data directory is not writable: {ex.Message}"));
        }
    }
}
