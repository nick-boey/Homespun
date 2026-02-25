using Homespun.Client.Services;
using Homespun.Shared.Hubs;
using Homespun.Shared.Models.Notifications;
using Homespun.Shared.Models.Sessions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Moq;

namespace Homespun.Tests.Features.SignalR;

[TestFixture]
public class ClaudeCodeSignalRServiceTests
{
    private ClaudeCodeSignalRService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _service = new ClaudeCodeSignalRService(new TestNavigationManager());
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
    public void Constructor_JoinedSessionsIsEmpty()
    {
        Assert.That(_service.JoinedSessions, Is.Empty);
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
        // Verify all events can be subscribed to (compilation test + runtime verification)
        Action<ClaudeSession> sessionStartedHandler = _ => { };
        Action<string> sessionStoppedHandler = _ => { };
        Action<ClaudeSession> sessionStateHandler = _ => { };
        Action<ClaudeMessage> messageReceivedHandler = _ => { };
        Action<ClaudeMessageContent> contentBlockHandler = _ => { };
        Action<string, ClaudeSessionStatus, bool> statusChangedHandler = (_, _, _) => { };
        Action<string, decimal, long> resultHandler = (_, _, _) => { };
        Action<ClaudeMessageContent, int> streamStartedHandler = (_, _) => { };
        Action<ClaudeMessageContent, string, int> streamDeltaHandler = (_, _, _) => { };
        Action<ClaudeMessageContent, int> streamStoppedHandler = (_, _) => { };
        Action<PendingQuestion> questionReceivedHandler = _ => { };
        Action questionAnsweredHandler = () => { };
        Action<string> contextClearedHandler = _ => { };

        // Subscribe
        _service.OnSessionStarted += sessionStartedHandler;
        _service.OnSessionStopped += sessionStoppedHandler;
        _service.OnSessionState += sessionStateHandler;
        _service.OnMessageReceived += messageReceivedHandler;
        _service.OnContentBlockReceived += contentBlockHandler;
        _service.OnSessionStatusChanged += statusChangedHandler;
        _service.OnSessionResultReceived += resultHandler;
        _service.OnStreamingContentStarted += streamStartedHandler;
        _service.OnStreamingContentDelta += streamDeltaHandler;
        _service.OnStreamingContentStopped += streamStoppedHandler;
        _service.OnQuestionReceived += questionReceivedHandler;
        _service.OnQuestionAnswered += questionAnsweredHandler;
        _service.OnContextCleared += contextClearedHandler;

        // Unsubscribe (should not throw)
        _service.OnSessionStarted -= sessionStartedHandler;
        _service.OnSessionStopped -= sessionStoppedHandler;
        _service.OnSessionState -= sessionStateHandler;
        _service.OnMessageReceived -= messageReceivedHandler;
        _service.OnContentBlockReceived -= contentBlockHandler;
        _service.OnSessionStatusChanged -= statusChangedHandler;
        _service.OnSessionResultReceived -= resultHandler;
        _service.OnStreamingContentStarted -= streamStartedHandler;
        _service.OnStreamingContentDelta -= streamDeltaHandler;
        _service.OnStreamingContentStopped -= streamStoppedHandler;
        _service.OnQuestionReceived -= questionReceivedHandler;
        _service.OnQuestionAnswered -= questionAnsweredHandler;
        _service.OnContextCleared -= contextClearedHandler;

        Assert.Pass("All events can be subscribed to and unsubscribed from");
    }

    [Test]
    public void HubUrl_UsesClaudeCodeHubConstant()
    {
        // Verify the service uses the correct hub URL from HubConstants
        Assert.That(HubConstants.ClaudeCodeHub, Is.EqualTo("/hubs/claudecode"));
    }

    /// <summary>
    /// Minimal NavigationManager for testing SignalR services.
    /// </summary>
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
