using System.Text.Json;
using System.Text.Json.Serialization;
using Homespun.Shared.Models.Observability;
using Microsoft.AspNetCore.Mvc;

namespace Homespun.Features.Observability;

/// <summary>
/// Controller for receiving client-side telemetry events.
/// Events are logged to stdout as JSON where Promtail can collect them for Loki.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class ClientTelemetryController : ControllerBase
{
    /// <summary>
    /// Receives a batch of telemetry events from the Blazor WASM client.
    /// Events are logged to stdout as JSON where Promtail can collect them.
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

    private static void LogTelemetryEvent(ClientTelemetryEvent evt, string? sessionId)
    {
        // Write JSON directly to stdout for Promtail to parse
        // This includes dedicated telemetry fields alongside standard log fields
        var telemetryLog = new ClientTelemetryLogEntry
        {
            Timestamp = evt.Timestamp.ToString("O"),
            Level = evt.Type == TelemetryEventType.Exception ? "Error" : "Information",
            Message = $"ClientTelemetry: {evt.Type} - {evt.Name}",
            SourceContext = "ClientTelemetry",
            TelemetryType = evt.Type.ToString(),
            TelemetryName = evt.Name,
            SessionId = sessionId ?? "unknown",
            Properties = evt.Properties,
            DurationMs = evt.DurationMs,
            Success = evt.Success,
            StatusCode = evt.StatusCode
        };

        Console.WriteLine(JsonSerializer.Serialize(telemetryLog, ClientTelemetryLogEntryContext.Default.ClientTelemetryLogEntry));
    }
}

/// <summary>
/// Represents a client telemetry log entry with fields for Promtail/Loki.
/// </summary>
internal sealed class ClientTelemetryLogEntry
{
    public required string Timestamp { get; init; }
    public required string Level { get; init; }
    public required string Message { get; init; }
    public required string SourceContext { get; init; }
    public required string TelemetryType { get; init; }
    public required string TelemetryName { get; init; }
    public required string SessionId { get; init; }
    public Dictionary<string, string>? Properties { get; init; }
    public double? DurationMs { get; init; }
    public bool? Success { get; init; }
    public int? StatusCode { get; init; }
}

/// <summary>
/// Source-generated JSON serialization context for AOT compatibility.
/// </summary>
[JsonSerializable(typeof(ClientTelemetryLogEntry))]
internal sealed partial class ClientTelemetryLogEntryContext : JsonSerializerContext
{
}
