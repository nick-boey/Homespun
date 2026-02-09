using Homespun.Client.Services;
using Homespun.Shared.Hubs;
using Homespun.Shared.Models.Notifications;
using Microsoft.AspNetCore.Components;

namespace Homespun.Tests.Features.SignalR;

[TestFixture]
public class NotificationSignalRServiceTests
{
    private NotificationSignalRService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _service = new NotificationSignalRService(new TestNavigationManager());
    }

    [TearDown]
    public async Task TearDown()
    {
        await _service.DisposeAsync();
    }

    [Test]
    public void Constructor_IsConnectedIsFalse()
    {
        Assert.That(_service.IsConnected, Is.False);
    }

    [Test]
    public async Task DisposeAsync_WhenNotConnected_DoesNotThrow()
    {
        await _service.DisposeAsync();
        Assert.That(_service.IsConnected, Is.False);
    }

    [Test]
    public void Events_CanSubscribeAndUnsubscribe()
    {
        Action<NotificationDto> notificationReceivedHandler = _ => { };
        Action<string> notificationDismissedHandler = _ => { };

        // Subscribe
        _service.OnNotificationReceived += notificationReceivedHandler;
        _service.OnNotificationDismissed += notificationDismissedHandler;

        // Unsubscribe
        _service.OnNotificationReceived -= notificationReceivedHandler;
        _service.OnNotificationDismissed -= notificationDismissedHandler;

        Assert.Pass("All events can be subscribed to and unsubscribed from");
    }

    [Test]
    public void HubUrl_UsesNotificationHubConstant()
    {
        Assert.That(HubConstants.NotificationHub, Is.EqualTo("/hubs/notifications"));
    }

    private class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager()
        {
            Initialize("https://localhost/", "https://localhost/");
        }

        protected override void NavigateToCore(string uri, bool forceLoad)
        {
        }
    }
}
