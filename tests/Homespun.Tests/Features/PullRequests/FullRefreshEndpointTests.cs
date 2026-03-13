using Homespun.Features.Fleece.Services;
using Homespun.Features.Gitgraph.Services;
using Homespun.Features.PullRequests.Controllers;
using Homespun.Features.PullRequests.Data;
using Homespun.Shared.Models.GitHub;
using Homespun.Shared.Models.Projects;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Homespun.Tests.Features.PullRequests;

[TestFixture]
public class FullRefreshEndpointTests
{
    private Mock<IDataStore> _dataStoreMock = null!;
    private Mock<IGitHubService> _gitHubServiceMock = null!;
    private Mock<IFleeceService> _fleeceServiceMock = null!;
    private Mock<IGraphCacheService> _graphCacheServiceMock = null!;
    private Mock<PullRequestWorkflowService> _workflowServiceMock = null!;
    private PullRequestsController _controller = null!;

    private static readonly Project TestProject = new()
    {
        Id = "project-123",
        Name = "Test Project",
        LocalPath = "/path/to/project",
        DefaultBranch = "main",
        GitHubOwner = "test-owner",
        GitHubRepo = "test-repo"
    };

    [SetUp]
    public void SetUp()
    {
        _dataStoreMock = new Mock<IDataStore>();
        _gitHubServiceMock = new Mock<IGitHubService>();
        _fleeceServiceMock = new Mock<IFleeceService>();
        _graphCacheServiceMock = new Mock<IGraphCacheService>();
        _workflowServiceMock = new Mock<PullRequestWorkflowService>(
            MockBehavior.Loose, null!, null!, null!, null!, null!);

        _controller = new PullRequestsController(
            _dataStoreMock.Object,
            _gitHubServiceMock.Object,
            _fleeceServiceMock.Object,
            _graphCacheServiceMock.Object,
            _workflowServiceMock.Object);
    }

    [Test]
    public async Task FullRefresh_ReturnsCorrectCounts_WhenSuccessful()
    {
        // Arrange
        var openPrs = new List<PullRequestInfo>
        {
            new() { Number = 1, Title = "Open PR 1", Status = PullRequestStatus.ReadyForReview },
            new() { Number = 2, Title = "Open PR 2", Status = PullRequestStatus.InProgress }
        };
        var closedPrs = new List<PullRequestInfo>
        {
            new() { Number = 3, Title = "Merged PR 1", Status = PullRequestStatus.Merged },
            new() { Number = 4, Title = "Closed PR 1", Status = PullRequestStatus.Closed },
            new() { Number = 5, Title = "Merged PR 2", Status = PullRequestStatus.Merged }
        };

        _dataStoreMock.Setup(x => x.GetProject(TestProject.Id)).Returns(TestProject);
        _gitHubServiceMock.Setup(x => x.GetOpenPullRequestsAsync(TestProject.Id)).ReturnsAsync(openPrs);
        _gitHubServiceMock.Setup(x => x.GetClosedPullRequestsAsync(TestProject.Id)).ReturnsAsync(closedPrs);
        _gitHubServiceMock.Setup(x => x.SyncPullRequestsAsync(TestProject.Id)).ReturnsAsync(new SyncResult
        {
            Imported = 2,
            Updated = 3,
            Removed = 0,
            Errors = []
        });

        // Act
        var result = await _controller.FullRefresh(TestProject.Id);

        // Assert
        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result!;
        var refreshResult = (FullRefreshResult)okResult.Value!;

        Assert.That(refreshResult.OpenPrs, Is.EqualTo(2));
        Assert.That(refreshResult.ClosedPrs, Is.EqualTo(3));
        Assert.That(refreshResult.LinkedIssues, Is.EqualTo(5)); // Imported + Updated
        Assert.That(refreshResult.Errors, Is.Empty);
    }

    [Test]
    public async Task FullRefresh_Returns404_WhenProjectNotFound()
    {
        // Arrange
        _dataStoreMock.Setup(x => x.GetProject(It.IsAny<string>())).Returns((Project?)null);

        // Act
        var result = await _controller.FullRefresh("nonexistent");

        // Assert
        Assert.That(result.Result, Is.TypeOf<NotFoundObjectResult>());
    }

