using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Homespun.Api.Tests.Features;

/// <summary>
/// Guards the Program.cs logging-provider ordering (ClearProviders MUST run
/// before AddServiceDefaults). If a future edit inverts that order, the OTLP
/// logger provider wired by ServiceDefaults gets wiped and `aspire otel logs`
/// goes silent. This test asserts the OpenTelemetry logger provider survives
/// DI registration end-to-end.
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
}
