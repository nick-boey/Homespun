using Homespun.Shared.Models.Fleece;
using Microsoft.AspNetCore.SignalR;

namespace Homespun.Features.Notifications;

/// <summary>
/// SignalR hub for real-time notification delivery.
/// </summary>
public class NotificationHub(INotificationService notificationService) : Hub
{
    /// <summary>
    /// Join a project group to receive project-specific notifications.
    /// </summary>
    public async Task JoinProjectGroup(string projectId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"project-{projectId}");
    }

    /// <summary>
    /// Leave a project group.
    /// </summary>
    public async Task LeaveProjectGroup(string projectId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"project-{projectId}");
    }

    /// <summary>
    /// Get all active notifications for a project.
    /// </summary>
    public IReadOnlyList<Notification> GetActiveNotifications(string? projectId = null)
    {
        return notificationService.GetActiveNotifications(projectId);
    }

    /// <summary>
    /// Dismiss a notification.
    /// </summary>
    public async Task DismissNotification(string notificationId)
    {
        notificationService.DismissNotification(notificationId);
        await Clients.All.SendAsync("NotificationDismissed", notificationId);
    }
}

/// <summary>
/// Extension methods for broadcasting notifications via SignalR.
/// </summary>
public static class NotificationHubExtensions
{
    /// <summary>
    /// Broadcasts a new notification to all connected clients.
    /// </summary>
    public static async Task BroadcastNotificationAdded(
        this IHubContext<NotificationHub> hubContext,
        Notification notification)
    {
        // Send to all clients
        await hubContext.Clients.All.SendAsync("NotificationAdded", notification);

        // Also send to project-specific group if applicable
        if (!string.IsNullOrEmpty(notification.ProjectId))
        {
            await hubContext.Clients.Group($"project-{notification.ProjectId}")
                .SendAsync("NotificationAdded", notification);
        }
    }

    /// <summary>
    /// Broadcasts a notification dismissal to all connected clients.
    /// </summary>
    public static async Task BroadcastNotificationDismissed(
        this IHubContext<NotificationHub> hubContext,
        string notificationId)
    {
        await hubContext.Clients.All.SendAsync("NotificationDismissed", notificationId);
    }

    /// <summary>
    /// Broadcasts a unified per-issue mutation event. Sends a single
    /// <c>IssueChanged</c> message to every client and to the project group,
    /// carrying the canonical post-mutation issue body for create and update
    /// kinds; <paramref name="issue"/> is <c>null</c> for delete.
    /// </summary>
    /// <remarks>
    /// <paramref name="issueId"/> may be <c>null</c> for bulk events (clone /
    /// fleece-sync flows) — the client treats a null id as "invalidate every
    /// issue cache for this project". The helper does no snapshot bookkeeping;
    /// client-side layout makes server-side snapshots redundant.
    /// </remarks>
    public static async Task BroadcastIssueChanged(
        this IHubContext<NotificationHub> hubContext,
        string projectId,
        IssueChangeType kind,
        string? issueId,
        IssueResponse? issue)
    {
        await hubContext.Clients.All.SendAsync("IssueChanged", projectId, kind, issueId, issue);
        await hubContext.Clients.Group($"project-{projectId}")
            .SendAsync("IssueChanged", projectId, kind, issueId, issue);
    }

    /// <summary>
    /// Broadcasts when an agent is starting for an issue.
    /// </summary>
    public static async Task BroadcastAgentStarting(
        this IHubContext<NotificationHub> hubContext,
        string issueId,
        string projectId,
        string branchName)
    {
        // Send to all clients
        await hubContext.Clients.All.SendAsync("AgentStarting", issueId, projectId, branchName);

        // Also send to project-specific group
        await hubContext.Clients.Group($"project-{projectId}")
            .SendAsync("AgentStarting", issueId, projectId, branchName);
    }

    /// <summary>
    /// Broadcasts when agent startup fails for an issue.
    /// </summary>
    public static async Task BroadcastAgentStartFailed(
        this IHubContext<NotificationHub> hubContext,
        string issueId,
        string projectId,
        string error)
    {
        // Send to all clients
        await hubContext.Clients.All.SendAsync("AgentStartFailed", issueId, projectId, error);

        // Also send to project-specific group
        await hubContext.Clients.Group($"project-{projectId}")
            .SendAsync("AgentStartFailed", issueId, projectId, error);
    }
}
