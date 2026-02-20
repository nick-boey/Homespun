namespace Homespun.Shared.Models.Observability;

/// <summary>
/// Batch of telemetry events from the client.
/// </summary>
public record ClientTelemetryBatch
{
    /// <summary>
    /// Client session identifier for correlation.
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// Collection of telemetry events.
    /// </summary>
    public List<ClientTelemetryEvent> Events { get; init; } = [];
}

/// <summary>
/// Individual telemetry event from the client.
/// </summary>
public record ClientTelemetryEvent
{
    /// <summary>
    /// Type of telemetry event.
    /// </summary>
    public TelemetryEventType Type { get; init; }

    /// <summary>
    /// Event name (page name, event name, or exception type).
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Event timestamp (client-side).
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Additional properties for the event.
    /// </summary>
    public Dictionary<string, string>? Properties { get; init; }

    /// <summary>
    /// Duration in milliseconds (for dependency tracking).
    /// </summary>
    public double? DurationMs { get; init; }

    /// <summary>
    /// Whether the operation succeeded (for dependency tracking).
    /// </summary>
    public bool? Success { get; init; }

    /// <summary>
    /// Status code (for HTTP dependency tracking).
    /// </summary>
    public int? StatusCode { get; init; }
}

/// <summary>
/// Types of client telemetry events.
/// </summary>
public enum TelemetryEventType
{
    PageView,
    Event,
    Exception,
    Dependency
}
