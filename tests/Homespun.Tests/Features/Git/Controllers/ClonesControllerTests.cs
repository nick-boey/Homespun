using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.Fleece.Services;
using Homespun.Features.Git;
using Homespun.Features.Git.Controllers;
using Homespun.Features.Projects;
using Homespun.Shared.Models.Fleece;
using Homespun.Shared.Models.Git;
using Homespun.Shared.Models.Projects;
using Homespun.Shared.Models.Sessions;
using Homespun.Shared.Requests;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Homespun.Tests.Features.Git.Controllers;

[TestFixture]
public class ClonesControllerTests
{
    private ClonesController _controller = null!;
    private Mock<IGitCloneService> _cloneServiceMock = null!;
    private Mock<IProjectService> _projectServiceMock = null!;
    private Mock<IFleeceIssuesSyncService> _fleeceIssuesSyncServiceMock = null!;
    private Mock<IClaudeSessionService> _sessionServiceMock = null!;
    private Mock<ICloneEnrichmentService> _cloneEnrichmentServiceMock = null!;
    private Mock<ILogger<ClonesController>> _loggerMock = null!;

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
        _fleeceIssuesSyncServiceMock = new Mock<IFleeceIssuesSyncService>();
        _sessionServiceMock = new Mock<IClaudeSessionService>();
        _cloneEnrichmentServiceMock = new Mock<ICloneEnrichmentService>();
        _loggerMock = new Mock<ILogger<ClonesController>>();
        _controller = new ClonesController(
            _cloneServiceMock.Object,
            _projectServiceMock.Object,
            _fleeceIssuesSyncServiceMock.Object,
            _sessionServiceMock.Object,
            _cloneEnrichmentServiceMock.Object,
            _loggerMock.Object);

