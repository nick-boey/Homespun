using Homespun.Client.Services;
using Homespun.Shared.Models.Sessions;
using NUnit.Framework;
using System.Collections.Concurrent;

namespace Homespun.Tests.Features.ClaudeCode;

/// <summary>
/// Tests for AGUIEventStateService which maintains client-side state for
/// in-flight AG-UI messages and tool calls.
/// </summary>
[TestFixture]
public class AGUIEventStateServiceTests
{
    private AGUIEventStateService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _service = new AGUIEventStateService();
    }

    #region TextMessage Start/Content/End Tests

    [Test]
    public void TextMessageStart_CreatesNewInFlightMessage()
    {
        // Arrange
        var sessionId = "session-1";
        var messageId = "msg-1";
        var startEvent = new TextMessageStartEvent
        {
            MessageId = messageId,
            Role = "assistant"
        };

        // Act
        _service.HandleTextMessageStart(sessionId, startEvent);

        // Assert
        Assert.That(_service.HasInFlightMessage(sessionId, messageId), Is.True);
    }

    [Test]
    public void TextMessageContent_AccumulatesContent()
    {
        // Arrange
        var sessionId = "session-1";
        var messageId = "msg-1";

        var startEvent = new TextMessageStartEvent { MessageId = messageId, Role = "assistant" };
        var contentEvent1 = new TextMessageContentEvent { MessageId = messageId, Delta = "Hello " };
        var contentEvent2 = new TextMessageContentEvent { MessageId = messageId, Delta = "World!" };

        _service.HandleTextMessageStart(sessionId, startEvent);

        // Act
        _service.HandleTextMessageContent(sessionId, contentEvent1);
        _service.HandleTextMessageContent(sessionId, contentEvent2);

        // Assert
        var content = _service.GetInFlightMessageContent(sessionId, messageId);
        Assert.That(content, Is.EqualTo("Hello World!"));
    }

    [Test]
    public void TextMessageEnd_CompletesMessage()
    {
        // Arrange
        var sessionId = "session-1";
        var messageId = "msg-1";
        ClaudeMessage? completedMessage = null;

        _service.OnMessageCompleted += (sid, msg) =>
        {
            if (sid == sessionId) completedMessage = msg;
        };

        var startEvent = new TextMessageStartEvent { MessageId = messageId, Role = "assistant" };
        var contentEvent = new TextMessageContentEvent { MessageId = messageId, Delta = "Test content" };
        var endEvent = new TextMessageEndEvent { MessageId = messageId };

        _service.HandleTextMessageStart(sessionId, startEvent);
        _service.HandleTextMessageContent(sessionId, contentEvent);

        // Act
        _service.HandleTextMessageEnd(sessionId, endEvent);

        // Assert
        Assert.That(completedMessage, Is.Not.Null);
        Assert.That(completedMessage!.Role, Is.EqualTo(ClaudeMessageRole.Assistant));
        Assert.That(completedMessage.Content, Has.Count.EqualTo(1));
        Assert.That(completedMessage.Content[0].Text, Is.EqualTo("Test content"));
        Assert.That(_service.HasInFlightMessage(sessionId, messageId), Is.False);
    }

    [Test]
    public void MultipleContentDeltas_AccumulateCorrectly()
    {
        // Arrange
        var sessionId = "session-1";
        var messageId = "msg-1";
        var deltas = new[] { "Line 1\n", "Line 2\n", "Line 3" };

        var startEvent = new TextMessageStartEvent { MessageId = messageId, Role = "assistant" };
        _service.HandleTextMessageStart(sessionId, startEvent);

        // Act
        foreach (var delta in deltas)
        {
            _service.HandleTextMessageContent(sessionId, new TextMessageContentEvent { MessageId = messageId, Delta = delta });
        }

        // Assert
        var content = _service.GetInFlightMessageContent(sessionId, messageId);
        Assert.That(content, Is.EqualTo("Line 1\nLine 2\nLine 3"));
    }

    #endregion

    #region ToolCall Start/Args/End Tests

    [Test]
    public void ToolCallStart_CreatesNewInFlightToolCall()
    {
        // Arrange
        var sessionId = "session-1";
        var toolCallId = "tool-1";
        var startEvent = new ToolCallStartEvent
        {
            ToolCallId = toolCallId,
            ToolCallName = "Read",
            ParentMessageId = "msg-1"
        };

        // Act
        _service.HandleToolCallStart(sessionId, startEvent);

        // Assert
        Assert.That(_service.HasInFlightToolCall(sessionId, toolCallId), Is.True);
    }

    [Test]
    public void ToolCallArgs_AccumulatesArgs()
    {
        // Arrange
        var sessionId = "session-1";
        var toolCallId = "tool-1";

        var startEvent = new ToolCallStartEvent { ToolCallId = toolCallId, ToolCallName = "Read" };
        var argsEvent1 = new ToolCallArgsEvent { ToolCallId = toolCallId, Delta = "{\"file_path\"" };
        var argsEvent2 = new ToolCallArgsEvent { ToolCallId = toolCallId, Delta = ": \"/test.txt\"}" };

        _service.HandleToolCallStart(sessionId, startEvent);

        // Act
        _service.HandleToolCallArgs(sessionId, argsEvent1);
        _service.HandleToolCallArgs(sessionId, argsEvent2);

        // Assert
        var args = _service.GetInFlightToolCallArgs(sessionId, toolCallId);
        Assert.That(args, Is.EqualTo("{\"file_path\": \"/test.txt\"}"));
    }

    [Test]
    public void ToolCallEnd_CompletesToolCall()
    {
        // Arrange
        var sessionId = "session-1";
        var toolCallId = "tool-1";
        ClaudeMessageContent? completedToolCall = null;

        _service.OnToolCallCompleted += (sid, content) =>
        {
            if (sid == sessionId) completedToolCall = content;
        };

        var startEvent = new ToolCallStartEvent { ToolCallId = toolCallId, ToolCallName = "Read" };
        var argsEvent = new ToolCallArgsEvent { ToolCallId = toolCallId, Delta = "{\"file_path\": \"/test.txt\"}" };
        var endEvent = new ToolCallEndEvent { ToolCallId = toolCallId };

        _service.HandleToolCallStart(sessionId, startEvent);
        _service.HandleToolCallArgs(sessionId, argsEvent);

        // Act
        _service.HandleToolCallEnd(sessionId, endEvent);

        // Assert
        Assert.That(completedToolCall, Is.Not.Null);
        Assert.That(completedToolCall!.Type, Is.EqualTo(ClaudeContentType.ToolUse));
        Assert.That(completedToolCall.ToolName, Is.EqualTo("Read"));
        Assert.That(completedToolCall.ToolInput, Is.EqualTo("{\"file_path\": \"/test.txt\"}"));
        Assert.That(_service.HasInFlightToolCall(sessionId, toolCallId), Is.False);
    }

    #endregion

    #region ToolCallResult Tests

    [Test]
    public void ToolCallResult_EmitsToolResultEvent()
    {
        // Arrange
        var sessionId = "session-1";
        var toolCallId = "tool-1";
        ClaudeMessageContent? toolResult = null;

        _service.OnToolResultReceived += (sid, content) =>
        {
            if (sid == sessionId) toolResult = content;
        };

        var resultEvent = new ToolCallResultEvent
        {
            ToolCallId = toolCallId,
            Content = "File contents here",
            MessageId = "result-msg-1"
        };

        // Act
        _service.HandleToolCallResult(sessionId, resultEvent);

        // Assert
        Assert.That(toolResult, Is.Not.Null);
        Assert.That(toolResult!.Type, Is.EqualTo(ClaudeContentType.ToolResult));
        Assert.That(toolResult.ToolUseId, Is.EqualTo(toolCallId));
        Assert.That(toolResult.ToolResult, Is.EqualTo("File contents here"));
    }

    #endregion

    #region Interleaved Messages Tests

    [Test]
    public void InterleavedMessages_MaintainSeparateState()
    {
        // Arrange
        var sessionId = "session-1";
        var messageId1 = "msg-1";
        var messageId2 = "msg-2";

        // Start two messages
        _service.HandleTextMessageStart(sessionId, new TextMessageStartEvent { MessageId = messageId1, Role = "assistant" });
        _service.HandleTextMessageStart(sessionId, new TextMessageStartEvent { MessageId = messageId2, Role = "assistant" });

        // Send content to each in interleaved fashion
        _service.HandleTextMessageContent(sessionId, new TextMessageContentEvent { MessageId = messageId1, Delta = "A" });
        _service.HandleTextMessageContent(sessionId, new TextMessageContentEvent { MessageId = messageId2, Delta = "X" });
        _service.HandleTextMessageContent(sessionId, new TextMessageContentEvent { MessageId = messageId1, Delta = "B" });
        _service.HandleTextMessageContent(sessionId, new TextMessageContentEvent { MessageId = messageId2, Delta = "Y" });

        // Assert
        Assert.That(_service.GetInFlightMessageContent(sessionId, messageId1), Is.EqualTo("AB"));
        Assert.That(_service.GetInFlightMessageContent(sessionId, messageId2), Is.EqualTo("XY"));
    }

    #endregion

    #region Run Lifecycle Tests

    [Test]
    public void RunStarted_EmitsEvent()
    {
        // Arrange
        var sessionId = "session-1";
        string? startedSession = null;

        _service.OnRunStarted += sid => startedSession = sid;

        var runStartEvent = new RunStartedEvent { ThreadId = sessionId, RunId = "run-1" };

        // Act
        _service.HandleRunStarted(sessionId, runStartEvent);

        // Assert
        Assert.That(startedSession, Is.EqualTo(sessionId));
    }

    [Test]
    public void RunFinished_ClearsInFlightState()
    {
        // Arrange
        var sessionId = "session-1";
        var messageId = "msg-1";
        string? finishedSession = null;

        _service.OnRunFinished += sid => finishedSession = sid;

        // Create in-flight message
        _service.HandleTextMessageStart(sessionId, new TextMessageStartEvent { MessageId = messageId, Role = "assistant" });

        var runFinishEvent = new RunFinishedEvent { ThreadId = sessionId, RunId = "run-1" };

        // Act
        _service.HandleRunFinished(sessionId, runFinishEvent);

        // Assert
        Assert.That(finishedSession, Is.EqualTo(sessionId));
        Assert.That(_service.HasInFlightMessage(sessionId, messageId), Is.False);
    }

    [Test]
    public void RunError_ClearsInFlightState()
    {
        // Arrange
        var sessionId = "session-1";
        var messageId = "msg-1";
        var toolCallId = "tool-1";
        string? errorSession = null;
        string? errorMessage = null;

        _service.OnRunError += (sid, msg) =>
        {
            errorSession = sid;
            errorMessage = msg;
        };

        // Create in-flight state
        _service.HandleTextMessageStart(sessionId, new TextMessageStartEvent { MessageId = messageId, Role = "assistant" });
        _service.HandleToolCallStart(sessionId, new ToolCallStartEvent { ToolCallId = toolCallId, ToolCallName = "Read" });

        var runErrorEvent = new RunErrorEvent { Message = "An error occurred" };

        // Act
        _service.HandleRunError(sessionId, runErrorEvent);

        // Assert
        Assert.That(errorSession, Is.EqualTo(sessionId));
        Assert.That(errorMessage, Is.EqualTo("An error occurred"));
        Assert.That(_service.HasInFlightMessage(sessionId, messageId), Is.False);
        Assert.That(_service.HasInFlightToolCall(sessionId, toolCallId), Is.False);
    }

    #endregion

    #region Multiple Sessions Tests

    [Test]
    public void MultipleSessions_MaintainIndependentState()
    {
        // Arrange
        var session1 = "session-1";
        var session2 = "session-2";
        var messageId = "msg-1";

        // Same message ID in different sessions
        _service.HandleTextMessageStart(session1, new TextMessageStartEvent { MessageId = messageId, Role = "assistant" });
        _service.HandleTextMessageStart(session2, new TextMessageStartEvent { MessageId = messageId, Role = "assistant" });

        _service.HandleTextMessageContent(session1, new TextMessageContentEvent { MessageId = messageId, Delta = "Session 1 content" });
        _service.HandleTextMessageContent(session2, new TextMessageContentEvent { MessageId = messageId, Delta = "Session 2 content" });

        // Assert
        Assert.That(_service.GetInFlightMessageContent(session1, messageId), Is.EqualTo("Session 1 content"));
        Assert.That(_service.GetInFlightMessageContent(session2, messageId), Is.EqualTo("Session 2 content"));
    }

    #endregion
}
