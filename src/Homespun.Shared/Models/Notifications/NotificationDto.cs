namespace Homespun.Shared.Models.Notifications;

/// <summary>
/// Data transfer object for notifications sent to clients.
/// Does not include server-side Action callback.
/// </summary>
public class NotificationDto
{
    /// <summary>
    /// Unique identifier for this notification.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Type of notification (Info, Warning, ActionRequired).
    /// </summary>
    public required NotificationType Type { get; init; }

    /// <summary>
    /// Short title for the notification.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Detailed message for the notification.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Optional project ID this notification is associated with.
    /// </summary>
    public string? ProjectId { get; init; }

    /// <summary>
    /// Optional label for the action button. If null, no action button is shown.
    /// </summary>
    public string? ActionLabel { get; init; }

    /// <summary>
    /// When this notification was created.
    /// </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// Whether this notification can be dismissed by the user.
    /// </summary>
    public bool IsDismissible { get; init; } = true;

    /// <summary>
    /// Optional key to prevent duplicate notifications.
    /// If set, only one notification with this key can exist at a time.
    /// </summary>
    public string? DeduplicationKey { get; init; }
}
