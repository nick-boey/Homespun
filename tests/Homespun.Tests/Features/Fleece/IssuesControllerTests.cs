using Fleece.Core.Models;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.Fleece.Controllers;
using Homespun.Features.Fleece.Services;
using Homespun.Features.Git;
using Homespun.Features.Notifications;
using Homespun.Features.Projects;
using Homespun.Features.AgentOrchestration.Services;
using Homespun.Shared.Models.Fleece;
using Homespun.Shared.Models.Projects;
using Homespun.Shared.Models.Sessions;
using Homespun.Shared.Requests;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Homespun.Tests.Features.Fleece;

/// <summary>
/// Unit tests for IssuesController SignalR broadcasting.
/// </summary>
[TestFixture]
public class IssuesControllerTests
{
    private Mock<IFleeceService> _fleeceServiceMock = null!;
    private Mock<IProjectService> _projectServiceMock = null!;
    private Mock<IHubContext<NotificationHub>> _notificationHubMock = null!;
    private Mock<IIssueBranchResolverService> _branchResolverServiceMock = null!;
    private Mock<IIssueHistoryService> _historyServiceMock = null!;
    private Mock<IClaudeSessionService> _sessionServiceMock = null!;
    private Mock<IAgentPromptService> _agentPromptServiceMock = null!;
    private Mock<IGitCloneService> _cloneServiceMock = null!;
    private Mock<IBranchIdBackgroundService> _branchIdBackgroundServiceMock = null!;
    private Mock<IFleeceChangeApplicationService> _changeApplicationServiceMock = null!;
    private Mock<ILogger<IssuesController>> _loggerMock = null!;
    private Mock<IHubClients> _clientsMock = null!;
    private Mock<IClientProxy> _allClientsMock = null!;
    private Mock<IClientProxy> _groupClientsMock = null!;
    private IssuesController _controller = null!;

    private static readonly Project TestProject = new()
    {
        Id = "project-123",
        Name = "Test Project",
        LocalPath = "/path/to/project",
        DefaultBranch = "main"
    };

    private static Issue CreateTestIssue(string id, string title, IssueType type = IssueType.Task) => new()
    {
        Id = id,
        Title = title,
        Type = type,
        Status = IssueStatus.Open,
        LastUpdate = DateTimeOffset.UtcNow
    };

    [SetUp]
    public void SetUp()
    {
        _fleeceServiceMock = new Mock<IFleeceService>();
        _projectServiceMock = new Mock<IProjectService>();
        _notificationHubMock = new Mock<IHubContext<NotificationHub>>();
        _branchResolverServiceMock = new Mock<IIssueBranchResolverService>();
        _historyServiceMock = new Mock<IIssueHistoryService>();
        _sessionServiceMock = new Mock<IClaudeSessionService>();
        _agentPromptServiceMock = new Mock<IAgentPromptService>();
        _cloneServiceMock = new Mock<IGitCloneService>();
        _branchIdBackgroundServiceMock = new Mock<IBranchIdBackgroundService>();
        _changeApplicationServiceMock = new Mock<IFleeceChangeApplicationService>();
        _loggerMock = new Mock<ILogger<IssuesController>>();
        _clientsMock = new Mock<IHubClients>();
        _allClientsMock = new Mock<IClientProxy>();
        _groupClientsMock = new Mock<IClientProxy>();

        _notificationHubMock.Setup(x => x.Clients).Returns(_clientsMock.Object);
        _clientsMock.Setup(x => x.All).Returns(_allClientsMock.Object);
        _clientsMock.Setup(x => x.Group(It.IsAny<string>())).Returns(_groupClientsMock.Object);

        _controller = new IssuesController(
            _fleeceServiceMock.Object,
            _projectServiceMock.Object,
            _notificationHubMock.Object,
            _branchResolverServiceMock.Object,
            _historyServiceMock.Object,
            _sessionServiceMock.Object,
            _agentPromptServiceMock.Object,
            _cloneServiceMock.Object,
            _branchIdBackgroundServiceMock.Object,
            _changeApplicationServiceMock.Object,
            NullLogger<IssuesController>.Instance);
    }

    #region Create Tests

