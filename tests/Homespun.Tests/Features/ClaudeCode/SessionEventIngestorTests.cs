using System.Text.Json;
using Homespun.Features.ClaudeCode.Hubs;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Shared.Models.Sessions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.ClaudeCode;

/// <summary>
/// TDD tests for <see cref="SessionEventIngestor"/>.
/// Covers task 6.5 of the a2a-native-messaging OpenSpec change: append-before-broadcast
/// ordering, envelope shape, and failure isolation.
/// </summary>
[TestFixture]
public class SessionEventIngestorTests
{
    private const string ProjectId = "proj-1";
    private const string SessionId = "sess-1";

    private Mock<IA2AEventStore> _storeMock = null!;
    private Mock<IA2AToAGUITranslator> _translatorMock = null!;
    private Mock<IHubContext<ClaudeCodeHub>> _hubMock = null!;
    private Mock<IHubClients> _hubClientsMock = null!;
    private Mock<IClientProxy> _groupClientMock = null!;
    private Mock<ILogger<SessionEventIngestor>> _loggerMock = null!;

    private SessionEventIngestor _ingestor = null!;

    [SetUp]
    public void SetUp()
    {
        _storeMock = new Mock<IA2AEventStore>();
        _translatorMock = new Mock<IA2AToAGUITranslator>();
        _loggerMock = new Mock<ILogger<SessionEventIngestor>>();

        _groupClientMock = new Mock<IClientProxy>();
        _hubClientsMock = new Mock<IHubClients>();
        _hubClientsMock
            .Setup(c => c.Group(It.IsAny<string>()))
            .Returns(_groupClientMock.Object);

        _hubMock = new Mock<IHubContext<ClaudeCodeHub>>();
        _hubMock.SetupGet(h => h.Clients).Returns(_hubClientsMock.Object);

        _ingestor = new SessionEventIngestor(
            _storeMock.Object,
            _translatorMock.Object,
            _hubMock.Object,
            _loggerMock.Object);
    }

    private static JsonElement Payload(string kind) =>
        JsonDocument.Parse($$"""{"kind":"{{kind}}"}""").RootElement.Clone();

    private static A2AEventRecord Record(long seq, string eventId, string kind) =>
        new(seq, SessionId, eventId, kind, DateTime.UtcNow, Payload(kind));

    // ---------------- Ordering ----------------

    [Test]
    public async Task IngestAsync_AppendsToStoreBeforeBroadcasting()
    {
        var sequence = new List<string>();

        _storeMock
            .Setup(s => s.AppendAsync(ProjectId, SessionId, "task", It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                sequence.Add("append");
                return Task.FromResult(Record(1, "event-1", "task"));
            });

        _translatorMock
            .Setup(t => t.Translate(It.IsAny<ParsedA2AEvent>(), It.IsAny<TranslationContext>()))
            .Returns(new AGUIBaseEvent[] { AGUIEventFactory.CreateRunStarted(SessionId, "run-1") });

        _groupClientMock
            .Setup(c => c.SendCoreAsync(
                It.IsAny<string>(),
                It.IsAny<object?[]>(),
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                sequence.Add("broadcast");
                return Task.CompletedTask;
            });

        await _ingestor.IngestAsync(ProjectId, SessionId, "task",
            JsonDocument.Parse("""{"id":"t-1","status":{"state":"submitted"}}""").RootElement);

        Assert.That(sequence, Is.EqualTo(new[] { "append", "broadcast" }));
    }

