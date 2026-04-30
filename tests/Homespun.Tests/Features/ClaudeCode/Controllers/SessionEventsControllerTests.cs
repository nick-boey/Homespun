using System.Text.Json;
using Homespun.Features.ClaudeCode.Controllers;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.ClaudeCode.Settings;
using Homespun.Features.Observability;
using Homespun.Shared.Models.Sessions;
using Homespun.Tests.Helpers;
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
        out CapturingLogger<SessionEventsController> logger)
    {
        var store = new Mock<IA2AEventStore>();
        store.Setup(s => s.ReadAsync(SessionId, It.IsAny<long?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(records);

        var translator = new A2AToAGUITranslator(new PendingToolCallRegistry());
        var options = Options.Create(new SessionEventsOptions { ReplayMode = SessionEventsReplayMode.Incremental });
        var debugOptions = new SessionEventIngestorTests.StaticOptionsMonitor<SessionDebugLoggingOptions>(
            new SessionDebugLoggingOptions { FullMessages = fullMessages });

        logger = new CapturingLogger<SessionEventsController>();

        return new SessionEventsController(
            store.Object,
            translator,
            options,
            debugOptions,
            logger);
    }

    [Test]
    public async Task GetEvents_FullMessagesOn_OpensReplayScopeAndEmitsPerEventLogs()
    {
        var records = new[]
        {
            Record(1, "task", """{"kind":"task","id":"t-1","contextId":"ctx-1","status":{"state":"submitted"}}"""),
        };

        var controller = BuildController(records, fullMessages: true, out var logger);

        await controller.GetEvents(SessionId, since: null, mode: null, ct: default);

        // Replay scope must carry homespun.replay=true so agui.replay + agui.translate entries are filterable.
        Assert.That(logger.Scopes, Is.Not.Empty, "expected a logger scope for replay full-body path");
        var replayScope = logger.Scopes.FirstOrDefault(s => s.ContainsKey("homespun.replay"));
        Assert.That(replayScope, Is.Not.Null);
        Assert.That(replayScope!["homespun.replay"], Is.EqualTo(true));

        var aguiReplay = logger.Entries.SingleOrDefault(e => e.EventId.Name == "agui.replay");
        Assert.That(aguiReplay, Is.Not.Null, "expected per-event agui.replay log");
        Assert.Multiple(() =>
        {
            Assert.That(aguiReplay!.Tags["homespun.seq"], Is.EqualTo(1L));
            Assert.That(aguiReplay.Tags["homespun.session.id"], Is.EqualTo(SessionId));
            Assert.That(aguiReplay.Tags["homespun.agui.type"], Is.EqualTo("RUN_STARTED"));
            Assert.That(aguiReplay.Tags["homespun.body"], Is.Not.Null);
        });

        var batch = logger.Entries.SingleOrDefault(e => e.EventId.Name == "agui.replay.batch");
        Assert.That(batch, Is.Not.Null, "expected per-batch summary log");
        Assert.Multiple(() =>
        {
            Assert.That(batch!.Tags["homespun.session.id"], Is.EqualTo(SessionId));
            Assert.That(batch.Tags["homespun.replay.mode"], Is.EqualTo(SessionEventsReplayMode.Incremental));
            Assert.That(batch.Tags["homespun.replay.count"], Is.EqualTo(1));
        });
    }

    [Test]
    public async Task GetEvents_FullMessagesOff_EmitsNoDebugLogsAndNoReplayScope()
    {
        var records = new[]
        {
            Record(1, "task", """{"kind":"task","id":"t-1","contextId":"ctx-1","status":{"state":"submitted"}}"""),
        };

        var controller = BuildController(records, fullMessages: false, out var logger);

        await controller.GetEvents(SessionId, since: null, mode: null, ct: default);

        Assert.That(logger.Scopes.Any(s => s.ContainsKey("homespun.replay")), Is.False,
            "no replay scope should be opened when full-body logging is off");
        Assert.That(
            logger.Entries.Any(e => e.EventId.Name == "agui.replay" || e.EventId.Name == "agui.replay.batch"),
            Is.False,
            "no agui.replay* log entries expected when full-body logging is off");
    }
}
