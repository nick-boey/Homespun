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
            Name = "BuildPrompt",
            Mode = SessionMode.Build,
            Category = PromptCategory.IssueAgent,
            InitialMessage = "Build template"
        };

        _agentPromptServiceMock.Setup(a => a.GetPrompt("prompt-build", null)).Returns(buildPrompt);
        _agentPromptServiceMock.Setup(a => a.GetPromptBySessionType(SessionType.IssueAgentSystem))
            .Returns(new AgentPrompt { InitialMessage = "System prompt" });

        var request = new CreateIssuesAgentSessionRequest
        {
            ProjectId = TestProject.Id,
            PromptName = "prompt-build",
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
            Name = "PlanPrompt",
            Mode = SessionMode.Plan,
            Category = PromptCategory.IssueAgent,
            InitialMessage = "Plan template"
        };

        _agentPromptServiceMock.Setup(a => a.GetPrompt("prompt-plan", null)).Returns(planPrompt);
        _agentPromptServiceMock.Setup(a => a.GetPromptBySessionType(SessionType.IssueAgentSystem))
            .Returns(new AgentPrompt { InitialMessage = "System prompt" });

        var request = new CreateIssuesAgentSessionRequest
        {
            ProjectId = TestProject.Id,
            PromptName = "prompt-plan",
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
            Name = "IssueModify",
            Mode = SessionMode.Build,
            Category = PromptCategory.IssueAgent,
            InitialMessage = "Default template"
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

        // Should send UserInstructions verbatim (no RenderTemplate call)
        _agentPromptServiceMock.Verify(a => a.RenderTemplate(
            It.IsAny<string?>(), It.IsAny<PromptContext>()), Times.Never);

        // Give fire-and-forget task time to execute
        await Task.Delay(100);

        _sessionServiceMock.Verify(s => s.SendMessageAsync(
            "session-abc", "Do something", SessionMode.Build,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task CreateSession_WithInvalidPromptId_ReturnsBadRequest()
    {
        // Arrange
        _agentPromptServiceMock.Setup(a => a.GetPrompt("invalid-id", null)).Returns((AgentPrompt?)null);

        var request = new CreateIssuesAgentSessionRequest
        {
            ProjectId = TestProject.Id,
            PromptName = "invalid-id"
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
            Name = "StandardPrompt",
            Mode = SessionMode.Build,
            Category = PromptCategory.Standard
        };

        _agentPromptServiceMock.Setup(a => a.GetPrompt("standard-prompt", null)).Returns(standardPrompt);

        var request = new CreateIssuesAgentSessionRequest
        {
            ProjectId = TestProject.Id,
            PromptName = "standard-prompt"
        };

        // Act
        var result = await _controller.CreateSession(request);

        // Assert
        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result.Result!;
        Assert.That(badRequest.Value?.ToString(), Does.Contain("IssueAgent"));
    }

    [Test]
    public async Task CreateSession_WithPromptId_SendsUserInstructionsVerbatim()
    {
        // Arrange
        var selectedPrompt = new AgentPrompt
        {
            Name = "CustomPrompt",
            Mode = SessionMode.Build,
            Category = PromptCategory.IssueAgent,
            InitialMessage = "Custom template for {{selectedIssueId}}"
        };

        _agentPromptServiceMock.Setup(a => a.GetPrompt("prompt-custom", null)).Returns(selectedPrompt);
        _agentPromptServiceMock.Setup(a => a.GetPromptBySessionType(SessionType.IssueAgentSystem))
            .Returns(new AgentPrompt { InitialMessage = "System prompt" });

        var request = new CreateIssuesAgentSessionRequest
        {
            ProjectId = TestProject.Id,
            PromptName = "prompt-custom",
            UserInstructions = "Fix bug",
            SelectedIssueId = "issue-1"
        };

        // Act
        var result = await _controller.CreateSession(request);

        // Assert - should NOT call RenderTemplate (frontend now handles rendering)
        _agentPromptServiceMock.Verify(a => a.RenderTemplate(
            It.IsAny<string?>(), It.IsAny<PromptContext>()), Times.Never);

        // Give fire-and-forget task time to execute
        await Task.Delay(100);

        // Should send UserInstructions verbatim
        _sessionServiceMock.Verify(s => s.SendMessageAsync(
            "session-abc", "Fix bug", SessionMode.Build,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task CreateSession_WithPromptId_SendsMessageWithPromptMode()
    {
        // Arrange
        var planPrompt = new AgentPrompt
        {
            Name = "PlanPrompt",
            Mode = SessionMode.Plan,
            Category = PromptCategory.IssueAgent,
            InitialMessage = "Plan template"
        };

        _agentPromptServiceMock.Setup(a => a.GetPrompt("prompt-plan", null)).Returns(planPrompt);
        _agentPromptServiceMock.Setup(a => a.GetPromptBySessionType(SessionType.IssueAgentSystem))
            .Returns(new AgentPrompt { InitialMessage = "System prompt" });

        var request = new CreateIssuesAgentSessionRequest
        {
            ProjectId = TestProject.Id,
            PromptName = "prompt-plan",
            UserInstructions = "Do the work"
        };

        // Act
        var result = await _controller.CreateSession(request);

        // Assert - give the fire-and-forget task time to execute
        await Task.Delay(100);

        // Should send verbatim UserInstructions with the prompt's mode
        _sessionServiceMock.Verify(s => s.SendMessageAsync(
            "session-abc", "Do the work", SessionMode.Plan,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task CreateSession_AlwaysUsesIssueAgentSystemPrompt()
    {
        // Arrange
        var prompt = new AgentPrompt
        {
            Name = "TestPrompt",
            Mode = SessionMode.Plan,
            Category = PromptCategory.IssueAgent,
            InitialMessage = "test"
        };

        var systemPrompt = new AgentPrompt
        {
            InitialMessage = "You are the system prompt"
        };

        _agentPromptServiceMock.Setup(a => a.GetPrompt("prompt-1", null)).Returns(prompt);
        _agentPromptServiceMock.Setup(a => a.GetPromptBySessionType(SessionType.IssueAgentSystem))
            .Returns(systemPrompt);

        var request = new CreateIssuesAgentSessionRequest
        {
            ProjectId = TestProject.Id,
            PromptName = "prompt-1"
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
    public async Task CreateSession_WithEmptyUserInstructions_RendersPromptTemplateAndSendsMessage()
    {
        // Arrange
        var prompt = new AgentPrompt
        {
            Name = "TestPrompt",
            Mode = SessionMode.Build,
            Category = PromptCategory.IssueAgent,
            InitialMessage = "Do the work for {{selectedIssueId}}"
        };

        _agentPromptServiceMock.Setup(a => a.GetPrompt("TestPrompt", null)).Returns(prompt);
        _agentPromptServiceMock.Setup(a => a.GetPromptBySessionType(SessionType.IssueAgentSystem))
            .Returns(new AgentPrompt { InitialMessage = "System prompt" });
        _agentPromptServiceMock.Setup(a => a.RenderTemplate(
                prompt.InitialMessage, It.IsAny<PromptContext>()))
            .Returns("Do the work for issue-1");

        var request = new CreateIssuesAgentSessionRequest
        {
            ProjectId = TestProject.Id,
            PromptName = "TestPrompt",
            SelectedIssueId = "issue-1"
            // No UserInstructions — server-side template rendering should kick in
        };

        // Act
        await _controller.CreateSession(request);

        // Assert - give fire-and-forget task time to execute
        await Task.Delay(100);

        // Should render the prompt template server-side
        _agentPromptServiceMock.Verify(a => a.RenderTemplate(
            prompt.InitialMessage,
            It.Is<PromptContext>(ctx => ctx.SelectedIssueId == "issue-1")), Times.Once);

        // Should send the rendered template as initial message
        _sessionServiceMock.Verify(s => s.SendMessageAsync(
            "session-abc", "Do the work for issue-1", SessionMode.Build,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task CreateSession_WithNoPromptAndNoInstructions_SendsFallbackMessage()
    {
        // Arrange - no prompts available at all, no user instructions
        _agentPromptServiceMock.Setup(a => a.GetIssueAgentPromptsForProject(TestProject.Id))
            .Returns(new List<AgentPrompt>());
        _agentPromptServiceMock.Setup(a => a.GetPromptBySessionType(SessionType.IssueAgentModification))
            .Returns((AgentPrompt?)null);
        _agentPromptServiceMock.Setup(a => a.GetPromptBySessionType(SessionType.IssueAgentSystem))
            .Returns((AgentPrompt?)null);

        var request = new CreateIssuesAgentSessionRequest
        {
            ProjectId = TestProject.Id
            // No PromptName, no UserInstructions
        };

        // Act
        await _controller.CreateSession(request);

        // Assert - give fire-and-forget task time to execute
        await Task.Delay(100);

        // Should send a fallback message to ensure the Docker container starts
        _sessionServiceMock.Verify(s => s.SendMessageAsync(
            "session-abc", "Begin working on the assigned issues.", SessionMode.Build,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task CreateSession_WithSelectedIssueId_UsesIssueIdAsEntityId()
    {
        // Arrange
        var prompt = new AgentPrompt
        {
            Name = "TestPrompt",
            Mode = SessionMode.Build,
            Category = PromptCategory.IssueAgent,
            InitialMessage = "Fix: {{userPrompt}}"
        };

        _agentPromptServiceMock.Setup(a => a.GetPrompt("TestPrompt", null)).Returns(prompt);
        _agentPromptServiceMock.Setup(a => a.GetPromptBySessionType(SessionType.IssueAgentSystem))
            .Returns(new AgentPrompt { InitialMessage = "System prompt" });

        var request = new CreateIssuesAgentSessionRequest
        {
            ProjectId = TestProject.Id,
            PromptName = "TestPrompt",
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
        var prompt = new AgentPrompt
        {
            Name = "TestPrompt",
            Mode = SessionMode.Build,
            Category = PromptCategory.IssueAgent,
            InitialMessage = "Fix: {{userPrompt}}"
        };

        _agentPromptServiceMock.Setup(a => a.GetPrompt("TestPrompt", null)).Returns(prompt);
        _agentPromptServiceMock.Setup(a => a.GetPromptBySessionType(SessionType.IssueAgentSystem))
            .Returns(new AgentPrompt { InitialMessage = "System prompt" });

        var request = new CreateIssuesAgentSessionRequest
        {
            ProjectId = TestProject.Id,
            PromptName = "TestPrompt",
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

        // Assert - entityId should be the branch name (starts with "issues-agent-")
        _sessionServiceMock.Verify(s => s.StartSessionAsync(
            It.Is<string>(id => id.StartsWith("issues-agent-") && !id.Contains("abc")),
            TestProject.Id, It.IsAny<string>(),
            It.IsAny<SessionMode>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Once);
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
