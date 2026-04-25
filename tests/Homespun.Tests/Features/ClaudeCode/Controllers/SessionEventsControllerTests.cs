using System.Text.Json;
using Homespun.Features.ClaudeCode.Controllers;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.ClaudeCode.Settings;
using Homespun.Features.Observability;
using Homespun.Shared.Models.Sessions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Homespun.Tests.Features.ClaudeCode.Controllers;

/// <summary>
/// Tests the replay-endpoint's full-body debug log emission and
/// <c>homespun.replay=true</c> scope tagging.
/// </summary>
[TestFixture]
public class SessionEventsControllerTests
{
    private const string SessionId = "session-replay";
    private const string ProjectId = "proj-replay";

    private static A2AEventRecord Record(long seq, string kind, string rawJson) =>
        new(seq, SessionId, $"event-{seq}", kind, DateTime.UtcNow,
            JsonDocument.Parse(rawJson).RootElement.Clone());

    private static SessionEventsController BuildController(
        IReadOnlyList<A2AEventRecord> records,
        bool fullMessages,
        out Mock<ILogger<SessionEventsController>> loggerMock,
        out List<IReadOnlyDictionary<string, object>> capturedScopes)
    {
        var store = new Mock<IA2AEventStore>();
        store.Setup(s => s.ReadAsync(SessionId, It.IsAny<long?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(records);

        var translator = new A2AToAGUITranslator(new PendingToolCallRegistry());
        var options = Options.Create(new SessionEventsOptions { ReplayMode = SessionEventsReplayMode.Incremental });
        var debugOptions = new SessionEventIngestorTests.StaticOptionsMonitor<SessionDebugLoggingOptions>(
            new SessionDebugLoggingOptions { FullMessages = fullMessages });

        loggerMock = new Mock<ILogger<SessionEventsController>>();
        loggerMock.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);

        // Capture scopes so tests can assert homespun.replay=true was pushed.
        var scopes = new List<IReadOnlyDictionary<string, object>>();
        capturedScopes = scopes;
        loggerMock.Setup(l => l.BeginScope(It.IsAny<IReadOnlyDictionary<string, object>>()))
            .Callback<IReadOnlyDictionary<string, object>>(scopes.Add)
            .Returns(new DummyDisposable());

        return new SessionEventsController(
            store.Object,
            translator,
            options,
            debugOptions,
            loggerMock.Object);
    }

    private sealed class DummyDisposable : IDisposable
    {
        public void Dispose() { }
    }

    [Test]
    public async Task GetEvents_FullMessagesOn_OpensReplayScopeAndEmitsPerEventLogs()
    {
        var records = new[]
        {
            Record(1, "task", """{"kind":"task","id":"t-1","contextId":"ctx-1","status":{"state":"submitted"}}"""),
        };

        var controller = BuildController(
            records,
            fullMessages: true,
            out var loggerMock,
            out var scopes);

        await controller.GetEvents(SessionId, since: null, mode: null, ct: default);

        // Replay scope must carry homespun.replay=true so agui.replay + agui.translate entries are filterable.
        Assert.That(scopes, Is.Not.Empty, "expected a logger scope for replay full-body path");
        var replayScope = scopes.FirstOrDefault(s => s.ContainsKey("homespun.replay"));
        Assert.That(replayScope, Is.Not.Null);
        Assert.That(replayScope!["homespun.replay"], Is.EqualTo(true));

        // agui.replay per-event body log + agui.replay.batch summary log.
        loggerMock.Verify(l => l.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((o, _) => o.ToString()!.Contains("agui.replay") && o.ToString()!.Contains("seq=1")),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce,
            "expected per-event agui.replay log");

        loggerMock.Verify(l => l.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((o, _) => o.ToString()!.Contains("agui.replay.batch")),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once,
            "expected per-batch summary log");
    }

    [Test]
    public async Task GetEvents_FullMessagesOff_EmitsNoDebugLogsAndNoReplayScope()
    {
        var records = new[]
        {
            Record(1, "task", """{"kind":"task","id":"t-1","contextId":"ctx-1","status":{"state":"submitted"}}"""),
        };

        var controller = BuildController(
            records,
            fullMessages: false,
            out var loggerMock,
            out var scopes);

        await controller.GetEvents(SessionId, since: null, mode: null, ct: default);

        Assert.That(scopes.Any(s => s.ContainsKey("homespun.replay")), Is.False,
            "no replay scope should be opened when full-body logging is off");

        loggerMock.Verify(l => l.Log(
            It.IsAny<LogLevel>(),
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((o, _) => o.ToString()!.Contains("agui.replay")),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never,
            "no agui.replay log entries expected when full-body logging is off");
    }
}
