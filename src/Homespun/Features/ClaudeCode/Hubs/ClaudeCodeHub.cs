using System.Text.Json;
using Homespun.ClaudeAgentSdk;
using Homespun.Features.ClaudeCode.Data;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.ClaudeCode.Settings;
using Microsoft.AspNetCore.SignalR;

namespace Homespun.Features.ClaudeCode.Hubs;

/// <summary>
/// SignalR hub for Claude Code session real-time communication.
/// </summary>
public class ClaudeCodeHub(IClaudeSessionService sessionService) : Hub
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
}
