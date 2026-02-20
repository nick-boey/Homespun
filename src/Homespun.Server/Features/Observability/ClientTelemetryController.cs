using System.Text.Json;
using Homespun.Shared.Models.Observability;
using Microsoft.AspNetCore.Mvc;

namespace Homespun.Features.Observability;

/// <summary>
/// Controller for receiving client-side telemetry events.
/// Events are logged to stdout where Promtail can collect them for Loki.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class ClientTelemetryController(ILogger<ClientTelemetryController> logger) : ControllerBase
{
    /// <summary>
    /// Receives a batch of telemetry events from the Blazor WASM client.
    /// Events are logged to stdout where Promtail can collect them.
    /// </summary>
    /// <param name="batch">The batch of telemetry events.</param>
    /// <returns>202 Accepted on success, 400 Bad Request if batch is invalid.</returns>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult ReceiveTelemetry([FromBody] ClientTelemetryBatch? batch)
    {
        if (batch?.Events is null || batch.Events.Count == 0)
        {
            return BadRequest("No telemetry events provided");
        }

        foreach (var evt in batch.Events)
        {
            LogTelemetryEvent(evt, batch.SessionId);
        }

        return Accepted();
    }

    private void LogTelemetryEvent(ClientTelemetryEvent evt, string? sessionId)
    {
        // Log in a structured format that Promtail can parse
        // Use a special prefix for client telemetry identification
        var propertiesJson = evt.Properties is not null
            ? JsonSerializer.Serialize(evt.Properties)
            : "{}";

        var logMessage = $"[ClientTelemetry] Type={evt.Type}, Name={evt.Name}, " +
                        $"SessionId={sessionId ?? "unknown"}, " +
                        $"Properties={propertiesJson}";

        // Add dependency-specific fields if present
        if (evt.Type == TelemetryEventType.Dependency)
        {
            logMessage += $", DurationMs={evt.DurationMs}, Success={evt.Success}, StatusCode={evt.StatusCode}";
        }

        switch (evt.Type)
        {
            case TelemetryEventType.Exception:
                logger.LogError(logMessage);
                break;
            case TelemetryEventType.PageView:
            case TelemetryEventType.Event:
            case TelemetryEventType.Dependency:
            default:
                logger.LogInformation(logMessage);
                break;
        }
    }
}
