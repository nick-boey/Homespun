namespace Homespun.Features.Notifications;

/// <summary>
/// Service for managing application-wide notifications.
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Event raised when a notification is added.
    /// </summary>
    event Action<Notification>? OnNotificationAdded;

    /// <summary>
    /// Event raised when a notification is dismissed.
    /// </summary>
    event Action<string>? OnNotificationDismissed;

    /// <summary>
    /// Adds a notification to the active list.
    /// If the notification has a DeduplicationKey, any existing notification with the same key is replaced.
    /// </summary>
    void AddNotification(Notification notification);

    /// <summary>
    /// Dismisses a notification by ID.
    /// </summary>
    void DismissNotification(string notificationId);

    /// <summary>
    /// Dismisses all notifications with the specified deduplication key.
    /// </summary>
    void DismissNotificationsByKey(string deduplicationKey);

    /// <summary>
    /// Gets all active notifications, optionally filtered by project.
    /// </summary>
    IReadOnlyList<Notification> GetActiveNotifications(string? projectId = null);

    /// <summary>
    /// Checks if a notification with the given deduplication key exists.
    /// </summary>
    bool HasNotificationWithKey(string deduplicationKey);
}
