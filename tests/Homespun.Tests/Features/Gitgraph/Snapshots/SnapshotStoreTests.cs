using Homespun.Features.Gitgraph.Snapshots;
using Homespun.Shared.Models.Fleece;
using Microsoft.Extensions.Time.Testing;

namespace Homespun.Tests.Features.Gitgraph.Snapshots;

[TestFixture]
public class SnapshotStoreTests
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

    [Test]
    public void TryGet_On_Miss_Returns_Null()
    {
        Assert.That(_store.TryGet("proj", 5), Is.Null);
    }

    [Test]
    public void Store_Then_TryGet_Returns_Same_Entry_And_Updates_LastAccessedAt()
    {
        var response = new TaskGraphResponse();
        _store.Store("proj", 5, response, _time.GetUtcNow());

        _time.Advance(TimeSpan.FromSeconds(30));

        var hit = _store.TryGet("proj", 5);
        Assert.That(hit, Is.Not.Null);
        Assert.That(hit!.Response, Is.SameAs(response));
        Assert.That(hit.LastAccessedAt, Is.EqualTo(_time.GetUtcNow()));
    }

    [Test]
    public void Store_Twice_Replaces_Entry()
    {
        var first = new TaskGraphResponse();
        var second = new TaskGraphResponse();
        _store.Store("proj", 5, first, _time.GetUtcNow());
        _store.Store("proj", 5, second, _time.GetUtcNow());

        var hit = _store.TryGet("proj", 5);
        Assert.That(hit!.Response, Is.SameAs(second));
    }

    [Test]
    public void InvalidateProject_Removes_All_Keys_For_That_Project()
    {
        _store.Store("proj", 5, new TaskGraphResponse(), _time.GetUtcNow());
        _store.Store("proj", 10, new TaskGraphResponse(), _time.GetUtcNow());
        _store.Store("other", 5, new TaskGraphResponse(), _time.GetUtcNow());

        _store.InvalidateProject("proj");

        Assert.That(_store.TryGet("proj", 5), Is.Null);
        Assert.That(_store.TryGet("proj", 10), Is.Null);
        Assert.That(_store.TryGet("other", 5), Is.Not.Null);
    }

    [Test]
    public void EvictIdle_Drops_Stale_Entries()
    {
        _store.Store("fresh", 5, new TaskGraphResponse(), _time.GetUtcNow());
        _time.Advance(TimeSpan.FromMinutes(10));
        _store.Store("still-warm", 5, new TaskGraphResponse(), _time.GetUtcNow());

        // Read "still-warm" to refresh its LastAccessedAt.
        _store.TryGet("still-warm", 5);

        var evicted = _store.EvictIdle(_time.GetUtcNow() - TimeSpan.FromMinutes(5));
        Assert.That(evicted, Is.EqualTo(1));
        Assert.That(_store.TryGet("fresh", 5), Is.Null);
        Assert.That(_store.TryGet("still-warm", 5), Is.Not.Null);
    }

    [Test]
    public void GetTrackedKeys_Returns_All_Current_Keys()
    {
        _store.Store("a", 5, new TaskGraphResponse(), _time.GetUtcNow());
        _store.Store("b", 10, new TaskGraphResponse(), _time.GetUtcNow());

        var keys = _store.GetTrackedKeys();
        Assert.That(keys, Has.Count.EqualTo(2));
        Assert.That(keys, Does.Contain(("a", 5)));
        Assert.That(keys, Does.Contain(("b", 10)));
    }
}
