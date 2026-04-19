using Homespun.Shared.Models.Observability;
using Microsoft.AspNetCore.Mvc;

namespace Homespun.Features.Observability;

/// <summary>
/// Controller for receiving client-side telemetry events.
/// Events flow through ILogger so they reach both the Aspire dashboard and
/// Seq via the OTLP log exporters wired by ServiceDefaults.
/// </summary>
[ApiController]
[Route("api/client-telemetry")]
[Produces("application/json")]
public class ClientTelemetryController(ILogger<ClientTelemetryController> logger) : ControllerBase
{
    /// <summary>
    /// Receives a batch of telemetry events from the web client.
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
        // Every key landed into the scope becomes an OTLP log attribute
        // (Aspire dashboard + Seq). SourceContext/Component are overridden
        // so downstream log queries see these entries as coming from the
        // Client tier, not the Server.
        var scopeState = new Dictionary<string, object?>
        {
            ["SourceContext"] = "ClientTelemetry",
            ["Component"] = "Client",
            ["TelemetryType"] = evt.Type.ToString(),
            ["TelemetryName"] = evt.Name,
            ["SessionId"] = sessionId ?? "unknown",
            ["ClientTimestamp"] = evt.Timestamp.ToString("O")
        };

        if (evt.DurationMs.HasValue)
        {
            scopeState["DurationMs"] = evt.DurationMs.Value;
        }
        if (evt.Success.HasValue)
        {
            scopeState["Success"] = evt.Success.Value;
        }
        if (evt.StatusCode.HasValue)
        {
            scopeState["StatusCode"] = evt.StatusCode.Value;
        }
        if (evt.Properties is { Count: > 0 })
        {
            scopeState["Properties"] = evt.Properties;
        }

        using var scope = logger.BeginScope(scopeState);

        var level = evt.Type == TelemetryEventType.Exception
            ? LogLevel.Error
            : LogLevel.Information;

        logger.Log(
            level,
            "ClientTelemetry: {TelemetryType} - {TelemetryName}",
            evt.Type,
            evt.Name);
    }
}
