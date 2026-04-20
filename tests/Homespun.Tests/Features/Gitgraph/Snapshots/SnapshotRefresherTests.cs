using Homespun.Features.Gitgraph.Services;
using Homespun.Features.Gitgraph.Snapshots;
using Homespun.Shared.Models.Fleece;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace Homespun.Tests.Features.Gitgraph.Snapshots;

[TestFixture]
public class SnapshotRefresherTests
{
    [Test]
    public async Task RefreshOnceAsync_Rebuilds_All_Tracked_Snapshots()
    {
        var time = new FakeTimeProvider();
        var store = new ProjectTaskGraphSnapshotStore(time);

        var initial = new TaskGraphResponse();
        var refreshed = new TaskGraphResponse();

        store.Store("proj", 5, initial, time.GetUtcNow());

        var graphService = new Mock<IGraphService>();
        graphService.Setup(g => g.BuildEnhancedTaskGraphAsync("proj", 5))
            .ReturnsAsync(refreshed);

        var services = new ServiceCollection();
        services.AddSingleton(graphService.Object);
        using var provider = services.BuildServiceProvider();

        var refresher = new TaskGraphSnapshotRefresher(
            provider,
            store,
            Options.Create(new TaskGraphSnapshotOptions { Enabled = true }),
            time,
            NullLogger<TaskGraphSnapshotRefresher>.Instance);

        await refresher.RefreshOnceAsync(CancellationToken.None);

        Assert.That(store.TryGet("proj", 5)!.Response, Is.SameAs(refreshed));
        graphService.Verify(g => g.BuildEnhancedTaskGraphAsync("proj", 5), Times.Once);
    }

    [Test]
    public async Task RefreshOnceAsync_Does_Not_Store_When_Build_Returns_Null()
    {
        var time = new FakeTimeProvider();
        var store = new ProjectTaskGraphSnapshotStore(time);
        var initial = new TaskGraphResponse();
        store.Store("proj", 5, initial, time.GetUtcNow());

        var graphService = new Mock<IGraphService>();
        graphService.Setup(g => g.BuildEnhancedTaskGraphAsync("proj", 5))
            .ReturnsAsync((TaskGraphResponse?)null);

        var services = new ServiceCollection();
        services.AddSingleton(graphService.Object);
        using var provider = services.BuildServiceProvider();

        var refresher = new TaskGraphSnapshotRefresher(
            provider, store,
            Options.Create(new TaskGraphSnapshotOptions { Enabled = true }),
            time,
            NullLogger<TaskGraphSnapshotRefresher>.Instance);

        await refresher.RefreshOnceAsync(CancellationToken.None);

        // Existing entry preserved.
        Assert.That(store.TryGet("proj", 5)!.Response, Is.SameAs(initial));
    }

    [Test]
    public async Task InvalidateProject_Removes_Entry_Forcing_Next_Rebuild()
    {
        var time = new FakeTimeProvider();
        var store = new ProjectTaskGraphSnapshotStore(time);
        store.Store("proj", 5, new TaskGraphResponse(), time.GetUtcNow());

        store.InvalidateProject("proj");

        Assert.That(store.TryGet("proj", 5), Is.Null);
    }

    [Test]
    public async Task RefreshOnceAsync_Swallows_Exceptions_Per_Project()
    {
        var time = new FakeTimeProvider();
        var store = new ProjectTaskGraphSnapshotStore(time);
        store.Store("bad", 5, new TaskGraphResponse(), time.GetUtcNow());
        store.Store("good", 5, new TaskGraphResponse(), time.GetUtcNow());

        var graphService = new Mock<IGraphService>();
        graphService.Setup(g => g.BuildEnhancedTaskGraphAsync("bad", 5))
            .ThrowsAsync(new InvalidOperationException("boom"));
        var goodResponse = new TaskGraphResponse();
        graphService.Setup(g => g.BuildEnhancedTaskGraphAsync("good", 5))
            .ReturnsAsync(goodResponse);

        var services = new ServiceCollection();
        services.AddSingleton(graphService.Object);
        using var provider = services.BuildServiceProvider();

        var refresher = new TaskGraphSnapshotRefresher(
            provider, store,
            Options.Create(new TaskGraphSnapshotOptions { Enabled = true }),
            time,
            NullLogger<TaskGraphSnapshotRefresher>.Instance);

        await refresher.RefreshOnceAsync(CancellationToken.None);

        // Good project updated even though bad threw.
        Assert.That(store.TryGet("good", 5)!.Response, Is.SameAs(goodResponse));
    }
}
