using Fleece.Core.Models;
using Homespun.Features.AgentOrchestration.Services;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.Fleece.Services;
using Homespun.Features.Git;
using Homespun.Features.Notifications;
using Homespun.Shared.Models.Fleece;
using Homespun.Shared.Models.Sessions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using AgentStartRequest = Homespun.Features.AgentOrchestration.Services.AgentStartRequest;

namespace Homespun.Tests.Features.AgentOrchestration;

[TestFixture]
public class AgentStartBackgroundServiceTests
{
    private Mock<IServiceProvider> _mockServiceProvider = null!;
    private Mock<IServiceScope> _mockServiceScope = null!;
    private Mock<IServiceScopeFactory> _mockServiceScopeFactory = null!;
    private Mock<IGitCloneService> _mockCloneService = null!;
    private Mock<IClaudeSessionService> _mockSessionService = null!;
    private Mock<IAgentPromptService> _mockAgentPromptService = null!;
    private Mock<IFleeceService> _mockFleeceService = null!;
    private Mock<IFleeceIssuesSyncService> _mockIssuesSyncService = null!;
    private Mock<IHubContext<NotificationHub>> _mockHubContext = null!;
    private Mock<IHubClients> _mockHubClients = null!;
    private Mock<IClientProxy> _mockClientProxy = null!;
    private Mock<IAgentStartupTracker> _mockStartupTracker = null!;
    private Mock<ILogger<AgentStartBackgroundService>> _mockLogger = null!;
    private AgentStartBackgroundService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _mockServiceProvider = new Mock<IServiceProvider>();
        _mockServiceScope = new Mock<IServiceScope>();
        _mockServiceScopeFactory = new Mock<IServiceScopeFactory>();
        _mockCloneService = new Mock<IGitCloneService>();
        _mockSessionService = new Mock<IClaudeSessionService>();
        _mockAgentPromptService = new Mock<IAgentPromptService>();
        _mockFleeceService = new Mock<IFleeceService>();
        _mockIssuesSyncService = new Mock<IFleeceIssuesSyncService>();
        _mockHubContext = new Mock<IHubContext<NotificationHub>>();
        _mockHubClients = new Mock<IHubClients>();
        _mockClientProxy = new Mock<IClientProxy>();
        _mockStartupTracker = new Mock<IAgentStartupTracker>();
        _mockLogger = new Mock<ILogger<AgentStartBackgroundService>>();

        // Setup service scope
        var scopedServiceProvider = new Mock<IServiceProvider>();
        scopedServiceProvider.Setup(x => x.GetService(typeof(IGitCloneService)))
            .Returns(_mockCloneService.Object);
        scopedServiceProvider.Setup(x => x.GetService(typeof(IClaudeSessionService)))
            .Returns(_mockSessionService.Object);
        scopedServiceProvider.Setup(x => x.GetService(typeof(IAgentPromptService)))
            .Returns(_mockAgentPromptService.Object);
        scopedServiceProvider.Setup(x => x.GetService(typeof(IFleeceService)))
            .Returns(_mockFleeceService.Object);
        scopedServiceProvider.Setup(x => x.GetService(typeof(IFleeceIssuesSyncService)))
            .Returns(_mockIssuesSyncService.Object);
        scopedServiceProvider.Setup(x => x.GetService(typeof(IHubContext<NotificationHub>)))
            .Returns(_mockHubContext.Object);

        _mockServiceScope.Setup(x => x.ServiceProvider).Returns(scopedServiceProvider.Object);
        _mockServiceScopeFactory.Setup(x => x.CreateScope()).Returns(_mockServiceScope.Object);
        _mockServiceProvider.Setup(x => x.GetService(typeof(IServiceScopeFactory)))
            .Returns(_mockServiceScopeFactory.Object);

        // Setup hub context
        _mockHubContext.Setup(x => x.Clients).Returns(_mockHubClients.Object);
        _mockHubClients.Setup(x => x.All).Returns(_mockClientProxy.Object);
        _mockHubClients.Setup(x => x.Group(It.IsAny<string>())).Returns(_mockClientProxy.Object);

