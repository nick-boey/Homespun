using Homespun.Features.AgentOrchestration.Services;
using Microsoft.Extensions.Options;

namespace Homespun.Features.Observability.HealthChecks;

public static class HealthCheckExtensions
{
    /// <summary>
    /// Registers component health checks for worker sidecar, data directory, and GitHub API.
    /// </summary>
    public static IServiceCollection AddHomespunHealthChecks(
        this IServiceCollection services,
        string dataDirectory)
    {
        services.AddHealthChecks()
            .AddCheck(
                "data-directory",
                new DataDirectoryHealthCheck(dataDirectory),
                tags: ["ready"])
            .Add(new Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckRegistration(
                "worker-sidecar",
                sp =>
                {
                    var options = sp.GetRequiredService<IOptions<MiniPromptOptions>>();
                    var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
                    return new WorkerSidecarHealthCheck(httpClientFactory, options.Value.SidecarUrl);
                },
                failureStatus: null,
                tags: ["ready"]))
            .Add(new Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckRegistration(
                "github-api",
                sp =>
                {
                    var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
                    return new GitHubApiHealthCheck(httpClientFactory);
                },
                failureStatus: Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded,
                tags: ["ready"]));

        return services;
    }
}
