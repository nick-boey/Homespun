using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

public static class Extensions
{
    public static IHostApplicationBuilder AddServiceDefaults(this IHostApplicationBuilder builder)
    {
        builder.ConfigureOpenTelemetry();

        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });

        return builder;
    }

    public static IHostApplicationBuilder ConfigureOpenTelemetry(this IHostApplicationBuilder builder)
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
            })
            .WithTracing(tracing =>
            {
                // NOTE: we deliberately do NOT add "Microsoft.AspNetCore.SignalR.Server" here.
                // The Homespun TraceparentHubFilter owns the SignalR span tree so that each
                // server-side span can be parented to the client's traceparent (which is
                // impossible for the native source — WebSocket transports give it no place
                // to read the client context from). See client-otel/proposal.md.
                tracing.AddSource(builder.Environment.ApplicationName)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation();
            });

        AddOpenTelemetryExporters(builder);

        return builder;
    }

    private static IHostApplicationBuilder AddOpenTelemetryExporters(IHostApplicationBuilder builder)
    {
        // Aspire dashboard leg — per-signal OTLP exporters driven by
        // OTEL_EXPORTER_OTLP_ENDPOINT. `UseOtlpExporter()` cannot coexist with
        // a second OTLP exporter (opentelemetry-dotnet#5538), so we register
        // each signal explicitly and let `AddSeqEndpoint` attach its own
        // OTLP exporter alongside.
        var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter)
        {
            builder.Services.Configure<OpenTelemetryLoggerOptions>(o => o.AddOtlpExporter());
            builder.Services.ConfigureOpenTelemetryTracerProvider(t => t.AddOtlpExporter());
            builder.Services.ConfigureOpenTelemetryMeterProvider(m => m.AddOtlpExporter());
        }

        // Seq leg — logs + traces only. Seq doesn't accept metrics, so we
        // don't register a meter exporter for it. Reads the `seq` connection
        // string injected by the AppHost's `WithReference(seq)` wiring;
        // skip when absent (test hosts, cold boots before Seq is up) so the
        // component doesn't throw "ServerUrl setting is missing".
        var seqEndpoint = builder.Configuration.GetConnectionString("seq");
        if (!string.IsNullOrWhiteSpace(seqEndpoint))
        {
            builder.AddSeqEndpoint("seq");
        }

        return builder;
    }

    public static IHostApplicationBuilder AddDefaultHealthChecks(this IHostApplicationBuilder builder)
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks("/health");

        app.MapHealthChecks("/alive", new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("live")
        });

        return app;
    }
}
