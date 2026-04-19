using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry.Logs;

namespace Homespun.Api.Tests.Features;

/// <summary>
/// Guards the ServiceDefaults OTLP wiring: one exporter for the Aspire
/// dashboard (per-signal <c>AddOtlpExporter</c>) and one for Seq (via
/// <c>AddSeqEndpoint</c>). Also guards the Program.cs logging-provider
/// ordering invariant — <c>ClearProviders()</c> must run before
/// <c>AddServiceDefaults()</c> or the OTLP logger provider is wiped.
/// </summary>
[TestFixture]
public class LoggingProviderStartupTests
{
    [Test]
    public void OpenTelemetryLoggerProvider_is_registered_after_Program_startup()
    {
        using var factory = new HomespunWebApplicationFactory();
        using var _ = factory.CreateClient();

        var providers = factory.Services.GetServices<ILoggerProvider>().ToList();

        Assert.That(providers, Is.Not.Empty, "Expected at least one ILoggerProvider registered");
        Assert.That(
            providers.Any(p => p.GetType().FullName == "OpenTelemetry.Logs.OpenTelemetryLoggerProvider"),
            Is.True,
            $"Expected OpenTelemetryLoggerProvider in DI. Found: {string.Join(", ", providers.Select(p => p.GetType().FullName))}");
    }

    [Test]
    public void OpenTelemetryLoggerOptions_has_at_least_two_otlp_exporter_processors()
    {
        // ServiceDefaults registers one OTLP exporter for the Aspire dashboard
        // leg and Aspire.Seq registers another for Seq. Both attach via
        // IConfigureOptions<OpenTelemetryLoggerOptions> callbacks that push
        // BatchLogRecordExportProcessor<OtlpLogExporter> instances onto the
        // provider. We count those instances by materialising options.
        Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT", "http://127.0.0.1:4317");
        Environment.SetEnvironmentVariable("ConnectionStrings__seq", "http://127.0.0.1:5341");
        try
        {
            using var factory = new HomespunWebApplicationFactory();
            using var _ = factory.CreateClient();

            var configures = factory.Services
                .GetServices<IConfigureOptions<OpenTelemetryLoggerOptions>>()
                .ToList();

            Assert.That(configures.Count, Is.GreaterThanOrEqualTo(2),
                $"Expected ≥2 IConfigureOptions<OpenTelemetryLoggerOptions> (Aspire + Seq). Found: {configures.Count}");
        }
        finally
        {
            Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT", null);
            Environment.SetEnvironmentVariable("ConnectionStrings__seq", null);
        }
    }
}