    [Test]
    public async Task Create_BroadcastsIssuesChangedEvent_WithCreatedChangeType()
    {
        // Arrange
        var issue = CreateTestIssue("ABC123", "Test Issue");

        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _fleeceServiceMock
            .Setup(x => x.CreateIssueAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IssueType>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<ExecutionMode?>()))
            .ReturnsAsync(issue);

        var request = new CreateIssueRequest { ProjectId = TestProject.Id, Title = "Test Issue" };

        // Act
        await _controller.Create(request);

        // Assert
        _groupClientsMock.Verify(
            x => x.SendCoreAsync("IssuesChanged",
                It.Is<object?[]>(args =>
                    (string)args[0]! == TestProject.Id &&
                    (IssueChangeType)args[1]! == IssueChangeType.Created &&
                    (string)args[2]! == issue.Id),
                default),
            Times.Once);
    }

    [Test]
    public async Task Create_ReturnsCreatedResult()
    {
        // Arrange
        var issue = CreateTestIssue("ABC123", "Test Issue");

        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _fleeceServiceMock
            .Setup(x => x.CreateIssueAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IssueType>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<ExecutionMode?>()))
            .ReturnsAsync(issue);

        var request = new CreateIssueRequest { ProjectId = TestProject.Id, Title = "Test Issue" };

        // Act
        var result = await _controller.Create(request);

        // Assert
        Assert.That(result.Result, Is.TypeOf<CreatedAtActionResult>());
    }

    [Test]
    public async Task Create_ProjectNotFound_DoesNotBroadcast()
    {
        // Arrange
        _projectServiceMock
            .Setup(x => x.GetByIdAsync(It.IsAny<string>()))
            .ReturnsAsync((Project?)null);

        var request = new CreateIssueRequest { ProjectId = "nonexistent", Title = "Test Issue" };

        // Act
        await _controller.Create(request);

        // Assert
        _allClientsMock.Verify(
            x => x.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), default),
            Times.Never);
    }

    #endregion

    #region Update Tests

    [Test]
    public async Task Update_BroadcastsIssuesChangedEvent_WithUpdatedChangeType()
    {
        // Arrange
        var issueId = "ABC123";
        var issue = CreateTestIssue(issueId, "Updated Issue", IssueType.Bug);

        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _fleeceServiceMock
            .Setup(x => x.GetIssueAsync(It.IsAny<string>(), issueId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestIssue(issueId, "Original Issue", IssueType.Bug));
        _fleeceServiceMock
            .Setup(x => x.UpdateIssueAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<IssueStatus?>(), It.IsAny<IssueType?>(), It.IsAny<string?>(),
                It.IsAny<int?>(), It.IsAny<ExecutionMode?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(issue);

        var request = new UpdateIssueRequest { ProjectId = TestProject.Id, Title = "Updated Issue" };

        // Act
        await _controller.Update(issueId, request);

        // Assert
        _groupClientsMock.Verify(
            x => x.SendCoreAsync("IssuesChanged",
                It.Is<object?[]>(args =>
                    (string)args[0]! == TestProject.Id &&
                    (IssueChangeType)args[1]! == IssueChangeType.Updated &&
                    (string)args[2]! == issueId),
                default),
            Times.Once);
    }

    [Test]
    public async Task Update_IssueNotFound_DoesNotBroadcast()
    {
        // Arrange
        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _fleeceServiceMock
            .Setup(x => x.UpdateIssueAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<IssueStatus?>(), It.IsAny<IssueType?>(), It.IsAny<string?>(),
                It.IsAny<int?>(), It.IsAny<ExecutionMode?>(), It.IsAny<string?>()))
            .ReturnsAsync((Issue?)null);

        var request = new UpdateIssueRequest { ProjectId = TestProject.Id, Title = "Updated Issue" };

        // Act
        await _controller.Update("nonexistent", request);

        // Assert
        _allClientsMock.Verify(
            x => x.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), default),
            Times.Never);
    }

    #endregion

    #region Delete Tests

    [Test]
    public async Task Delete_BroadcastsIssuesChangedEvent_WithDeletedChangeType()
    {
        // Arrange
        var issueId = "ABC123";

        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _fleeceServiceMock
            .Setup(x => x.DeleteIssueAsync(TestProject.LocalPath, issueId))
            .ReturnsAsync(true);

        // Act
        await _controller.Delete(issueId, TestProject.Id);

        // Assert
        _groupClientsMock.Verify(
            x => x.SendCoreAsync("IssuesChanged",
                It.Is<object?[]>(args =>
                    (string)args[0]! == TestProject.Id &&
                    (IssueChangeType)args[1]! == IssueChangeType.Deleted &&
                    (string)args[2]! == issueId),
                default),
            Times.Once);
    }

    [Test]
    public async Task Delete_IssueNotFound_DoesNotBroadcast()
    {
        // Arrange
        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _fleeceServiceMock
            .Setup(x => x.DeleteIssueAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(false);

        // Act
        await _controller.Delete("nonexistent", TestProject.Id);

        // Assert
        _allClientsMock.Verify(
            x => x.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), default),
            Times.Never);
    }

    #endregion

    #region RunAgent Tests

    [Test]
    public async Task RunAgent_RendersPromptTemplateWithIssueContext()
    {
        // Arrange
        var issueId = "ABC123";
        var issue = new Issue
        {
            Id = issueId,
            Title = "Fix authentication bug",
            Type = IssueType.Bug,
            Status = IssueStatus.Open,
            Description = "Users cannot log in with OAuth",
            LastUpdate = DateTimeOffset.UtcNow
        };

        var prompt = new AgentPrompt
        {
            Id = "prompt-1",
            Name = "Build",
            InitialMessage = "## Issue: {{title}}\n\n**ID:** {{id}}\n**Type:** {{type}}\n\n### Description\n{{description}}",
            Mode = SessionMode.Build
        };

        var session = new ClaudeSession
        {
            Id = "session-123",
            EntityId = issueId,
            ProjectId = TestProject.Id,
            WorkingDirectory = "/path/to/clone",
            Model = "sonnet",
            Mode = SessionMode.Build
        };

        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _fleeceServiceMock
            .Setup(x => x.GetIssueAsync(TestProject.LocalPath, issueId))
            .ReturnsAsync(issue);
        _agentPromptServiceMock
            .Setup(x => x.GetPrompt("prompt-1"))
            .Returns(prompt);
        _branchResolverServiceMock
            .Setup(x => x.ResolveIssueBranchAsync(TestProject.Id, issueId))
            .ReturnsAsync((string?)null);
        _cloneServiceMock
            .Setup(x => x.GetClonePathForBranchAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("/path/to/clone");
        _agentPromptServiceMock
            .Setup(x => x.RenderTemplate(prompt.InitialMessage, It.IsAny<PromptContext>()))
            .Returns<string?, PromptContext>((template, context) =>
            {
                // Verify the context is populated correctly
                Assert.That(context.Title, Is.EqualTo("Fix authentication bug"));
                Assert.That(context.Id, Is.EqualTo("ABC123"));
                Assert.That(context.Description, Is.EqualTo("Users cannot log in with OAuth"));
                Assert.That(context.Type, Is.EqualTo("Bug"));
                return $"## Issue: {context.Title}\n\n**ID:** {context.Id}\n**Type:** {context.Type}\n\n### Description\n{context.Description}";
            });
        _sessionServiceMock
            .Setup(x => x.StartSessionAsync(
                issueId, TestProject.Id, "/path/to/clone", SessionMode.Build, It.IsAny<string>(), null, default))
            .ReturnsAsync(session);

        var request = new RunAgentRequest
        {
            ProjectId = TestProject.Id,
            PromptId = "prompt-1",
            Model = "sonnet"
        };

        // Act
        var result = await _controller.RunAgent(issueId, request);

        // Assert
        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result!;
        var response = (RunAgentResponse)okResult.Value!;
        Assert.That(response.SessionId, Is.EqualTo("session-123"));

        // Verify RenderTemplate was called with the correct context
        _agentPromptServiceMock.Verify(
            x => x.RenderTemplate(prompt.InitialMessage, It.Is<PromptContext>(ctx =>
                ctx.Title == "Fix authentication bug" &&
                ctx.Id == "ABC123" &&
                ctx.Description == "Users cannot log in with OAuth" &&
                ctx.Type == "Bug")),
            Times.Once);
    }

    [Test]
    public async Task RunAgent_ProjectNotFound_ReturnsNotFound()
    {
        // Arrange
        _projectServiceMock
            .Setup(x => x.GetByIdAsync(It.IsAny<string>()))
            .ReturnsAsync((Project?)null);

        var request = new RunAgentRequest { ProjectId = "nonexistent", PromptId = "prompt-1" };

        // Act
        var result = await _controller.RunAgent("issue-123", request);

        // Assert
        Assert.That(result.Result, Is.TypeOf<NotFoundObjectResult>());
    }

    [Test]
    public async Task RunAgent_IssueNotFound_ReturnsNotFound()
    {
        // Arrange
        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _fleeceServiceMock
            .Setup(x => x.GetIssueAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((Issue?)null);

        var request = new RunAgentRequest { ProjectId = TestProject.Id, PromptId = "prompt-1" };

        // Act
        var result = await _controller.RunAgent("nonexistent", request);

        // Assert
        Assert.That(result.Result, Is.TypeOf<NotFoundObjectResult>());
    }

    [Test]
    public async Task RunAgent_PromptNotFound_ReturnsNotFound()
    {
        // Arrange
        var issue = CreateTestIssue("ABC123", "Test Issue");

        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _fleeceServiceMock
            .Setup(x => x.GetIssueAsync(TestProject.LocalPath, "ABC123"))
            .ReturnsAsync(issue);
        _agentPromptServiceMock
            .Setup(x => x.GetPrompt(It.IsAny<string>()))
            .Returns((AgentPrompt?)null);

        var request = new RunAgentRequest { ProjectId = TestProject.Id, PromptId = "nonexistent" };

        // Act
        var result = await _controller.RunAgent("ABC123", request);

        // Assert
        Assert.That(result.Result, Is.TypeOf<NotFoundObjectResult>());
    }

    [Test]
    public async Task RunAgent_CreatesCloneIfNotExists()
    {
        // Arrange
        var issueId = "ABC123";
        var issue = CreateTestIssue(issueId, "Test Issue");
        var prompt = new AgentPrompt { Id = "prompt-1", Name = "Build", Mode = SessionMode.Build };
        var session = new ClaudeSession
        {
            Id = "session-123",
            EntityId = issueId,
            ProjectId = TestProject.Id,
            WorkingDirectory = "/path/to/new-clone",
            Model = "sonnet",
            Mode = SessionMode.Build
        };

        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _fleeceServiceMock
            .Setup(x => x.GetIssueAsync(TestProject.LocalPath, issueId))
            .ReturnsAsync(issue);
        _agentPromptServiceMock
            .Setup(x => x.GetPrompt("prompt-1"))
            .Returns(prompt);
        _branchResolverServiceMock
            .Setup(x => x.ResolveIssueBranchAsync(TestProject.Id, issueId))
            .ReturnsAsync((string?)null);
        _cloneServiceMock
            .Setup(x => x.GetClonePathForBranchAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((string?)null); // No existing clone
        _cloneServiceMock
            .Setup(x => x.CreateCloneAsync(TestProject.LocalPath, It.IsAny<string>(), true, TestProject.DefaultBranch))
            .ReturnsAsync("/path/to/new-clone");
        _sessionServiceMock
            .Setup(x => x.StartSessionAsync(
                issueId, TestProject.Id, "/path/to/new-clone", SessionMode.Build, It.IsAny<string>(), null, default))
            .ReturnsAsync(session);

        var request = new RunAgentRequest { ProjectId = TestProject.Id, PromptId = "prompt-1" };

        // Act
        var result = await _controller.RunAgent(issueId, request);

        // Assert
        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());

        // Verify CreateCloneAsync was called
        _cloneServiceMock.Verify(
            x => x.CreateCloneAsync(TestProject.LocalPath, It.IsAny<string>(), true, TestProject.DefaultBranch),
            Times.Once);
    }

    [Test]
    public async Task RunAgent_UsesExistingCloneIfExists()
    {
        // Arrange
        var issueId = "ABC123";
        var issue = CreateTestIssue(issueId, "Test Issue");
        var prompt = new AgentPrompt { Id = "prompt-1", Name = "Build", Mode = SessionMode.Build };
        var session = new ClaudeSession
        {
            Id = "session-123",
            EntityId = issueId,
            ProjectId = TestProject.Id,
            WorkingDirectory = "/path/to/existing-clone",
            Model = "sonnet",
            Mode = SessionMode.Build
        };

        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _fleeceServiceMock
            .Setup(x => x.GetIssueAsync(TestProject.LocalPath, issueId))
            .ReturnsAsync(issue);
        _agentPromptServiceMock
            .Setup(x => x.GetPrompt("prompt-1"))
            .Returns(prompt);
        _branchResolverServiceMock
            .Setup(x => x.ResolveIssueBranchAsync(TestProject.Id, issueId))
            .ReturnsAsync((string?)null);
        _cloneServiceMock
            .Setup(x => x.GetClonePathForBranchAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("/path/to/existing-clone"); // Clone exists
        _sessionServiceMock
            .Setup(x => x.StartSessionAsync(
                issueId, TestProject.Id, "/path/to/existing-clone", SessionMode.Build, It.IsAny<string>(), null, default))
            .ReturnsAsync(session);

        var request = new RunAgentRequest { ProjectId = TestProject.Id, PromptId = "prompt-1" };

        // Act
        var result = await _controller.RunAgent(issueId, request);

        // Assert
        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result!;
        var response = (RunAgentResponse)okResult.Value!;
        Assert.That(response.ClonePath, Is.EqualTo("/path/to/existing-clone"));

        // Verify CreateCloneAsync was NOT called
        _cloneServiceMock.Verify(
            x => x.CreateCloneAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string?>()),
            Times.Never);
    }

    [Test]
    public async Task RunAgent_WithNullPromptId_CreatesSessionWithoutInitialMessage()
    {
        // Arrange
        var issueId = "ABC123";
        var issue = CreateTestIssue(issueId, "Test Issue");
        var session = new ClaudeSession
        {
            Id = "session-123",
            EntityId = issueId,
            ProjectId = TestProject.Id,
            WorkingDirectory = "/path/to/clone",
            Model = "sonnet",
            Mode = SessionMode.Plan // Default for None prompt
        };

        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _fleeceServiceMock
            .Setup(x => x.GetIssueAsync(TestProject.LocalPath, issueId))
            .ReturnsAsync(issue);
        _branchResolverServiceMock
            .Setup(x => x.ResolveIssueBranchAsync(TestProject.Id, issueId))
            .ReturnsAsync((string?)null);
        _cloneServiceMock
            .Setup(x => x.GetClonePathForBranchAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("/path/to/clone");
        _sessionServiceMock
            .Setup(x => x.StartSessionAsync(
                issueId, TestProject.Id, "/path/to/clone", SessionMode.Plan, It.IsAny<string>(), null, default))
            .ReturnsAsync(session);

        var request = new RunAgentRequest
        {
            ProjectId = TestProject.Id,
            PromptId = null, // Null prompt ID for "None" option
            Model = "sonnet"
        };

        // Act
        var result = await _controller.RunAgent(issueId, request);

        // Assert
        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result!;
        var response = (RunAgentResponse)okResult.Value!;
        Assert.That(response.SessionId, Is.EqualTo("session-123"));

        // Verify GetPrompt was NOT called
        _agentPromptServiceMock.Verify(
            x => x.GetPrompt(It.IsAny<string>()),
            Times.Never);

        // Verify RenderTemplate was NOT called
        _agentPromptServiceMock.Verify(
            x => x.RenderTemplate(It.IsAny<string?>(), It.IsAny<PromptContext>()),
            Times.Never);

        // Verify session was started with Plan mode and null initial message
        _sessionServiceMock.Verify(
            x => x.StartSessionAsync(
                issueId,
                TestProject.Id,
                "/path/to/clone",
                SessionMode.Plan,  // Should use Plan mode for None
                It.IsAny<string>(),
                null,  // No initial message
                default),
            Times.Once);
    }

    #endregion
}
