using Homespun.Shared.Models.Sessions;

namespace Homespun.Shared.Hubs;

/// <summary>
/// Defines server-to-client SignalR messages for the Claude Code hub.
/// Uses AG-UI events for message streaming.
/// </summary>
public interface IClaudeCodeHubClient
{
    // Session lifecycle events
    Task SessionStarted(ClaudeSession session);
    Task SessionStopped(string sessionId);
    Task SessionState(ClaudeSession session);
Task SessionStatusChanged(string sessionId, ClaudeSessionStatus status, bool hasPendingPlanApproval = false);

    /// <summary>
    /// Notifies clients when a session's mode or model changes.
    /// </summary>
    /// <param name="sessionId">The session ID</param>
    /// <param name="mode">The new session mode (Plan or Build)</param>
    /// <param name="model">The current model being used</param>
    Task SessionModeModelChanged(string sessionId, SessionMode mode, string model);

    Task SessionResultReceived(string sessionId, decimal totalCostUsd, long durationMs);
    Task ContextCleared(string sessionId);

    /// <summary>
    /// Notifies clients when a session encounters an error.
    /// </summary>
    /// <param name="sessionId">The session ID</param>
    /// <param name="errorMessage">User-friendly error message</param>
    /// <param name="errorSubtype">SDK error subtype (e.g., error_max_turns, error_during_execution)</param>
    /// <param name="isRecoverable">Whether the session can be resumed by sending another message</param>
    Task SessionError(string sessionId, string errorMessage, string? errorSubtype, bool isRecoverable);

    /// <summary>
    /// Notifies clients when a session's container is being restarted.
    /// </summary>
    /// <param name="sessionId">The session ID</param>
    Task SessionContainerRestarting(string sessionId);

    /// <summary>
    /// Notifies clients when a session's container has been restarted.
    /// </summary>
    /// <param name="sessionId">The session ID</param>
    /// <param name="session">The updated session with new container info</param>
    Task SessionContainerRestarted(string sessionId, ClaudeSession session);

    /// <summary>
    /// Delivers a session event (AG-UI envelope) to clients. This is the single broadcast
    /// channel for all AG-UI events — canonical AG-UI events and Homespun-specific
    /// <c>Custom</c> events alike flow through this one method wrapped in
    /// <see cref="SessionEventEnvelope"/>.
    /// </summary>
    /// <param name="sessionId">The session id the envelope belongs to.</param>
    /// <param name="envelope">The envelope carrying <c>seq</c>, <c>eventId</c>, and the AG-UI event payload.</param>
    Task ReceiveSessionEvent(string sessionId, SessionEventEnvelope envelope);

    /// <summary>
    /// Sends a custom AG-UI event to clients. Retained as a fallback channel for server-initiated
    /// custom events that do not originate from an A2A event (e.g. context.cleared). All
    /// A2A-derived traffic flows through <see cref="ReceiveSessionEvent"/> as a
    /// <see cref="SessionEventEnvelope"/>.
    /// </summary>
    Task AGUICustomEvent(CustomEvent evt);
}
