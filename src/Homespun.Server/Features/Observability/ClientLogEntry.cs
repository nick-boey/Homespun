using System.ComponentModel.DataAnnotations;

namespace Homespun.Features.Observability;

/// <summary>
/// Client-originated session-event log entry, posted as part of a batch to
/// <c>POST /api/log/client</c>. The server validates each entry and forwards
/// it to the shared Serilog pipeline under
/// <c>SourceContext = "Homespun.ClientSessionEvents"</c>.
///
/// <para>
/// The client MAY include <see cref="EventId"/> and <see cref="Seq"/> when it
/// is propagating a server-received envelope — this lets Seq/Aspire stitch
/// client and server lines with a shared id. Server-only fields like
/// <c>Homespun.SessionEvents</c> <c>SourceContext</c> are overwritten on forward
/// to prevent client impersonation.
/// </para>
/// </summary>
public sealed record ClientLogEntry
{
    [Required]
    public string Timestamp { get; init; } = string.Empty;

    [Required]
    public string Level { get; init; } = "Information";

    [Required]
    public string Message { get; init; } = string.Empty;

    [Required]
    public string Hop { get; init; } = string.Empty;

    [Required]
    public string SessionId { get; init; } = string.Empty;

    public string? TaskId { get; init; }
    public string? MessageId { get; init; }
    public string? ArtifactId { get; init; }
    public string? StatusTimestamp { get; init; }
    public string? EventId { get; init; }
    public long? Seq { get; init; }
    public string? A2AKind { get; init; }
    public string? AGUIType { get; init; }
    public string? AGUICustomName { get; init; }
    public string? ContentPreview { get; init; }
}