    [Test]
    public async Task IngestAsync_BroadcastsEnvelopePerTranslatedEvent_AllSharingParentSeqAndEventId()
    {
        var record = Record(7, "event-7", "message");
        _storeMock
            .Setup(s => s.AppendAsync(ProjectId, SessionId, "message", It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);

        var translated = new AGUIBaseEvent[]
        {
            AGUIEventFactory.CreateTextMessageStart("msg-1"),
            AGUIEventFactory.CreateTextMessageContent("msg-1", "hello"),
            AGUIEventFactory.CreateTextMessageEnd("msg-1"),
        };
        _translatorMock
            .Setup(t => t.Translate(It.IsAny<ParsedA2AEvent>(), It.IsAny<TranslationContext>()))
            .Returns(translated);

        var captured = new List<SessionEventEnvelope>();
        _groupClientMock
            .Setup(c => c.SendCoreAsync(
                AGUIEventType.ReceiveSessionEvent,
                It.IsAny<object?[]>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, object?[], CancellationToken>((_, args, _) =>
            {
                // args: [sessionId, envelope]
                if (args.Length >= 2 && args[1] is SessionEventEnvelope env)
                {
                    captured.Add(env);
                }
            })
            .Returns(Task.CompletedTask);

        // An A2A message JSON shape the parser accepts (matches A2AMessageParserTests fixture).
        var payload = JsonDocument.Parse("""
        {
            "kind": "message",
            "messageId": "m-1",
            "role": "agent",
            "parts": [ { "kind": "text", "text": "hi" } ],
            "contextId": "ctx-1",
            "taskId": "t-1"
        }
        """).RootElement;
        await _ingestor.IngestAsync(ProjectId, SessionId, "message", payload);

        Assert.That(captured, Has.Count.EqualTo(3));
        Assert.Multiple(() =>
        {
            Assert.That(captured[0].Seq, Is.EqualTo(7));
            Assert.That(captured[1].Seq, Is.EqualTo(7));
            Assert.That(captured[2].Seq, Is.EqualTo(7));
            Assert.That(captured[0].EventId, Is.EqualTo("event-7"));
            Assert.That(captured[1].EventId, Is.EqualTo("event-7"));
            Assert.That(captured[2].EventId, Is.EqualTo("event-7"));
            Assert.That(captured[0].Event, Is.InstanceOf<TextMessageStartEvent>());
            Assert.That(captured[1].Event, Is.InstanceOf<TextMessageContentEvent>());
            Assert.That(captured[2].Event, Is.InstanceOf<TextMessageEndEvent>());
        });
    }

    // ---------------- Failure isolation ----------------

    [Test]
    public async Task IngestAsync_BroadcastFailure_DoesNotReverseStoreAppend()
    {
        var record = Record(3, "event-3", "task");
        _storeMock
            .Setup(s => s.AppendAsync(ProjectId, SessionId, "task", It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);

        _translatorMock
            .Setup(t => t.Translate(It.IsAny<ParsedA2AEvent>(), It.IsAny<TranslationContext>()))
            .Returns(new AGUIBaseEvent[] { AGUIEventFactory.CreateRunStarted(SessionId, "run-3") });

        _groupClientMock
            .Setup(c => c.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("hub is down"));

        // Broadcast failure must not propagate — the append already succeeded and future
        // replays will serve this event.
        var payload = JsonDocument.Parse("""{"id":"t-3","status":{"state":"submitted"}}""").RootElement;
        await _ingestor.IngestAsync(ProjectId, SessionId, "task", payload);

        _storeMock.Verify(s =>
            s.AppendAsync(ProjectId, SessionId, "task", It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task IngestAsync_UnparsableA2APayload_EmitsRawCustomEnvelope()
    {
        var record = Record(1, "event-1", "unknown-kind");
        _storeMock
            .Setup(s => s.AppendAsync(ProjectId, SessionId, "unknown-kind", It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);

        var captured = new List<SessionEventEnvelope>();
        _groupClientMock
            .Setup(c => c.SendCoreAsync(
                AGUIEventType.ReceiveSessionEvent,
                It.IsAny<object?[]>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, object?[], CancellationToken>((_, args, _) =>
            {
                if (args.Length >= 2 && args[1] is SessionEventEnvelope env)
                {
                    captured.Add(env);
                }
            })
            .Returns(Task.CompletedTask);

        var payload = JsonDocument.Parse("""{"weird":"payload"}""").RootElement;
        // unknown-kind is not an A2A event kind, so the parser returns null and the
        // ingestor must still deliver a Custom "raw" envelope.
        await _ingestor.IngestAsync(ProjectId, SessionId, "unknown-kind", payload);

        Assert.That(captured, Has.Count.EqualTo(1));
        Assert.That(captured[0].Event, Is.InstanceOf<CustomEvent>());
        var custom = (CustomEvent)captured[0].Event;
        Assert.That(custom.Name, Is.EqualTo(AGUICustomEventName.Raw));

        // Translator must not be called for unparsable input — that's the escape hatch.
        _translatorMock.Verify(
            t => t.Translate(It.IsAny<ParsedA2AEvent>(), It.IsAny<TranslationContext>()),
            Times.Never);
    }
}
