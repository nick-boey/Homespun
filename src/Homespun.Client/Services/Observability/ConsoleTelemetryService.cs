using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Homespun.Client.Services.Observability;

/// <summary>
/// Telemetry service that logs to the browser console.
/// Used for Docker and local development modes.
/// </summary>
public class ConsoleTelemetryService : ITelemetryService
{
    private readonly ILogger<ConsoleTelemetryService> _logger;

    public ConsoleTelemetryService(ILogger<ConsoleTelemetryService> logger)
    {
        _logger = logger;
    }

    public bool IsEnabled => true;

    public Task InitializeAsync()
    {
        _logger.LogInformation("[Telemetry] Console telemetry service initialized");
        return Task.CompletedTask;
    }

    public void TrackPageView(string pageName, string? uri = null)
    {
        _logger.LogInformation("[Telemetry] PageView: {PageName}, URI: {Uri}", pageName, uri ?? "N/A");
    }

    public void TrackEvent(string eventName, IDictionary<string, string>? properties = null)
    {
        var propsJson = properties != null ? JsonSerializer.Serialize(properties) : "{}";
        _logger.LogInformation("[Telemetry] Event: {EventName}, Properties: {Properties}", eventName, propsJson);
    }

    public void TrackException(Exception exception, IDictionary<string, string>? properties = null)
    {
        var propsJson = properties != null ? JsonSerializer.Serialize(properties) : "{}";
        _logger.LogError(exception, "[Telemetry] Exception: {Message}, Properties: {Properties}", exception.Message, propsJson);
    }

    public void TrackDependency(string type, string target, string name, TimeSpan duration, bool success, int? statusCode = null)
    {
        _logger.LogInformation(
            "[Telemetry] Dependency: Type={Type}, Target={Target}, Name={Name}, Duration={Duration}ms, Success={Success}, StatusCode={StatusCode}",
            type, target, name, duration.TotalMilliseconds, success, statusCode?.ToString() ?? "N/A");
    }
}
