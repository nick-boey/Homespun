namespace Homespun.Features.ClaudeCode.Exceptions;

/// <summary>
/// Thrown by <c>SingleContainerAgentExecutionService</c> when a second
/// concurrent session start is attempted while an active session already owns
/// the single dev-only worker. Carries both session ids so the caller can
/// surface a user-visible error and emit a diagnostic log line without
/// further state lookups.
/// </summary>
public sealed class SingleContainerBusyException : InvalidOperationException
{
    public string RequestedSessionId { get; }
    public string CurrentSessionId { get; }

    public SingleContainerBusyException(string requestedSessionId, string currentSessionId)
        : base($"SingleContainer worker is busy: requested session {requestedSessionId} but session {currentSessionId} is already active.")
    {
        RequestedSessionId = requestedSessionId;
        CurrentSessionId = currentSessionId;
    }
}