    [Test]
    public async Task FullRefresh_InvalidatesCache_BeforeFetchingFresh()
    {
        // Arrange
        var callOrder = new List<string>();

        _dataStoreMock.Setup(x => x.GetProject(TestProject.Id)).Returns(TestProject);
        _graphCacheServiceMock.Setup(x => x.InvalidateCacheAsync(TestProject.Id))
            .Callback(() => callOrder.Add("InvalidateCache"))
            .Returns(Task.CompletedTask);
        _gitHubServiceMock.Setup(x => x.GetOpenPullRequestsAsync(TestProject.Id))
            .Callback(() => callOrder.Add("GetOpenPRs"))
            .ReturnsAsync(new List<PullRequestInfo>());
        _gitHubServiceMock.Setup(x => x.GetClosedPullRequestsAsync(TestProject.Id))
            .Callback(() => callOrder.Add("GetClosedPRs"))
            .ReturnsAsync(new List<PullRequestInfo>());
        _gitHubServiceMock.Setup(x => x.SyncPullRequestsAsync(TestProject.Id))
            .ReturnsAsync(new SyncResult());

        // Act
        await _controller.FullRefresh(TestProject.Id);

        // Assert - Cache must be invalidated before fetching fresh data
        Assert.That(callOrder, Is.EqualTo(new[] { "InvalidateCache", "GetOpenPRs", "GetClosedPRs" }));
        _graphCacheServiceMock.Verify(x => x.InvalidateCacheAsync(TestProject.Id), Times.Once);
    }

    [Test]
    public async Task FullRefresh_CachesPRData_WhenProjectHasLocalPath()
    {
        // Arrange
        var openPrs = new List<PullRequestInfo> { new() { Number = 1, Title = "PR 1", Status = PullRequestStatus.ReadyForReview } };
        var closedPrs = new List<PullRequestInfo> { new() { Number = 2, Title = "PR 2", Status = PullRequestStatus.Merged } };

        _dataStoreMock.Setup(x => x.GetProject(TestProject.Id)).Returns(TestProject);
        _gitHubServiceMock.Setup(x => x.GetOpenPullRequestsAsync(TestProject.Id)).ReturnsAsync(openPrs);
        _gitHubServiceMock.Setup(x => x.GetClosedPullRequestsAsync(TestProject.Id)).ReturnsAsync(closedPrs);
        _gitHubServiceMock.Setup(x => x.SyncPullRequestsAsync(TestProject.Id)).ReturnsAsync(new SyncResult());

        // Act
        await _controller.FullRefresh(TestProject.Id);

        // Assert
        _graphCacheServiceMock.Verify(
            x => x.CachePRDataAsync(TestProject.Id, TestProject.LocalPath, openPrs, closedPrs),
            Times.Once);
    }

    [Test]
    public async Task FullRefresh_IncludesErrors_WhenSyncFails()
    {
        // Arrange
        _dataStoreMock.Setup(x => x.GetProject(TestProject.Id)).Returns(TestProject);
        _gitHubServiceMock.Setup(x => x.GetOpenPullRequestsAsync(TestProject.Id))
            .ReturnsAsync(new List<PullRequestInfo>());
        _gitHubServiceMock.Setup(x => x.GetClosedPullRequestsAsync(TestProject.Id))
            .ReturnsAsync(new List<PullRequestInfo>());
        _gitHubServiceMock.Setup(x => x.SyncPullRequestsAsync(TestProject.Id))
            .ReturnsAsync(new SyncResult { Errors = ["PR #123: Rate limit exceeded"] });

        // Act
        var result = await _controller.FullRefresh(TestProject.Id);

        // Assert
        var okResult = (OkObjectResult)result.Result!;
        var refreshResult = (FullRefreshResult)okResult.Value!;
        Assert.That(refreshResult.Errors, Contains.Item("PR #123: Rate limit exceeded"));
    }

    [Test]
    public async Task FullRefresh_HandlesException_Gracefully()
    {
        // Arrange
        _dataStoreMock.Setup(x => x.GetProject(TestProject.Id)).Returns(TestProject);
        _gitHubServiceMock.Setup(x => x.GetOpenPullRequestsAsync(TestProject.Id))
            .ThrowsAsync(new Exception("GitHub API error"));

        // Act
        var result = await _controller.FullRefresh(TestProject.Id);

        // Assert - Should return OK with error in the result
        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result!;
        var refreshResult = (FullRefreshResult)okResult.Value!;
        Assert.That(refreshResult.Errors, Contains.Item("GitHub API error"));
    }

    [Test]
    public async Task FullRefresh_RunsIncrementalSync_AfterCachingData()
    {
        // Arrange
        _dataStoreMock.Setup(x => x.GetProject(TestProject.Id)).Returns(TestProject);
        _gitHubServiceMock.Setup(x => x.GetOpenPullRequestsAsync(TestProject.Id))
            .ReturnsAsync(new List<PullRequestInfo>());
        _gitHubServiceMock.Setup(x => x.GetClosedPullRequestsAsync(TestProject.Id))
            .ReturnsAsync(new List<PullRequestInfo>());
        _gitHubServiceMock.Setup(x => x.SyncPullRequestsAsync(TestProject.Id))
            .ReturnsAsync(new SyncResult { Imported = 1, Updated = 2 });

        // Act
        await _controller.FullRefresh(TestProject.Id);

        // Assert
        _gitHubServiceMock.Verify(x => x.SyncPullRequestsAsync(TestProject.Id), Times.Once);
    }
}
