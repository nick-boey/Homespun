using System.Text.Json;
using Homespun.ClaudeAgentSdk;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.ClaudeCode.Settings;
using Homespun.Shared.Requests;
using Microsoft.AspNetCore.SignalR;

namespace Homespun.Features.ClaudeCode.Hubs;

/// <summary>
/// SignalR hub for Claude Code session real-time communication.
/// </summary>
public class ClaudeCodeHub(IClaudeSessionService sessionService, IMessageCacheStore messageCacheStore) : Hub
{
    /// <summary>
    /// Join a session group to receive session-specific messages.
    /// </summary>
    public async Task JoinSession(string sessionId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"session-{sessionId}");

        // Send current session state to the joining client
        var session = sessionService.GetSession(sessionId);
        if (session != null)
        {
            await Clients.Caller.SendAsync("SessionState", session);
        }
    }

    /// <summary>
    /// Leave a session group.
    /// </summary>
    public async Task LeaveSession(string sessionId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"session-{sessionId}");
    }

    /// <summary>
    /// Send a message to a session.
    /// </summary>
    public async Task SendMessage(string sessionId, string message, PermissionMode permissionMode = PermissionMode.BypassPermissions)
    {
        await sessionService.SendMessageAsync(sessionId, message, permissionMode);
    }

    /// <summary>
    /// Stop a session.
    /// </summary>
    public async Task StopSession(string sessionId)
    {
        await sessionService.StopSessionAsync(sessionId);
    }

    /// <summary>
    /// Interrupt a session's current execution without fully stopping it.
    /// The session remains alive so the user can send another message to resume.
    /// </summary>
    public async Task InterruptSession(string sessionId)
    {
        await sessionService.InterruptSessionAsync(sessionId);
    }

    /// <summary>
    /// Get all active sessions.
    /// </summary>
    public IReadOnlyList<ClaudeSession> GetAllSessions()
    {
        return sessionService.GetAllSessions();
    }

    /// <summary>
    /// Get sessions for a specific project.
    /// </summary>
    public IReadOnlyList<ClaudeSession> GetProjectSessions(string projectId)
    {
        return sessionService.GetSessionsForProject(projectId);
    }

    /// <summary>
    /// Get a specific session by ID.
    /// </summary>
    public ClaudeSession? GetSession(string sessionId)
    {
        return sessionService.GetSession(sessionId);
    }

    /// <summary>
    /// Answer a pending question in a session.
    /// </summary>
    /// <param name="sessionId">The session ID</param>
    /// <param name="answersJson">JSON string of dictionary mapping question text to answer text</param>
    public async Task AnswerQuestion(string sessionId, string answersJson)
    {
        try
        {
            var answers = JsonSerializer.Deserialize<Dictionary<string, string>>(answersJson)
                ?? throw new ArgumentException("Invalid answers JSON");
            await sessionService.AnswerQuestionAsync(sessionId, answers);
        }
        catch (Exception ex)
        {
            throw new HubException($"Failed to answer question: {ex.Message}");
        }
    }

    /// <summary>
    /// Execute a plan by optionally clearing context and sending it as a message.
    /// </summary>
    /// <param name="sessionId">The session ID</param>
    /// <param name="clearContext">Whether to clear context before execution</param>
    public async Task ExecutePlan(string sessionId, bool clearContext = true)
    {
        await sessionService.ExecutePlanAsync(sessionId, clearContext);
    }

    /// <summary>
    /// Approve or reject a pending plan from ExitPlanMode.
    /// </summary>
    /// <param name="sessionId">The session ID</param>
    /// <param name="approved">Whether the plan is approved</param>
    /// <param name="keepContext">If approved, whether to keep existing context</param>
    /// <param name="feedback">User feedback when rejecting the plan</param>
    public async Task ApprovePlan(string sessionId, bool approved, bool keepContext, string? feedback = null)
    {
        await sessionService.ApprovePlanAsync(sessionId, approved, keepContext, feedback);
    }

    /// <summary>
    /// Checks the state of any existing container for a working directory.
    /// Returns information about what action should be taken before starting a new session.
    /// </summary>
    /// <param name="workingDirectory">The working directory (clone path)</param>
    public async Task<AgentStartCheckResult> CheckCloneState(string workingDirectory)
    {
        return await sessionService.CheckCloneStateAsync(workingDirectory);
    }

    /// <summary>
    /// Starts a session after optionally terminating any existing session in the container.
    /// </summary>
    public async Task<ClaudeSession> StartSessionWithTermination(CreateSessionRequest request, bool terminateExisting)
    {
        return await sessionService.StartSessionWithTerminationAsync(
            request.EntityId,
            request.ProjectId,
            request.WorkingDirectory,
            request.Mode,
            request.Model,
            terminateExisting,
            request.SystemPrompt);
    }

    /// <summary>
    /// Gets the number of cached messages for a session.
    /// Used by clients to determine if historical messages are available.
    /// </summary>
    /// <param name="sessionId">The session ID</param>
    /// <returns>The number of cached messages, or 0 if no cache exists</returns>
    public async Task<int> GetCachedMessageCount(string sessionId)
    {
        var summary = await messageCacheStore.GetSessionSummaryAsync(sessionId);
        return summary?.MessageCount ?? 0;
    }
}

