using System.Net;
using System.Net.Http.Json;
using Homespun.Features.Notifications;
using Homespun.Features.Notifications.Controllers;

namespace Homespun.Api.Tests;

/// <summary>
/// Integration tests for the Notifications API endpoints.
/// </summary>
[TestFixture]
public class NotificationsApiTests
{
    private HomespunWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    [SetUp]
    public void SetUp()
    {
        _factory = new HomespunWebApplicationFactory();
        _client = _factory.CreateClient();
    }

    [TearDown]
    public void TearDown()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Test]
    public async Task GetNotifications_ReturnsEmptyList_WhenNoNotifications()
    {
        // Act
        var response = await _client.GetAsync("/api/notifications");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var notifications = await response.Content.ReadFromJsonAsync<List<NotificationDto>>();
        Assert.That(notifications, Is.Not.Null);
        Assert.That(notifications, Is.Empty);
    }

    [Test]
    public async Task CreateNotification_ReturnsCreated_WhenValid()
    {
        // Arrange
        var createRequest = new CreateNotificationRequest
        {
            Type = NotificationType.Info,
            Title = "Test Notification",
            Message = "This is a test message"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/notifications", createRequest);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var result = await response.Content.ReadFromJsonAsync<NotificationDto>();
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Title, Is.EqualTo("Test Notification"));
        Assert.That(result.Message, Is.EqualTo("This is a test message"));
        Assert.That(result.Type, Is.EqualTo(NotificationType.Info));
    }

    [Test]
    public async Task CreateNotification_AddsToActiveList()
    {
        // Arrange
        var createRequest = new CreateNotificationRequest
        {
            Title = "Test",
            Message = "Test message"
        };

        // Act
        await _client.PostAsJsonAsync("/api/notifications", createRequest);
        var response = await _client.GetAsync("/api/notifications");

        // Assert
        var notifications = await response.Content.ReadFromJsonAsync<List<NotificationDto>>();
        Assert.That(notifications, Is.Not.Null);
        Assert.That(notifications, Has.Count.EqualTo(1));
        Assert.That(notifications[0].Title, Is.EqualTo("Test"));
    }

    [Test]
    public async Task DismissNotification_RemovesFromList()
    {
        // Arrange
        var createRequest = new CreateNotificationRequest
        {
            Title = "Test",
            Message = "Test message"
        };
        var createResponse = await _client.PostAsJsonAsync("/api/notifications", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<NotificationDto>();

        // Act
        var dismissResponse = await _client.DeleteAsync($"/api/notifications/{created!.Id}");

        // Assert
        Assert.That(dismissResponse.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var getResponse = await _client.GetAsync("/api/notifications");
        var notifications = await getResponse.Content.ReadFromJsonAsync<List<NotificationDto>>();
        Assert.That(notifications, Is.Not.Null);
        Assert.That(notifications, Is.Empty);
    }

    [Test]
    public async Task DismissByKey_RemovesMatchingNotifications()
    {
        // Arrange
        var createRequest1 = new CreateNotificationRequest
        {
            Title = "Test1",
            Message = "Message1",
            DeduplicationKey = "test-key"
        };
        var createRequest2 = new CreateNotificationRequest
        {
            Title = "Test2",
            Message = "Message2",
            DeduplicationKey = "other-key"
        };
        await _client.PostAsJsonAsync("/api/notifications", createRequest1);
        await _client.PostAsJsonAsync("/api/notifications", createRequest2);

        // Act
        var dismissResponse = await _client.DeleteAsync("/api/notifications/by-key/test-key");

        // Assert
        Assert.That(dismissResponse.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var getResponse = await _client.GetAsync("/api/notifications");
        var notifications = await getResponse.Content.ReadFromJsonAsync<List<NotificationDto>>();
        Assert.That(notifications, Is.Not.Null);
        Assert.That(notifications, Has.Count.EqualTo(1));
        Assert.That(notifications[0].Title, Is.EqualTo("Test2"));
    }
}
