using System.Collections.Concurrent;
using Homespun.Features.Gitgraph.Snapshots;
using Homespun.Features.Notifications;
using Homespun.Shared.Models.Fleece;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace Homespun.Tests.Features.Gitgraph.Snapshots;

/// <summary>
/// Race regression — fires many concurrent <c>BroadcastIssueTopologyChanged</c>
/// calls and asserts that any <c>TryGet</c> interleaved between
/// invalidation and broadcast never observes a pre-mutation snapshot.
/// </summary>
[TestFixture]
public class BroadcastHelperRaceTests
{
    [Test]
    public async Task ConcurrentBroadcasts_NeverLeakStaleSnapshot()
    {
        var time = new FakeTimeProvider();
        time.SetUtcNow(new DateTimeOffset(2026, 4, 21, 0, 0, 0, TimeSpan.Zero));
        var store = new ProjectTaskGraphSnapshotStore(time);

        const string projectId = "race-project";

        // Seed the store with a baseline snapshot; BuiltAt acts as a version.
        var baseline = new TaskGraphResponse();
        var baselineBuiltAt = time.GetUtcNow();
        store.Store(projectId, 10, baseline, baselineBuiltAt);

        // Advance the clock to define a "mutation start" cutoff. Any snapshot
        // observable after this moment must have been built at or after it.
        time.Advance(TimeSpan.FromSeconds(1));
        var mutationStart = time.GetUtcNow();

        var allProxy = new Mock<IClientProxy>();
        allProxy
            .Setup(x => x.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), default))
            .Returns(Task.CompletedTask);

        var groupProxy = new Mock<IClientProxy>();
        groupProxy
            .Setup(x => x.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), default))
            .Returns(Task.CompletedTask);

        var clients = new Mock<IHubClients>();
        clients.Setup(x => x.All).Returns(allProxy.Object);
        clients.Setup(x => x.Group(It.IsAny<string>())).Returns(groupProxy.Object);

        var hubContext = new Mock<IHubContext<NotificationHub>>();
        hubContext.Setup(x => x.Clients).Returns(clients.Object);

        var services = new ServiceCollection();
        services.AddSingleton<IProjectTaskGraphSnapshotStore>(store);
        var provider = services.BuildServiceProvider();

        var staleObservations = new ConcurrentBag<DateTimeOffset>();

        const int threads = 10;
        const int iterationsPerThread = 5;

        var tasks = Enumerable.Range(0, threads).Select(_ => Task.Run(async () =>
        {
            for (var i = 0; i < iterationsPerThread; i++)
            {
                await hubContext.Object.BroadcastIssueTopologyChanged(
                    provider, projectId, IssueChangeType.Updated, "issue-1");

                // Immediately probe the store — any entry observed here must either
                // be null (invalidated) or built after the mutation started.
                var hit = store.TryGet(projectId, 10);
                if (hit is not null && hit.LastBuiltAt < mutationStart)
                {
                    staleObservations.Add(hit.LastBuiltAt);
                }
            }
        })).ToArray();

        await Task.WhenAll(tasks);

        Assert.That(staleObservations, Is.Empty,
            $"A stale pre-mutation snapshot was observable after invalidation fired: " +
            $"{string.Join(",", staleObservations)}");
    }
}
