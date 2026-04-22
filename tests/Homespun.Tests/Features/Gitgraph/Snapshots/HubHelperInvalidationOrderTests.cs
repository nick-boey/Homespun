using System.Collections.Concurrent;
using Homespun.Features.Gitgraph.Snapshots;
using Homespun.Features.Notifications;
using Homespun.Shared.Models.Fleece;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Homespun.Tests.Features.Gitgraph.Snapshots;

/// <summary>
/// Asserts that <see cref="NotificationHubExtensions.BroadcastIssueTopologyChanged"/>
/// calls <c>InvalidateProject</c> strictly before <c>SendAsync("IssuesChanged", …)</c>.
/// Inverting this ordering reintroduces the stale-refetch race that prompted this change.
/// </summary>
[TestFixture]
public class HubHelperInvalidationOrderTests
{
    [Test]
    public async Task BroadcastIssueTopologyChanged_InvalidatesBeforeSending()
    {
        var callLog = new ConcurrentQueue<string>();

        var store = new Mock<IProjectTaskGraphSnapshotStore>();
        store.Setup(s => s.InvalidateProject(It.IsAny<string>()))
            .Callback<string>(_ => callLog.Enqueue("invalidate"));

        var allProxy = new Mock<IClientProxy>();
        allProxy
            .Setup(x => x.SendCoreAsync("IssuesChanged", It.IsAny<object?[]>(), default))
            .Callback(() => callLog.Enqueue("send-all"))
            .Returns(Task.CompletedTask);

        var groupProxy = new Mock<IClientProxy>();
        groupProxy
            .Setup(x => x.SendCoreAsync("IssuesChanged", It.IsAny<object?[]>(), default))
            .Callback(() => callLog.Enqueue("send-group"))
            .Returns(Task.CompletedTask);

        var clients = new Mock<IHubClients>();
        clients.Setup(x => x.All).Returns(allProxy.Object);
        clients.Setup(x => x.Group(It.IsAny<string>())).Returns(groupProxy.Object);

        var hubContext = new Mock<IHubContext<NotificationHub>>();
        hubContext.Setup(x => x.Clients).Returns(clients.Object);

        var services = new ServiceCollection();
        services.AddSingleton(store.Object);
        var provider = services.BuildServiceProvider();

        await hubContext.Object.BroadcastIssueTopologyChanged(
            provider, "project-abc", IssueChangeType.Updated, "issue-1");

        var log = callLog.ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(log, Is.Not.Empty);
            Assert.That(log[0], Is.EqualTo("invalidate"),
                "InvalidateProject MUST run before any SendAsync — the client's refetch races the mutation, so a stale snapshot must not be readable when the broadcast fires");
            Assert.That(log.Contains("send-all"), Is.True, "broadcast to All must fire");
            Assert.That(log.Contains("send-group"), Is.True, "broadcast to project group must fire");
        });
    }

    [Test]
    public async Task BroadcastIssueTopologyChanged_ToleratesMissingSnapshotStore()
    {
        // TaskGraphSnapshot:Enabled=false leaves IProjectTaskGraphSnapshotStore unregistered.
        // The helper must still broadcast without throwing.
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

        var provider = new ServiceCollection().BuildServiceProvider();

        Assert.DoesNotThrowAsync(async () =>
        {
            await hubContext.Object.BroadcastIssueTopologyChanged(
                provider, "project-none", IssueChangeType.Updated, "issue-x");
        });

        allProxy.Verify(
            x => x.SendCoreAsync("IssuesChanged", It.IsAny<object?[]>(), default),
            Times.Once);
    }
}
