namespace Homespun.Shared.Models.Observability;

/// <summary>
/// Configuration for client-side telemetry.
/// </summary>
public record TelemetryConfigDto
{
    /// <summary>
    /// Application Insights connection string. Null if not configured.
    /// </summary>
    public string? ApplicationInsightsConnectionString { get; init; }

    /// <summary>
    /// Whether Application Insights telemetry is enabled.
    /// </summary>
    public bool IsEnabled { get; init; }
}