/// <summary>
/// Extension methods for broadcasting Claude Code events via SignalR.
/// </summary>
public static class ClaudeCodeHubExtensions
{
    /// <summary>
    /// Broadcasts when a new session starts.
    /// </summary>
    public static async Task BroadcastSessionStarted(
        this IHubContext<ClaudeCodeHub> hubContext,
        ClaudeSession session)
    {
        await hubContext.Clients.All.SendAsync("SessionStarted", session);
    }

    /// <summary>
    /// Broadcasts when a session stops.
    /// </summary>
    public static async Task BroadcastSessionStopped(
        this IHubContext<ClaudeCodeHub> hubContext,
        string sessionId)
    {
        await hubContext.Clients.All.SendAsync("SessionStopped", sessionId);
        await hubContext.Clients.Group($"session-{sessionId}").SendAsync("SessionStopped", sessionId);
    }

    /// <summary>
    /// Broadcasts a message to a session group.
    /// </summary>
    public static async Task BroadcastMessageReceived(
        this IHubContext<ClaudeCodeHub> hubContext,
        string sessionId,
        ClaudeMessage message)
    {
        await hubContext.Clients.Group($"session-{sessionId}").SendAsync("MessageReceived", message);
    }

    /// <summary>
    /// Broadcasts a content block to a session group (for streaming).
    /// </summary>
    public static async Task BroadcastContentBlockReceived(
        this IHubContext<ClaudeCodeHub> hubContext,
        string sessionId,
        ClaudeMessageContent content)
    {
        await hubContext.Clients.Group($"session-{sessionId}").SendAsync("ContentBlockReceived", content);
    }

    /// <summary>
    /// Broadcasts session status change.
    /// </summary>
    public static async Task BroadcastSessionStatusChanged(
        this IHubContext<ClaudeCodeHub> hubContext,
        string sessionId,
        ClaudeSessionStatus status)
    {
        await hubContext.Clients.All.SendAsync("SessionStatusChanged", sessionId, status);
        await hubContext.Clients.Group($"session-{sessionId}").SendAsync("SessionStatusChanged", sessionId, status);
    }

    /// <summary>
    /// Broadcasts session mode and model change.
    /// </summary>
    public static async Task BroadcastSessionModeModelChanged(
        this IHubContext<ClaudeCodeHub> hubContext,
        string sessionId,
        SessionMode mode,
        string model)
    {
        await hubContext.Clients.All.SendAsync("SessionModeModelChanged", sessionId, mode, model);
        await hubContext.Clients.Group($"session-{sessionId}").SendAsync("SessionModeModelChanged", sessionId, mode, model);
    }

    /// <summary>
    /// Broadcasts session result (cost, duration, etc.).
    /// </summary>
    public static async Task BroadcastSessionResultReceived(
        this IHubContext<ClaudeCodeHub> hubContext,
        string sessionId,
        decimal totalCostUsd,
        long durationMs)
    {
        await hubContext.Clients.Group($"session-{sessionId}")
            .SendAsync("SessionResultReceived", sessionId, totalCostUsd, durationMs);
    }

    /// <summary>
    /// Broadcasts when a new streaming content block starts.
    /// </summary>
    public static async Task BroadcastStreamingContentStarted(
        this IHubContext<ClaudeCodeHub> hubContext,
        string sessionId,
        ClaudeMessageContent content,
        int index = -1)
    {
        await hubContext.Clients.Group($"session-{sessionId}")
            .SendAsync("StreamingContentStarted", content, index);
    }

    /// <summary>
    /// Broadcasts a streaming content delta (partial text update).
    /// </summary>
    public static async Task BroadcastStreamingContentDelta(
        this IHubContext<ClaudeCodeHub> hubContext,
        string sessionId,
        ClaudeMessageContent content,
        string delta,
        int index = -1)
    {
        await hubContext.Clients.Group($"session-{sessionId}")
            .SendAsync("StreamingContentDelta", content, delta, index);
    }

    /// <summary>
    /// Broadcasts when a streaming content block finishes.
    /// </summary>
    public static async Task BroadcastStreamingContentStopped(
        this IHubContext<ClaudeCodeHub> hubContext,
        string sessionId,
        ClaudeMessageContent content,
        int index = -1)
    {
        await hubContext.Clients.Group($"session-{sessionId}")
            .SendAsync("StreamingContentStopped", content, index);
    }

