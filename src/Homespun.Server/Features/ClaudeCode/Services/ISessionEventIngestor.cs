using System.Text.Json;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Orchestrates the per-event flow from the worker's SSE stream through persistence and
/// client broadcast:
/// <c>worker A2A event → <see cref="IA2AEventStore"/>.Append → <see cref="IA2AToAGUITranslator"/>
///   → <see cref="Homespun.Features.ClaudeCode.Hubs.ClaudeCodeHub"/> envelope broadcast</c>.
///
/// <para>
/// The ingestor is the single point where the append-before-broadcast invariant lives: the
/// on-disk write completes before any <see cref="Homespun.Shared.Models.Sessions.SessionEventEnvelope"/>
/// is delivered to clients. That invariant is what lets a refresh served mid-stream see every
/// event that any live client has already observed.
/// </para>
/// </summary>
public interface ISessionEventIngestor
{
    /// <summary>
    /// Ingest one A2A event received from the worker for the given session.
    ///
    /// <para>
    /// Appends the raw payload to the session's event log first, assigns a monotonic
    /// <c>seq</c> and stable <c>eventId</c>, translates to zero-or-more AG-UI events, and
    /// broadcasts an envelope per translated event to the session's SignalR group.
    /// </para>
    /// </summary>
    /// <param name="projectId">
    /// Project id used to scope the event log directory (<c>{baseDir}/{projectId}/</c>).
    /// </param>
    /// <param name="sessionId">Session id.</param>
    /// <param name="eventKind">
    /// The A2A SSE event kind string (<c>task</c>, <c>message</c>, <c>status-update</c>,
    /// <c>artifact-update</c>) — this is what the translator dispatches on.
    /// </param>
    /// <param name="payload">
    /// Raw A2A event payload. Stored verbatim; re-parsed through
    /// <see cref="A2AMessageParser"/> during translation.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task IngestAsync(
        string projectId,
        string sessionId,
        string eventKind,
        JsonElement payload,
        CancellationToken cancellationToken = default);
}
