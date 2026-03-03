namespace Homespun.Client.Services.Observability;

/// <summary>
/// Abstraction for client-side telemetry and logging.
/// </summary>
public interface ITelemetryService
{
    /// <summary>
    /// Initializes the telemetry service. Should be called once at app startup.
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    /// Tracks a page view event.
    /// </summary>
    /// <param name="pageName">Name of the page being viewed</param>
    /// <param name="uri">Optional URI of the page</param>
    void TrackPageView(string pageName, string? uri = null);

    /// <summary>
    /// Tracks a custom event.
    /// </summary>
    /// <param name="eventName">Name of the event</param>
    /// <param name="properties">Optional custom properties</param>
    void TrackEvent(string eventName, IDictionary<string, string>? properties = null);

    /// <summary>
    /// Tracks an exception.
    /// </summary>
    /// <param name="exception">The exception that occurred</param>
    /// <param name="properties">Optional custom properties</param>
    void TrackException(Exception exception, IDictionary<string, string>? properties = null);

    /// <summary>
    /// Tracks a dependency call (e.g., HTTP request, database call).
    /// </summary>
    /// <param name="type">Type of dependency (e.g., "HTTP", "SQL")</param>
    /// <param name="target">Target of the dependency (e.g., hostname)</param>
    /// <param name="name">Name of the dependency call (e.g., endpoint path)</param>
    /// <param name="duration">How long the call took</param>
    /// <param name="success">Whether the call succeeded</param>
    /// <param name="statusCode">Optional HTTP status code or error code</param>
    void TrackDependency(string type, string target, string name, TimeSpan duration, bool success, int? statusCode = null);

    /// <summary>
    /// Whether telemetry is enabled and initialized.
    /// </summary>
    bool IsEnabled { get; }
}
