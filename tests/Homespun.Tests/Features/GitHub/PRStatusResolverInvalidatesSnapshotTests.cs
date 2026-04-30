using System.Collections.Concurrent;
using Homespun.Features.Gitgraph.Services;
using Homespun.Features.Gitgraph.Snapshots;
using Homespun.Features.GitHub;
using Homespun.Features.Notifications;
using Homespun.Features.Testing;
using Homespun.Shared.Models.GitHub;
using Homespun.Shared.Models.PullRequests;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Homespun.Tests.Features.GitHub;

/// <summary>
/// Asserts that <see cref="PRStatusResolver.ResolveClosedPRStatusesAsync"/>
/// invalidates the per-project task-graph snapshot and broadcasts
/// <c>IssuesChanged</c> via the hub helper after a PR resolves to Merged or
/// Closed. Mirrors the pattern in
/// <c>HubHelperInvalidationOrderTests</c>: invalidation MUST run before
/// <c>SendCoreAsync</c> so a client refetch cannot read a pre-transition snapshot.
/// </summary>
[TestFixture]
public class PRStatusResolverInvalidatesSnapshotTests
{
    private Mock<IGitHubService> _gitHubService = null!;
    private Mock<IGraphCacheService> _graphCacheService = null!;
    private Mock<IIssuePrLinkingService> _linkingService = null!;
    private Mock<IProjectTaskGraphSnapshotStore> _snapshotStore = null!;
    private Mock<IClientProxy> _allClientsMock = null!;
    private Mock<IClientProxy> _groupClientsMock = null!;
    private Mock<IHubContext<NotificationHub>> _hubContext = null!;
    private MockDataStore _dataStore = null!;
    private PRStatusResolver _resolver = null!;
    private ConcurrentQueue<string> _callLog = null!;

    [SetUp]
    public void SetUp()
    {
        _gitHubService = new Mock<IGitHubService>();
        _graphCacheService = new Mock<IGraphCacheService>();
        _linkingService = new Mock<IIssuePrLinkingService>();
        _dataStore = new MockDataStore();

        _callLog = new ConcurrentQueue<string>();

        _snapshotStore = new Mock<IProjectTaskGraphSnapshotStore>();
        _snapshotStore.Setup(s => s.InvalidateProject(It.IsAny<string>()))
            .Callback<string>(_ => _callLog.Enqueue("invalidate"));

        _allClientsMock = new Mock<IClientProxy>();
        _allClientsMock
            .Setup(x => x.SendCoreAsync("IssuesChanged", It.IsAny<object?[]>(), default))
            .Callback(() => _callLog.Enqueue("send-all"))
            .Returns(Task.CompletedTask);
        _groupClientsMock = new Mock<IClientProxy>();
        _groupClientsMock
            .Setup(x => x.SendCoreAsync("IssuesChanged", It.IsAny<object?[]>(), default))
            .Callback(() => _callLog.Enqueue("send-group"))
            .Returns(Task.CompletedTask);
        var clientsMock = new Mock<IHubClients>();
        clientsMock.Setup(x => x.All).Returns(_allClientsMock.Object);
        clientsMock.Setup(x => x.Group(It.IsAny<string>())).Returns(_groupClientsMock.Object);
        _hubContext = new Mock<IHubContext<NotificationHub>>();
        _hubContext.Setup(x => x.Clients).Returns(clientsMock.Object);

        var services = new ServiceCollection();
        services.AddSingleton(_snapshotStore.Object);
        var provider = services.BuildServiceProvider();

        _resolver = new PRStatusResolver(
            _gitHubService.Object,
            _graphCacheService.Object,
            _linkingService.Object,
            _dataStore,
            NullLogger<PRStatusResolver>.Instance,
            _hubContext.Object,
            provider);
    }

    [TearDown]
    public void TearDown()
    {
        _dataStore.Clear();
    }