        // Set up HTTP context for controller
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
    }

    #region GetSessionBranchInfo Tests

    [Test]
    public async Task GetSessionBranchInfo_ValidWorkingDirectory_ReturnsOkWithBranchInfo()
    {
        // Arrange
        var workingDirectory = "/test/clone/workdir";
        var expectedInfo = new SessionBranchInfo
        {
            BranchName = "feature/test",
            CommitSha = "abc1234",
            CommitMessage = "Test commit",
            CommitDate = DateTime.UtcNow,
            AheadCount = 2,
            BehindCount = 1,
            HasUncommittedChanges = true
        };

        _cloneServiceMock.Setup(s => s.GetSessionBranchInfoAsync(workingDirectory))
            .ReturnsAsync(expectedInfo);

        // Act
        var result = await _controller.GetSessionBranchInfo(workingDirectory);

        // Assert
        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result!;
        var branchInfo = okResult.Value as SessionBranchInfo;
        Assert.That(branchInfo, Is.Not.Null);
        Assert.That(branchInfo!.BranchName, Is.EqualTo("feature/test"));
        Assert.That(branchInfo.CommitSha, Is.EqualTo("abc1234"));
        Assert.That(branchInfo.AheadCount, Is.EqualTo(2));
        Assert.That(branchInfo.HasUncommittedChanges, Is.True);
    }

    [Test]
    public async Task GetSessionBranchInfo_ServiceReturnsNull_ReturnsNotFound()
    {
        // Arrange
        var workingDirectory = "/nonexistent/directory";

        _cloneServiceMock.Setup(s => s.GetSessionBranchInfoAsync(workingDirectory))
            .ReturnsAsync((SessionBranchInfo?)null);

        // Act
        var result = await _controller.GetSessionBranchInfo(workingDirectory);

        // Assert
        Assert.That(result.Result, Is.InstanceOf<NotFoundResult>());
    }

    [Test]
    public async Task GetSessionBranchInfo_DetachedHead_ReturnsOkWithNullBranchName()
    {
        // Arrange
        var workingDirectory = "/test/clone/workdir";
        var expectedInfo = new SessionBranchInfo
        {
            BranchName = null, // Detached HEAD
            CommitSha = "def5678",
            CommitMessage = "Detached commit",
            CommitDate = DateTime.UtcNow,
            AheadCount = 0,
            BehindCount = 0,
            HasUncommittedChanges = false
        };

        _cloneServiceMock.Setup(s => s.GetSessionBranchInfoAsync(workingDirectory))
            .ReturnsAsync(expectedInfo);

        // Act
        var result = await _controller.GetSessionBranchInfo(workingDirectory);

        // Assert
        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result!;
        var branchInfo = okResult.Value as SessionBranchInfo;
        Assert.That(branchInfo, Is.Not.Null);
        Assert.That(branchInfo!.BranchName, Is.Null);
        Assert.That(branchInfo.CommitSha, Is.EqualTo("def5678"));
    }

    #endregion

    #region CreateBranchSession Tests

    [Test]
    public async Task CreateBranchSession_ReturnsNotFound_WhenProjectNotFound()
    {
        // Arrange
        _projectServiceMock
            .Setup(x => x.GetByIdAsync(It.IsAny<string>()))
            .ReturnsAsync((Project?)null);

        var request = new CreateBranchSessionRequest
        {
            ProjectId = "nonexistent",
            BranchName = "feature/test"
        };

        // Act
        var result = await _controller.CreateBranchSession(request);

        // Assert
        Assert.That(result.Result, Is.TypeOf<NotFoundObjectResult>());
        var notFoundResult = (NotFoundObjectResult)result.Result!;
        Assert.That(notFoundResult.Value, Is.EqualTo("Project not found"));
    }

    [Test]
    public async Task CreateBranchSession_ReturnsBadRequest_WhenBranchNameEmpty()
    {
        // Arrange
        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);

        var request = new CreateBranchSessionRequest
        {
            ProjectId = TestProject.Id,
            BranchName = ""
        };

        // Act
        var result = await _controller.CreateBranchSession(request);

        // Assert
        Assert.That(result.Result, Is.TypeOf<BadRequestObjectResult>());
        var badRequestResult = (BadRequestObjectResult)result.Result!;
        Assert.That(badRequestResult.Value, Is.EqualTo("Branch name is required"));
    }

    [Test]
    public async Task CreateBranchSession_ReturnsBadRequest_WhenBranchNameWhitespace()
    {
        // Arrange
        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);

        var request = new CreateBranchSessionRequest
        {
            ProjectId = TestProject.Id,
            BranchName = "   "
        };

        // Act
        var result = await _controller.CreateBranchSession(request);

        // Assert
        Assert.That(result.Result, Is.TypeOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task CreateBranchSession_CreatesCloneAndSession_WhenCloneDoesNotExist()
    {
        // Arrange
        var branchName = "feature/test";
        var clonePath = "/path/to/clone";
        var session = new ClaudeSession
        {
            Id = "session-123",
            EntityId = branchName,
            ProjectId = TestProject.Id,
            WorkingDirectory = clonePath,
            Model = "sonnet",
            Mode = SessionMode.Plan
        };

        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _fleeceIssuesSyncServiceMock
            .Setup(x => x.PullFleeceOnlyAsync(TestProject.LocalPath, TestProject.DefaultBranch, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FleecePullResult(
                Success: true,
                ErrorMessage: null,
                IssuesMerged: 0,
                WasBehindRemote: false,
                CommitsPulled: 0));
        _cloneServiceMock
            .Setup(x => x.GetClonePathForBranchAsync(TestProject.LocalPath, branchName))
            .ReturnsAsync((string?)null);
        _cloneServiceMock
            .Setup(x => x.CreateCloneAsync(TestProject.LocalPath, branchName, true, TestProject.DefaultBranch))
            .ReturnsAsync(clonePath);
        _cloneServiceMock
            .Setup(x => x.PullLatestAsync(clonePath))
            .ReturnsAsync(true);
        _sessionServiceMock
            .Setup(x => x.StartSessionAsync(branchName, TestProject.Id, clonePath, SessionMode.Plan, "sonnet", null, default))
            .ReturnsAsync(session);

        var request = new CreateBranchSessionRequest
        {
            ProjectId = TestProject.Id,
            BranchName = branchName
        };

        // Act
        var result = await _controller.CreateBranchSession(request);

        // Assert
        Assert.That(result.Result, Is.TypeOf<CreatedResult>());
        var createdResult = (CreatedResult)result.Result!;
        var response = (CreateBranchSessionResponse)createdResult.Value!;
        Assert.That(response.SessionId, Is.EqualTo("session-123"));
        Assert.That(response.BranchName, Is.EqualTo(branchName));
        Assert.That(response.ClonePath, Is.EqualTo(clonePath));

        // Verify clone was created
        _cloneServiceMock.Verify(
            x => x.CreateCloneAsync(TestProject.LocalPath, branchName, true, TestProject.DefaultBranch),
            Times.Once);
    }

    [Test]
    public async Task CreateBranchSession_UsesExistingClone_WhenCloneExists()
    {
        // Arrange
        var branchName = "feature/test";
        var clonePath = "/path/to/existing-clone";
        var session = new ClaudeSession
        {
            Id = "session-123",
            EntityId = branchName,
            ProjectId = TestProject.Id,
            WorkingDirectory = clonePath,
            Model = "sonnet",
            Mode = SessionMode.Plan
        };

        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _fleeceIssuesSyncServiceMock
            .Setup(x => x.PullFleeceOnlyAsync(TestProject.LocalPath, TestProject.DefaultBranch, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FleecePullResult(
                Success: true,
                ErrorMessage: null,
                IssuesMerged: 0,
                WasBehindRemote: false,
                CommitsPulled: 0));
        _cloneServiceMock
            .Setup(x => x.GetClonePathForBranchAsync(TestProject.LocalPath, branchName))
            .ReturnsAsync(clonePath);
        _cloneServiceMock
            .Setup(x => x.PullLatestAsync(clonePath))
            .ReturnsAsync(true);
        _sessionServiceMock
            .Setup(x => x.StartSessionAsync(branchName, TestProject.Id, clonePath, SessionMode.Plan, "sonnet", null, default))
            .ReturnsAsync(session);

        var request = new CreateBranchSessionRequest
        {
            ProjectId = TestProject.Id,
            BranchName = branchName
        };

        // Act
        var result = await _controller.CreateBranchSession(request);

        // Assert
        Assert.That(result.Result, Is.TypeOf<CreatedResult>());
        var createdResult = (CreatedResult)result.Result!;
        var response = (CreateBranchSessionResponse)createdResult.Value!;
        Assert.That(response.ClonePath, Is.EqualTo(clonePath));

        // Verify clone was NOT created
        _cloneServiceMock.Verify(
            x => x.CreateCloneAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string?>()),
            Times.Never);
    }

    [Test]
    public async Task CreateBranchSession_PullsMainBeforeCreatingClone()
    {
        // Arrange
        var branchName = "feature/test";
        var clonePath = "/path/to/clone";
        var session = new ClaudeSession
        {
            Id = "session-123",
            EntityId = branchName,
            ProjectId = TestProject.Id,
            WorkingDirectory = clonePath,
            Model = "sonnet",
            Mode = SessionMode.Plan
        };

        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _fleeceIssuesSyncServiceMock
            .Setup(x => x.PullFleeceOnlyAsync(TestProject.LocalPath, TestProject.DefaultBranch, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FleecePullResult(
                Success: true,
                ErrorMessage: null,
                IssuesMerged: 2,
                WasBehindRemote: true,
                CommitsPulled: 3));
        _cloneServiceMock
            .Setup(x => x.GetClonePathForBranchAsync(TestProject.LocalPath, branchName))
            .ReturnsAsync((string?)null);
        _cloneServiceMock
            .Setup(x => x.CreateCloneAsync(TestProject.LocalPath, branchName, true, TestProject.DefaultBranch))
            .ReturnsAsync(clonePath);
        _cloneServiceMock
            .Setup(x => x.PullLatestAsync(clonePath))
            .ReturnsAsync(true);
        _sessionServiceMock
            .Setup(x => x.StartSessionAsync(branchName, TestProject.Id, clonePath, SessionMode.Plan, "sonnet", null, default))
            .ReturnsAsync(session);

        var request = new CreateBranchSessionRequest
        {
            ProjectId = TestProject.Id,
            BranchName = branchName
        };

        // Act
        var result = await _controller.CreateBranchSession(request);

        // Assert
        Assert.That(result.Result, Is.TypeOf<CreatedResult>());

        // Verify pull was called with default branch
        _fleeceIssuesSyncServiceMock.Verify(
            x => x.PullFleeceOnlyAsync(TestProject.LocalPath, TestProject.DefaultBranch, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task CreateBranchSession_PullsCloneAfterCreation()
    {
        // Arrange
        var branchName = "feature/test";
        var clonePath = "/path/to/clone";
        var session = new ClaudeSession
        {
            Id = "session-123",
            EntityId = branchName,
            ProjectId = TestProject.Id,
            WorkingDirectory = clonePath,
            Model = "sonnet",
            Mode = SessionMode.Plan
        };

        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _fleeceIssuesSyncServiceMock
            .Setup(x => x.PullFleeceOnlyAsync(TestProject.LocalPath, TestProject.DefaultBranch, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FleecePullResult(
                Success: true,
                ErrorMessage: null,
                IssuesMerged: 0,
                WasBehindRemote: false,
                CommitsPulled: 0));
        _cloneServiceMock
            .Setup(x => x.GetClonePathForBranchAsync(TestProject.LocalPath, branchName))
            .ReturnsAsync((string?)null);
        _cloneServiceMock
            .Setup(x => x.CreateCloneAsync(TestProject.LocalPath, branchName, true, TestProject.DefaultBranch))
            .ReturnsAsync(clonePath);
        _cloneServiceMock
            .Setup(x => x.PullLatestAsync(clonePath))
            .ReturnsAsync(true);
        _sessionServiceMock
            .Setup(x => x.StartSessionAsync(branchName, TestProject.Id, clonePath, SessionMode.Plan, "sonnet", null, default))
            .ReturnsAsync(session);

        var request = new CreateBranchSessionRequest
        {
            ProjectId = TestProject.Id,
            BranchName = branchName
        };

        // Act
        var result = await _controller.CreateBranchSession(request);

        // Assert
        Assert.That(result.Result, Is.TypeOf<CreatedResult>());

        // Verify pull was called on the clone
        _cloneServiceMock.Verify(
            x => x.PullLatestAsync(clonePath),
            Times.Once);
    }

    [Test]
    public async Task CreateBranchSession_ContinuesOnPullFailure()
    {
        // Arrange
        var branchName = "feature/test";
        var clonePath = "/path/to/clone";
        var session = new ClaudeSession
        {
            Id = "session-123",
            EntityId = branchName,
            ProjectId = TestProject.Id,
            WorkingDirectory = clonePath,
            Model = "sonnet",
            Mode = SessionMode.Plan
        };

        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _fleeceIssuesSyncServiceMock
            .Setup(x => x.PullFleeceOnlyAsync(TestProject.LocalPath, TestProject.DefaultBranch, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FleecePullResult(
                Success: false,
                ErrorMessage: "Network error",
                IssuesMerged: 0,
                WasBehindRemote: false,
                CommitsPulled: 0));
        _cloneServiceMock
            .Setup(x => x.GetClonePathForBranchAsync(TestProject.LocalPath, branchName))
            .ReturnsAsync((string?)null);
        _cloneServiceMock
            .Setup(x => x.CreateCloneAsync(TestProject.LocalPath, branchName, true, TestProject.DefaultBranch))
            .ReturnsAsync(clonePath);
        _cloneServiceMock
            .Setup(x => x.PullLatestAsync(clonePath))
            .ReturnsAsync(true);
        _sessionServiceMock
            .Setup(x => x.StartSessionAsync(branchName, TestProject.Id, clonePath, SessionMode.Plan, "sonnet", null, default))
            .ReturnsAsync(session);

        var request = new CreateBranchSessionRequest
        {
            ProjectId = TestProject.Id,
            BranchName = branchName
        };

        // Act
        var result = await _controller.CreateBranchSession(request);

        // Assert - should succeed despite pull failure
        Assert.That(result.Result, Is.TypeOf<CreatedResult>());
        var createdResult = (CreatedResult)result.Result!;
        var response = (CreateBranchSessionResponse)createdResult.Value!;
        Assert.That(response.SessionId, Is.EqualTo("session-123"));
    }

    [Test]
    public async Task CreateBranchSession_UsesCustomBaseBranch_WhenProvided()
    {
        // Arrange
        var branchName = "feature/test";
        var baseBranch = "develop";
        var clonePath = "/path/to/clone";
        var session = new ClaudeSession
        {
            Id = "session-123",
            EntityId = branchName,
            ProjectId = TestProject.Id,
            WorkingDirectory = clonePath,
            Model = "sonnet",
            Mode = SessionMode.Plan
        };

        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _fleeceIssuesSyncServiceMock
            .Setup(x => x.PullFleeceOnlyAsync(TestProject.LocalPath, baseBranch, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FleecePullResult(
                Success: true,
                ErrorMessage: null,
                IssuesMerged: 0,
                WasBehindRemote: false,
                CommitsPulled: 0));
        _cloneServiceMock
            .Setup(x => x.GetClonePathForBranchAsync(TestProject.LocalPath, branchName))
            .ReturnsAsync((string?)null);
        _cloneServiceMock
            .Setup(x => x.CreateCloneAsync(TestProject.LocalPath, branchName, true, baseBranch))
            .ReturnsAsync(clonePath);
        _cloneServiceMock
            .Setup(x => x.PullLatestAsync(clonePath))
            .ReturnsAsync(true);
        _sessionServiceMock
            .Setup(x => x.StartSessionAsync(branchName, TestProject.Id, clonePath, SessionMode.Plan, "sonnet", null, default))
            .ReturnsAsync(session);

        var request = new CreateBranchSessionRequest
        {
            ProjectId = TestProject.Id,
            BranchName = branchName,
            BaseBranch = baseBranch
        };

        // Act
        var result = await _controller.CreateBranchSession(request);

        // Assert
        Assert.That(result.Result, Is.TypeOf<CreatedResult>());

        // Verify pull was called with custom base branch
        _fleeceIssuesSyncServiceMock.Verify(
            x => x.PullFleeceOnlyAsync(TestProject.LocalPath, baseBranch, It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify clone was created with custom base branch
        _cloneServiceMock.Verify(
            x => x.CreateCloneAsync(TestProject.LocalPath, branchName, true, baseBranch),
            Times.Once);
    }

    [Test]
    public async Task CreateBranchSession_ReturnsBadRequest_WhenCloneCreationFails()
    {
        // Arrange
        var branchName = "feature/test";

        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _fleeceIssuesSyncServiceMock
            .Setup(x => x.PullFleeceOnlyAsync(TestProject.LocalPath, TestProject.DefaultBranch, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FleecePullResult(
                Success: true,
                ErrorMessage: null,
                IssuesMerged: 0,
                WasBehindRemote: false,
                CommitsPulled: 0));
        _cloneServiceMock
            .Setup(x => x.GetClonePathForBranchAsync(TestProject.LocalPath, branchName))
            .ReturnsAsync((string?)null);
        _cloneServiceMock
            .Setup(x => x.CreateCloneAsync(TestProject.LocalPath, branchName, true, TestProject.DefaultBranch))
            .ReturnsAsync((string?)null);

        var request = new CreateBranchSessionRequest
        {
            ProjectId = TestProject.Id,
            BranchName = branchName
        };

        // Act
        var result = await _controller.CreateBranchSession(request);

        // Assert
        Assert.That(result.Result, Is.TypeOf<BadRequestObjectResult>());
        var badRequestResult = (BadRequestObjectResult)result.Result!;
        Assert.That(badRequestResult.Value, Is.EqualTo("Failed to create clone for branch"));
    }

    [Test]
    public async Task CreateBranchSession_TrimsBranchName()
    {
        // Arrange
        var branchName = "  feature/test  ";
        var trimmedBranchName = "feature/test";
        var clonePath = "/path/to/clone";
        var session = new ClaudeSession
        {
            Id = "session-123",
            EntityId = trimmedBranchName,
            ProjectId = TestProject.Id,
            WorkingDirectory = clonePath,
            Model = "sonnet",
            Mode = SessionMode.Plan
        };

        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _fleeceIssuesSyncServiceMock
            .Setup(x => x.PullFleeceOnlyAsync(TestProject.LocalPath, TestProject.DefaultBranch, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FleecePullResult(
                Success: true,
                ErrorMessage: null,
                IssuesMerged: 0,
                WasBehindRemote: false,
                CommitsPulled: 0));
        _cloneServiceMock
            .Setup(x => x.GetClonePathForBranchAsync(TestProject.LocalPath, trimmedBranchName))
            .ReturnsAsync(clonePath);
        _cloneServiceMock
            .Setup(x => x.PullLatestAsync(clonePath))
            .ReturnsAsync(true);
        _sessionServiceMock
            .Setup(x => x.StartSessionAsync(trimmedBranchName, TestProject.Id, clonePath, SessionMode.Plan, "sonnet", null, default))
            .ReturnsAsync(session);

        var request = new CreateBranchSessionRequest
        {
            ProjectId = TestProject.Id,
            BranchName = branchName
        };

        // Act
        var result = await _controller.CreateBranchSession(request);

        // Assert
        Assert.That(result.Result, Is.TypeOf<CreatedResult>());
        var createdResult = (CreatedResult)result.Result!;
        var response = (CreateBranchSessionResponse)createdResult.Value!;
        Assert.That(response.BranchName, Is.EqualTo(trimmedBranchName));

        // Verify trimmed name was used
        _cloneServiceMock.Verify(
            x => x.GetClonePathForBranchAsync(TestProject.LocalPath, trimmedBranchName),
            Times.Once);
    }

    #endregion

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
}
