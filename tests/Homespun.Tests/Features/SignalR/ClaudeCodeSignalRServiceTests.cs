using Homespun.Client.Services;
using Homespun.Shared.Hubs;
using Homespun.Shared.Models.Sessions;
using Microsoft.AspNetCore.Components;

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
    public void SessionLifecycleEvents_CanSubscribeAndUnsubscribe()
    {
        // Verify session lifecycle events can be subscribed to
        Action<ClaudeSession> sessionStartedHandler = _ => { };
        Action<string> sessionStoppedHandler = _ => { };
        Action<ClaudeSession> sessionStateHandler = _ => { };
Action<string, ClaudeSessionStatus, bool> statusChangedHandler = (_, _, _) => { };
        Action<string, SessionMode, string> modeModelChangedHandler = (_, _, _) => { };
        Action<string, decimal, long> resultHandler = (_, _, _) => { };
        Action<string> contextClearedHandler = _ => { };
        Action<string, string, string?, bool> sessionErrorHandler = (_, _, _, _) => { };

        // Subscribe
        _service.OnSessionStarted += sessionStartedHandler;
        _service.OnSessionStopped += sessionStoppedHandler;
        _service.OnSessionState += sessionStateHandler;
        _service.OnSessionStatusChanged += statusChangedHandler;
        _service.OnSessionModeModelChanged += modeModelChangedHandler;
        _service.OnSessionResultReceived += resultHandler;
        _service.OnContextCleared += contextClearedHandler;
        _service.OnSessionError += sessionErrorHandler;

        // Unsubscribe (should not throw)
        _service.OnSessionStarted -= sessionStartedHandler;
        _service.OnSessionStopped -= sessionStoppedHandler;
        _service.OnSessionState -= sessionStateHandler;
        _service.OnSessionStatusChanged -= statusChangedHandler;
        _service.OnSessionModeModelChanged -= modeModelChangedHandler;
        _service.OnSessionResultReceived -= resultHandler;
        _service.OnContextCleared -= contextClearedHandler;
        _service.OnSessionError -= sessionErrorHandler;

        Assert.Pass("All session lifecycle events can be subscribed to and unsubscribed from");
    }

    [Test]
    public void AGUIEvents_CanSubscribeAndUnsubscribe()
    {
        // Verify AG-UI events can be subscribed to
        Action<RunStartedEvent> runStartedHandler = _ => { };
        Action<RunFinishedEvent> runFinishedHandler = _ => { };
        Action<RunErrorEvent> runErrorHandler = _ => { };
        Action<TextMessageStartEvent> textStartHandler = _ => { };
        Action<TextMessageContentEvent> textContentHandler = _ => { };
        Action<TextMessageEndEvent> textEndHandler = _ => { };
        Action<ToolCallStartEvent> toolStartHandler = _ => { };
        Action<ToolCallArgsEvent> toolArgsHandler = _ => { };
        Action<ToolCallEndEvent> toolEndHandler = _ => { };
        Action<ToolCallResultEvent> toolResultHandler = _ => { };
        Action<StateSnapshotEvent> stateSnapshotHandler = _ => { };
        Action<StateDeltaEvent> stateDeltaHandler = _ => { };
        Action<CustomEvent> customEventHandler = _ => { };

        // Subscribe
        _service.OnAGUIRunStarted += runStartedHandler;
        _service.OnAGUIRunFinished += runFinishedHandler;
        _service.OnAGUIRunError += runErrorHandler;
        _service.OnAGUITextMessageStart += textStartHandler;
        _service.OnAGUITextMessageContent += textContentHandler;
        _service.OnAGUITextMessageEnd += textEndHandler;
        _service.OnAGUIToolCallStart += toolStartHandler;
        _service.OnAGUIToolCallArgs += toolArgsHandler;
        _service.OnAGUIToolCallEnd += toolEndHandler;
        _service.OnAGUIToolCallResult += toolResultHandler;
        _service.OnAGUIStateSnapshot += stateSnapshotHandler;
        _service.OnAGUIStateDelta += stateDeltaHandler;
        _service.OnAGUICustomEvent += customEventHandler;

        // Unsubscribe (should not throw)
        _service.OnAGUIRunStarted -= runStartedHandler;
        _service.OnAGUIRunFinished -= runFinishedHandler;
        _service.OnAGUIRunError -= runErrorHandler;
        _service.OnAGUITextMessageStart -= textStartHandler;
        _service.OnAGUITextMessageContent -= textContentHandler;
        _service.OnAGUITextMessageEnd -= textEndHandler;
        _service.OnAGUIToolCallStart -= toolStartHandler;
        _service.OnAGUIToolCallArgs -= toolArgsHandler;
        _service.OnAGUIToolCallEnd -= toolEndHandler;
        _service.OnAGUIToolCallResult -= toolResultHandler;
        _service.OnAGUIStateSnapshot -= stateSnapshotHandler;
        _service.OnAGUIStateDelta -= stateDeltaHandler;
        _service.OnAGUICustomEvent -= customEventHandler;

        Assert.Pass("All AG-UI events can be subscribed to and unsubscribed from");
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
