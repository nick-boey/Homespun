using Homespun.Features.Commands.Telemetry;
using Homespun.Features.Gitgraph.Telemetry;
using Homespun.Features.OpenSpec.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;

namespace Homespun.Features.Observability;

public static class HomespunTelemetryExtensions
{
    /// <summary>
    /// Adds Homespun custom activity sources to the OpenTelemetry tracing pipeline.
    /// </summary>
    public static IServiceCollection AddHomespunInstrumentation(this IServiceCollection services)
    {
        services.ConfigureOpenTelemetryTracerProvider(tracing =>
        {
            tracing.AddSource(HomespunActivitySources.AllSourceNames);
            tracing.AddSource(
                GraphgraphActivitySource.Name,
                OpenSpecActivitySource.Name,
                CommandsActivitySource.Name);
        });

        return services;
    }
}
