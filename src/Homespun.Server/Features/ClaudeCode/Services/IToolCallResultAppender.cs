namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Appends a synthetic <c>TOOL_CALL_RESULT</c> AG-UI event to a session's
/// event log when the user has answered an <c>ask_user_question</c> or
/// <c>propose_plan</c> interactive tool call.
///
/// <para>
/// The implementation builds a minimal A2A user <c>Message</c> carrying a
/// <c>tool_result</c> <c>DataPart</c> (the same wire shape that worker-emitted
/// tool results use) and feeds it to <see cref="ISessionEventIngestor"/>, so
/// <c>seq</c>, <c>eventId</c>, persistence, translation, and SignalR
/// broadcast all follow the existing A2A ingestion pipeline. Live and replay
/// therefore produce identical envelopes — the invariant the ingestor already
/// guarantees for worker-sourced events applies unchanged here.
/// </para>
/// </summary>
public interface IToolCallResultAppender
{
    /// <summary>
    /// Append a synthetic <c>TOOL_CALL_RESULT</c> for the given session.
    /// </summary>
    /// <param name="projectId">Project id used to scope the event log file.</param>
    /// <param name="sessionId">Session id.</param>
    /// <param name="toolCallId">
    /// The tool-call id assigned by the translator when the matching
    /// <c>TOOL_CALL_START</c> was emitted. When <c>null</c>, the call is a
    /// no-op — used by hub handlers that dequeue a registry slot that may have
    /// been cleared by a double-submit or server restart.
    /// </param>
    /// <param name="resultPayload">
    /// Arbitrary object serialised into the tool-result content. For questions
    /// this is the answers dictionary; for plans it is
    /// <c>{ approved, keepContext, feedback }</c>.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AppendAsync(
        string projectId,
        string sessionId,
        string? toolCallId,
        object resultPayload,
        CancellationToken cancellationToken = default);
}
