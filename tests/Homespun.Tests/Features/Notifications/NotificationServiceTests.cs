using Microsoft.Extensions.Logging.Abstractions;

namespace Homespun.Tests.Features.Notifications;

[TestFixture]
public class NotificationServiceTests
{
    private NotificationService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _service = new NotificationService(NullLogger<NotificationService>.Instance);
    }

    [Test]
    public void AddNotification_AddsToActiveList()
    {
        // Arrange
        var notification = new Notification
        {
            Type = NotificationType.Info,
            Title = "Test",
            Message = "Test message"
        };

        // Act
        _service.AddNotification(notification);

        // Assert
        var active = _service.GetActiveNotifications();
        Assert.That(active, Has.Count.EqualTo(1));
        Assert.That(active[0].Title, Is.EqualTo("Test"));
    }

    [Test]
    public void AddNotification_RaisesEvent()
    {
        // Arrange
        Notification? receivedNotification = null;
        _service.OnNotificationAdded += n => receivedNotification = n;

        var notification = new Notification
        {
            Type = NotificationType.Info,
            Title = "Test",
            Message = "Test message"
        };

        // Act
        _service.AddNotification(notification);

        // Assert
        Assert.That(receivedNotification, Is.Not.Null);
        Assert.That(receivedNotification!.Title, Is.EqualTo("Test"));
    }

    [Test]
    public void DismissNotification_RemovesFromList()
    {
        // Arrange
        var notification = new Notification
        {
            Type = NotificationType.Info,
            Title = "Test",
            Message = "Test message"
        };
        _service.AddNotification(notification);

        // Act
        _service.DismissNotification(notification.Id);

        // Assert
        var active = _service.GetActiveNotifications();
        Assert.That(active, Is.Empty);
    }

    [Test]
    public void DismissNotification_RaisesEvent()
    {
        // Arrange
        string? dismissedId = null;
        _service.OnNotificationDismissed += id => dismissedId = id;

        var notification = new Notification
        {
            Type = NotificationType.Info,
            Title = "Test",
            Message = "Test message"
        };
        _service.AddNotification(notification);

        // Act
        _service.DismissNotification(notification.Id);

        // Assert
        Assert.That(dismissedId, Is.EqualTo(notification.Id));
    }

    [Test]
    public void AddNotification_WithDeduplicationKey_ReplacesExisting()
    {
        // Arrange
        var notification1 = new Notification
        {
            Type = NotificationType.Info,
            Title = "First",
            Message = "First message",
            DeduplicationKey = "test-key"
        };
        var notification2 = new Notification
        {
            Type = NotificationType.Warning,
            Title = "Second",
            Message = "Second message",
            DeduplicationKey = "test-key"
        };

        // Act
        _service.AddNotification(notification1);
        _service.AddNotification(notification2);

        // Assert
        var active = _service.GetActiveNotifications();
        Assert.That(active, Has.Count.EqualTo(1));
        Assert.That(active[0].Title, Is.EqualTo("Second"));
    }

    [Test]
    public void GetActiveNotifications_FiltersByProjectId()
    {
        // Arrange
        var globalNotification = new Notification
        {
            Type = NotificationType.Info,
            Title = "Global",
            Message = "Global message"
        };
        var projectNotification = new Notification
        {
            Type = NotificationType.Info,
            Title = "Project",
            Message = "Project message",
            ProjectId = "project-1"
        };
        var otherProjectNotification = new Notification
        {
            Type = NotificationType.Info,
            Title = "Other Project",
            Message = "Other project message",
            ProjectId = "project-2"
        };

        _service.AddNotification(globalNotification);
        _service.AddNotification(projectNotification);
        _service.AddNotification(otherProjectNotification);

        // Act
        var project1Notifications = _service.GetActiveNotifications("project-1");

        // Assert - Should return global + project-1 notifications
        Assert.That(project1Notifications, Has.Count.EqualTo(2));
        Assert.That(project1Notifications.Any(n => n.Title == "Global"), Is.True);
        Assert.That(project1Notifications.Any(n => n.Title == "Project"), Is.True);
        Assert.That(project1Notifications.Any(n => n.Title == "Other Project"), Is.False);
    }

    [Test]
    public void DismissNotificationsByKey_RemovesAllMatchingNotifications()
    {
        // Arrange
        var notification1 = new Notification
        {
            Type = NotificationType.Info,
            Title = "First",
            Message = "First message",
            DeduplicationKey = "test-key"
        };
        var notification2 = new Notification
        {
            Type = NotificationType.Info,
            Title = "Other",
            Message = "Other message",
            DeduplicationKey = "other-key"
        };

        _service.AddNotification(notification1);
        _service.AddNotification(notification2);

        // Act
        _service.DismissNotificationsByKey("test-key");

        // Assert
        var active = _service.GetActiveNotifications();
        Assert.That(active, Has.Count.EqualTo(1));
        Assert.That(active[0].Title, Is.EqualTo("Other"));
    }

    [Test]
    public void HasNotificationWithKey_ReturnsTrueWhenExists()
    {
        // Arrange
        var notification = new Notification
        {
            Type = NotificationType.Info,
            Title = "Test",
            Message = "Test message",
            DeduplicationKey = "test-key"
        };
        _service.AddNotification(notification);

        // Act & Assert
        Assert.That(_service.HasNotificationWithKey("test-key"), Is.True);
        Assert.That(_service.HasNotificationWithKey("non-existent"), Is.False);
    }

    [Test]
    public void GetActiveNotifications_ReturnsInReverseChronologicalOrder()
    {
        // Arrange
        var notification1 = new Notification
        {
            Type = NotificationType.Info,
            Title = "First",
            Message = "First message"
        };
        
        // Small delay to ensure different CreatedAt times
        Thread.Sleep(10);
        
        var notification2 = new Notification
        {
            Type = NotificationType.Info,
            Title = "Second",
            Message = "Second message"
        };

        _service.AddNotification(notification1);
        _service.AddNotification(notification2);

        // Act
        var active = _service.GetActiveNotifications();

        // Assert - Most recent first
        Assert.That(active[0].Title, Is.EqualTo("Second"));
        Assert.That(active[1].Title, Is.EqualTo("First"));
    }
}
