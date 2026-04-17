using System.Text.Json;
using A2A;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.Observability;
using Homespun.Shared.Models.Observability;
using Homespun.Shared.Models.Sessions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Homespun.Tests.Features.Observability;

[TestFixture]
[NonParallelizable]
public class SessionEventLogTests
{
    private ILogger _logger = null!;
    private StringWriter _consoleOutput = null!;
    private TextWriter _originalOutput = null!;

    [SetUp]
    public void SetUp()
    {
        _logger = NullLogger.Instance;
        _originalOutput = Console.Out;
        _consoleOutput = new StringWriter();
        Console.SetOut(_consoleOutput);
    }

    [TearDown]
    public void TearDown()
    {
        Console.SetOut(_originalOutput);
        _consoleOutput.Dispose();
    }

    // TruncatePreview boundary conditions

    [Test]
    public void TruncatePreview_ZeroChars_ReturnsNull()
    {
        Assert.That(SessionEventLog.TruncatePreview("hello", 0), Is.Null);
    }

    [Test]
    public void TruncatePreview_NullText_ReturnsNull()
    {
        Assert.That(SessionEventLog.TruncatePreview(null, 10), Is.Null);
    }

    [Test]
    public void TruncatePreview_ExactLength_ReturnsOriginal()
    {
        Assert.That(SessionEventLog.TruncatePreview("abcde", 5), Is.EqualTo("abcde"));
    }

    [Test]
    public void TruncatePreview_Longer_TruncatesWithEllipsis()
    {
        Assert.That(SessionEventLog.TruncatePreview("abcdefgh", 3), Is.EqualTo("abc\u2026"));
    }

    [Test]
    public void TruncatePreview_Shorter_ReturnsOriginal()
    {
        Assert.That(SessionEventLog.TruncatePreview("ab", 5), Is.EqualTo("ab"));
    }

    [Test]
    public void TruncatePreview_UnicodeExactLength_ReturnsOriginal()
    {
        // 5 unicode chars, 5 code units (BMP-only)
        Assert.That(SessionEventLog.TruncatePreview("héllo", 5), Is.EqualTo("héllo"));
    }

    // Per-hop Enabled flag suppression

    [Test]
    public void LogA2AHop_WhenHopDisabled_DoesNotEmit()
    {
        var options = new SessionEventLogOptions
        {
            Hops = new Dictionary<string, SessionEventLogOptions.HopSettings>
            {
                [SessionEventHops.ServerSseRx] = new() { Enabled = false },
            },
        };

        SessionEventLog.LogA2AHop(
            _logger, options,
            hop: SessionEventHops.ServerSseRx,
            sessionId: "s1",
            a2aKind: HomespunA2AEventKind.Message,
            parsed: null);

        Assert.That(_consoleOutput.ToString(), Is.Empty);
    }

    [Test]
    public void LogA2AHop_WhenHopEnabled_EmitsJson()
    {
        var options = new SessionEventLogOptions();
        SessionEventLog.LogA2AHop(
            _logger, options,
            hop: SessionEventHops.ServerSseRx,
            sessionId: "s1",
            a2aKind: HomespunA2AEventKind.Message,
            parsed: null);

        var output = _consoleOutput.ToString().Trim();
        Assert.That(output, Is.Not.Empty);
        using var doc = JsonDocument.Parse(output);
        Assert.That(doc.RootElement.GetProperty("Hop").GetString(), Is.EqualTo(SessionEventHops.ServerSseRx));
        Assert.That(doc.RootElement.GetProperty("SessionId").GetString(), Is.EqualTo("s1"));
        Assert.That(doc.RootElement.GetProperty("A2AKind").GetString(), Is.EqualTo(HomespunA2AEventKind.Message));
        Assert.That(doc.RootElement.GetProperty("SourceContext").GetString(), Is.EqualTo(SessionEventSourceContexts.Server));
    }

    // Correlation field extraction per A2A variant

