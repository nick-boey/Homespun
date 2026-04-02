using Homespun.Features.ClaudeCode.Services;
using Homespun.Shared.Models.Fleece;
using Homespun.Features.Fleece.Controllers;
using Homespun.Features.Fleece.Services;
using Homespun.Features.Git;
using Homespun.Features.Gitgraph.Services;
using Homespun.Features.Notifications;
using Homespun.Features.Projects;
using Homespun.Features.PullRequests.Data;
using Homespun.Shared.Models.Projects;
using Homespun.Shared.Models.Sessions;
using Homespun.Shared.Requests;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.Fleece;

[TestFixture]
public class IssuesAgentControllerTests
{
    private Mock<IProjectService> _projectServiceMock = null!;
    private Mock<IFleeceService> _fleeceServiceMock = null!;
    private Mock<IFleeceIssuesSyncService> _fleeceIssuesSyncServiceMock = null!;
    private Mock<IFleeceChangeDetectionService> _changeDetectionServiceMock = null!;
    private Mock<IFleeceChangeApplicationService> _changeApplicationServiceMock = null!;
    private Mock<IFleecePostMergeService> _postMergeServiceMock = null!;
    private Mock<IDataStore> _dataStoreMock = null!;
    private Mock<IGitCloneService> _cloneServiceMock = null!;
    private Mock<IClaudeSessionService> _sessionServiceMock = null!;
    private Mock<IAgentPromptService> _agentPromptServiceMock = null!;
    private Mock<IGraphService> _graphServiceMock = null!;
    private Mock<IHubContext<NotificationHub>> _notificationHubMock = null!;
    private Mock<ILogger<IssuesAgentController>> _loggerMock = null!;
    private IssuesAgentController _controller = null!;

    private static readonly Project TestProject = new()
    {
        Id = "project-123",
        Name = "Test Project",
        LocalPath = "/path/to/project",
        DefaultBranch = "main"
    };

    [SetUp]
    public void SetUp()
    {
        _projectServiceMock = new Mock<IProjectService>();
        _fleeceServiceMock = new Mock<IFleeceService>();
        _fleeceIssuesSyncServiceMock = new Mock<IFleeceIssuesSyncService>();
        _changeDetectionServiceMock = new Mock<IFleeceChangeDetectionService>();
        _changeApplicationServiceMock = new Mock<IFleeceChangeApplicationService>();
        _postMergeServiceMock = new Mock<IFleecePostMergeService>();
        _dataStoreMock = new Mock<IDataStore>();
        _cloneServiceMock = new Mock<IGitCloneService>();
        _sessionServiceMock = new Mock<IClaudeSessionService>();
        _agentPromptServiceMock = new Mock<IAgentPromptService>();
        _graphServiceMock = new Mock<IGraphService>();
        _notificationHubMock = new Mock<IHubContext<NotificationHub>>();
        _loggerMock = new Mock<ILogger<IssuesAgentController>>();

        _controller = new IssuesAgentController(
            _projectServiceMock.Object,
            _fleeceServiceMock.Object,
            _fleeceIssuesSyncServiceMock.Object,
            _changeDetectionServiceMock.Object,
            _changeApplicationServiceMock.Object,
            _postMergeServiceMock.Object,
            _dataStoreMock.Object,
            _cloneServiceMock.Object,
            _sessionServiceMock.Object,
            _agentPromptServiceMock.Object,
            _graphServiceMock.Object,
            _notificationHubMock.Object,
            _loggerMock.Object);

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        // Default setup: project exists, clone succeeds, pull succeeds
        _projectServiceMock.Setup(p => p.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);

        _cloneServiceMock.Setup(c => c.CreateCloneAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>()))
            .ReturnsAsync("/clone/path");

        _fleeceIssuesSyncServiceMock.Setup(f => f.PullFleeceOnlyAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FleecePullResult(Success: true, ErrorMessage: null, IssuesMerged: 0, WasBehindRemote: false, CommitsPulled: 0));

