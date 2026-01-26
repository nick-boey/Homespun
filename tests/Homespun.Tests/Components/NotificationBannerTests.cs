using Bunit;
using Homespun.Components.Shared;
using Homespun.Features.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Homespun.Tests.Components;

/// <summary>
/// bUnit tests for the NotificationBanner component.
/// </summary>
[TestFixture]
public class NotificationBannerTests : BunitTestContext
{
    private Mock<INotificationService> _mockNotificationService = null!;

    [SetUp]
    public new void Setup()
    {
        base.Setup();
        _mockNotificationService = new Mock<INotificationService>();
        Services.AddSingleton(_mockNotificationService.Object);
    }

    [Test]
    public void NotificationBanner_RendersEmpty_WhenNoNotifications()
    {
        // Arrange
        _mockNotificationService
            .Setup(s => s.GetActiveNotifications(null))
            .Returns(new List<Notification>());

        // Act
        var cut = Render<NotificationBanner>();

        // Assert
        Assert.That(cut.Markup, Is.Empty.Or.EqualTo(""));
    }

    [Test]
    public void NotificationBanner_DisplaysNotification_WhenExists()
    {
        // Arrange
        var notifications = new List<Notification>
        {
            new()
            {
                Id = "n1",
                Type = NotificationType.Info,
                Title = "Test Title",
                Message = "Test Message"
            }
        };
        _mockNotificationService
            .Setup(s => s.GetActiveNotifications(null))
            .Returns(notifications);

        // Act
        var cut = Render<NotificationBanner>();

        // Assert
        Assert.That(cut.Find(".notification-title").TextContent, Is.EqualTo("Test Title"));
        Assert.That(cut.Find(".notification-message").TextContent, Is.EqualTo("Test Message"));
    }

    [Test]
    public void NotificationBanner_AppliesCorrectClass_ForInfoType()
    {
        // Arrange
        var notifications = new List<Notification>
        {
            new()
            {
                Id = "n1",
                Type = NotificationType.Info,
                Title = "Info",
                Message = "Message"
            }
        };
        _mockNotificationService
            .Setup(s => s.GetActiveNotifications(null))
            .Returns(notifications);

        // Act
        var cut = Render<NotificationBanner>();

        // Assert
        Assert.That(cut.Find(".notification-banner").ClassList, Does.Contain("notification-info"));
    }

    [Test]
    public void NotificationBanner_AppliesCorrectClass_ForWarningType()
    {
        // Arrange
        var notifications = new List<Notification>
        {
            new()
            {
                Id = "n1",
                Type = NotificationType.Warning,
                Title = "Warning",
                Message = "Message"
            }
        };
        _mockNotificationService
            .Setup(s => s.GetActiveNotifications(null))
            .Returns(notifications);

        // Act
        var cut = Render<NotificationBanner>();

        // Assert
        Assert.That(cut.Find(".notification-banner").ClassList, Does.Contain("notification-warning"));
    }

    [Test]
    public void NotificationBanner_ShowsDismissButton_WhenDismissible()
    {
        // Arrange
        var notifications = new List<Notification>
        {
            new()
            {
                Id = "n1",
                Type = NotificationType.Info,
                Title = "Title",
                Message = "Message",
                IsDismissible = true
            }
        };
        _mockNotificationService
            .Setup(s => s.GetActiveNotifications(null))
            .Returns(notifications);

        // Act
        var cut = Render<NotificationBanner>();

        // Assert
        Assert.That(cut.FindAll(".notification-dismiss"), Has.Count.EqualTo(1));
    }

    [Test]
    public void NotificationBanner_CallsDismiss_WhenDismissButtonClicked()
    {
        // Arrange
        var notifications = new List<Notification>
        {
            new()
            {
                Id = "test-id",
                Type = NotificationType.Info,
                Title = "Title",
                Message = "Message",
                IsDismissible = true
            }
        };
        _mockNotificationService
            .Setup(s => s.GetActiveNotifications(null))
            .Returns(notifications);

        var cut = Render<NotificationBanner>();

        // Act
        cut.Find(".notification-dismiss").Click();

        // Assert
        _mockNotificationService.Verify(s => s.DismissNotification("test-id"), Times.Once);
    }

    [Test]
    public void NotificationBanner_ShowsActionButton_WhenActionProvided()
    {
        // Arrange
        var notifications = new List<Notification>
        {
            new()
            {
                Id = "n1",
                Type = NotificationType.Info,
                Title = "Title",
                Message = "Message",
                ActionLabel = "Click Me",
                Action = () => Task.CompletedTask
            }
        };
        _mockNotificationService
            .Setup(s => s.GetActiveNotifications(null))
            .Returns(notifications);

        // Act
        var cut = Render<NotificationBanner>();

        // Assert
        var actionButton = cut.Find(".notification-action-btn");
        Assert.That(actionButton.TextContent.Trim(), Is.EqualTo("Click Me"));
    }

    [Test]
    public void NotificationBanner_DisplaysMultipleNotifications()
    {
        // Arrange
        var notifications = new List<Notification>
        {
            new() { Id = "n1", Type = NotificationType.Info, Title = "First", Message = "Message 1" },
            new() { Id = "n2", Type = NotificationType.Warning, Title = "Second", Message = "Message 2" }
        };
        _mockNotificationService
            .Setup(s => s.GetActiveNotifications(null))
            .Returns(notifications);

        // Act
        var cut = Render<NotificationBanner>();

        // Assert
        var banners = cut.FindAll(".notification-banner");
        Assert.That(banners, Has.Count.EqualTo(2));
    }

    [Test]
    public void NotificationBanner_FiltersNotificationsByProjectId()
    {
        // Arrange
        _mockNotificationService
            .Setup(s => s.GetActiveNotifications("proj1"))
            .Returns(new List<Notification>
            {
                new() { Id = "n1", Type = NotificationType.Info, Title = "Project Notification", Message = "Message" }
            });

        // Act
        var cut = Render<NotificationBanner>(parameters =>
            parameters.Add(p => p.ProjectId, "proj1"));

        // Assert - method is called in OnInitialized and OnParametersSet
        _mockNotificationService.Verify(s => s.GetActiveNotifications("proj1"), Times.AtLeastOnce);
    }
}
