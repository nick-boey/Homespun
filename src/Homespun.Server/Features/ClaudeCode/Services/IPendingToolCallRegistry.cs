namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Per-session holding slot for the tool-call id assigned by
/// <see cref="A2AToAGUITranslator"/> when it translates an <c>input-required</c>
/// A2A <c>StatusUpdate</c> into a <c>TOOL_CALL_START/ARGS/END</c> sequence.
///
/// <para>
/// The translator generates the id; the hub handler later reads it back via
/// <see cref="Dequeue"/> so the synthesised <c>TOOL_CALL_RESULT</c> can carry
/// the same id as the original start. At most one interactive tool call is in
/// flight per session — the worker will not emit a second <c>input-required</c>
/// before the first is resolved — so the registry keeps a single slot per
/// session rather than a queue.
/// </para>
///
/// <para>
/// The slot is in-process state only. A server restart discards any pending
/// entries; the hub handler treats a missing id as a no-op (logs + returns)
/// and the orphaned <c>TOOL_CALL_START</c> simply never completes. That is
/// consistent with the existing retry behaviour on the worker HTTP resolve
/// path.
/// </para>
/// </summary>
public interface IPendingToolCallRegistry
{
    /// <summary>
    /// Stores <paramref name="toolCallId"/> as the pending id for
    /// <paramref name="sessionId"/>. If another id is already pending for the
    /// session, it is overwritten (last-write-wins). The older id becomes an
    /// orphaned <c>TOOL_CALL_START</c>; acceptable because the worker is
    /// expected to emit at most one pending interactive tool per session and a
    /// second emission signals that the client's prior interaction was
    /// cancelled upstream.
    /// </summary>
    void Register(string sessionId, string toolCallId);

    /// <summary>
    /// Atomically reads and clears the pending tool-call id for the session.
    /// Returns <c>null</c> when no id is pending.
    /// </summary>
    string? Dequeue(string sessionId);
}