        _sessionServiceMock.Setup(s => s.StartSessionAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<SessionMode>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string entityId, string projectId, string workDir,
                SessionMode mode, string model, string? systemPrompt, CancellationToken _) => new ClaudeSession
            {
                Id = "session-abc",
                EntityId = entityId,
                ProjectId = projectId,
                WorkingDirectory = workDir,
                Model = model,
                Mode = mode,
                Status = ClaudeSessionStatus.Running,
                CreatedAt = DateTime.UtcNow
            });
    }

    [Test]
    public async Task CreateSession_WithExplicitBuildMode_UsesBuildSessionMode()
    {
        // Arrange
        _agentPromptServiceMock.Setup(a => a.GetPromptBySessionType(SessionType.IssueAgentSystem))
            .Returns(new AgentPrompt { InitialMessage = "System prompt" });

        var request = new CreateIssuesAgentSessionRequest
        {
            ProjectId = TestProject.Id,
            Mode = SessionMode.Build,
            UserInstructions = "Fix the bug"
        };

        // Act
        await _controller.CreateSession(request);

        // Assert
        _sessionServiceMock.Verify(s => s.StartSessionAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            SessionMode.Build, It.IsAny<string>(), It.IsAny<string?>()), Times.Once);
    }

    [Test]
    public async Task CreateSession_WithExplicitPlanMode_UsesPlanSessionMode()
    {
        // Arrange
        _agentPromptServiceMock.Setup(a => a.GetPromptBySessionType(SessionType.IssueAgentSystem))
            .Returns(new AgentPrompt { InitialMessage = "System prompt" });

        var request = new CreateIssuesAgentSessionRequest
        {
            ProjectId = TestProject.Id,
            Mode = SessionMode.Plan,
            UserInstructions = "Plan the work"
        };

        // Act
        await _controller.CreateSession(request);

        // Assert
        _sessionServiceMock.Verify(s => s.StartSessionAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            SessionMode.Plan, It.IsAny<string>(), It.IsAny<string?>()), Times.Once);
    }

    [Test]
    public async Task CreateSession_WithoutMode_DefaultsToBuildMode()
    {
        // Arrange
        _agentPromptServiceMock.Setup(a => a.GetPromptBySessionType(SessionType.IssueAgentSystem))
            .Returns(new AgentPrompt { InitialMessage = "System prompt" });

        var request = new CreateIssuesAgentSessionRequest
        {
            ProjectId = TestProject.Id,
            UserInstructions = "Do something"
        };

        // Act
        await _controller.CreateSession(request);

        // Assert - defaults to Build mode
        _sessionServiceMock.Verify(s => s.StartSessionAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            SessionMode.Build, It.IsAny<string>(), It.IsAny<string?>()), Times.Once);
    }

    [Test]
    public async Task CreateSession_SendsUserInstructionsVerbatim()
    {
        // Arrange
        _agentPromptServiceMock.Setup(a => a.GetPromptBySessionType(SessionType.IssueAgentSystem))
            .Returns(new AgentPrompt { InitialMessage = "System prompt" });

        var request = new CreateIssuesAgentSessionRequest
        {
            ProjectId = TestProject.Id,
            Mode = SessionMode.Build,
            UserInstructions = "Fix bug",
            SelectedIssueId = "issue-1"
        };

        // Act
        await _controller.CreateSession(request);

        // Give fire-and-forget task time to execute
        await Task.Delay(100);

        // Should send UserInstructions verbatim
        _sessionServiceMock.Verify(s => s.SendMessageAsync(
            "session-abc", "Fix bug", SessionMode.Build,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task CreateSession_WithNoInstructions_DoesNotSendMessage()
    {
        // Arrange
        _agentPromptServiceMock.Setup(a => a.GetPromptBySessionType(SessionType.IssueAgentSystem))
            .Returns((AgentPrompt?)null);

        var request = new CreateIssuesAgentSessionRequest
        {
            ProjectId = TestProject.Id
        };

        // Act
        await _controller.CreateSession(request);

        // Give fire-and-forget task time to execute
        await Task.Delay(100);

        // Should not send any message - session starts in waiting state
        _sessionServiceMock.Verify(s => s.SendMessageAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<SessionMode>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task CreateSession_AlwaysUsesIssueAgentSystemPrompt()
    {
        // Arrange
        var systemPrompt = new AgentPrompt
        {
            InitialMessage = "You are the system prompt"
        };

        _agentPromptServiceMock.Setup(a => a.GetPromptBySessionType(SessionType.IssueAgentSystem))
            .Returns(systemPrompt);

        var request = new CreateIssuesAgentSessionRequest
        {
            ProjectId = TestProject.Id,
            Mode = SessionMode.Plan
        };

        // Act
        await _controller.CreateSession(request);

        // Assert - system prompt is always the IssueAgentSystem one
        _sessionServiceMock.Verify(s => s.StartSessionAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<SessionMode>(), It.IsAny<string>(),
            "You are the system prompt"), Times.Once);
    }

    [Test]
    public async Task CreateSession_WithSelectedIssueId_UsesIssueIdAsEntityId()
    {
        // Arrange
        _agentPromptServiceMock.Setup(a => a.GetPromptBySessionType(SessionType.IssueAgentSystem))
            .Returns(new AgentPrompt { InitialMessage = "System prompt" });

        var request = new CreateIssuesAgentSessionRequest
        {
            ProjectId = TestProject.Id,
            Mode = SessionMode.Build,
            UserInstructions = "Fix the bug",
            SelectedIssueId = "abc123"
        };

        // Act
        await _controller.CreateSession(request);

        // Assert - entityId should be the SelectedIssueId, not the branch name
        _sessionServiceMock.Verify(s => s.StartSessionAsync(
            "abc123", TestProject.Id, It.IsAny<string>(),
            It.IsAny<SessionMode>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Once);
    }

    [Test]
    public async Task CreateSession_WithSelectedIssueId_IncludesIssueIdInBranchName()
    {
        // Arrange
        _agentPromptServiceMock.Setup(a => a.GetPromptBySessionType(SessionType.IssueAgentSystem))
            .Returns(new AgentPrompt { InitialMessage = "System prompt" });

        var request = new CreateIssuesAgentSessionRequest
        {
            ProjectId = TestProject.Id,
            Mode = SessionMode.Build,
            UserInstructions = "Fix the bug",
            SelectedIssueId = "abc123"
        };

        // Act
        var result = await _controller.CreateSession(request);

        // Assert - branch name should contain the issue ID
        var created = result.Result as CreatedResult;
        Assert.That(created, Is.Not.Null);
        var response = created!.Value as CreateIssuesAgentSessionResponse;
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.BranchName, Does.Contain("abc123"));
    }

    [Test]
    public async Task CreateSession_WithoutSelectedIssueId_UsesBranchNameAsEntityId()
    {
        // Arrange
        _agentPromptServiceMock.Setup(a => a.GetPromptBySessionType(SessionType.IssueAgentSystem))
            .Returns(new AgentPrompt { InitialMessage = "System prompt" });

        var request = new CreateIssuesAgentSessionRequest
        {
            ProjectId = TestProject.Id,
            UserInstructions = "Do something"
        };

        // Act
        await _controller.CreateSession(request);

        // Assert - entityId should be the branch name (starts with "issues-agent-")
        _sessionServiceMock.Verify(s => s.StartSessionAsync(
            It.Is<string>(id => id.StartsWith("issues-agent-") && !id.Contains("abc")),
            TestProject.Id, It.IsAny<string>(),
            It.IsAny<SessionMode>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Once);
    }

    [Test]
    public async Task CreateSession_SendsMessageWithExplicitMode()
    {
        // Arrange
        _agentPromptServiceMock.Setup(a => a.GetPromptBySessionType(SessionType.IssueAgentSystem))
            .Returns(new AgentPrompt { InitialMessage = "System prompt" });

        var request = new CreateIssuesAgentSessionRequest
        {
            ProjectId = TestProject.Id,
            Mode = SessionMode.Plan,
            UserInstructions = "Do the work"
        };

        // Act
        await _controller.CreateSession(request);

        // Give the fire-and-forget task time to execute
        await Task.Delay(100);

        // Should send with the explicit mode
        _sessionServiceMock.Verify(s => s.SendMessageAsync(
            "session-abc", "Do the work", SessionMode.Plan,
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
