using Homespun.Features.Gitgraph.Snapshots;
using Homespun.Shared.Models.Fleece;
using Microsoft.Extensions.Time.Testing;

namespace Homespun.Tests.Features.Gitgraph.Snapshots;

/// <summary>
/// Coverage for <see cref="ProjectTaskGraphSnapshotStore.PatchIssueFields"/>:
/// the in-place field merge that skips a full rebuild for structure-preserving edits.
/// </summary>
[TestFixture]
public class PatchIssueFieldsTests
{
    private FakeTimeProvider _time = null!;
    private ProjectTaskGraphSnapshotStore _store = null!;

    [SetUp]
    public void SetUp()
    {
        _time = new FakeTimeProvider();
        _time.SetUtcNow(new DateTimeOffset(2026, 4, 21, 9, 0, 0, TimeSpan.Zero));
        _store = new ProjectTaskGraphSnapshotStore(_time);
    }

    private static TaskGraphResponse BuildResponse(params IssueResponse[] issues)
    {
        var response = new TaskGraphResponse();
        for (var i = 0; i < issues.Length; i++)
        {
            response.Nodes.Add(new TaskGraphNodeResponse
            {
                Issue = issues[i],
                Lane = 0,
                Row = i,
                IsActionable = false,
            });
        }
        return response;
    }

    private static IssueResponse Issue(string id, string title = "original", string? description = null)
        => new() { Id = id, Title = title, Description = description };

    [Test]
    public void Patch_Present_Issue_Updates_Title_And_Bumps_LastBuiltAt()
    {
        var response = BuildResponse(Issue("i-1", "original title"), Issue("i-2", "other"));
        var built = _time.GetUtcNow();
        _store.Store("proj", 5, response, built);

        _time.Advance(TimeSpan.FromSeconds(42));

        _store.PatchIssueFields("proj", "i-1", new IssueFieldPatch { Title = "patched title" });

        var entry = _store.TryGet("proj", 5)!;
        Assert.Multiple(() =>
        {
            Assert.That(entry.Response.Nodes[0].Issue.Title, Is.EqualTo("patched title"));
            Assert.That(entry.Response.Nodes[1].Issue.Title, Is.EqualTo("other"),
                "Non-matching nodes must not be touched.");
            Assert.That(entry.LastBuiltAt, Is.EqualTo(_time.GetUtcNow()),
                "LastBuiltAt must be bumped to 'now' after a successful patch.");
        });
    }

    [Test]
    public void Patch_Missing_Issue_Is_NoOp_And_Does_Not_Create_Entry()
    {
        _store.PatchIssueFields("ghost-project", "ghost-issue", new IssueFieldPatch { Title = "x" });
        Assert.That(_store.TryGet("ghost-project", 5), Is.Null);
    }

    [Test]
    public void Patch_Issue_Absent_From_Node_List_Is_NoOp_On_Existing_Entry()
    {
        var response = BuildResponse(Issue("i-1"), Issue("i-2"));
        _store.Store("proj", 5, response, _time.GetUtcNow());
        var entryBefore = _store.TryGet("proj", 5)!;
        var responseBefore = entryBefore.Response;
        var builtBefore = entryBefore.LastBuiltAt;

        _time.Advance(TimeSpan.FromSeconds(5));
        _store.PatchIssueFields("proj", "not-in-snapshot", new IssueFieldPatch { Title = "ignored" });

        var entryAfter = _store.TryGet("proj", 5)!;
        Assert.Multiple(() =>
        {
            Assert.That(entryAfter.Response, Is.SameAs(responseBefore),
                "Missing issue must leave response untouched (no fresh clone).");
            Assert.That(entryAfter.LastBuiltAt, Is.EqualTo(builtBefore),
                "LastBuiltAt must not bump when no node was patched.");
        });
    }

