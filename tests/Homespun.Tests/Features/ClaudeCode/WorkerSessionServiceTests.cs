using Homespun.AgentWorker.Models;
using Homespun.AgentWorker.Services;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Homespun.Tests.Features.ClaudeCode;

[TestFixture]
public class WorkerSessionServiceTests
{
    private WorkerSessionService _service = null!;
    private Mock<ILogger<WorkerSessionService>> _loggerMock = null!;

    [SetUp]
    public void SetUp()
    {
        _loggerMock = new Mock<ILogger<WorkerSessionService>>();
        _service = new WorkerSessionService(_loggerMock.Object);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _service.DisposeAsync();
    }

    [Test]
    public async Task SendMessageAsync_NonExistentSession_ReturnsSessionNotFoundError()
    {
        // Arrange & Act
        var events = new List<(string EventType, object Data)>();
        await foreach (var evt in _service.SendMessageAsync("non-existent", new SendMessageRequest { Message = "Hello" }))
        {
            events.Add(evt);
        }

        // Assert
        Assert.That(events, Has.Count.EqualTo(1));
        Assert.That(events[0].EventType, Is.EqualTo(SseEventTypes.Error));
        var errorData = events[0].Data as ErrorData;
        Assert.That(errorData, Is.Not.Null);
        Assert.That(errorData!.Code, Is.EqualTo("SESSION_NOT_FOUND"));
    }

