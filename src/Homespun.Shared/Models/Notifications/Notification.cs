namespace Homespun.Shared.Models.Notifications;

/// <summary>
/// Represents an application notification displayed to the user.
/// Server-side only - contains Action callback not suitable for serialization.
/// </summary>
public class Notification
{
    /// <summary>
    /// Unique identifier for this notification.
    /// </summary>
    public string Id { get; init; } = Guid.NewGuid().ToString();

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
    /// Optional callback to execute when the action button is clicked.
    /// </summary>
    public Func<Task>? Action { get; init; }

    /// <summary>
    /// When this notification was created.
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

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
