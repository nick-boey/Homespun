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
    public async Task CreateSession_WithBuildModePromptId_UsesBuildSessionMode()
    {
        // Arrange
        var buildPrompt = new AgentPrompt
        {
            Id = "prompt-build",
            Name = "BuildPrompt",
            Mode = SessionMode.Build,
            Category = PromptCategory.IssueAgent,
            InitialMessage = "Build: {{userPrompt}}"
        };

        _agentPromptServiceMock.Setup(a => a.GetPrompt("prompt-build")).Returns(buildPrompt);
        _agentPromptServiceMock.Setup(a => a.GetPromptBySessionType(SessionType.IssueAgentSystem))
            .Returns(new AgentPrompt { InitialMessage = "System prompt" });

        var request = new CreateIssuesAgentSessionRequest
        {
            ProjectId = TestProject.Id,
            PromptId = "prompt-build",
            UserInstructions = "Fix the bug"
        };

        // Act
        var result = await _controller.CreateSession(request);

        // Assert
        _sessionServiceMock.Verify(s => s.StartSessionAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            SessionMode.Build, It.IsAny<string>(), It.IsAny<string?>()), Times.Once);
    }

    [Test]
    public async Task CreateSession_WithPlanModePromptId_UsesPlanSessionMode()
    {
        // Arrange
        var planPrompt = new AgentPrompt
        {
            Id = "prompt-plan",
            Name = "PlanPrompt",
            Mode = SessionMode.Plan,
            Category = PromptCategory.IssueAgent,
            InitialMessage = "Plan: {{userPrompt}}"
        };

        _agentPromptServiceMock.Setup(a => a.GetPrompt("prompt-plan")).Returns(planPrompt);
        _agentPromptServiceMock.Setup(a => a.GetPromptBySessionType(SessionType.IssueAgentSystem))
            .Returns(new AgentPrompt { InitialMessage = "System prompt" });

        var request = new CreateIssuesAgentSessionRequest
        {
            ProjectId = TestProject.Id,
            PromptId = "prompt-plan",
            UserInstructions = "Plan the work"
        };

        // Act
        var result = await _controller.CreateSession(request);

        // Assert
        _sessionServiceMock.Verify(s => s.StartSessionAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            SessionMode.Plan, It.IsAny<string>(), It.IsAny<string?>()), Times.Once);
    }

    [Test]
    public async Task CreateSession_WithoutPromptId_FallsBackToDefaultBehavior()
    {
        // Arrange
        var defaultPrompt = new AgentPrompt
        {
            Id = "default-issue",
            Name = "IssueModify",
            Mode = SessionMode.Build,
            Category = PromptCategory.IssueAgent,
            InitialMessage = "Default: {{userPrompt}}"
        };

        _agentPromptServiceMock.Setup(a => a.GetIssueAgentPromptsForProject(TestProject.Id))
            .Returns(new List<AgentPrompt> { defaultPrompt });
        _agentPromptServiceMock.Setup(a => a.GetPromptBySessionType(SessionType.IssueAgentSystem))
            .Returns(new AgentPrompt { InitialMessage = "System prompt" });

        var request = new CreateIssuesAgentSessionRequest
        {
            ProjectId = TestProject.Id,
            UserInstructions = "Do something"
        };

        // Act
        var result = await _controller.CreateSession(request);

        // Assert - should use Build mode (default)
        _sessionServiceMock.Verify(s => s.StartSessionAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            SessionMode.Build, It.IsAny<string>(), It.IsAny<string?>()), Times.Once);

        // Should use the default prompt's template for rendering
        _agentPromptServiceMock.Verify(a => a.RenderTemplate(
            defaultPrompt.InitialMessage, It.IsAny<PromptContext>()), Times.Once);
    }

    [Test]
    public async Task CreateSession_WithInvalidPromptId_ReturnsBadRequest()
    {
        // Arrange
        _agentPromptServiceMock.Setup(a => a.GetPrompt("invalid-id")).Returns((AgentPrompt?)null);

        var request = new CreateIssuesAgentSessionRequest
        {
            ProjectId = TestProject.Id,
            PromptId = "invalid-id"
        };

        // Act
        var result = await _controller.CreateSession(request);

        // Assert
        Assert.That(result.Result, Is.InstanceOf<NotFoundObjectResult>());
        var notFound = (NotFoundObjectResult)result.Result!;
        Assert.That(notFound.Value?.ToString(), Does.Contain("Prompt not found"));
    }

    [Test]
    public async Task CreateSession_WithNonIssueAgentPromptId_ReturnsBadRequest()
    {
        // Arrange
        var standardPrompt = new AgentPrompt
        {
            Id = "standard-prompt",
            Name = "StandardPrompt",
            Mode = SessionMode.Build,
            Category = PromptCategory.Standard
        };

        _agentPromptServiceMock.Setup(a => a.GetPrompt("standard-prompt")).Returns(standardPrompt);

        var request = new CreateIssuesAgentSessionRequest
        {
            ProjectId = TestProject.Id,
            PromptId = "standard-prompt"
        };

        // Act
        var result = await _controller.CreateSession(request);

        // Assert
        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result.Result!;
        Assert.That(badRequest.Value?.ToString(), Does.Contain("IssueAgent"));
    }

    [Test]
    public async Task CreateSession_WithPromptId_UsesSelectedPromptTemplate()
    {
        // Arrange
        var selectedPrompt = new AgentPrompt
        {
            Id = "prompt-custom",
            Name = "CustomPrompt",
            Mode = SessionMode.Build,
            Category = PromptCategory.IssueAgent,
            InitialMessage = "Custom template: {{userPrompt}} for {{selectedIssueId}}"
        };

        _agentPromptServiceMock.Setup(a => a.GetPrompt("prompt-custom")).Returns(selectedPrompt);
        _agentPromptServiceMock.Setup(a => a.GetPromptBySessionType(SessionType.IssueAgentSystem))
            .Returns(new AgentPrompt { InitialMessage = "System prompt" });
        _agentPromptServiceMock.Setup(a => a.RenderTemplate(
                selectedPrompt.InitialMessage, It.IsAny<PromptContext>()))
            .Returns("Custom template: Fix bug for issue-1");

        var request = new CreateIssuesAgentSessionRequest
        {
            ProjectId = TestProject.Id,
            PromptId = "prompt-custom",
            UserInstructions = "Fix bug",
            SelectedIssueId = "issue-1"
        };

        // Act
        var result = await _controller.CreateSession(request);

        // Assert - should render with the selected prompt's template, not the hardcoded one
        _agentPromptServiceMock.Verify(a => a.RenderTemplate(
            selectedPrompt.InitialMessage, It.Is<PromptContext>(c =>
                c.UserPrompt == "Fix bug" && c.SelectedIssueId == "issue-1")), Times.Once);

        // Should NOT fetch the IssueAgentModification prompt
        _agentPromptServiceMock.Verify(a => a.GetPromptBySessionType(SessionType.IssueAgentModification), Times.Never);
    }

    [Test]
    public async Task CreateSession_WithPromptId_SendsMessageWithPromptMode()
    {
        // Arrange
        var planPrompt = new AgentPrompt
        {
            Id = "prompt-plan",
            Name = "PlanPrompt",
            Mode = SessionMode.Plan,
            Category = PromptCategory.IssueAgent,
            InitialMessage = "Plan: {{userPrompt}}"
        };

        _agentPromptServiceMock.Setup(a => a.GetPrompt("prompt-plan")).Returns(planPrompt);
        _agentPromptServiceMock.Setup(a => a.GetPromptBySessionType(SessionType.IssueAgentSystem))
            .Returns(new AgentPrompt { InitialMessage = "System prompt" });
        _agentPromptServiceMock.Setup(a => a.RenderTemplate(
                planPrompt.InitialMessage, It.IsAny<PromptContext>()))
            .Returns("Plan: Do the work");

        var request = new CreateIssuesAgentSessionRequest
        {
            ProjectId = TestProject.Id,
            PromptId = "prompt-plan",
            UserInstructions = "Do the work"
        };

        // Act
        var result = await _controller.CreateSession(request);

        // Assert - give the fire-and-forget task time to execute
        await Task.Delay(100);

        _sessionServiceMock.Verify(s => s.SendMessageAsync(
            "session-abc", "Plan: Do the work", SessionMode.Plan,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task CreateSession_AlwaysUsesIssueAgentSystemPrompt()
    {
        // Arrange
        var prompt = new AgentPrompt
        {
            Id = "prompt-1",
            Name = "TestPrompt",
            Mode = SessionMode.Plan,
            Category = PromptCategory.IssueAgent,
            InitialMessage = "test"
        };

        var systemPrompt = new AgentPrompt
        {
            InitialMessage = "You are the system prompt"
        };

        _agentPromptServiceMock.Setup(a => a.GetPrompt("prompt-1")).Returns(prompt);
        _agentPromptServiceMock.Setup(a => a.GetPromptBySessionType(SessionType.IssueAgentSystem))
            .Returns(systemPrompt);

        var request = new CreateIssuesAgentSessionRequest
        {
            ProjectId = TestProject.Id,
            PromptId = "prompt-1"
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
    public async Task CreateSession_WithoutPromptId_DefaultsToBuildMode()
    {
        // Arrange - no prompts available at all
        _agentPromptServiceMock.Setup(a => a.GetIssueAgentPromptsForProject(TestProject.Id))
            .Returns(new List<AgentPrompt>());
        _agentPromptServiceMock.Setup(a => a.GetPromptBySessionType(SessionType.IssueAgentModification))
            .Returns(new AgentPrompt { InitialMessage = "Fallback template" });
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
}