    [Test]
    public async Task StartSessionAsync_CreatesSessionWithOptions()
    {
        // Arrange
        var request = new StartSessionRequest
        {
            WorkingDirectory = "/test/path",
            Mode = "Build",
            Model = "claude-sonnet-4-20250514",
            Prompt = "Hello"
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        string? sessionId = null;

        // Act - consume events until cancelled (ProcessMessagesAsync will fail without Claude)
        try
        {
            await foreach (var evt in _service.StartSessionAsync(request, cts.Token))
            {
                if (evt.EventType == SseEventTypes.SessionStarted)
                {
                    var startedData = evt.Data as SessionStartedData;
                    sessionId = startedData?.SessionId;
                    // Cancel after getting session ID - we don't need Claude to actually connect
                    await cts.CancelAsync();
                }
            }
        }
        catch (OperationCanceledException) { }

        // Assert
        Assert.That(sessionId, Is.Not.Null);
        var session = _service.GetSession(sessionId!);
        Assert.That(session, Is.Not.Null);
        Assert.That(session!.Options, Is.Not.Null);
        Assert.That(session.Options!.Cwd, Is.EqualTo("/test/path"));
        Assert.That(session.Options.Model, Is.EqualTo("claude-sonnet-4-20250514"));
    }

    [Test]
    public async Task StartSessionAsync_WithResumeSessionId_SetsResumeOnOptions()
    {
        // Arrange
        var request = new StartSessionRequest
        {
            WorkingDirectory = "/test/path",
            Mode = "Build",
            Model = "claude-sonnet-4-20250514",
            Prompt = "Hello",
            ResumeSessionId = "previous-conversation-123"
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        string? sessionId = null;

        // Act
        try
        {
            await foreach (var evt in _service.StartSessionAsync(request, cts.Token))
            {
                if (evt.EventType == SseEventTypes.SessionStarted)
                {
                    var startedData = evt.Data as SessionStartedData;
                    sessionId = startedData?.SessionId;
                    await cts.CancelAsync();
                }
            }
        }
        catch (OperationCanceledException) { }

        // Assert
        Assert.That(sessionId, Is.Not.Null);
        var session = _service.GetSession(sessionId!);
        Assert.That(session, Is.Not.Null);
        Assert.That(session!.Options!.Resume, Is.EqualTo("previous-conversation-123"));
        Assert.That(session.ConversationId, Is.EqualTo("previous-conversation-123"));
    }

    [Test]
    public async Task SendMessageAsync_UpdatesSessionOptionsWithResume()
    {
        // Arrange - First start a session to register it
        var startRequest = new StartSessionRequest
        {
            WorkingDirectory = "/test/path",
            Mode = "Build",
            Model = "claude-sonnet-4-20250514",
            Prompt = "Initial prompt"
        };

        using var startCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        string? sessionId = null;

        try
        {
            await foreach (var evt in _service.StartSessionAsync(startRequest, startCts.Token))
            {
                if (evt.EventType == SseEventTypes.SessionStarted)
                {
                    var startedData = evt.Data as SessionStartedData;
                    sessionId = startedData?.SessionId;
                    await startCts.CancelAsync();
                }
            }
        }
        catch (OperationCanceledException) { }

        Assert.That(sessionId, Is.Not.Null, "Session should have been created");

        // Simulate what ProcessMessagesAsync does when it receives a ResultMessage:
        // it sets session.ConversationId from the result
        var session = _service.GetSession(sessionId!);
        Assert.That(session, Is.Not.Null);
        session!.ConversationId = "conversation-abc-123";

        // Act - Send a follow-up message (will fail at Claude connection but should update options first)
        using var sendCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        try
        {
            await foreach (var evt in _service.SendMessageAsync(sessionId!, new SendMessageRequest { Message = "Follow up" }, sendCts.Token))
            {
                // Consume events until cancellation or error
            }
        }
        catch (OperationCanceledException) { }

        // Assert - After SendMessageAsync, session.Options should have Resume set
        var updatedSession = _service.GetSession(sessionId!);
        Assert.That(updatedSession, Is.Not.Null);
        Assert.That(updatedSession!.Options, Is.Not.Null);
        Assert.That(updatedSession.Options!.Resume, Is.EqualTo("conversation-abc-123"),
            "Session options should have Resume set to the conversation ID for session continuation");
    }

    [Test]
    public async Task SendMessageAsync_WithModelOverride_UpdatesSessionOptionsModel()
    {
        // Arrange - First start a session
        var startRequest = new StartSessionRequest
        {
            WorkingDirectory = "/test/path",
            Mode = "Build",
            Model = "claude-sonnet-4-20250514",
            Prompt = "Initial prompt"
        };

        using var startCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        string? sessionId = null;

        try
        {
            await foreach (var evt in _service.StartSessionAsync(startRequest, startCts.Token))
            {
                if (evt.EventType == SseEventTypes.SessionStarted)
                {
                    var startedData = evt.Data as SessionStartedData;
                    sessionId = startedData?.SessionId;
                    await startCts.CancelAsync();
                }
            }
        }
        catch (OperationCanceledException) { }

        Assert.That(sessionId, Is.Not.Null);

        var session = _service.GetSession(sessionId!);
        session!.ConversationId = "conversation-abc-123";

        // Act - Send message with model override
        using var sendCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        try
        {
            await foreach (var evt in _service.SendMessageAsync(sessionId!, new SendMessageRequest
            {
                Message = "Follow up",
                Model = "claude-opus-4-20250514"
            }, sendCts.Token))
            {
                // Consume events
            }
        }
        catch (OperationCanceledException) { }

        // Assert
        var updatedSession = _service.GetSession(sessionId!);
        Assert.That(updatedSession, Is.Not.Null);
        Assert.That(updatedSession!.Options!.Resume, Is.EqualTo("conversation-abc-123"));
        Assert.That(updatedSession.Options.Model, Is.EqualTo("claude-opus-4-20250514"),
            "Session options model should be updated when a model override is provided");
    }

    [Test]
    public async Task StopSessionAsync_RemovesSession()
    {
        // Arrange - Start a session
        var startRequest = new StartSessionRequest
        {
            WorkingDirectory = "/test/path",
            Mode = "Build",
            Model = "claude-sonnet-4-20250514",
            Prompt = "Hello"
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        string? sessionId = null;

        try
        {
            await foreach (var evt in _service.StartSessionAsync(startRequest, cts.Token))
            {
                if (evt.EventType == SseEventTypes.SessionStarted)
                {
                    var startedData = evt.Data as SessionStartedData;
                    sessionId = startedData?.SessionId;
                    await cts.CancelAsync();
                }
            }
        }
        catch (OperationCanceledException) { }

        Assert.That(sessionId, Is.Not.Null);
        Assert.That(_service.GetSession(sessionId!), Is.Not.Null);

        // Act
        await _service.StopSessionAsync(sessionId!);

        // Assert
        Assert.That(_service.GetSession(sessionId!), Is.Null);
    }

    [Test]
    public async Task GetSession_NonExistentSession_ReturnsNull()
    {
        // Act & Assert
        Assert.That(_service.GetSession("non-existent"), Is.Null);
    }
}