    [Test]
    public void LogA2AHop_Message_ExtractsMessageId()
    {
        var options = new SessionEventLogOptions();
        var message = new AgentMessage
        {
            MessageId = "M1",
            TaskId = "T1",
            ContextId = "S1",
            Role = MessageRole.Agent,
            Parts = [new TextPart { Text = "hi" }],
        };
        var parsed = new ParsedAgentMessage(message);

        SessionEventLog.LogA2AHop(
            _logger, options,
            hop: SessionEventHops.ServerSseRx,
            sessionId: "S1",
            a2aKind: HomespunA2AEventKind.Message,
            parsed: parsed);

        using var doc = JsonDocument.Parse(_consoleOutput.ToString().Trim());
        Assert.That(doc.RootElement.GetProperty("MessageId").GetString(), Is.EqualTo("M1"));
        Assert.That(doc.RootElement.GetProperty("TaskId").GetString(), Is.EqualTo("T1"));
        Assert.That(doc.RootElement.TryGetProperty("ArtifactId", out _), Is.False);
        Assert.That(doc.RootElement.TryGetProperty("StatusTimestamp", out _), Is.False);
    }

    [Test]
    public void LogA2AHop_Task_ExtractsTaskId()
    {
        var options = new SessionEventLogOptions();
        var task = new AgentTask
        {
            Id = "T1",
            ContextId = "S1",
            Status = new AgentTaskStatus { State = TaskState.Working },
        };
        var parsed = new ParsedAgentTask(task);

        SessionEventLog.LogA2AHop(
            _logger, options,
            hop: SessionEventHops.ServerSseRx,
            sessionId: "S1",
            a2aKind: HomespunA2AEventKind.Task,
            parsed: parsed);

        using var doc = JsonDocument.Parse(_consoleOutput.ToString().Trim());
        Assert.That(doc.RootElement.GetProperty("TaskId").GetString(), Is.EqualTo("T1"));
        Assert.That(doc.RootElement.TryGetProperty("MessageId", out _), Is.False);
    }

    [Test]
    public void LogA2AHop_StatusUpdate_ExtractsStatusTimestamp()
    {
        var options = new SessionEventLogOptions();
        var update = new TaskStatusUpdateEvent
        {
            TaskId = "T1",
            ContextId = "S1",
            Status = new AgentTaskStatus
            {
                State = TaskState.Working,
                Timestamp = DateTimeOffset.Parse("2026-04-17T10:00:00.000Z"),
            },
        };
        var parsed = new ParsedTaskStatusUpdateEvent(update);

        SessionEventLog.LogA2AHop(
            _logger, options,
            hop: SessionEventHops.ServerSseRx,
            sessionId: "S1",
            a2aKind: HomespunA2AEventKind.StatusUpdate,
            parsed: parsed);

        using var doc = JsonDocument.Parse(_consoleOutput.ToString().Trim());
        Assert.That(doc.RootElement.GetProperty("TaskId").GetString(), Is.EqualTo("T1"));
        var emittedTs = doc.RootElement.GetProperty("StatusTimestamp").GetString();
        Assert.That(emittedTs, Is.Not.Null.And.Not.Empty);
        Assert.That(DateTimeOffset.Parse(emittedTs!), Is.EqualTo(DateTimeOffset.Parse("2026-04-17T10:00:00.000Z")));
        Assert.That(doc.RootElement.TryGetProperty("MessageId", out _), Is.False);
    }

    [Test]
    public void LogA2AHop_ArtifactUpdate_ExtractsArtifactId()
    {
        var options = new SessionEventLogOptions();
        var update = new TaskArtifactUpdateEvent
        {
            TaskId = "T1",
            ContextId = "S1",
            Artifact = new Artifact { ArtifactId = "A1" },
        };
        var parsed = new ParsedTaskArtifactUpdateEvent(update);

        SessionEventLog.LogA2AHop(
            _logger, options,
            hop: SessionEventHops.ServerIngestAppend,
            sessionId: "S1",
            a2aKind: HomespunA2AEventKind.ArtifactUpdate,
            parsed: parsed,
            seq: 42,
            eventId: "e1");

        using var doc = JsonDocument.Parse(_consoleOutput.ToString().Trim());
        Assert.That(doc.RootElement.GetProperty("ArtifactId").GetString(), Is.EqualTo("A1"));
        Assert.That(doc.RootElement.GetProperty("Seq").GetInt64(), Is.EqualTo(42L));
        Assert.That(doc.RootElement.GetProperty("EventId").GetString(), Is.EqualTo("e1"));
    }

