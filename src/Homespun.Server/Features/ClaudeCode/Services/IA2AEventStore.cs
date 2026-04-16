using System.Text.Json;
using Homespun.Shared.Models.Sessions;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Append-only per-session event store for raw A2A events.
///
/// <para>
/// Every A2A event received from the worker is appended to a JSONL file at
/// <c>{baseDir}/{projectId}/{sessionId}.events.jsonl</c>, assigned a strictly monotonic
/// per-session <see cref="A2AEventRecord.Seq"/> starting at 1 and a stable
/// <see cref="A2AEventRecord.EventId"/> UUID. The store is the server's ingestion-side
/// persistence boundary; downstream translation to AG-UI happens separately.
/// </para>
///
/// <para>
/// <see cref="AppendAsync"/> serializes per-session appends so seq assignment is monotonic
/// even under concurrent writes. <see cref="ReadAsync"/> is used by both the replay
/// endpoint and any internal consumers that need to reconstruct session state.
/// </para>
/// </summary>
public interface IA2AEventStore
{
    /// <summary>
    /// Append an A2A event to a session's on-disk log and return the persisted record
    /// (including the assigned <see cref="A2AEventRecord.Seq"/>,
    /// <see cref="A2AEventRecord.EventId"/>, and <see cref="A2AEventRecord.ReceivedAt"/>).
    /// </summary>
    /// <param name="projectId">Project id used to scope the on-disk path.</param>
    /// <param name="sessionId">Session id used to identify the event log file.</param>
    /// <param name="eventKind">The A2A SSE event kind string (e.g. <c>task</c>, <c>message</c>).</param>
    /// <param name="payload">The raw A2A event payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The persisted record.</returns>
    Task<A2AEventRecord> AppendAsync(
        string projectId,
        string sessionId,
        string eventKind,
        JsonElement payload,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Read events for a session.
    /// </summary>
    /// <param name="sessionId">Session id.</param>
    /// <param name="since">
    /// If provided, only return events with <c>Seq &gt; since</c> (incremental replay).
    /// If null, return all events from seq 1.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The events in ascending seq order, or <c>null</c> if the session has no event log
    /// on disk (distinguishable from an empty-but-exists session).
    /// </returns>
    Task<IReadOnlyList<A2AEventRecord>?> ReadAsync(
        string sessionId,
        long? since = null,
        CancellationToken cancellationToken = default);
}
