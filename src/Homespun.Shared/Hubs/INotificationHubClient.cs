using Homespun.Shared.Models.Fleece;
using Homespun.Shared.Models.Notifications;

namespace Homespun.Shared.Hubs;

/// <summary>
/// Defines server-to-client SignalR messages for the Notification hub.
/// </summary>
public interface INotificationHubClient
{
    Task NotificationAdded(NotificationDto notification);
    Task NotificationDismissed(string notificationId);

    /// <summary>
    /// Notifies clients when issues are changed (created, updated, deleted) in a project.
    /// </summary>
    Task IssuesChanged(string projectId, IssueChangeType changeType, string issueId);
}
