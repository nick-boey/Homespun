using Homespun.Client.Services;
using Homespun.Shared.Hubs;
using Homespun.Shared.Models.Sessions;
using Microsoft.AspNetCore.Components;
using NUnit.Framework;

namespace Homespun.Tests.Features.ClaudeCode;

/// <summary>
/// Tests for client-side AG-UI event handling.
/// Verifies that ClaudeCodeSignalRService properly exposes AG-UI events.
/// </summary>
[TestFixture]
public class AGUIClientTests
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

    #region AG-UI Event Subscription Tests

    [Test]
    public void AGUIEvents_CanSubscribeToRunStarted()
    {
        // Arrange
        RunStartedEvent? receivedEvent = null;
        Action<RunStartedEvent> handler = evt => receivedEvent = evt;

        // Act - verify subscription works without errors
        _service.OnAGUIRunStarted += handler;
        _service.OnAGUIRunStarted -= handler;

        // Assert - no exceptions means success
        Assert.Pass("Can subscribe to and unsubscribe from OnAGUIRunStarted");
    }

    [Test]
    public void AGUIEvents_CanSubscribeToRunFinished()
    {
        // Arrange
        RunFinishedEvent? receivedEvent = null;
        Action<RunFinishedEvent> handler = evt => receivedEvent = evt;

        // Act
        _service.OnAGUIRunFinished += handler;
        _service.OnAGUIRunFinished -= handler;

        // Assert
        Assert.Pass("Can subscribe to and unsubscribe from OnAGUIRunFinished");
    }

    [Test]
    public void AGUIEvents_CanSubscribeToRunError()
    {
        // Arrange
        RunErrorEvent? receivedEvent = null;
        Action<RunErrorEvent> handler = evt => receivedEvent = evt;

        // Act
        _service.OnAGUIRunError += handler;
        _service.OnAGUIRunError -= handler;

        // Assert
        Assert.Pass("Can subscribe to and unsubscribe from OnAGUIRunError");
    }

    [Test]
    public void AGUIEvents_CanSubscribeToTextMessageStart()
    {
        // Arrange
        TextMessageStartEvent? receivedEvent = null;
        Action<TextMessageStartEvent> handler = evt => receivedEvent = evt;

        // Act
        _service.OnAGUITextMessageStart += handler;
        _service.OnAGUITextMessageStart -= handler;

        // Assert
        Assert.Pass("Can subscribe to and unsubscribe from OnAGUITextMessageStart");
    }

    [Test]
    public void AGUIEvents_CanSubscribeToTextMessageContent()
    {
        // Arrange
        TextMessageContentEvent? receivedEvent = null;
        Action<TextMessageContentEvent> handler = evt => receivedEvent = evt;

        // Act
        _service.OnAGUITextMessageContent += handler;
        _service.OnAGUITextMessageContent -= handler;

        // Assert
        Assert.Pass("Can subscribe to and unsubscribe from OnAGUITextMessageContent");
    }

    [Test]
    public void AGUIEvents_CanSubscribeToTextMessageEnd()
    {
        // Arrange
        TextMessageEndEvent? receivedEvent = null;
        Action<TextMessageEndEvent> handler = evt => receivedEvent = evt;

        // Act
        _service.OnAGUITextMessageEnd += handler;
        _service.OnAGUITextMessageEnd -= handler;

        // Assert
        Assert.Pass("Can subscribe to and unsubscribe from OnAGUITextMessageEnd");
    }

    [Test]
    public void AGUIEvents_CanSubscribeToToolCallStart()
    {
        // Arrange
        ToolCallStartEvent? receivedEvent = null;
        Action<ToolCallStartEvent> handler = evt => receivedEvent = evt;

        // Act
        _service.OnAGUIToolCallStart += handler;
        _service.OnAGUIToolCallStart -= handler;

        // Assert
        Assert.Pass("Can subscribe to and unsubscribe from OnAGUIToolCallStart");
    }

    [Test]
    public void AGUIEvents_CanSubscribeToToolCallArgs()
    {
        // Arrange
        ToolCallArgsEvent? receivedEvent = null;
        Action<ToolCallArgsEvent> handler = evt => receivedEvent = evt;

        // Act
        _service.OnAGUIToolCallArgs += handler;
        _service.OnAGUIToolCallArgs -= handler;

        // Assert
        Assert.Pass("Can subscribe to and unsubscribe from OnAGUIToolCallArgs");
    }

    [Test]
    public void AGUIEvents_CanSubscribeToToolCallEnd()
    {
        // Arrange
        ToolCallEndEvent? receivedEvent = null;
        Action<ToolCallEndEvent> handler = evt => receivedEvent = evt;

        // Act
        _service.OnAGUIToolCallEnd += handler;
        _service.OnAGUIToolCallEnd -= handler;

        // Assert
        Assert.Pass("Can subscribe to and unsubscribe from OnAGUIToolCallEnd");
    }

    [Test]
    public void AGUIEvents_CanSubscribeToToolCallResult()
    {
        // Arrange
        ToolCallResultEvent? receivedEvent = null;
        Action<ToolCallResultEvent> handler = evt => receivedEvent = evt;

        // Act
        _service.OnAGUIToolCallResult += handler;
        _service.OnAGUIToolCallResult -= handler;

        // Assert
        Assert.Pass("Can subscribe to and unsubscribe from OnAGUIToolCallResult");
    }

    [Test]
    public void AGUIEvents_CanSubscribeToStateSnapshot()
    {
        // Arrange
        StateSnapshotEvent? receivedEvent = null;
        Action<StateSnapshotEvent> handler = evt => receivedEvent = evt;

        // Act
        _service.OnAGUIStateSnapshot += handler;
        _service.OnAGUIStateSnapshot -= handler;

        // Assert
        Assert.Pass("Can subscribe to and unsubscribe from OnAGUIStateSnapshot");
    }

    [Test]
    public void AGUIEvents_CanSubscribeToStateDelta()
    {
        // Arrange
        StateDeltaEvent? receivedEvent = null;
        Action<StateDeltaEvent> handler = evt => receivedEvent = evt;

        // Act
        _service.OnAGUIStateDelta += handler;
        _service.OnAGUIStateDelta -= handler;

        // Assert
        Assert.Pass("Can subscribe to and unsubscribe from OnAGUIStateDelta");
    }

    [Test]
    public void AGUIEvents_CanSubscribeToCustomEvent()
    {
        // Arrange
        CustomEvent? receivedEvent = null;
        Action<CustomEvent> handler = evt => receivedEvent = evt;

        // Act
        _service.OnAGUICustomEvent += handler;
        _service.OnAGUICustomEvent -= handler;

        // Assert
        Assert.Pass("Can subscribe to and unsubscribe from OnAGUICustomEvent");
    }

    #endregion

    #region AG-UI Event Type Tests

    [Test]
    public void AGUIEventType_RunStarted_HasCorrectValue()
    {
        Assert.That(AGUIEventType.RunStarted, Is.EqualTo("AGUI_RunStarted"));
    }

    [Test]
    public void AGUIEventType_RunFinished_HasCorrectValue()
    {
        Assert.That(AGUIEventType.RunFinished, Is.EqualTo("AGUI_RunFinished"));
    }

    [Test]
    public void AGUIEventType_RunError_HasCorrectValue()
    {
        Assert.That(AGUIEventType.RunError, Is.EqualTo("AGUI_RunError"));
    }

    [Test]
    public void AGUIEventType_TextMessageStart_HasCorrectValue()
    {
        Assert.That(AGUIEventType.TextMessageStart, Is.EqualTo("AGUI_TextMessageStart"));
    }

    [Test]
    public void AGUIEventType_TextMessageContent_HasCorrectValue()
    {
        Assert.That(AGUIEventType.TextMessageContent, Is.EqualTo("AGUI_TextMessageContent"));
    }

    [Test]
    public void AGUIEventType_TextMessageEnd_HasCorrectValue()
    {
        Assert.That(AGUIEventType.TextMessageEnd, Is.EqualTo("AGUI_TextMessageEnd"));
    }

    [Test]
    public void AGUIEventType_ToolCallStart_HasCorrectValue()
    {
        Assert.That(AGUIEventType.ToolCallStart, Is.EqualTo("AGUI_ToolCallStart"));
    }

    [Test]
    public void AGUIEventType_ToolCallArgs_HasCorrectValue()
    {
        Assert.That(AGUIEventType.ToolCallArgs, Is.EqualTo("AGUI_ToolCallArgs"));
    }

    [Test]
    public void AGUIEventType_ToolCallEnd_HasCorrectValue()
    {
        Assert.That(AGUIEventType.ToolCallEnd, Is.EqualTo("AGUI_ToolCallEnd"));
    }

    [Test]
    public void AGUIEventType_ToolCallResult_HasCorrectValue()
    {
        Assert.That(AGUIEventType.ToolCallResult, Is.EqualTo("AGUI_ToolCallResult"));
    }

    [Test]
    public void AGUIEventType_Custom_HasCorrectValue()
    {
        Assert.That(AGUIEventType.Custom, Is.EqualTo("AGUI_Custom"));
    }

    #endregion

    #region AG-UI Custom Event Name Tests

    [Test]
    public void AGUICustomEventName_QuestionPending_HasCorrectValue()
    {
        Assert.That(AGUICustomEventName.QuestionPending, Is.EqualTo("QuestionPending"));
    }

    [Test]
    public void AGUICustomEventName_PlanPending_HasCorrectValue()
    {
        Assert.That(AGUICustomEventName.PlanPending, Is.EqualTo("PlanPending"));
    }

    [Test]
    public void AGUICustomEventName_HookExecuted_HasCorrectValue()
    {
        Assert.That(AGUICustomEventName.HookExecuted, Is.EqualTo("HookExecuted"));
    }

    [Test]
    public void AGUICustomEventName_ContextCleared_HasCorrectValue()
    {
        Assert.That(AGUICustomEventName.ContextCleared, Is.EqualTo("ContextCleared"));
    }

    #endregion

    #region HubConstants Tests

    [Test]
    public void HubConstants_ClaudeCodeHub_HasCorrectPath()
    {
        Assert.That(HubConstants.ClaudeCodeHub, Is.EqualTo("/hubs/claudecode"));
    }

    #endregion

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
