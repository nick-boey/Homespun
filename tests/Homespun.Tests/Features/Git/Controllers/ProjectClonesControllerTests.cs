using Homespun.Features.Git;
using Homespun.Features.Git.Controllers;
using Homespun.Features.Notifications;
using Homespun.Features.Projects;
using Homespun.Shared.Models.Git;
using Homespun.Shared.Models.Projects;
using Homespun.Shared.Requests;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;

namespace Homespun.Tests.Features.Git.Controllers;

[TestFixture]
public class ProjectClonesControllerTests
{
    private ProjectClonesController _controller = null!;
    private Mock<IGitCloneService> _cloneServiceMock = null!;
    private Mock<IProjectService> _projectServiceMock = null!;
    private Mock<ICloneEnrichmentService> _cloneEnrichmentServiceMock = null!;
    private Mock<IHubContext<NotificationHub>> _hubContextMock = null!;
    private Mock<IClientProxy> _allClientsMock = null!;
    private Mock<IClientProxy> _groupClientsMock = null!;

    private static readonly Project TestProject = new()
    {
        Id = "project-123",
        Name = "Test Project",
        LocalPath = "/path/to/project",
        DefaultBranch = "main",
        DefaultModel = "sonnet"
    };

    [SetUp]
    public void SetUp()
    {
        _cloneServiceMock = new Mock<IGitCloneService>();
        _projectServiceMock = new Mock<IProjectService>();
        _cloneEnrichmentServiceMock = new Mock<ICloneEnrichmentService>();

        _allClientsMock = new Mock<IClientProxy>();
        _allClientsMock
            .Setup(x => x.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), default))
            .Returns(Task.CompletedTask);
        _groupClientsMock = new Mock<IClientProxy>();
        _groupClientsMock
            .Setup(x => x.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), default))
            .Returns(Task.CompletedTask);
        var clientsMock = new Mock<IHubClients>();
        clientsMock.Setup(x => x.All).Returns(_allClientsMock.Object);
        clientsMock.Setup(x => x.Group(It.IsAny<string>())).Returns(_groupClientsMock.Object);
        _hubContextMock = new Mock<IHubContext<NotificationHub>>();
        _hubContextMock.Setup(x => x.Clients).Returns(clientsMock.Object);

        _controller = new ProjectClonesController(
            _cloneServiceMock.Object,
            _projectServiceMock.Object,
            _cloneEnrichmentServiceMock.Object,
            _hubContextMock.Object);

        var httpContext = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().BuildServiceProvider()
        };
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };
    }

    #region ListEnriched Tests

    [Test]
    public async Task ListEnriched_ReturnsEnrichedCloneData()
    {
        // Arrange
        var enrichedClones = new List<EnrichedCloneInfo>
        {
            new()
            {
                Clone = new CloneInfo
                {
                    Path = "/path/to/clone1",
                    Branch = "feature/test-1"
                },
                LinkedIssueId = "abc123",
                LinkedIssue = new EnrichedIssueInfo
                {
                    Id = "abc123",
                    Title = "Test Issue",
                    Status = "Open"
                },
                IsDeletable = false
            },
            new()
            {
                Clone = new CloneInfo
                {
                    Path = "/path/to/clone2",
                    Branch = "feature/test-2"
                },
                IsDeletable = true,
                DeletionReason = "PR has been merged"
            }
        };

        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);

        _cloneEnrichmentServiceMock
            .Setup(x => x.EnrichClonesAsync(TestProject.Id, TestProject.LocalPath))
            .ReturnsAsync(enrichedClones);

        // Act
        var result = await _controller.ListEnriched(TestProject.Id);

        // Assert
        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result!;
        var returnedClones = okResult.Value as List<EnrichedCloneInfo>;
        Assert.That(returnedClones, Is.Not.Null);
        Assert.That(returnedClones!.Count, Is.EqualTo(2));
        Assert.That(returnedClones[0].LinkedIssueId, Is.EqualTo("abc123"));
        Assert.That(returnedClones[1].IsDeletable, Is.True);
    }

    [Test]
    public async Task ListEnriched_ProjectNotFound_Returns404()
    {
        // Arrange
        _projectServiceMock
            .Setup(x => x.GetByIdAsync(It.IsAny<string>()))
            .ReturnsAsync((Project?)null);

        // Act
        var result = await _controller.ListEnriched("nonexistent");

        // Assert
        Assert.That(result.Result, Is.TypeOf<NotFoundObjectResult>());
        var notFoundResult = (NotFoundObjectResult)result.Result!;
        Assert.That(notFoundResult.Value, Is.EqualTo("Project not found"));
    }

    #endregion

    #region BulkDelete Tests

    [Test]
    public async Task BulkDelete_DeletesMultipleClones()
    {
        // Arrange
        var clonePaths = new List<string>
        {
            "/path/to/clone1",
            "/path/to/clone2"
        };

        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);

        _cloneServiceMock
            .Setup(x => x.RemoveCloneAsync(TestProject.LocalPath, It.IsAny<string>()))
            .ReturnsAsync(true);

        var request = new BulkDeleteClonesRequest { ClonePaths = clonePaths };

        // Act
        var result = await _controller.BulkDelete(TestProject.Id, request);

        // Assert
        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result!;
        var response = okResult.Value as BulkDeleteClonesResponse;
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Results.Count, Is.EqualTo(2));
        Assert.That(response.Results.All(r => r.Success), Is.True);
        Assert.That(response.Results.All(r => r.Error == null), Is.True);

        // Verify both clones were removed
        _cloneServiceMock.Verify(
            x => x.RemoveCloneAsync(TestProject.LocalPath, "/path/to/clone1"),
            Times.Once);
        _cloneServiceMock.Verify(
            x => x.RemoveCloneAsync(TestProject.LocalPath, "/path/to/clone2"),
            Times.Once);
    }

    [Test]
    public async Task BulkDelete_ProjectNotFound_Returns404()
    {
        // Arrange
        _projectServiceMock
            .Setup(x => x.GetByIdAsync(It.IsAny<string>()))
            .ReturnsAsync((Project?)null);

        var request = new BulkDeleteClonesRequest
        {
            ClonePaths = new List<string> { "/path/to/clone" }
        };

        // Act
        var result = await _controller.BulkDelete("nonexistent", request);

        // Assert
        Assert.That(result.Result, Is.TypeOf<NotFoundObjectResult>());
        var notFoundResult = (NotFoundObjectResult)result.Result!;
        Assert.That(notFoundResult.Value, Is.EqualTo("Project not found"));
    }

    [Test]
    public async Task BulkDelete_PartialFailure_ReturnsIndividualResults()
    {
        // Arrange
        var clonePaths = new List<string>
        {
            "/path/to/clone1",
            "/path/to/clone2",
            "/path/to/clone3"
        };

        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);

        // First and third succeed, second fails
        _cloneServiceMock
            .Setup(x => x.RemoveCloneAsync(TestProject.LocalPath, "/path/to/clone1"))
            .ReturnsAsync(true);
        _cloneServiceMock
            .Setup(x => x.RemoveCloneAsync(TestProject.LocalPath, "/path/to/clone2"))
            .ReturnsAsync(false);
        _cloneServiceMock
            .Setup(x => x.RemoveCloneAsync(TestProject.LocalPath, "/path/to/clone3"))
            .ReturnsAsync(true);

        var request = new BulkDeleteClonesRequest { ClonePaths = clonePaths };

        // Act
        var result = await _controller.BulkDelete(TestProject.Id, request);

        // Assert
        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result!;
        var response = okResult.Value as BulkDeleteClonesResponse;
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Results.Count, Is.EqualTo(3));

        // First clone succeeded
        Assert.That(response.Results[0].ClonePath, Is.EqualTo("/path/to/clone1"));
        Assert.That(response.Results[0].Success, Is.True);
        Assert.That(response.Results[0].Error, Is.Null);

        // Second clone failed
        Assert.That(response.Results[1].ClonePath, Is.EqualTo("/path/to/clone2"));
        Assert.That(response.Results[1].Success, Is.False);
        Assert.That(response.Results[1].Error, Is.EqualTo("Failed to remove clone"));

        // Third clone succeeded
        Assert.That(response.Results[2].ClonePath, Is.EqualTo("/path/to/clone3"));
        Assert.That(response.Results[2].Success, Is.True);
        Assert.That(response.Results[2].Error, Is.Null);
    }

    #endregion

    #region Snapshot invalidation on clone lifecycle

    [Test]
    public async Task Create_InvalidatesGraphSnapshotAfterSuccess()
    {
        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _cloneServiceMock
            .Setup(x => x.CreateCloneAsync(TestProject.LocalPath, "feature/x", true, "main"))
            .ReturnsAsync("/path/to/clone");

        var request = new CreateCloneRequest
        {
            BranchName = "feature/x",
            CreateBranch = true,
            BaseBranch = "main"
        };

        var result = await _controller.Create(TestProject.Id, request);

        Assert.That(result.Result, Is.TypeOf<CreatedResult>());
        VerifyIssuesChangedBroadcast();
    }

    [Test]
    public async Task Create_FailedCreateDoesNotInvalidate()
    {
        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _cloneServiceMock
            .Setup(x => x.CreateCloneAsync(
                TestProject.LocalPath, It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>()))
            .ReturnsAsync((string?)null);

        var request = new CreateCloneRequest { BranchName = "feature/x", CreateBranch = true };

        var result = await _controller.Create(TestProject.Id, request);

        Assert.That(result.Result, Is.TypeOf<BadRequestObjectResult>());
        VerifyNoIssuesChangedBroadcast();
    }

    [Test]
    public async Task Delete_InvalidatesGraphSnapshotAfterSuccess()
    {
        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _cloneServiceMock
            .Setup(x => x.RemoveCloneAsync(TestProject.LocalPath, "/path/to/clone"))
            .ReturnsAsync(true);

        var result = await _controller.Delete(TestProject.Id, "/path/to/clone");

        Assert.That(result, Is.TypeOf<NoContentResult>());
        VerifyIssuesChangedBroadcast();
    }

    [Test]
    public async Task Delete_FailedRemoveDoesNotInvalidate()
    {
        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _cloneServiceMock
            .Setup(x => x.RemoveCloneAsync(TestProject.LocalPath, It.IsAny<string>()))
            .ReturnsAsync(false);

        var result = await _controller.Delete(TestProject.Id, "/path/to/clone");

        Assert.That(result, Is.TypeOf<BadRequestObjectResult>());
        VerifyNoIssuesChangedBroadcast();
    }

    [Test]
    public async Task BulkDelete_InvalidatesOnceWhenAnySucceeds()
    {
        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _cloneServiceMock
            .Setup(x => x.RemoveCloneAsync(TestProject.LocalPath, "/path/to/clone1"))
            .ReturnsAsync(true);
        _cloneServiceMock
            .Setup(x => x.RemoveCloneAsync(TestProject.LocalPath, "/path/to/clone2"))
            .ReturnsAsync(false);

        var request = new BulkDeleteClonesRequest
        {
            ClonePaths = new List<string> { "/path/to/clone1", "/path/to/clone2" }
        };

        var result = await _controller.BulkDelete(TestProject.Id, request);

        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        // Helper broadcasts to All + Group → exactly two SendCoreAsync invocations
        // for one BroadcastIssueTopologyChanged call.
        _allClientsMock.Verify(
            x => x.SendCoreAsync("IssuesChanged", It.IsAny<object?[]>(), default),
            Times.Once);
        _groupClientsMock.Verify(
            x => x.SendCoreAsync("IssuesChanged", It.IsAny<object?[]>(), default),
            Times.Once);
    }

    [Test]
    public async Task BulkDelete_AllFailDoesNotInvalidate()
    {
        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _cloneServiceMock
            .Setup(x => x.RemoveCloneAsync(TestProject.LocalPath, It.IsAny<string>()))
            .ReturnsAsync(false);

        var request = new BulkDeleteClonesRequest
        {
            ClonePaths = new List<string> { "/path/to/clone1", "/path/to/clone2" }
        };

        var result = await _controller.BulkDelete(TestProject.Id, request);

        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        VerifyNoIssuesChangedBroadcast();
    }

    [Test]
    public async Task Prune_InvalidatesGraphSnapshot()
    {
        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _cloneServiceMock
            .Setup(x => x.PruneClonesAsync(TestProject.LocalPath))
            .Returns(Task.CompletedTask);

        var result = await _controller.Prune(TestProject.Id);

        Assert.That(result, Is.TypeOf<NoContentResult>());
        VerifyIssuesChangedBroadcast();
    }

    private void VerifyIssuesChangedBroadcast()
    {
        _allClientsMock.Verify(
            x => x.SendCoreAsync("IssuesChanged", It.IsAny<object?[]>(), default),
            Times.Once,
            "expected exactly one IssuesChanged broadcast to Clients.All");
        _groupClientsMock.Verify(
            x => x.SendCoreAsync("IssuesChanged", It.IsAny<object?[]>(), default),
            Times.Once,
            "expected exactly one IssuesChanged broadcast to the project group");
    }

    private void VerifyNoIssuesChangedBroadcast()
    {
        _allClientsMock.Verify(
            x => x.SendCoreAsync("IssuesChanged", It.IsAny<object?[]>(), default),
            Times.Never,
            "no broadcast expected when the underlying clone op did not change state");
    }

    #endregion
}
