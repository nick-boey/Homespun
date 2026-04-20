using System.Diagnostics;
using System.Text.Json;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.ClaudeCode.Settings;
using Homespun.Features.Observability;
using Homespun.Shared.Models.Sessions;
using Homespun.Shared.Requests;
using Microsoft.AspNetCore.SignalR;

namespace Homespun.Features.ClaudeCode.Hubs;

/// <summary>
/// SignalR hub for Claude Code session real-time communication.
///
/// <para>
/// Every client-facing hub method takes <c>string traceparent</c> as its
/// first parameter. The client's <c>traceInvoke</c> helper injects the W3C
/// traceparent of the active browser span; <see cref="TraceparentHubFilter"/>
/// intercepts the invocation, extracts arg0, and starts a server span
/// parented to the client's context. Hub method bodies themselves ignore
/// the traceparent — the filter owns the span lifecycle — but the parameter
/// must be present on the wire signature.
/// </para>
/// </summary>
public class ClaudeCodeHub(
    IClaudeSessionService sessionService,
    ILogger<ClaudeCodeHub> logger) : Hub
{
    private const string ConnectActivityKey = "Homespun.Signalr.ConnectActivity";

    public override Task OnConnectedAsync()
    {
        var activity = HomespunActivitySources.SignalrSource.StartActivity("homespun.signalr.connect", ActivityKind.Server);
        if (activity is not null)
        {
            activity.SetTag("signalr.connection.id", Context.ConnectionId);
            activity.AddEvent(new ActivityEvent(SessionEventSpanEvents.Connected));
            Context.Items[ConnectActivityKey] = activity;
        }
        _ = logger;
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items.TryGetValue(ConnectActivityKey, out var value) && value is Activity activity)
        {
            var disconnectEvent = new ActivityEvent(
                SessionEventSpanEvents.Disconnected,
                tags: exception is null
                    ? default
                    : new ActivityTagsCollection { ["reason"] = exception.Message });
            activity.AddEvent(disconnectEvent);
            if (exception is not null)
            {
                activity.SetStatus(ActivityStatusCode.Error, exception.Message);
                activity.AddException(exception);
            }
            activity.Stop();
            activity.Dispose();
            Context.Items.Remove(ConnectActivityKey);
        }
        return base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Join a session group to receive session-specific messages.
    /// </summary>
    public async Task JoinSession(string traceparent, string sessionId)
    {
        _ = traceparent;
        using var activity = HomespunActivitySources.SignalrSource.StartActivity("homespun.signalr.join", ActivityKind.Server);
        activity?.SetTag("homespun.session.id", sessionId);
        activity?.SetTag("signalr.connection.id", Context.ConnectionId);

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
    public async Task LeaveSession(string traceparent, string sessionId)
    {
        _ = traceparent;
        using var activity = HomespunActivitySources.SignalrSource.StartActivity("homespun.signalr.leave", ActivityKind.Server);
        activity?.SetTag("homespun.session.id", sessionId);
        activity?.SetTag("signalr.connection.id", Context.ConnectionId);

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"session-{sessionId}");
    }

    /// <summary>
    /// Send a message to a session.
    /// </summary>
    public async Task SendMessage(string traceparent, string sessionId, string message, SessionMode mode = SessionMode.Build)
    {
        _ = traceparent;
        await sessionService.SendMessageAsync(sessionId, message, mode);
    }

    /// <summary>
    /// Stop a session.
    /// </summary>
    public async Task StopSession(string traceparent, string sessionId)
    {
        _ = traceparent;
        await sessionService.StopSessionAsync(sessionId);
    }

    /// <summary>
    /// Interrupt a session's current execution without fully stopping it.
    /// The session remains alive so the user can send another message to resume.
    /// </summary>
    public async Task InterruptSession(string traceparent, string sessionId)
    {
        _ = traceparent;
        await sessionService.InterruptSessionAsync(sessionId);
    }

    /// <summary>
    /// Get all active sessions.
    /// </summary>
    public IReadOnlyList<ClaudeSession> GetAllSessions(string traceparent)
    {
        _ = traceparent;
        return sessionService.GetAllSessions();
    }

    /// <summary>
    /// Get sessions for a specific project.
    /// </summary>
    public IReadOnlyList<ClaudeSession> GetProjectSessions(string traceparent, string projectId)
    {
        _ = traceparent;
        return sessionService.GetSessionsForProject(projectId);
    }

    /// <summary>
    /// Get a specific session by ID.
    /// </summary>
    public ClaudeSession? GetSession(string traceparent, string sessionId)
    {
        _ = traceparent;
        return sessionService.GetSession(sessionId);
    }

    /// <summary>
    /// Answer a pending question in a session.
    /// </summary>
    /// <param name="traceparent">W3C traceparent from the client's active span.</param>
    /// <param name="sessionId">The session ID</param>
    /// <param name="answersJson">JSON string of dictionary mapping question text to answer text</param>
    public async Task AnswerQuestion(string traceparent, string sessionId, string answersJson)
    {
        _ = traceparent;
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
    public async Task ExecutePlan(string traceparent, string sessionId, bool clearContext = true)
    {
        _ = traceparent;
        await sessionService.ExecutePlanAsync(sessionId, clearContext);
    }

    /// <summary>
    /// Approve or reject a pending plan from ExitPlanMode.
    /// </summary>
    public async Task ApprovePlan(string traceparent, string sessionId, bool approved, bool keepContext, string? feedback = null)
    {
        _ = traceparent;
        await sessionService.ApprovePlanAsync(sessionId, approved, keepContext, feedback);
    }

    /// <summary>
    /// Checks the state of any existing container for a working directory.
    /// </summary>
    public async Task<AgentStartCheckResult> CheckCloneState(string traceparent, string workingDirectory)
    {
        _ = traceparent;
        return await sessionService.CheckCloneStateAsync(workingDirectory);
    }

    /// <summary>
    /// Starts a session after optionally terminating any existing session in the container.
    /// </summary>
    public async Task<ClaudeSession> StartSessionWithTermination(string traceparent, CreateSessionRequest request, bool terminateExisting)
    {
        _ = traceparent;
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
    /// Restart the container for a session and prepare for resumption.
    /// </summary>
    public async Task<ClaudeSession?> RestartSession(string traceparent, string sessionId)
    {
        _ = traceparent;
        return await sessionService.RestartSessionAsync(sessionId);
    }

    /// <summary>
    /// Sets the session mode without sending a new message.
    /// </summary>
    public async Task SetSessionMode(string traceparent, string sessionId, SessionMode mode)
    {
        _ = traceparent;
        try
        {
            await sessionService.SetSessionModeAsync(sessionId, mode);
        }
        catch (KeyNotFoundException)
        {
            throw new HubException("Session not found");
        }
        catch (Exception ex)
        {
            throw new HubException($"Failed to set session mode: {ex.Message}");
        }
    }

    /// <summary>
    /// Sets the session model without sending a new message.
    /// </summary>
    public async Task SetSessionModel(string traceparent, string sessionId, string model)
    {
        _ = traceparent;
        try
        {
            await sessionService.SetSessionModelAsync(sessionId, model);
        }
        catch (KeyNotFoundException)
        {
            throw new HubException("Session not found");
        }
        catch (Exception ex)
        {
            throw new HubException($"Failed to set session model: {ex.Message}");
        }
    }

    /// <summary>
    /// Clears context and starts a new session for the same entity/project.
    /// </summary>
    public async Task<ClaudeSession> ClearContextAndStartNew(string traceparent, string sessionId, string? initialPrompt = null)
    {
        _ = traceparent;
        try
        {
            return await sessionService.ClearContextAndStartNewAsync(sessionId, initialPrompt);
        }
        catch (KeyNotFoundException)
        {
            throw new HubException("Session not found");
        }
        catch (Exception ex)
        {
            throw new HubException($"Failed to clear context and start new session: {ex.Message}");
        }
    }
}

/// <summary>
/// Extension methods for broadcasting Claude Code events via SignalR.
/// Uses AG-UI events for message streaming.
/// </summary>
public static class ClaudeCodeHubExtensions
{
    #region Session Lifecycle Events

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
    /// Broadcasts session status change.
    /// </summary>
    public static async Task BroadcastSessionStatusChanged(
        this IHubContext<ClaudeCodeHub> hubContext,
        string sessionId,
        ClaudeSessionStatus status,
        bool hasPendingPlanApproval = false)
    {
        await hubContext.Clients.All.SendAsync("SessionStatusChanged", sessionId, status, hasPendingPlanApproval);
        await hubContext.Clients.Group($"session-{sessionId}").SendAsync("SessionStatusChanged", sessionId, status, hasPendingPlanApproval);
    }

    /// <summary>
    /// Broadcasts session status change with full session info.
    /// </summary>
    public static async Task BroadcastSessionStatusChanged(
        this IHubContext<ClaudeCodeHub> hubContext,
        string sessionId,
        ClaudeSession session)
    {
        await hubContext.Clients.All.SendAsync("SessionStatusChanged", sessionId, session.Status, session.HasPendingPlanApproval);
        await hubContext.Clients.All.SendAsync("SessionState", session);
        await hubContext.Clients.Group($"session-{sessionId}").SendAsync("SessionStatusChanged", sessionId, session.Status, session.HasPendingPlanApproval);
        await hubContext.Clients.Group($"session-{sessionId}").SendAsync("SessionState", session);
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
    /// Broadcasts when session context is cleared and a new session is started.
    /// </summary>
    public static async Task BroadcastSessionContextCleared(
        this IHubContext<ClaudeCodeHub> hubContext,
        string oldSessionId,
        ClaudeSession newSession)
    {
        // Notify old session group about the transition
        await hubContext.Clients.Group($"session-{oldSessionId}")
            .SendAsync("SessionContextCleared", oldSessionId, newSession);
        // Broadcast new session to all clients
        await hubContext.Clients.All.SendAsync("SessionStarted", newSession);
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
    /// Broadcasts when a session encounters an error.
    /// </summary>
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

    #endregion

    #region AG-UI Envelope Broadcast (single channel)

    /// <summary>
    /// Broadcasts a <see cref="SessionEventEnvelope"/> to the session group.
    /// This is the single broadcast channel for all AG-UI events — canonical AG-UI events
    /// and Homespun <c>Custom</c> variants alike flow through this one method.
    ///
    /// <para>
    /// Callers MUST persist the underlying A2A event (via <c>IA2AEventStore.AppendAsync</c>)
    /// BEFORE invoking this method so that a refresh served while the broadcast is in flight
    /// is guaranteed to see the event in its replay log. That ordering invariant is the
    /// reason we don't have a "broadcast then append" convenience overload.
    /// </para>
    /// </summary>
    public static async Task BroadcastSessionEvent(
        this IHubContext<ClaudeCodeHub> hubContext,
        string sessionId,
        SessionEventEnvelope envelope)
    {
        await hubContext.Clients.Group($"session-{sessionId}")
            .SendAsync(AGUIEventType.ReceiveSessionEvent, sessionId, envelope);
    }

    #endregion

    #region AG-UI Legacy Custom Event Broadcasting
    // BroadcastAGUICustomEvent remains as a fallback broadcast channel for server-initiated
    // custom events (e.g. context.cleared) that are not derived from an A2A event. All
    // A2A-derived traffic flows through BroadcastSessionEvent above.

    /// <summary>
    /// Broadcasts an AG-UI Custom event for application-specific events that do not originate
    /// from an A2A event (e.g. server-initiated context.cleared notifications).
    /// </summary>
    public static async Task BroadcastAGUICustomEvent(
        this IHubContext<ClaudeCodeHub> hubContext,
        string sessionId,
        CustomEvent evt)
    {
        await hubContext.Clients.Group($"session-{sessionId}")
            .SendAsync(AGUIEventType.Custom, evt);
    }

    #endregion
}