        _service = new AgentStartBackgroundService(
            _mockServiceProvider.Object,
            _mockStartupTracker.Object,
            _mockLogger.Object);
    }

    private AgentStartRequest CreateTestRequest(string issueId = "issue123", string projectId = "proj123")
    {
        var ts = DateTimeOffset.UtcNow;
        return new AgentStartRequest
        {
            IssueId = issueId,
            ProjectId = projectId,
            ProjectLocalPath = "/path/to/project",
            ProjectDefaultBranch = "main",
            Issue = new Issue
            {
                Id = issueId,
                Title = "Test Issue",
                Status = IssueStatus.Progress,
                Type = IssueType.Task,
                LastUpdate = ts
            },
            PromptId = "prompt123",
            BaseBranch = null,
            Model = "sonnet",
            BranchName = "task/test-issue+issue123"
        };
    }

    #region QueueAgentStartAsync Tests

    [Test]
    public async Task QueueAgentStartAsync_CreatesCloneWhenNotExists_AndStartsSession()
    {
        // Arrange
        var request = CreateTestRequest();
        var clonePath = "/path/to/clone";
        var session = new ClaudeSession
        {
            Id = "session123",
            EntityId = request.IssueId,
            ProjectId = request.ProjectId,
            WorkingDirectory = clonePath,
            Model = "sonnet",
            Mode = SessionMode.Build,
            Status = ClaudeSessionStatus.Running
        };

        _mockCloneService.Setup(x => x.GetClonePathForBranchAsync(
                request.ProjectLocalPath, request.BranchName))
            .ReturnsAsync((string?)null);

        _mockIssuesSyncService.Setup(x => x.PullFleeceOnlyAsync(
                request.ProjectLocalPath, "main", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FleecePullResult(Success: true, ErrorMessage: null, IssuesMerged: 0, WasBehindRemote: false, CommitsPulled: 0));

        _mockCloneService.Setup(x => x.CreateCloneAsync(
                request.ProjectLocalPath, request.BranchName, true, "main"))
            .ReturnsAsync(clonePath);

        _mockAgentPromptService.Setup(x => x.GetPrompt("prompt123"))
            .Returns(new AgentPrompt
            {
                Id = "prompt123",
                Name = "Build",
                Mode = SessionMode.Build,
                InitialMessage = "Work on {{title}}"
            });

        _mockFleeceService.Setup(x => x.ListIssuesAsync(
                request.ProjectLocalPath, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Issue>());

        _mockSessionService.Setup(x => x.StartSessionAsync(
                request.IssueId, request.ProjectId, clonePath,
                SessionMode.Build, "sonnet", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        // Act
        await _service.QueueAgentStartAsync(request);
        await Task.Delay(200); // Wait for background task

        // Assert
        _mockCloneService.Verify(x => x.CreateCloneAsync(
            request.ProjectLocalPath, request.BranchName, true, "main"), Times.Once);

        _mockSessionService.Verify(x => x.StartSessionAsync(
            request.IssueId, request.ProjectId, clonePath,
            SessionMode.Build, "sonnet", null, It.IsAny<CancellationToken>()), Times.Once);

        _mockStartupTracker.Verify(x => x.MarkAsStarted(request.IssueId), Times.Once);

        // Verify AgentStarting notification was sent (twice: once to All, once to Group)
        _mockClientProxy.Verify(x => x.SendCoreAsync(
            "AgentStarting",
            It.Is<object?[]>(args =>
                args[0]!.Equals(request.IssueId) &&
                args[1]!.Equals(request.ProjectId) &&
                args[2]!.Equals(request.BranchName)),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Test]
    public async Task QueueAgentStartAsync_UsesExistingClone_WhenCloneExists()
    {
        // Arrange
        var request = CreateTestRequest();
        var existingClonePath = "/path/to/existing/clone";
        var session = new ClaudeSession
        {
            Id = "session123",
            EntityId = request.IssueId,
            ProjectId = request.ProjectId,
            WorkingDirectory = existingClonePath,
            Model = "sonnet",
            Mode = SessionMode.Plan,
            Status = ClaudeSessionStatus.Running
        };

        _mockCloneService.Setup(x => x.GetClonePathForBranchAsync(
                request.ProjectLocalPath, request.BranchName))
            .ReturnsAsync(existingClonePath);

        _mockAgentPromptService.Setup(x => x.GetPrompt("prompt123"))
            .Returns(new AgentPrompt
            {
                Id = "prompt123",
                Name = "Plan",
                Mode = SessionMode.Plan,
                InitialMessage = "Plan for {{title}}"
            });

        _mockFleeceService.Setup(x => x.ListIssuesAsync(
                request.ProjectLocalPath, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Issue>());

        _mockSessionService.Setup(x => x.StartSessionAsync(
                request.IssueId, request.ProjectId, existingClonePath,
                SessionMode.Plan, "sonnet", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        // Act
        await _service.QueueAgentStartAsync(request);
        await Task.Delay(200);

        // Assert
        _mockCloneService.Verify(x => x.CreateCloneAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
            It.IsAny<string>()), Times.Never);

        _mockSessionService.Verify(x => x.StartSessionAsync(
            request.IssueId, request.ProjectId, existingClonePath,
            SessionMode.Plan, "sonnet", null, It.IsAny<CancellationToken>()), Times.Once);

        _mockStartupTracker.Verify(x => x.MarkAsStarted(request.IssueId), Times.Once);
    }

    [Test]
    public async Task QueueAgentStartAsync_BroadcastsFailure_WhenCloneCreationFails()
    {
        // Arrange
        var request = CreateTestRequest();

        _mockCloneService.Setup(x => x.GetClonePathForBranchAsync(
                request.ProjectLocalPath, request.BranchName))
            .ReturnsAsync((string?)null);

        _mockIssuesSyncService.Setup(x => x.PullFleeceOnlyAsync(
                request.ProjectLocalPath, "main", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FleecePullResult(Success: true, ErrorMessage: null, IssuesMerged: 0, WasBehindRemote: false, CommitsPulled: 0));

        _mockCloneService.Setup(x => x.CreateCloneAsync(
                request.ProjectLocalPath, request.BranchName, true, "main"))
            .ReturnsAsync((string?)null); // Clone creation fails

        // Act
        await _service.QueueAgentStartAsync(request);
        await Task.Delay(200);

        // Assert
        _mockStartupTracker.Verify(x => x.MarkAsFailed(
            request.IssueId, It.IsAny<string>()), Times.Once);

        // Verify AgentStartFailed notification was sent (twice: once to All, once to Group)
        _mockClientProxy.Verify(x => x.SendCoreAsync(
            "AgentStartFailed",
            It.Is<object?[]>(args =>
                args[0]!.Equals(request.IssueId) &&
                args[1]!.Equals(request.ProjectId)),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Test]
    public async Task QueueAgentStartAsync_BroadcastsFailure_WhenSessionCreationFails()
    {
        // Arrange
        var request = CreateTestRequest();
        var clonePath = "/path/to/clone";

        _mockCloneService.Setup(x => x.GetClonePathForBranchAsync(
                request.ProjectLocalPath, request.BranchName))
            .ReturnsAsync(clonePath);

        _mockAgentPromptService.Setup(x => x.GetPrompt("prompt123"))
            .Returns(new AgentPrompt
            {
                Id = "prompt123",
                Name = "Build",
                Mode = SessionMode.Build,
                InitialMessage = "Work on {{title}}"
            });

        _mockFleeceService.Setup(x => x.ListIssuesAsync(
                request.ProjectLocalPath, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Issue>());

        _mockSessionService.Setup(x => x.StartSessionAsync(
                request.IssueId, request.ProjectId, clonePath,
                SessionMode.Build, "sonnet", null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Session creation failed"));

        // Act
        await _service.QueueAgentStartAsync(request);
        await Task.Delay(200);

        // Assert
        _mockStartupTracker.Verify(x => x.MarkAsFailed(
            request.IssueId, "Session creation failed"), Times.Once);

        // Verify AgentStartFailed notification was sent (twice: once to All, once to Group)
        _mockClientProxy.Verify(x => x.SendCoreAsync(
            "AgentStartFailed",
            It.Is<object?[]>(args =>
                args[0]!.Equals(request.IssueId) &&
                args[1]!.Equals(request.ProjectId) &&
                args[2]!.Equals("Session creation failed")),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Test]
    public async Task QueueAgentStartAsync_SkipsDuplicate_WhenAlreadyStarting()
    {
        // Arrange
        var request = CreateTestRequest();
        var clonePath = "/path/to/clone";
        var session = new ClaudeSession
        {
            Id = "session123",
            EntityId = request.IssueId,
            ProjectId = request.ProjectId,
            WorkingDirectory = clonePath,
            Model = "sonnet",
            Mode = SessionMode.Plan,
            Status = ClaudeSessionStatus.Running
        };

        _mockCloneService.Setup(x => x.GetClonePathForBranchAsync(
                request.ProjectLocalPath, request.BranchName))
            .Returns(async () =>
            {
                await Task.Delay(200); // Simulate slow operation
                return clonePath;
            });

        _mockAgentPromptService.Setup(x => x.GetPrompt("prompt123"))
            .Returns(new AgentPrompt { Id = "prompt123", Name = "Plan", Mode = SessionMode.Plan });

        _mockFleeceService.Setup(x => x.ListIssuesAsync(
                request.ProjectLocalPath, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Issue>());

        _mockSessionService.Setup(x => x.StartSessionAsync(
                request.IssueId, request.ProjectId, clonePath,
                SessionMode.Plan, "sonnet", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        // Act - Queue twice in quick succession
        await _service.QueueAgentStartAsync(request);
        await _service.QueueAgentStartAsync(request); // Should be ignored as duplicate

        await Task.Delay(400); // Wait for first task to complete

        // Assert - Only one session creation
        _mockSessionService.Verify(x => x.StartSessionAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<SessionMode>(), It.IsAny<string>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task QueueAgentStartAsync_DefaultsToPlanMode_WhenNoPromptProvided()
    {
        // Arrange
        var request = CreateTestRequest();
        request = request with { PromptId = null };
        var clonePath = "/path/to/clone";
        var session = new ClaudeSession
        {
            Id = "session123",
            EntityId = request.IssueId,
            ProjectId = request.ProjectId,
            WorkingDirectory = clonePath,
            Model = "sonnet",
            Mode = SessionMode.Plan,
            Status = ClaudeSessionStatus.Running
        };

        _mockCloneService.Setup(x => x.GetClonePathForBranchAsync(
                request.ProjectLocalPath, request.BranchName))
            .ReturnsAsync(clonePath);

        _mockSessionService.Setup(x => x.StartSessionAsync(
                request.IssueId, request.ProjectId, clonePath,
                SessionMode.Plan, "sonnet", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        // Act
        await _service.QueueAgentStartAsync(request);
        await Task.Delay(200);

        // Assert - Session should be created with Plan mode
        _mockSessionService.Verify(x => x.StartSessionAsync(
            request.IssueId, request.ProjectId, clonePath,
            SessionMode.Plan, "sonnet", null, It.IsAny<CancellationToken>()), Times.Once);

        // No prompt rendering should have happened
        _mockAgentPromptService.Verify(x => x.RenderTemplate(
            It.IsAny<string>(), It.IsAny<PromptContext>()), Times.Never);
    }

    [Test]
    public async Task QueueAgentStartAsync_UsesSpecifiedBaseBranch_WhenProvided()
    {
        // Arrange
        var request = CreateTestRequest();
        request = request with { BaseBranch = "develop" };
        var clonePath = "/path/to/clone";
        var session = new ClaudeSession
        {
            Id = "session123",
            EntityId = request.IssueId,
            ProjectId = request.ProjectId,
            WorkingDirectory = clonePath,
            Model = "sonnet",
            Mode = SessionMode.Plan,
            Status = ClaudeSessionStatus.Running
        };

        _mockCloneService.Setup(x => x.GetClonePathForBranchAsync(
                request.ProjectLocalPath, request.BranchName))
            .ReturnsAsync((string?)null);

        _mockIssuesSyncService.Setup(x => x.PullFleeceOnlyAsync(
                request.ProjectLocalPath, "develop", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FleecePullResult(Success: true, ErrorMessage: null, IssuesMerged: 0, WasBehindRemote: false, CommitsPulled: 0));

        _mockCloneService.Setup(x => x.CreateCloneAsync(
                request.ProjectLocalPath, request.BranchName, true, "develop"))
            .ReturnsAsync(clonePath);

        _mockAgentPromptService.Setup(x => x.GetPrompt("prompt123"))
            .Returns(new AgentPrompt { Id = "prompt123", Name = "Plan", Mode = SessionMode.Plan });

        _mockFleeceService.Setup(x => x.ListIssuesAsync(
                request.ProjectLocalPath, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Issue>());

        _mockSessionService.Setup(x => x.StartSessionAsync(
                request.IssueId, request.ProjectId, clonePath,
                SessionMode.Plan, "sonnet", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        // Act
        await _service.QueueAgentStartAsync(request);
        await Task.Delay(200);

        // Assert - Clone should be created from "develop" branch
        _mockCloneService.Verify(x => x.CreateCloneAsync(
            request.ProjectLocalPath, request.BranchName, true, "develop"), Times.Once);
    }

    #endregion
}
