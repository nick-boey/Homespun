using Homespun.Shared.Models.Notifications;

namespace Homespun.Shared.Requests;

/// <summary>
/// Request model for creating a notification.
/// </summary>
public class CreateNotificationRequest
{
    /// <summary>
    /// Notification type.
    /// </summary>
    public NotificationType Type { get; set; } = NotificationType.Info;

    /// <summary>
    /// Notification title.
    /// </summary>
    public required string Title { get; set; }

    /// <summary>
    /// Notification message.
    /// </summary>
    public required string Message { get; set; }

    /// <summary>
    /// Optional project ID.
    /// </summary>
    public string? ProjectId { get; set; }

    /// <summary>
    /// Optional action button label.
    /// </summary>
    public string? ActionLabel { get; set; }

    /// <summary>
    /// Whether the notification is dismissible.
    /// </summary>
    public bool? IsDismissible { get; set; }

    /// <summary>
    /// Optional deduplication key.
    /// </summary>
    public string? DeduplicationKey { get; set; }
}
