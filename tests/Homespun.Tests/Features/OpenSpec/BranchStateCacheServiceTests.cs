using Homespun.Features.OpenSpec.Services;
using Homespun.Shared.Models.OpenSpec;
using Microsoft.Extensions.Time.Testing;

namespace Homespun.Tests.Features.OpenSpec;

[TestFixture]
public class BranchStateCacheServiceTests
{
    private FakeTimeProvider _time = null!;
    private BranchStateCacheService _cache = null!;

    [SetUp]
    public void SetUp()
    {
        _time = new FakeTimeProvider(DateTimeOffset.Parse("2026-04-16T12:00:00Z"));
        _cache = new BranchStateCacheService(_time);
    }

    [Test]
    public void Ttl_IsSixtySeconds()
    {
        Assert.That(_cache.Ttl, Is.EqualTo(TimeSpan.FromSeconds(60)));
    }

    [Test]
    public void PutThenTryGet_ReturnsSnapshot()
    {
        var snapshot = CreateSnapshot("p1", "feat/foo+abc");
        _cache.Put(snapshot);

        var retrieved = _cache.TryGet("p1", "feat/foo+abc");

        Assert.That(retrieved, Is.SameAs(snapshot));
    }

    [Test]
    public void TryGet_ExpiredEntry_ReturnsNullAndEvicts()
    {
        _cache.Put(CreateSnapshot("p1", "b1"));
        _time.Advance(TimeSpan.FromSeconds(61));

        Assert.That(_cache.TryGet("p1", "b1"), Is.Null);

        // A subsequent fresh put should work without stale-entry interference.
        _cache.Put(CreateSnapshot("p1", "b1"));
        Assert.That(_cache.TryGet("p1", "b1"), Is.Not.Null);
    }

    [Test]
    public void TryGet_NearlyExpired_StillReturnsValue()
    {
        _cache.Put(CreateSnapshot("p1", "b1"));
        _time.Advance(TimeSpan.FromSeconds(59));

        Assert.That(_cache.TryGet("p1", "b1"), Is.Not.Null);
    }

    [Test]
    public void Invalidate_DropsEntry()
    {
        _cache.Put(CreateSnapshot("p1", "b1"));
        _cache.Invalidate("p1", "b1");

        Assert.That(_cache.TryGet("p1", "b1"), Is.Null);
    }

    [Test]
    public void DifferentBranches_IsolatedByKey()
    {
        var a = CreateSnapshot("p1", "a");
        var b = CreateSnapshot("p1", "b");
        _cache.Put(a);
        _cache.Put(b);

        Assert.That(_cache.TryGet("p1", "a"), Is.SameAs(a));
        Assert.That(_cache.TryGet("p1", "b"), Is.SameAs(b));
    }

    [Test]
    public void SameBranchDifferentProjects_IsolatedByKey()
    {
        var a = CreateSnapshot("p1", "b");
        var b = CreateSnapshot("p2", "b");
        _cache.Put(a);
        _cache.Put(b);

        Assert.That(_cache.TryGet("p1", "b"), Is.SameAs(a));
        Assert.That(_cache.TryGet("p2", "b"), Is.SameAs(b));
    }

    private BranchStateSnapshot CreateSnapshot(string projectId, string branch) => new()
    {
        ProjectId = projectId,
        Branch = branch,
        FleeceId = "id",
        CapturedAt = _time.GetUtcNow()
    };
}
