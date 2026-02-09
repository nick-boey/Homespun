using Homespun.Shared.Models.Notifications;

namespace Homespun.Shared.Hubs;

/// <summary>
/// Defines client-to-server SignalR messages for the Notification hub.
/// </summary>
public interface INotificationHub
{
    Task JoinProjectGroup(string projectId);
    Task LeaveProjectGroup(string projectId);
    IReadOnlyList<NotificationDto> GetActiveNotifications(string? projectId = null);
    Task DismissNotification(string notificationId);
}