    [Test]
    public async Task MergedTransitionWithLinkedIssue_InvalidatesAndBroadcasts()
    {
        var project = await CreateTestProject();
        var removedPrs = new List<RemovedPrInfo>
        {
            new() { PullRequestId = "pr-1", GitHubPrNumber = 42, FleeceIssueId = "issue-abc" }
        };

        _gitHubService.Setup(s => s.GetPullRequestAsync(project.Id, 42))
            .ReturnsAsync(new PullRequestInfo
            {
                Number = 42,
                Title = "merged",
                Status = PullRequestStatus.Merged,
                MergedAt = DateTime.UtcNow
            });

        await _resolver.ResolveClosedPRStatusesAsync(project.Id, removedPrs);

        var log = _callLog.ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(log, Is.Not.Empty, "expected invalidation + broadcast to fire");
            Assert.That(log[0], Is.EqualTo("invalidate"),
                "InvalidateProject MUST run before any SendAsync — clients refetch on broadcast");
            Assert.That(log.Contains("send-all"), Is.True);
            Assert.That(log.Contains("send-group"), Is.True);
        });

        _allClientsMock.Verify(
            x => x.SendCoreAsync(
                "IssuesChanged",
                It.Is<object?[]>(args =>
                    args.Length >= 3 && (string?)args[0] == project.Id && (string?)args[2] == "issue-abc"),
                default),
            Times.Once,
            "broadcast must carry the linked Fleece issue id");
    }

    [Test]
    public async Task ClosedTransitionWithoutLinkedIssue_InvalidatesWithNullIssueId()
    {
        var project = await CreateTestProject();
        var removedPrs = new List<RemovedPrInfo>
        {
            new() { PullRequestId = "pr-1", GitHubPrNumber = 99, FleeceIssueId = null }
        };

        _gitHubService.Setup(s => s.GetPullRequestAsync(project.Id, 99))
            .ReturnsAsync(new PullRequestInfo
            {
                Number = 99,
                Title = "closed",
                Status = PullRequestStatus.Closed,
                ClosedAt = DateTime.UtcNow
            });

        await _resolver.ResolveClosedPRStatusesAsync(project.Id, removedPrs);

        _snapshotStore.Verify(s => s.InvalidateProject(project.Id), Times.Once);
        _allClientsMock.Verify(
            x => x.SendCoreAsync(
                "IssuesChanged",
                It.Is<object?[]>(args =>
                    args.Length >= 3 && (string?)args[0] == project.Id && args[2] == null),
                default),
            Times.Once,
            "broadcast must carry null issueId when no linked Fleece issue exists");
    }

    [Test]
    public async Task EmptyFleeceIssueId_BroadcastsWithNullIssueId()
    {
        // Empty-string FleeceIssueId is normalised to null on the wire so client
        // handlers don't have to special-case "" vs null.
        var project = await CreateTestProject();
        var removedPrs = new List<RemovedPrInfo>
        {
            new() { PullRequestId = "pr-1", GitHubPrNumber = 7, FleeceIssueId = "" }
        };

        _gitHubService.Setup(s => s.GetPullRequestAsync(project.Id, 7))
            .ReturnsAsync(new PullRequestInfo
            {
                Number = 7,
                Title = "merged",
                Status = PullRequestStatus.Merged,
                MergedAt = DateTime.UtcNow
            });

        await _resolver.ResolveClosedPRStatusesAsync(project.Id, removedPrs);

        _allClientsMock.Verify(
            x => x.SendCoreAsync(
                "IssuesChanged",
                It.Is<object?[]>(args => args.Length >= 3 && args[2] == null),
                default),
            Times.Once);
    }

    [Test]
    public async Task UnexpectedPrStatus_DoesNotInvalidate()
    {
        // PRs returned by GetPullRequestAsync with a non-Merged / non-Closed status
        // hit the warn branch in the resolver. No transition occurred, so the
        // snapshot should NOT be invalidated.
        var project = await CreateTestProject();
        var removedPrs = new List<RemovedPrInfo>
        {
            new() { PullRequestId = "pr-1", GitHubPrNumber = 11 }
        };

        _gitHubService.Setup(s => s.GetPullRequestAsync(project.Id, 11))
            .ReturnsAsync(new PullRequestInfo
            {
                Number = 11,
                Title = "still-open?",
                Status = PullRequestStatus.InProgress
            });

        await _resolver.ResolveClosedPRStatusesAsync(project.Id, removedPrs);

        _snapshotStore.Verify(s => s.InvalidateProject(It.IsAny<string>()), Times.Never);
        _allClientsMock.Verify(
            x => x.SendCoreAsync("IssuesChanged", It.IsAny<object?[]>(), default),
            Times.Never);
    }

    [Test]
    public async Task MultiplePrs_EachTransitionInvalidatesOnce()
    {
        var project = await CreateTestProject();
        var removedPrs = new List<RemovedPrInfo>
        {
            new() { PullRequestId = "pr-1", GitHubPrNumber = 1, FleeceIssueId = "i-1" },
            new() { PullRequestId = "pr-2", GitHubPrNumber = 2, FleeceIssueId = "i-2" }
        };

        _gitHubService.Setup(s => s.GetPullRequestAsync(project.Id, 1))
            .ReturnsAsync(new PullRequestInfo
            {
                Number = 1,
                Title = "merged-pr",
                Status = PullRequestStatus.Merged,
                MergedAt = DateTime.UtcNow
            });
        _gitHubService.Setup(s => s.GetPullRequestAsync(project.Id, 2))
            .ReturnsAsync(new PullRequestInfo
            {
                Number = 2,
                Title = "closed-pr",
                Status = PullRequestStatus.Closed,
                ClosedAt = DateTime.UtcNow
            });

        await _resolver.ResolveClosedPRStatusesAsync(project.Id, removedPrs);

        _snapshotStore.Verify(s => s.InvalidateProject(project.Id), Times.Exactly(2));
        _allClientsMock.Verify(
            x => x.SendCoreAsync("IssuesChanged", It.IsAny<object?[]>(), default),
            Times.Exactly(2));
    }

    [Test]
    public void Constructor_WithoutHubOrServices_DoesNotThrow()
    {
        // Tests that don't wire SignalR pass null hub/services; the resolver still
        // runs cache + linking updates and the broadcast simply no-ops.
        Assert.DoesNotThrow(() => new PRStatusResolver(
            _gitHubService.Object,
            _graphCacheService.Object,
            _linkingService.Object,
            _dataStore,
            NullLogger<PRStatusResolver>.Instance));
    }

    private async Task<Project> CreateTestProject()
    {
        var project = new Project
        {
            Name = "test-repo",
            LocalPath = "/test/path",
            GitHubOwner = "test-owner",
            GitHubRepo = "test-repo",
            DefaultBranch = "main"
        };
        await _dataStore.AddProjectAsync(project);
        return project;
    }
}