    // ContentPreview behavior

    [Test]
    public void LogA2AHop_WithContentPreviewChars_IncludesPreview()
    {
        var options = new SessionEventLogOptions { ContentPreviewChars = 80 };
        var message = new AgentMessage
        {
            MessageId = "M1",
            ContextId = "S1",
            Role = MessageRole.Agent,
            Parts = [new TextPart { Text = "hello world" }],
        };

        SessionEventLog.LogA2AHop(
            _logger, options,
            hop: SessionEventHops.ServerSseRx,
            sessionId: "S1",
            a2aKind: HomespunA2AEventKind.Message,
            parsed: new ParsedAgentMessage(message));

        using var doc = JsonDocument.Parse(_consoleOutput.ToString().Trim());
        Assert.That(doc.RootElement.GetProperty("ContentPreview").GetString(), Is.EqualTo("hello world"));
    }

    [Test]
    public void LogA2AHop_ContentPreviewZero_OmitsPreview()
    {
        var options = new SessionEventLogOptions { ContentPreviewChars = 0 };
        var message = new AgentMessage
        {
            MessageId = "M1",
            ContextId = "S1",
            Role = MessageRole.Agent,
            Parts = [new TextPart { Text = "hello world" }],
        };

        SessionEventLog.LogA2AHop(
            _logger, options,
            hop: SessionEventHops.ServerSseRx,
            sessionId: "S1",
            a2aKind: HomespunA2AEventKind.Message,
            parsed: new ParsedAgentMessage(message));

        using var doc = JsonDocument.Parse(_consoleOutput.ToString().Trim());
        Assert.That(doc.RootElement.TryGetProperty("ContentPreview", out _), Is.False);
    }

    // AG-UI hop

    [Test]
    public void LogAGUIHop_TextMessageContent_IncludesAGUIType()
    {
        var options = new SessionEventLogOptions { ContentPreviewChars = 40 };
        var agui = new TextMessageContentEvent { MessageId = "M1", Delta = "hi" };

        SessionEventLog.LogAGUIHop(
            _logger, options,
            hop: SessionEventHops.ServerAguiTranslate,
            sessionId: "S1",
            agui: agui,
            seq: 7,
            eventId: "e7",
            parentMessageId: "M1",
            a2aKind: HomespunA2AEventKind.Message);

        using var doc = JsonDocument.Parse(_consoleOutput.ToString().Trim());
        Assert.That(doc.RootElement.GetProperty("AGUIType").GetString(), Is.EqualTo("TEXT_MESSAGE_CONTENT"));
        Assert.That(doc.RootElement.GetProperty("MessageId").GetString(), Is.EqualTo("M1"));
        Assert.That(doc.RootElement.GetProperty("Seq").GetInt64(), Is.EqualTo(7L));
        Assert.That(doc.RootElement.GetProperty("EventId").GetString(), Is.EqualTo("e7"));
        Assert.That(doc.RootElement.GetProperty("ContentPreview").GetString(), Is.EqualTo("hi"));
    }

    [Test]
    public void LogAGUIHop_Custom_ExposesCustomName()
    {
        var options = new SessionEventLogOptions();
        var agui = new CustomEvent { Name = AGUICustomEventName.Thinking, Value = new { text = "hmm" } };

        SessionEventLog.LogAGUIHop(
            _logger, options,
            hop: SessionEventHops.ServerSignalrTx,
            sessionId: "S1",
            agui: agui,
            seq: 1,
            eventId: "e1");

        using var doc = JsonDocument.Parse(_consoleOutput.ToString().Trim());
        Assert.That(doc.RootElement.GetProperty("AGUIType").GetString(), Is.EqualTo("CUSTOM"));
        Assert.That(doc.RootElement.GetProperty("AGUICustomName").GetString(), Is.EqualTo(AGUICustomEventName.Thinking));
    }
}
