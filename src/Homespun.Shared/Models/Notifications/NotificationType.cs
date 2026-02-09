namespace Homespun.Shared.Models.Notifications;

/// <summary>
/// Type of notification that determines styling and urgency.
/// </summary>
public enum NotificationType
{
    /// <summary>
    /// Informational notification (blue styling).
    /// </summary>
    Info,

    /// <summary>
    /// Warning notification requiring attention (yellow styling).
    /// </summary>
    Warning,

    /// <summary>
    /// Action required notification (red/orange styling).
    /// </summary>
    ActionRequired
}