    [Test]
    public void Patch_Applies_To_Every_MaxPastPRs_Key_Belonging_To_Project()
    {
        var shared = Issue("i-1", "original");
        _store.Store("proj", 5, BuildResponse(shared, Issue("i-2")), _time.GetUtcNow());
        _store.Store("proj", 10, BuildResponse(Issue("i-1", "original"), Issue("i-2")), _time.GetUtcNow());
        _store.Store("other-proj", 5, BuildResponse(Issue("i-1", "unaffected")), _time.GetUtcNow());

        _store.PatchIssueFields("proj", "i-1", new IssueFieldPatch { Title = "patched" });

        Assert.Multiple(() =>
        {
            Assert.That(_store.TryGet("proj", 5)!.Response.Nodes[0].Issue.Title, Is.EqualTo("patched"));
            Assert.That(_store.TryGet("proj", 10)!.Response.Nodes[0].Issue.Title, Is.EqualTo("patched"));
            Assert.That(_store.TryGet("other-proj", 5)!.Response.Nodes[0].Issue.Title, Is.EqualTo("unaffected"),
                "Patches must not bleed into other projects.");
        });
    }

    [Test]
    public void Patch_Does_Not_Mutate_Existing_Response_Object()
    {
        var original = Issue("i-1", "original");
        var response = BuildResponse(original);
        _store.Store("proj", 5, response, _time.GetUtcNow());
        var originalResponse = _store.TryGet("proj", 5)!.Response;

        _store.PatchIssueFields("proj", "i-1", new IssueFieldPatch { Title = "patched" });

        Assert.Multiple(() =>
        {
            Assert.That(originalResponse.Nodes[0].Issue.Title, Is.EqualTo("original"),
                "The previously-held response object must be immutable — callers mid-serialisation rely on that.");
            Assert.That(originalResponse.Nodes[0], Is.SameAs(response.Nodes[0]),
                "Original node reference must still be intact for concurrent readers.");
        });
    }

    [Test]
    public void Patch_Overlays_Only_NonNull_Fields()
    {
        var original = new IssueResponse
        {
            Id = "i-1",
            Title = "keep",
            Description = "keep-desc",
            Priority = 3,
            Tags = ["t1"],
            AssignedTo = "alice",
        };
        _store.Store("proj", 5, BuildResponse(original), _time.GetUtcNow());

        _store.PatchIssueFields("proj", "i-1", new IssueFieldPatch { Priority = 5 });

        var patched = _store.TryGet("proj", 5)!.Response.Nodes[0].Issue;
        Assert.Multiple(() =>
        {
            Assert.That(patched.Title, Is.EqualTo("keep"));
            Assert.That(patched.Description, Is.EqualTo("keep-desc"));
            Assert.That(patched.Priority, Is.EqualTo(5));
            Assert.That(patched.Tags, Is.EqualTo(new[] { "t1" }));
            Assert.That(patched.AssignedTo, Is.EqualTo("alice"));
        });
    }

    [Test]
    public async Task Concurrent_Patch_And_Invalidate_Does_Not_Corrupt_State()
    {
        // Arrange: run 50 iterations of (patch || invalidate) in parallel; final state
        // must match either "entry present with patched issue" OR "entry absent".
        // Any other outcome (half-patched response, ghost entry after invalidate) is corruption.
        for (var iter = 0; iter < 50; iter++)
        {
            _store.Store("proj", 5, BuildResponse(Issue("i-1", $"rev-{iter}")), _time.GetUtcNow());

            var patch = Task.Run(() =>
                _store.PatchIssueFields("proj", "i-1", new IssueFieldPatch { Title = $"patched-{iter}" }));
            var invalidate = Task.Run(() => _store.InvalidateProject("proj"));

            await Task.WhenAll(patch, invalidate);

            var entry = _store.TryGet("proj", 5);
            if (entry is not null)
            {
                Assert.That(entry.Response.Nodes, Has.Count.EqualTo(1));
                Assert.That(entry.Response.Nodes[0].Issue.Id, Is.EqualTo("i-1"),
                    $"iter {iter}: entry present but node shape corrupted");
            }
        }
    }
}
