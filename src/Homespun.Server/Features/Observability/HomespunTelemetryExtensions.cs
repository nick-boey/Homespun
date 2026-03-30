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
        });

        return services;
    }
}