    /// <summary>
    /// Broadcasts when Claude asks a question that needs user input.
    /// </summary>
    public static async Task BroadcastQuestionReceived(
        this IHubContext<ClaudeCodeHub> hubContext,
        string sessionId,
        PendingQuestion question)
    {
        await hubContext.Clients.Group($"session-{sessionId}")
            .SendAsync("QuestionReceived", question);
    }

    /// <summary>
    /// Broadcasts when a plan is received and ready for user approval.
    /// </summary>
    public static async Task BroadcastPlanReceived(
        this IHubContext<ClaudeCodeHub> hubContext,
        string sessionId,
        string planContent,
        string? planFilePath)
    {
        await hubContext.Clients.Group($"session-{sessionId}")
            .SendAsync("PlanReceived", planContent, planFilePath);
    }

    /// <summary>
    /// Broadcasts when a question has been answered.
    /// </summary>
    public static async Task BroadcastQuestionAnswered(
        this IHubContext<ClaudeCodeHub> hubContext,
        string sessionId)
    {
        await hubContext.Clients.Group($"session-{sessionId}")
            .SendAsync("QuestionAnswered");
    }

    /// <summary>
    /// Broadcasts when a hook has been executed.
    /// </summary>
    public static async Task BroadcastHookExecuted(
        this IHubContext<ClaudeCodeHub> hubContext,
        string sessionId,
        HookExecutionResult result)
    {
        await hubContext.Clients.Group($"session-{sessionId}")
            .SendAsync("HookExecuted", result);
    }

    /// <summary>
    /// Broadcasts when context has been cleared for a session.
    /// </summary>
    public static async Task BroadcastContextCleared(
        this IHubContext<ClaudeCodeHub> hubContext,
        string sessionId)
    {
        await hubContext.Clients.Group($"session-{sessionId}")
            .SendAsync("ContextCleared", sessionId);
    }

    /// <summary>
    /// Broadcasts when a session encounters an error.
    /// </summary>
    /// <param name="hubContext">The hub context</param>
    /// <param name="sessionId">The session ID</param>
    /// <param name="errorMessage">User-friendly error message</param>
    /// <param name="errorSubtype">SDK error subtype (e.g., error_max_turns, error_during_execution)</param>
    /// <param name="isRecoverable">Whether the session can be resumed by sending another message</param>
    public static async Task BroadcastSessionError(
        this IHubContext<ClaudeCodeHub> hubContext,
        string sessionId,
        string errorMessage,
        string? errorSubtype,
        bool isRecoverable)
    {
        await hubContext.Clients.All.SendAsync("SessionError", sessionId, errorMessage, errorSubtype, isRecoverable);
        await hubContext.Clients.Group($"session-{sessionId}")
            .SendAsync("SessionError", sessionId, errorMessage, errorSubtype, isRecoverable);
    }

    #region AG-UI Event Broadcasting
    // These methods broadcast AG-UI protocol events alongside legacy events.
    // The AG-UI events are prefixed with "AGUI_" to distinguish them.

    /// <summary>
    /// Broadcasts an AG-UI RunStarted event when a new agent run begins.
    /// </summary>
    public static async Task BroadcastAGUIRunStarted(
        this IHubContext<ClaudeCodeHub> hubContext,
        string sessionId,
        Data.RunStartedEvent evt)
    {
        await hubContext.Clients.Group($"session-{sessionId}")
            .SendAsync(Data.AGUIEventType.RunStarted, evt);
    }

    /// <summary>
    /// Broadcasts an AG-UI RunFinished event when an agent run completes.
    /// </summary>
    public static async Task BroadcastAGUIRunFinished(
        this IHubContext<ClaudeCodeHub> hubContext,
        string sessionId,
        Data.RunFinishedEvent evt)
    {
        await hubContext.Clients.Group($"session-{sessionId}")
            .SendAsync(Data.AGUIEventType.RunFinished, evt);
    }

    /// <summary>
    /// Broadcasts an AG-UI RunError event when an agent run encounters an error.
    /// </summary>
    public static async Task BroadcastAGUIRunError(
        this IHubContext<ClaudeCodeHub> hubContext,
        string sessionId,
        Data.RunErrorEvent evt)
    {
        await hubContext.Clients.Group($"session-{sessionId}")
            .SendAsync(Data.AGUIEventType.RunError, evt);
    }

    /// <summary>
    /// Broadcasts an AG-UI TextMessageStart event when a new text message begins.
    /// </summary>
    public static async Task BroadcastAGUITextMessageStart(
        this IHubContext<ClaudeCodeHub> hubContext,
        string sessionId,
        Data.TextMessageStartEvent evt)
    {
        await hubContext.Clients.Group($"session-{sessionId}")
            .SendAsync(Data.AGUIEventType.TextMessageStart, evt);
    }

