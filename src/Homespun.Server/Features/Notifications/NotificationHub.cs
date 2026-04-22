using Homespun.Features.Gitgraph.Snapshots;
using Homespun.Shared.Models.Fleece;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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
    /// Broadcasts a topology-class issue change: invalidates the per-project
    /// task-graph snapshot, kicks the refresher fire-and-forget, then broadcasts
    /// <c>IssuesChanged</c>.
    /// <para>
    /// Invalidation precedes broadcast so the client's post-broadcast refetch
    /// cannot race a stale snapshot. The refresher kick is fire-and-forget so
    /// the HTTP response is not delayed by the ~3s rebuild.
    /// </para>
    /// <para>
    /// Snapshot store and refresher are resolved from <paramref name="services"/>
    /// and are tolerated as missing (e.g. when <c>TaskGraphSnapshot:Enabled=false</c>).
    /// </para>
    /// </summary>
    public static async Task BroadcastIssueTopologyChanged(
        this IHubContext<NotificationHub> hubContext,
        IServiceProvider services,
        string projectId,
        IssueChangeType changeType,
        string? issueId)
    {
        var snapshotStore = services.GetService<IProjectTaskGraphSnapshotStore>();
        var refresher = services.GetService<ITaskGraphSnapshotRefresher>();

        snapshotStore?.InvalidateProject(projectId);
        if (refresher is not null)
        {
            _ = Task.Run(() => refresher.RefreshOnceAsync(CancellationToken.None));
        }

        await hubContext.Clients.All.SendAsync("IssuesChanged", projectId, changeType, issueId);
        await hubContext.Clients.Group($"project-{projectId}")
            .SendAsync("IssuesChanged", projectId, changeType, issueId);
    }

    /// <summary>
    /// Broadcasts a structure-preserving field patch. Applies the patch in place
    /// via <c>IProjectTaskGraphSnapshotStore.PatchIssueFields</c> (no rebuild),
    /// then emits either <c>IssueFieldsPatched</c> (when
    /// <c>TaskGraphSnapshot:PatchPush:Enabled</c> is <c>true</c>, default) or
    /// <c>IssuesChanged</c> (fallback — clients invalidate + refetch). Snapshot
    /// patching happens in both cases.
    /// </summary>
    public static async Task BroadcastIssueFieldsPatched(
        this IHubContext<NotificationHub> hubContext,
        IServiceProvider services,
        string projectId,
        string issueId,
        IssueFieldPatch patch)
    {
        var snapshotStore = services.GetService<IProjectTaskGraphSnapshotStore>();
        snapshotStore?.PatchIssueFields(projectId, issueId, patch);

        var patchPushOptions = services.GetService<IOptionsMonitor<TaskGraphPatchPushOptions>>();
        var patchPushEnabled = patchPushOptions?.CurrentValue.Enabled ?? true;

        if (patchPushEnabled)
        {
            await hubContext.Clients.All.SendAsync("IssueFieldsPatched", projectId, issueId, patch);
            await hubContext.Clients.Group($"project-{projectId}")
                .SendAsync("IssueFieldsPatched", projectId, issueId, patch);
        }
        else
        {
            await hubContext.Clients.All.SendAsync("IssuesChanged", projectId, IssueChangeType.Updated, issueId);
            await hubContext.Clients.Group($"project-{projectId}")
                .SendAsync("IssuesChanged", projectId, IssueChangeType.Updated, issueId);
        }
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
