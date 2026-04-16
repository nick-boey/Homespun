using System.Text.Json;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Shared.Models.Sessions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.ClaudeCode;

/// <summary>
/// TDD tests for <see cref="A2AEventStore"/>.
/// Covers tasks 2.1–2.7 of the a2a-native-messaging OpenSpec change.
/// </summary>
[TestFixture]
public class A2AEventStoreTests
{
    private string _testDir = null!;
    private A2AEventStore _store = null!;
    private Mock<ILogger<A2AEventStore>> _loggerMock = null!;

    private const string ProjectId = "project-1";
    private const string SessionId = "session-1";

    [SetUp]
    public void SetUp()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"a2a-event-store-{Guid.NewGuid()}");
        _loggerMock = new Mock<ILogger<A2AEventStore>>();
        _store = new A2AEventStore(_testDir, _loggerMock.Object);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }
    }

    private static JsonElement Payload(string kind = "task", int index = 0)
    {
        var json = $$"""{"kind":"{{kind}}","index":{{index}}}""";
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    // ---------------- 2.1 ----------------

    [Test]
    public async Task AppendAsync_AssignsMonotonicSeqStartingFromOne()
    {
        var first = await _store.AppendAsync(ProjectId, SessionId, "task", Payload("task", 0));
        var second = await _store.AppendAsync(ProjectId, SessionId, "message", Payload("message", 1));
        var third = await _store.AppendAsync(ProjectId, SessionId, "message", Payload("message", 2));

        Assert.Multiple(() =>
        {
            Assert.That(first.Seq, Is.EqualTo(1));
            Assert.That(second.Seq, Is.EqualTo(2));
            Assert.That(third.Seq, Is.EqualTo(3));
        });
    }

    [Test]
    public async Task AppendAsync_AssignsUniqueEventIds()
    {
        var a = await _store.AppendAsync(ProjectId, SessionId, "task", Payload());
        var b = await _store.AppendAsync(ProjectId, SessionId, "task", Payload());

        Assert.That(a.EventId, Is.Not.EqualTo(b.EventId));
        Assert.That(Guid.TryParse(a.EventId, out _), Is.True, "eventId should be a valid GUID");
    }

    // ---------------- 2.2 ----------------

    [Test]
    public async Task AppendAsync_PersistsToJsonlFileOneRecordPerLine()
    {
        await _store.AppendAsync(ProjectId, SessionId, "task", Payload("task", 0));
        await _store.AppendAsync(ProjectId, SessionId, "message", Payload("message", 1));

        var expectedPath = Path.Combine(_testDir, ProjectId, $"{SessionId}.events.jsonl");
        Assert.That(File.Exists(expectedPath), Is.True,
            $"expected events file at {expectedPath}");

        var lines = await File.ReadAllLinesAsync(expectedPath);
        var nonEmptyLines = lines.Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
        Assert.That(nonEmptyLines, Has.Length.EqualTo(2));

        // Each line is a parseable A2AEventRecord
        foreach (var line in nonEmptyLines)
        {
            var record = JsonSerializer.Deserialize<A2AEventRecord>(line, TestJsonOptions);
            Assert.That(record, Is.Not.Null);
        }
    }

    // ---------------- 2.3 ----------------

    [Test]
    public async Task ReadAsync_WithNullSince_ReturnsAllEventsInSeqOrder()
    {
        await _store.AppendAsync(ProjectId, SessionId, "task", Payload("task", 0));
        await _store.AppendAsync(ProjectId, SessionId, "message", Payload("message", 1));
        await _store.AppendAsync(ProjectId, SessionId, "status-update", Payload("status-update", 2));

        var events = await _store.ReadAsync(SessionId, since: null);

        Assert.That(events, Is.Not.Null);
        Assert.That(events!.Select(e => e.Seq), Is.EqualTo(new long[] { 1, 2, 3 }));
        Assert.That(events.Select(e => e.EventKind),
            Is.EqualTo(new[] { "task", "message", "status-update" }));
    }

    // ---------------- 2.4 ----------------

    [Test]
    public async Task ReadAsync_WithSinceN_ReturnsOnlyEventsWithSeqGreaterThanN()
    {
        for (var i = 0; i < 5; i++)
        {
            await _store.AppendAsync(ProjectId, SessionId, "message", Payload("message", i));
        }

        var events = await _store.ReadAsync(SessionId, since: 2);

        Assert.That(events, Is.Not.Null);
        Assert.That(events!.Select(e => e.Seq), Is.EqualTo(new long[] { 3, 4, 5 }));
    }

    // ---------------- 2.5 ----------------

    [Test]
    public async Task ReadAsync_WithSinceBeyondEnd_ReturnsEmptyArrayNotNull()
    {
        await _store.AppendAsync(ProjectId, SessionId, "task", Payload());
        await _store.AppendAsync(ProjectId, SessionId, "message", Payload());

        var events = await _store.ReadAsync(SessionId, since: 999);

        Assert.That(events, Is.Not.Null,
            "since-beyond-end must be distinguishable from unknown-session (empty, not null)");
        Assert.That(events, Is.Empty);
    }

    // ---------------- 2.6 ----------------

    [Test]
    public async Task ReadAsync_ForUnknownSession_ReturnsNull()
    {
        var events = await _store.ReadAsync("no-such-session", since: null);

        Assert.That(events, Is.Null,
            "unknown sessions must return null to distinguish from empty-but-exists");
    }

    [Test]
    public async Task ReadAsync_ForAppendedThenSinceBeyondEnd_ReturnsEmptyButReadForUnknownReturnsNull()
    {
        await _store.AppendAsync(ProjectId, SessionId, "task", Payload());

        var existingButCaughtUp = await _store.ReadAsync(SessionId, since: 999);
        var unknown = await _store.ReadAsync("other-session", since: null);

        Assert.That(existingButCaughtUp, Is.Not.Null);
        Assert.That(existingButCaughtUp, Is.Empty);
        Assert.That(unknown, Is.Null);
    }

    // ---------------- 2.7 ----------------

    [Test]
    public async Task AppendAsync_ConcurrentAppendsToSameSessionPreserveMonotonicity()
    {
        const int appendsPerTask = 25;
        const int taskCount = 8;

        var tasks = Enumerable.Range(0, taskCount)
            .Select(_ => Task.Run(async () =>
            {
                var records = new List<A2AEventRecord>();
                for (var i = 0; i < appendsPerTask; i++)
                {
                    records.Add(await _store.AppendAsync(ProjectId, SessionId, "message", Payload("message", i)));
                }
                return records;
            }))
            .ToArray();

        await Task.WhenAll(tasks);

        var events = await _store.ReadAsync(SessionId, since: null);
        Assert.That(events, Is.Not.Null);

        var seqs = events!.Select(e => e.Seq).ToArray();
        Assert.That(seqs, Has.Length.EqualTo(taskCount * appendsPerTask));

        // Seqs must be the exact sequence 1..N, strictly monotonic, without gaps or duplicates.
        Assert.That(seqs, Is.EqualTo(Enumerable.Range(1, taskCount * appendsPerTask).Select(i => (long)i).ToArray()));
    }

    // ---------------- Misc supporting ----------------

    [Test]
    public async Task AppendAsync_MultipleSessionsAreIndependent()
    {
        await _store.AppendAsync(ProjectId, "session-A", "task", Payload());
        await _store.AppendAsync(ProjectId, "session-A", "message", Payload());
        var bFirst = await _store.AppendAsync(ProjectId, "session-B", "task", Payload());

        Assert.That(bFirst.Seq, Is.EqualTo(1),
            "each session has its own monotonic seq starting from 1");

        var a = await _store.ReadAsync("session-A");
        var b = await _store.ReadAsync("session-B");
        Assert.That(a!.Select(e => e.Seq), Is.EqualTo(new long[] { 1, 2 }));
        Assert.That(b!.Select(e => e.Seq), Is.EqualTo(new long[] { 1 }));
    }

    [Test]
    public async Task ReadAsync_AfterRestart_RecoversFromDisk()
    {
        await _store.AppendAsync(ProjectId, SessionId, "task", Payload("task", 0));
        await _store.AppendAsync(ProjectId, SessionId, "message", Payload("message", 1));

        // Simulate process restart: new store instance, same dir
        var newStore = new A2AEventStore(_testDir, _loggerMock.Object);

        var events = await newStore.ReadAsync(SessionId);

        Assert.That(events, Is.Not.Null);
        Assert.That(events!.Select(e => e.Seq), Is.EqualTo(new long[] { 1, 2 }));
    }

    [Test]
    public async Task AppendAsync_AfterRestart_ContinuesSeqCounter()
    {
        await _store.AppendAsync(ProjectId, SessionId, "task", Payload());
        await _store.AppendAsync(ProjectId, SessionId, "message", Payload());

        var newStore = new A2AEventStore(_testDir, _loggerMock.Object);

        var next = await newStore.AppendAsync(ProjectId, SessionId, "message", Payload());
        Assert.That(next.Seq, Is.EqualTo(3));
    }

    private static readonly JsonSerializerOptions TestJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}
