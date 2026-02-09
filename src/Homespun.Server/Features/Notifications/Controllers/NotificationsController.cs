using Microsoft.AspNetCore.Mvc;

namespace Homespun.Features.Notifications.Controllers;

/// <summary>
/// API endpoints for managing notifications.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class NotificationsController(INotificationService notificationService) : ControllerBase
{
    /// <summary>
    /// Get all active notifications, optionally filtered by project.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<List<NotificationDto>>(StatusCodes.Status200OK)]
    public ActionResult<List<NotificationDto>> GetAll([FromQuery] string? projectId = null)
    {
        var notifications = notificationService.GetActiveNotifications(projectId);
        return Ok(notifications.Select(MapToDto).ToList());
    }

    /// <summary>
    /// Send a notification (for testing purposes).
    /// </summary>
    [HttpPost]
    [ProducesResponseType<NotificationDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<NotificationDto> Create([FromBody] CreateNotificationRequest request)
    {
        var notification = new Notification
        {
            Type = request.Type,
            Title = request.Title,
            Message = request.Message,
            ProjectId = request.ProjectId,
            ActionLabel = request.ActionLabel,
            IsDismissible = request.IsDismissible ?? true,
            DeduplicationKey = request.DeduplicationKey
        };

        notificationService.AddNotification(notification);

        return Created(string.Empty, MapToDto(notification));
    }

    /// <summary>
    /// Dismiss a notification by ID.
    /// </summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult Dismiss(string id)
    {
        notificationService.DismissNotification(id);
        return NoContent();
    }

    /// <summary>
    /// Dismiss all notifications with a deduplication key.
    /// </summary>
    [HttpDelete("by-key/{key}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult DismissByKey(string key)
    {
        notificationService.DismissNotificationsByKey(key);
        return NoContent();
    }

    private static NotificationDto MapToDto(Notification notification) => new()
    {
        Id = notification.Id,
        Type = notification.Type,
        Title = notification.Title,
        Message = notification.Message,
        ProjectId = notification.ProjectId,
        ActionLabel = notification.ActionLabel,
        CreatedAt = notification.CreatedAt,
        IsDismissible = notification.IsDismissible,
        DeduplicationKey = notification.DeduplicationKey
    };
}
