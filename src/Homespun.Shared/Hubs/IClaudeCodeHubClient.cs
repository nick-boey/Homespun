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

    #region AG-UI Events

    /// <summary>
    /// Notifies clients when an agent run starts.
    /// </summary>
    Task AGUIRunStarted(RunStartedEvent evt);

    /// <summary>
    /// Notifies clients when an agent run finishes successfully.
    /// </summary>
    Task AGUIRunFinished(RunFinishedEvent evt);

    /// <summary>
    /// Notifies clients when an agent run encounters an error.
    /// </summary>
    Task AGUIRunError(RunErrorEvent evt);

    /// <summary>
    /// Notifies clients when a text message starts streaming.
    /// </summary>
    Task AGUITextMessageStart(TextMessageStartEvent evt);

    /// <summary>
    /// Notifies clients with streaming text content.
    /// </summary>
    Task AGUITextMessageContent(TextMessageContentEvent evt);

    /// <summary>
    /// Notifies clients when a text message finishes streaming.
    /// </summary>
    Task AGUITextMessageEnd(TextMessageEndEvent evt);

    /// <summary>
    /// Notifies clients when a tool call starts.
    /// </summary>
    Task AGUIToolCallStart(ToolCallStartEvent evt);

    /// <summary>
    /// Notifies clients with streaming tool call arguments.
    /// </summary>
    Task AGUIToolCallArgs(ToolCallArgsEvent evt);

    /// <summary>
    /// Notifies clients when a tool call finishes.
    /// </summary>
    Task AGUIToolCallEnd(ToolCallEndEvent evt);

    /// <summary>
    /// Notifies clients when a tool call result is available.
    /// </summary>
    Task AGUIToolCallResult(ToolCallResultEvent evt);

    /// <summary>
    /// Sends a full state snapshot to clients.
    /// </summary>
    Task AGUIStateSnapshot(StateSnapshotEvent evt);

    /// <summary>
    /// Sends an incremental state delta to clients.
    /// </summary>
    Task AGUIStateDelta(StateDeltaEvent evt);

    /// <summary>
    /// Sends a custom AG-UI event to clients.
    /// </summary>
    Task AGUICustomEvent(CustomEvent evt);

    #endregion
}