    /// <summary>
    /// Broadcasts an AG-UI TextMessageContent event for streaming text content.
    /// </summary>
    public static async Task BroadcastAGUITextMessageContent(
        this IHubContext<ClaudeCodeHub> hubContext,
        string sessionId,
        Data.TextMessageContentEvent evt)
    {
        await hubContext.Clients.Group($"session-{sessionId}")
            .SendAsync(Data.AGUIEventType.TextMessageContent, evt);
    }

    /// <summary>
    /// Broadcasts an AG-UI TextMessageEnd event when a text message finishes.
    /// </summary>
    public static async Task BroadcastAGUITextMessageEnd(
        this IHubContext<ClaudeCodeHub> hubContext,
        string sessionId,
        Data.TextMessageEndEvent evt)
    {
        await hubContext.Clients.Group($"session-{sessionId}")
            .SendAsync(Data.AGUIEventType.TextMessageEnd, evt);
    }

    /// <summary>
    /// Broadcasts an AG-UI ToolCallStart event when a tool call begins.
    /// </summary>
    public static async Task BroadcastAGUIToolCallStart(
        this IHubContext<ClaudeCodeHub> hubContext,
        string sessionId,
        Data.ToolCallStartEvent evt)
    {
        await hubContext.Clients.Group($"session-{sessionId}")
            .SendAsync(Data.AGUIEventType.ToolCallStart, evt);
    }

    /// <summary>
    /// Broadcasts an AG-UI ToolCallArgs event for streaming tool call arguments.
    /// </summary>
    public static async Task BroadcastAGUIToolCallArgs(
        this IHubContext<ClaudeCodeHub> hubContext,
        string sessionId,
        Data.ToolCallArgsEvent evt)
    {
        await hubContext.Clients.Group($"session-{sessionId}")
            .SendAsync(Data.AGUIEventType.ToolCallArgs, evt);
    }

    /// <summary>
    /// Broadcasts an AG-UI ToolCallEnd event when a tool call finishes.
    /// </summary>
    public static async Task BroadcastAGUIToolCallEnd(
        this IHubContext<ClaudeCodeHub> hubContext,
        string sessionId,
        Data.ToolCallEndEvent evt)
    {
        await hubContext.Clients.Group($"session-{sessionId}")
            .SendAsync(Data.AGUIEventType.ToolCallEnd, evt);
    }

    /// <summary>
    /// Broadcasts an AG-UI ToolCallResult event when a tool call result is available.
    /// </summary>
    public static async Task BroadcastAGUIToolCallResult(
        this IHubContext<ClaudeCodeHub> hubContext,
        string sessionId,
        Data.ToolCallResultEvent evt)
    {
        await hubContext.Clients.Group($"session-{sessionId}")
            .SendAsync(Data.AGUIEventType.ToolCallResult, evt);
    }

    /// <summary>
    /// Broadcasts an AG-UI Custom event for application-specific events.
    /// </summary>
    public static async Task BroadcastAGUICustomEvent(
        this IHubContext<ClaudeCodeHub> hubContext,
        string sessionId,
        Data.CustomEvent evt)
    {
        await hubContext.Clients.Group($"session-{sessionId}")
            .SendAsync(Data.AGUIEventType.Custom, evt);
    }

    /// <summary>
    /// Broadcasts any AG-UI event.
    /// </summary>
    public static async Task BroadcastAGUIEvent(
        this IHubContext<ClaudeCodeHub> hubContext,
        string sessionId,
        Data.AGUIBaseEvent evt)
    {
        var eventType = evt switch
        {
            Data.RunStartedEvent => Data.AGUIEventType.RunStarted,
            Data.RunFinishedEvent => Data.AGUIEventType.RunFinished,
            Data.RunErrorEvent => Data.AGUIEventType.RunError,
            Data.TextMessageStartEvent => Data.AGUIEventType.TextMessageStart,
            Data.TextMessageContentEvent => Data.AGUIEventType.TextMessageContent,
            Data.TextMessageEndEvent => Data.AGUIEventType.TextMessageEnd,
            Data.ToolCallStartEvent => Data.AGUIEventType.ToolCallStart,
            Data.ToolCallArgsEvent => Data.AGUIEventType.ToolCallArgs,
            Data.ToolCallEndEvent => Data.AGUIEventType.ToolCallEnd,
            Data.ToolCallResultEvent => Data.AGUIEventType.ToolCallResult,
            Data.CustomEvent => Data.AGUIEventType.Custom,
            Data.StateSnapshotEvent => Data.AGUIEventType.StateSnapshot,
            Data.StateDeltaEvent => Data.AGUIEventType.StateDelta,
            _ => "AGUI_Unknown"
        };

        await hubContext.Clients.Group($"session-{sessionId}")
            .SendAsync(eventType, evt);
    }

    #endregion
}
