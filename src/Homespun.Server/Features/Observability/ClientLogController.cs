using Homespun.Shared.Models.Observability;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Homespun.Features.Observability;

/// <summary>
/// Receives batched client-side <see cref="SessionEventLogEntry"/> entries at
/// <c>POST /api/log/client</c> and forwards them to the server Serilog pipeline
/// under <c>SourceContext = "Homespun.ClientSessionEvents"</c>.
///
/// <para>
/// Accepts up to <see cref="MaxBatchSize"/> entries per request; larger batches
/// are rejected with <c>413 Payload Too Large</c>. Malformed entries reject the
/// whole batch with <c>400 Bad Request</c> naming the first invalid index.
/// </para>
/// </summary>
[ApiController]
[Produces("application/json")]
public sealed class ClientLogController : ControllerBase
{
    public const int MaxBatchSize = 100;

    private readonly ILogger<ClientLogController> _logger;
    private readonly SessionEventLogOptions _options;

    public ClientLogController(
        ILogger<ClientLogController> logger,
        IOptions<SessionEventLogOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    [HttpPost("api/log/client")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    public IActionResult Post([FromBody] ClientLogEntry[]? batch)
    {
        if (batch is null)
        {
            return BadRequest(new { error = "Request body is required" });
        }

        if (batch.Length > MaxBatchSize)
        {
            return StatusCode(
                StatusCodes.Status413PayloadTooLarge,
                new { error = $"Batch size {batch.Length} exceeds max {MaxBatchSize}" });
        }

        for (var i = 0; i < batch.Length; i++)
        {
            if (!IsValid(batch[i], out var error))
            {
                return BadRequest(new { error = $"Invalid entry at index {i}: {error}" });
            }
        }

        foreach (var entry in batch)
        {
            var forwarded = new SessionEventLogEntry
            {
                Timestamp = entry.Timestamp,
                Level = entry.Level,
                Message = entry.Message,
                SourceContext = SessionEventSourceContexts.Client,
                Component = SessionEventComponents.Web,
                Hop = entry.Hop,
                SessionId = entry.SessionId,
                TaskId = entry.TaskId,
                MessageId = entry.MessageId,
                ArtifactId = entry.ArtifactId,
                StatusTimestamp = entry.StatusTimestamp,
                EventId = entry.EventId,
                Seq = entry.Seq,
                A2AKind = entry.A2AKind,
                AGUIType = entry.AGUIType,
                AGUICustomName = entry.AGUICustomName,
                ContentPreview = entry.ContentPreview,
            };

            SessionEventLog.LogClientHop(_logger, _options, forwarded);
        }

        return Accepted();
    }

    private static bool IsValid(ClientLogEntry? entry, out string error)
    {
        if (entry is null)
        {
            error = "entry is null";
            return false;
        }
        if (string.IsNullOrWhiteSpace(entry.Hop))
        {
            error = "Hop is required";
            return false;
        }
        if (string.IsNullOrWhiteSpace(entry.SessionId))
        {
            error = "SessionId is required";
            return false;
        }
        if (string.IsNullOrWhiteSpace(entry.Timestamp))
        {
            error = "Timestamp is required";
            return false;
        }
        if (string.IsNullOrWhiteSpace(entry.Level))
        {
            error = "Level is required";
            return false;
        }

        error = string.Empty;
        return true;
    }
}
