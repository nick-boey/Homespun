using Homespun.ClaudeAgentSdk;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Shared.Models.Sessions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using SdkPermissionMode = Homespun.ClaudeAgentSdk.PermissionMode;
using SharedPermissionMode = Homespun.Shared.Models.Sessions.PermissionMode;

namespace Homespun.Tests.Features.ClaudeCode;

[TestFixture]
public class ClaudeSessionServiceTests
{
    private ClaudeSessionService _service = null!;
    private IClaudeSessionStore _sessionStore = null!;
    private SessionOptionsFactory _optionsFactory = null!;
    private Mock<ILogger<ClaudeSessionService>> _loggerMock = null!;
    private Mock<ILogger<SessionOptionsFactory>> _factoryLoggerMock = null!;
    private Mock<IHubContext<Homespun.Features.ClaudeCode.Hubs.ClaudeCodeHub>> _hubContextMock = null!;
    private Mock<IClaudeSessionDiscovery> _discoveryMock = null!;
    private Mock<ISessionMetadataStore> _metadataStoreMock = null!;
    private Mock<IMessageCacheStore> _messageCacheMock = null!;
    private IToolResultParser _toolResultParser = null!;
    private Mock<IHooksService> _hooksServiceMock = null!;
    private Mock<IAgentExecutionService> _agentExecutionServiceMock = null!;

    [SetUp]
    public void SetUp()
    {
        _sessionStore = new ClaudeSessionStore();
        _factoryLoggerMock = new Mock<ILogger<SessionOptionsFactory>>();
        _optionsFactory = new SessionOptionsFactory(_factoryLoggerMock.Object);
        _loggerMock = new Mock<ILogger<ClaudeSessionService>>();
        _hubContextMock = new Mock<IHubContext<Homespun.Features.ClaudeCode.Hubs.ClaudeCodeHub>>();
        _discoveryMock = new Mock<IClaudeSessionDiscovery>();
        _metadataStoreMock = new Mock<ISessionMetadataStore>();
        _messageCacheMock = new Mock<IMessageCacheStore>();
        _toolResultParser = new ToolResultParser();
        _hooksServiceMock = new Mock<IHooksService>();
        _agentExecutionServiceMock = new Mock<IAgentExecutionService>();

        // Setup mock hub clients
        var clientsMock = new Mock<IHubClients>();
        var clientProxyMock = new Mock<IClientProxy>();
        clientsMock.Setup(c => c.All).Returns(clientProxyMock.Object);
        clientsMock.Setup(c => c.Group(It.IsAny<string>())).Returns(clientProxyMock.Object);
        _hubContextMock.Setup(h => h.Clients).Returns(clientsMock.Object);

        _service = new ClaudeSessionService(
            _sessionStore,
            _optionsFactory,
            _loggerMock.Object,
            _hubContextMock.Object,
            _discoveryMock.Object,
            _metadataStoreMock.Object,
            _toolResultParser,
            _hooksServiceMock.Object,
            _messageCacheMock.Object,
            _agentExecutionServiceMock.Object);
    }

    [Test]
    public async Task StartSessionAsync_CreatesNewSession()
    {
        // Arrange
        var entityId = "entity-123";
        var projectId = "project-456";
        var workingDirectory = "/test/path";
        var model = "claude-sonnet-4-20250514";

        // Act
        var session = await _service.StartSessionAsync(
            entityId,
            projectId,
            workingDirectory,
            SessionMode.Plan,
            model);

        // Assert - Note: Session may be in Error state if SDK can't connect (no Claude Code CLI)
        Assert.Multiple(() =>
        {
            Assert.That(session, Is.Not.Null);
            Assert.That(session.EntityId, Is.EqualTo(entityId));
            Assert.That(session.ProjectId, Is.EqualTo(projectId));
            Assert.That(session.WorkingDirectory, Is.EqualTo(workingDirectory));
            Assert.That(session.Mode, Is.EqualTo(SessionMode.Plan));
            Assert.That(session.Model, Is.EqualTo(model));
            // Status will be Error if SDK can't connect, or WaitingForInput if it can
            Assert.That(session.Status, Is.AnyOf(ClaudeSessionStatus.Starting, ClaudeSessionStatus.WaitingForInput, ClaudeSessionStatus.Error));
        });
    }

    [Test]
    public async Task StartSessionAsync_StoresSessionInStore()
    {
        // Arrange
        var entityId = "entity-123";
        var projectId = "project-456";

        // Act
        var session = await _service.StartSessionAsync(
            entityId,
            projectId,
            "/test/path",
            SessionMode.Build,
            "claude-sonnet-4-20250514");

        // Assert
        var retrieved = _sessionStore.GetById(session.Id);
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved!.Id, Is.EqualTo(session.Id));
    }

    [Test]
    public async Task StartSessionAsync_WithSystemPrompt_IncludesInSession()
    {
        // Arrange
        var systemPrompt = "You are a helpful assistant.";

        // Act
        var session = await _service.StartSessionAsync(
            "entity-123",
            "project-456",
            "/test/path",
            SessionMode.Build,
            "claude-sonnet-4-20250514",
            systemPrompt);

        // Assert
        Assert.That(session.SystemPrompt, Is.EqualTo(systemPrompt));
    }

    [Test]
    public async Task StartSessionAsync_GeneratesUniqueId()
    {
        // Act
        var session1 = await _service.StartSessionAsync(
            "entity-1", "project-1", "/path1", SessionMode.Plan, "model");
        var session2 = await _service.StartSessionAsync(
            "entity-2", "project-2", "/path2", SessionMode.Plan, "model");

        // Assert
        Assert.That(session1.Id, Is.Not.EqualTo(session2.Id));
    }

    [Test]
    public void GetSession_ExistingSession_ReturnsSession()
    {
        // Arrange
        var session = new ClaudeSession
        {
            Id = "test-session-id",
            EntityId = "entity-123",
            ProjectId = "project-456",
            WorkingDirectory = "/test/path",
            Model = "claude-sonnet-4-20250514",
            Mode = SessionMode.Plan,
            Status = ClaudeSessionStatus.Running,
            CreatedAt = DateTime.UtcNow
        };
        _sessionStore.Add(session);

        // Act
        var result = _service.GetSession("test-session-id");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Id, Is.EqualTo("test-session-id"));
    }

    [Test]
    public void GetSession_NonExistentSession_ReturnsNull()
    {
        // Act
        var result = _service.GetSession("non-existent-id");

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void GetSessionByEntityId_ExistingEntity_ReturnsSession()
    {
        // Arrange
        var session = new ClaudeSession
        {
            Id = "session-id",
            EntityId = "entity-123",
            ProjectId = "project-456",
            WorkingDirectory = "/test/path",
            Model = "claude-sonnet-4-20250514",
            Mode = SessionMode.Plan,
            Status = ClaudeSessionStatus.Running,
            CreatedAt = DateTime.UtcNow
        };
        _sessionStore.Add(session);

        // Act
        var result = _service.GetSessionByEntityId("entity-123");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.EntityId, Is.EqualTo("entity-123"));
    }

    [Test]
    public void GetSessionsForProject_ReturnsProjectSessions()
    {
        // Arrange
        var session1 = new ClaudeSession
        {
            Id = "session-1",
            EntityId = "entity-1",
            ProjectId = "project-A",
            WorkingDirectory = "/path1",
            Model = "model",
            Mode = SessionMode.Plan,
            Status = ClaudeSessionStatus.Running,
            CreatedAt = DateTime.UtcNow
        };
        var session2 = new ClaudeSession
        {
            Id = "session-2",
            EntityId = "entity-2",
            ProjectId = "project-A",
            WorkingDirectory = "/path2",
            Model = "model",
            Mode = SessionMode.Build,
            Status = ClaudeSessionStatus.Running,
            CreatedAt = DateTime.UtcNow
        };
        var session3 = new ClaudeSession
        {
            Id = "session-3",
            EntityId = "entity-3",
            ProjectId = "project-B",
            WorkingDirectory = "/path3",
            Model = "model",
            Mode = SessionMode.Plan,
            Status = ClaudeSessionStatus.Running,
            CreatedAt = DateTime.UtcNow
        };
        _sessionStore.Add(session1);
        _sessionStore.Add(session2);
        _sessionStore.Add(session3);

        // Act
        var result = _service.GetSessionsForProject("project-A");

        // Assert
        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result.All(s => s.ProjectId == "project-A"), Is.True);
    }

    [Test]
    public void GetAllSessions_ReturnsAllSessions()
    {
        // Arrange
        for (int i = 0; i < 5; i++)
        {
            _sessionStore.Add(new ClaudeSession
            {
                Id = $"session-{i}",
                EntityId = $"entity-{i}",
                ProjectId = $"project-{i}",
                WorkingDirectory = $"/path{i}",
                Model = "model",
                Mode = SessionMode.Plan,
                Status = ClaudeSessionStatus.Running,
                CreatedAt = DateTime.UtcNow
            });
        }

        // Act
        var result = _service.GetAllSessions();

        // Assert
        Assert.That(result, Has.Count.EqualTo(5));
    }

    [Test]
    public async Task StopSessionAsync_RemovesSessionFromStore()
    {
        // Arrange
        var session = await _service.StartSessionAsync(
            "entity-123",
            "project-456",
            "/test/path",
            SessionMode.Build,
            "claude-sonnet-4-20250514");

        // Act
        await _service.StopSessionAsync(session.Id);

        // Assert
        var result = _sessionStore.GetById(session.Id);
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task StopSessionAsync_NonExistentSession_DoesNotThrow()
    {
        // Act & Assert
        Assert.DoesNotThrowAsync(async () =>
            await _service.StopSessionAsync("non-existent-session"));
    }

    [Test]
    public async Task InterruptSessionAsync_SetsStatusToWaitingForInput()
    {
        // Arrange
        var session = await _service.StartSessionAsync(
            "entity-123",
            "project-456",
            "/test/path",
            SessionMode.Build,
            "claude-sonnet-4-20250514");

        // Act
        await _service.InterruptSessionAsync(session.Id);

        // Assert - session should remain in the store with WaitingForInput status
        var result = _sessionStore.GetById(session.Id);
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Status, Is.EqualTo(ClaudeSessionStatus.WaitingForInput));
    }

    [Test]
    public async Task InterruptSessionAsync_KeepsSessionInStore()
    {
        // Arrange
        var session = await _service.StartSessionAsync(
            "entity-123",
            "project-456",
            "/test/path",
            SessionMode.Build,
            "claude-sonnet-4-20250514");

        // Act
        await _service.InterruptSessionAsync(session.Id);

        // Assert - session should NOT be removed from store (unlike StopSession)
        var result = _sessionStore.GetById(session.Id);
        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public async Task InterruptSessionAsync_ClearsPendingQuestion()
    {
        // Arrange
        var session = await _service.StartSessionAsync(
            "entity-123",
            "project-456",
            "/test/path",
            SessionMode.Build,
            "claude-sonnet-4-20250514");
        session.PendingQuestion = new PendingQuestion
        {
            Id = "q1",
            ToolUseId = "tu1",
            Questions = new List<UserQuestion>()
        };

        // Act
        await _service.InterruptSessionAsync(session.Id);

        // Assert
        var result = _sessionStore.GetById(session.Id);
        Assert.That(result!.PendingQuestion, Is.Null);
    }

    [Test]
    public async Task InterruptSessionAsync_NonExistentSession_DoesNotThrow()
    {
        // Act & Assert
        Assert.DoesNotThrowAsync(async () =>
            await _service.InterruptSessionAsync("non-existent-session"));
    }
}

/// <summary>
/// Tests for message handling in ClaudeSessionService.
/// Note: Full integration tests with actual SDK would require mocking the SDK client.
/// </summary>
[TestFixture]
public class ClaudeSessionServiceMessageTests
{
    private ClaudeSessionService _service = null!;
    private IClaudeSessionStore _sessionStore = null!;
    private SessionOptionsFactory _optionsFactory = null!;
    private Mock<ILogger<ClaudeSessionService>> _loggerMock = null!;
    private Mock<ILogger<SessionOptionsFactory>> _factoryLoggerMock = null!;
    private Mock<IHubContext<Homespun.Features.ClaudeCode.Hubs.ClaudeCodeHub>> _hubContextMock = null!;
    private Mock<IClaudeSessionDiscovery> _discoveryMock = null!;
    private Mock<ISessionMetadataStore> _metadataStoreMock = null!;
    private Mock<IMessageCacheStore> _messageCacheMock = null!;
    private IToolResultParser _toolResultParser = null!;
    private Mock<IHooksService> _hooksServiceMock = null!;
    private Mock<IAgentExecutionService> _agentExecutionServiceMock = null!;

    [SetUp]
    public void SetUp()
    {
        _sessionStore = new ClaudeSessionStore();
        _factoryLoggerMock = new Mock<ILogger<SessionOptionsFactory>>();
        _optionsFactory = new SessionOptionsFactory(_factoryLoggerMock.Object);
        _loggerMock = new Mock<ILogger<ClaudeSessionService>>();
        _hubContextMock = new Mock<IHubContext<Homespun.Features.ClaudeCode.Hubs.ClaudeCodeHub>>();
        _discoveryMock = new Mock<IClaudeSessionDiscovery>();
        _metadataStoreMock = new Mock<ISessionMetadataStore>();
        _messageCacheMock = new Mock<IMessageCacheStore>();
        _toolResultParser = new ToolResultParser();
        _hooksServiceMock = new Mock<IHooksService>();
        _agentExecutionServiceMock = new Mock<IAgentExecutionService>();

        var clientsMock = new Mock<IHubClients>();
        var clientProxyMock = new Mock<IClientProxy>();
        clientsMock.Setup(c => c.All).Returns(clientProxyMock.Object);
        clientsMock.Setup(c => c.Group(It.IsAny<string>())).Returns(clientProxyMock.Object);
        _hubContextMock.Setup(h => h.Clients).Returns(clientsMock.Object);

        _service = new ClaudeSessionService(
            _sessionStore,
            _optionsFactory,
            _loggerMock.Object,
            _hubContextMock.Object,
            _discoveryMock.Object,
            _metadataStoreMock.Object,
            _toolResultParser,
            _hooksServiceMock.Object,
            _messageCacheMock.Object,
            _agentExecutionServiceMock.Object);
    }

    [Test]
    public async Task SendMessageAsync_NonExistentSession_ThrowsInvalidOperationException()
    {
        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _service.SendMessageAsync("non-existent-session", "Hello"));
    }

    [Test]
    public async Task SendMessageAsync_StoppedSession_ThrowsInvalidOperationException()
    {
        // Arrange
        var session = new ClaudeSession
        {
            Id = "stopped-session",
            EntityId = "entity-123",
            ProjectId = "project-456",
            WorkingDirectory = "/test/path",
            Model = "model",
            Mode = SessionMode.Plan,
            Status = ClaudeSessionStatus.Stopped,
            CreatedAt = DateTime.UtcNow
        };
        _sessionStore.Add(session);

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _service.SendMessageAsync("stopped-session", "Hello"));
    }

    [Test]
    public async Task SendMessageAsync_WithPermissionMode_NonExistentSession_ThrowsInvalidOperationException()
    {
        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _service.SendMessageAsync("non-existent-session", "Hello", SdkPermissionMode.AcceptEdits));
    }

    [Test]
    public async Task SendMessageAsync_WithPermissionMode_StoppedSession_ThrowsInvalidOperationException()
    {
        // Arrange
        var session = new ClaudeSession
        {
            Id = "stopped-session",
            EntityId = "entity-123",
            ProjectId = "project-456",
            WorkingDirectory = "/test/path",
            Model = "model",
            Mode = SessionMode.Plan,
            Status = ClaudeSessionStatus.Stopped,
            CreatedAt = DateTime.UtcNow
        };
        _sessionStore.Add(session);

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _service.SendMessageAsync("stopped-session", "Hello", SdkPermissionMode.Plan));
    }
}

/// <summary>
/// Tests for permission mode parameter handling in ClaudeSessionService.
/// </summary>
[TestFixture]
public class ClaudeSessionServicePermissionModeTests
{
    private ClaudeSessionService _service = null!;
    private IClaudeSessionStore _sessionStore = null!;
    private SessionOptionsFactory _optionsFactory = null!;
    private Mock<ILogger<ClaudeSessionService>> _loggerMock = null!;
    private Mock<ILogger<SessionOptionsFactory>> _factoryLoggerMock = null!;
    private Mock<IHubContext<Homespun.Features.ClaudeCode.Hubs.ClaudeCodeHub>> _hubContextMock = null!;
    private Mock<IClaudeSessionDiscovery> _discoveryMock = null!;
    private Mock<ISessionMetadataStore> _metadataStoreMock = null!;
    private Mock<IMessageCacheStore> _messageCacheMock = null!;
    private IToolResultParser _toolResultParser = null!;
    private Mock<IHooksService> _hooksServiceMock = null!;
    private Mock<IAgentExecutionService> _agentExecutionServiceMock = null!;

    [SetUp]
    public void SetUp()
    {
        _sessionStore = new ClaudeSessionStore();
        _factoryLoggerMock = new Mock<ILogger<SessionOptionsFactory>>();
        _optionsFactory = new SessionOptionsFactory(_factoryLoggerMock.Object);
        _loggerMock = new Mock<ILogger<ClaudeSessionService>>();
        _hubContextMock = new Mock<IHubContext<Homespun.Features.ClaudeCode.Hubs.ClaudeCodeHub>>();
        _discoveryMock = new Mock<IClaudeSessionDiscovery>();
        _metadataStoreMock = new Mock<ISessionMetadataStore>();
        _messageCacheMock = new Mock<IMessageCacheStore>();
        _toolResultParser = new ToolResultParser();
        _hooksServiceMock = new Mock<IHooksService>();
        _agentExecutionServiceMock = new Mock<IAgentExecutionService>();

        var clientsMock = new Mock<IHubClients>();
        var clientProxyMock = new Mock<IClientProxy>();
        clientsMock.Setup(c => c.All).Returns(clientProxyMock.Object);
        clientsMock.Setup(c => c.Group(It.IsAny<string>())).Returns(clientProxyMock.Object);
        _hubContextMock.Setup(h => h.Clients).Returns(clientsMock.Object);

        _service = new ClaudeSessionService(
            _sessionStore,
            _optionsFactory,
            _loggerMock.Object,
            _hubContextMock.Object,
            _discoveryMock.Object,
            _metadataStoreMock.Object,
            _toolResultParser,
            _hooksServiceMock.Object,
            _messageCacheMock.Object,
            _agentExecutionServiceMock.Object);
    }

    [TestCase(SdkPermissionMode.Default)]
    [TestCase(SdkPermissionMode.AcceptEdits)]
    [TestCase(SdkPermissionMode.Plan)]
    [TestCase(SdkPermissionMode.BypassPermissions)]
    public void SendMessageAsync_WithPermissionMode_AcceptsAllModes(SdkPermissionMode permissionMode)
    {
        // Arrange - session without options will throw, but after permission mode validation
        var session = new ClaudeSession
        {
            Id = "test-session",
            EntityId = "entity-123",
            ProjectId = "project-456",
            WorkingDirectory = "/test/path",
            Model = "model",
            Mode = SessionMode.Plan,
            Status = ClaudeSessionStatus.Running,
            CreatedAt = DateTime.UtcNow
        };
        _sessionStore.Add(session);

        // Act & Assert - throws because no options, but gets past permission mode validation
        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _service.SendMessageAsync("test-session", "Hello", permissionMode));

        // Verify it throws for missing options, not for invalid permission mode
        Assert.That(ex!.Message, Does.Contain("No options found"));
    }

    [Test]
    public void SendMessageAsync_DefaultOverload_UsesDefaultPermissionMode()
    {
        // Arrange
        var session = new ClaudeSession
        {
            Id = "test-session",
            EntityId = "entity-123",
            ProjectId = "project-456",
            WorkingDirectory = "/test/path",
            Model = "model",
            Mode = SessionMode.Plan,
            Status = ClaudeSessionStatus.Running,
            CreatedAt = DateTime.UtcNow
        };
        _sessionStore.Add(session);

        // Act & Assert - calling the default overload should work the same as explicit BypassPermissions
        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _service.SendMessageAsync("test-session", "Hello"));

        // Verify it throws for missing options (getting past all validations)
        Assert.That(ex!.Message, Does.Contain("No options found"));
    }

    [Test]
    public void AgentMessageRequest_ShouldIncludePermissionMode()
    {
        // Verify that AgentMessageRequest includes a PermissionMode field
        var request = new AgentMessageRequest(
            SessionId: "session-1",
            Message: "Hello",
            PermissionMode: SharedPermissionMode.AcceptEdits);

        Assert.That(request.PermissionMode, Is.EqualTo(SharedPermissionMode.AcceptEdits));
    }

    [Test]
    public void AgentMessageRequest_PermissionMode_DefaultsBypassPermissions()
    {
        // AgentMessageRequest should default to BypassPermissions when not specified
        var request = new AgentMessageRequest(
            SessionId: "session-1",
            Message: "Hello");

        Assert.That(request.PermissionMode, Is.EqualTo(SharedPermissionMode.BypassPermissions));
    }
}

/// <summary>
/// Tests for plan content capture functionality.
/// </summary>
[TestFixture]
public class ClaudeSessionServicePlanCaptureTests
{
    private ClaudeSessionService _service = null!;
    private IClaudeSessionStore _sessionStore = null!;
    private SessionOptionsFactory _optionsFactory = null!;
    private Mock<ILogger<ClaudeSessionService>> _loggerMock = null!;
    private Mock<ILogger<SessionOptionsFactory>> _factoryLoggerMock = null!;
    private Mock<IHubContext<Homespun.Features.ClaudeCode.Hubs.ClaudeCodeHub>> _hubContextMock = null!;
    private Mock<IClaudeSessionDiscovery> _discoveryMock = null!;
    private Mock<ISessionMetadataStore> _metadataStoreMock = null!;
    private Mock<IMessageCacheStore> _messageCacheMock = null!;
    private IToolResultParser _toolResultParser = null!;
    private Mock<IHooksService> _hooksServiceMock = null!;
    private Mock<IAgentExecutionService> _agentExecutionServiceMock = null!;

    [SetUp]
    public void SetUp()
    {
        _sessionStore = new ClaudeSessionStore();
        _factoryLoggerMock = new Mock<ILogger<SessionOptionsFactory>>();
        _optionsFactory = new SessionOptionsFactory(_factoryLoggerMock.Object);
        _loggerMock = new Mock<ILogger<ClaudeSessionService>>();
        _hubContextMock = new Mock<IHubContext<Homespun.Features.ClaudeCode.Hubs.ClaudeCodeHub>>();
        _discoveryMock = new Mock<IClaudeSessionDiscovery>();
        _metadataStoreMock = new Mock<ISessionMetadataStore>();
        _messageCacheMock = new Mock<IMessageCacheStore>();
        _toolResultParser = new ToolResultParser();
        _hooksServiceMock = new Mock<IHooksService>();
        _agentExecutionServiceMock = new Mock<IAgentExecutionService>();

        var clientsMock = new Mock<IHubClients>();
        var clientProxyMock = new Mock<IClientProxy>();
        clientsMock.Setup(c => c.All).Returns(clientProxyMock.Object);
        clientsMock.Setup(c => c.Group(It.IsAny<string>())).Returns(clientProxyMock.Object);
        _hubContextMock.Setup(h => h.Clients).Returns(clientsMock.Object);

        _service = new ClaudeSessionService(
            _sessionStore,
            _optionsFactory,
            _loggerMock.Object,
            _hubContextMock.Object,
            _discoveryMock.Object,
            _metadataStoreMock.Object,
            _toolResultParser,
            _hooksServiceMock.Object,
            _messageCacheMock.Object,
            _agentExecutionServiceMock.Object);
    }

    [Test]
    public void Session_PlanContent_IsNullByDefault()
    {
        // Arrange
        var session = new ClaudeSession
        {
            Id = "test-session",
            EntityId = "entity-123",
            ProjectId = "project-456",
            WorkingDirectory = "/test/path",
            Model = "model",
            Mode = SessionMode.Plan,
            Status = ClaudeSessionStatus.Running,
            CreatedAt = DateTime.UtcNow
        };

        // Assert
        Assert.That(session.PlanContent, Is.Null);
        Assert.That(session.PlanFilePath, Is.Null);
    }

    [Test]
    public void Session_PlanContent_CanBeSetDirectly()
    {
        // Arrange
        var session = new ClaudeSession
        {
            Id = "test-session",
            EntityId = "entity-123",
            ProjectId = "project-456",
            WorkingDirectory = "/test/path",
            Model = "model",
            Mode = SessionMode.Plan,
            Status = ClaudeSessionStatus.Running,
            CreatedAt = DateTime.UtcNow
        };

        // Act
        session.PlanContent = "# My Plan\n\nThis is the plan content.";
        session.PlanFilePath = "/path/to/plans/my-plan.md";

        // Assert
        Assert.That(session.PlanContent, Is.EqualTo("# My Plan\n\nThis is the plan content."));
        Assert.That(session.PlanFilePath, Is.EqualTo("/path/to/plans/my-plan.md"));
    }

    [TestCase("/home/user/.claude/plans/fluffy-aurora.md", true, Description = "Unix path with /plans/")]
    [TestCase("C:\\Users\\test\\.claude\\plans\\fluffy-aurora.md", true, Description = "Windows path with \\plans\\")]
    [TestCase("/home/user/.claude/plan.md", true, Description = "Unix path with /.claude/ ending in plan.md")]
    [TestCase("C:\\Users\\test\\.claude\\plan.md", true, Description = "Windows path with \\.claude\\ ending in plan.md")]
    [TestCase("/home/user/project/PLAN.md", false, Description = "PLAN.md in project root (not in .claude)")]
    [TestCase("/home/user/project/src/readme.md", false, Description = "Regular file")]
    [TestCase("/home/user/project/src/handler.ts", false, Description = "TypeScript file")]
    [TestCase("/home/user/.claude/plans/random-name.md", true, Description = "Random plan name in plans directory")]
    [TestCase("C:\\Users\\test\\.claude\\plans\\xyz-abc-123.md", true, Description = "Windows random plan name")]
    public void IsPlanFilePath_DetectsCorrectly(string filePath, bool expectedIsPlanFile)
    {
        // This test documents the expected behavior of plan file path detection
        // Claude Code writes plans to ~/.claude/plans/ directory with random names
        // We also capture files in .claude/ directory ending with plan.md
        // The actual implementation is in TryCaptureWrittenPlanContent

        var normalizedPath = filePath.Replace('\\', '/').ToLowerInvariant();
        var isPlanFile = normalizedPath.Contains("/plans/") ||
                         (normalizedPath.Contains("/.claude/") && normalizedPath.EndsWith("plan.md"));

        Assert.That(isPlanFile, Is.EqualTo(expectedIsPlanFile),
            $"Path '{filePath}' should {(expectedIsPlanFile ? "" : "not ")}be detected as a plan file");
    }
}

/// <summary>
/// Tests for session resumption functionality.
/// </summary>
[TestFixture]
public class ClaudeSessionServiceResumeTests
{
    private ClaudeSessionService _service = null!;
    private IClaudeSessionStore _sessionStore = null!;
    private SessionOptionsFactory _optionsFactory = null!;
    private Mock<ILogger<ClaudeSessionService>> _loggerMock = null!;
    private Mock<ILogger<SessionOptionsFactory>> _factoryLoggerMock = null!;
    private Mock<IHubContext<Homespun.Features.ClaudeCode.Hubs.ClaudeCodeHub>> _hubContextMock = null!;
    private Mock<IClaudeSessionDiscovery> _discoveryMock = null!;
    private Mock<ISessionMetadataStore> _metadataStoreMock = null!;
    private Mock<IMessageCacheStore> _messageCacheMock = null!;
    private IToolResultParser _toolResultParser = null!;
    private Mock<IHooksService> _hooksServiceMock = null!;
    private Mock<IAgentExecutionService> _agentExecutionServiceMock = null!;
    private string _testClaudeDir = null!;

    [SetUp]
    public void SetUp()
    {
        _testClaudeDir = Path.Combine(Path.GetTempPath(), $"claude-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testClaudeDir);

        _sessionStore = new ClaudeSessionStore();
        _factoryLoggerMock = new Mock<ILogger<SessionOptionsFactory>>();
        _optionsFactory = new SessionOptionsFactory(_factoryLoggerMock.Object);
        _loggerMock = new Mock<ILogger<ClaudeSessionService>>();
        _hubContextMock = new Mock<IHubContext<Homespun.Features.ClaudeCode.Hubs.ClaudeCodeHub>>();
        _discoveryMock = new Mock<IClaudeSessionDiscovery>();
        _metadataStoreMock = new Mock<ISessionMetadataStore>();
        _messageCacheMock = new Mock<IMessageCacheStore>();
        _toolResultParser = new ToolResultParser();
        _hooksServiceMock = new Mock<IHooksService>();
        _agentExecutionServiceMock = new Mock<IAgentExecutionService>();

        var clientsMock = new Mock<IHubClients>();
        var clientProxyMock = new Mock<IClientProxy>();
        clientsMock.Setup(c => c.All).Returns(clientProxyMock.Object);
        clientsMock.Setup(c => c.Group(It.IsAny<string>())).Returns(clientProxyMock.Object);
        _hubContextMock.Setup(h => h.Clients).Returns(clientsMock.Object);

        _service = new ClaudeSessionService(
            _sessionStore,
            _optionsFactory,
            _loggerMock.Object,
            _hubContextMock.Object,
            _discoveryMock.Object,
            _metadataStoreMock.Object,
            _toolResultParser,
            _hooksServiceMock.Object,
            _messageCacheMock.Object,
            _agentExecutionServiceMock.Object);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testClaudeDir))
        {
            Directory.Delete(_testClaudeDir, recursive: true);
        }
    }

    [Test]
    public async Task ResumeSessionAsync_WithMetadata_UsesStoredMetadata()
    {
        // Arrange
        var claudeSessionId = Guid.NewGuid().ToString();
        var metadata = new SessionMetadata(
            SessionId: claudeSessionId,
            EntityId: "entity-123",
            ProjectId: "project-456",
            WorkingDirectory: "/test/path",
            Mode: SessionMode.Build,
            Model: "claude-opus-4",
            SystemPrompt: "Test prompt",
            CreatedAt: DateTime.UtcNow.AddHours(-1)
        );
        _metadataStoreMock.Setup(m => m.GetBySessionIdAsync(claudeSessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(metadata);

        // Act
        var session = await _service.ResumeSessionAsync(
            claudeSessionId,
            "entity-123",
            "project-456",
            "/test/path");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(session.ConversationId, Is.EqualTo(claudeSessionId));
            Assert.That(session.Mode, Is.EqualTo(SessionMode.Build));
            Assert.That(session.Model, Is.EqualTo("claude-opus-4"));
            Assert.That(session.SystemPrompt, Is.EqualTo("Test prompt"));
            Assert.That(session.Status, Is.EqualTo(ClaudeSessionStatus.Running));
        });
    }

    [Test]
    public async Task ResumeSessionAsync_WithoutMetadata_UsesDefaults()
    {
        // Arrange
        var claudeSessionId = Guid.NewGuid().ToString();
        _metadataStoreMock.Setup(m => m.GetBySessionIdAsync(claudeSessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SessionMetadata?)null);

        // Act
        var session = await _service.ResumeSessionAsync(
            claudeSessionId,
            "entity-123",
            "project-456",
            "/test/path");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(session.ConversationId, Is.EqualTo(claudeSessionId));
            Assert.That(session.Mode, Is.EqualTo(SessionMode.Build)); // Default
            Assert.That(session.Model, Is.EqualTo("sonnet")); // Default
            Assert.That(session.SystemPrompt, Is.Null);
            Assert.That(session.Status, Is.EqualTo(ClaudeSessionStatus.Running));
        });
    }

    [Test]
    public async Task ResumeSessionAsync_AddsSessionToStore()
    {
        // Arrange
        var claudeSessionId = Guid.NewGuid().ToString();
        _metadataStoreMock.Setup(m => m.GetBySessionIdAsync(claudeSessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SessionMetadata?)null);

        // Act
        var session = await _service.ResumeSessionAsync(
            claudeSessionId,
            "entity-123",
            "project-456",
            "/test/path");

        // Assert
        var retrieved = _sessionStore.GetById(session.Id);
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved!.ConversationId, Is.EqualTo(claudeSessionId));
    }

    [Test]
    public async Task ResumeSessionAsync_GeneratesNewHomespunSessionId()
    {
        // Arrange
        var claudeSessionId = Guid.NewGuid().ToString();
        _metadataStoreMock.Setup(m => m.GetBySessionIdAsync(claudeSessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SessionMetadata?)null);

        // Act
        var session = await _service.ResumeSessionAsync(
            claudeSessionId,
            "entity-123",
            "project-456",
            "/test/path");

        // Assert - Homespun session ID should be different from Claude's session ID
        Assert.That(session.Id, Is.Not.EqualTo(claudeSessionId));
        Assert.That(Guid.TryParse(session.Id, out _), Is.True);
    }

    [Test]
    public async Task GetResumableSessionsAsync_DiscoversSessions()
    {
        // Arrange
        var workingDirectory = "/test/path";
        var session1 = new DiscoveredSession(
            SessionId: Guid.NewGuid().ToString(),
            FilePath: "/path/to/session1.jsonl",
            LastModified: DateTime.UtcNow.AddHours(-1),
            FileSize: 1000
        );
        var session2 = new DiscoveredSession(
            SessionId: Guid.NewGuid().ToString(),
            FilePath: "/path/to/session2.jsonl",
            LastModified: DateTime.UtcNow,
            FileSize: 2000
        );
        _discoveryMock.Setup(d => d.DiscoverSessionsAsync(workingDirectory, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DiscoveredSession> { session2, session1 }); // Ordered by newest first

        _metadataStoreMock.Setup(m => m.GetBySessionIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SessionMetadata?)null);

        // Act
        var result = await _service.GetResumableSessionsAsync("entity-123", workingDirectory);

        // Assert
        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result[0].SessionId, Is.EqualTo(session2.SessionId)); // Newest first
        Assert.That(result[1].SessionId, Is.EqualTo(session1.SessionId));
    }

    [Test]
    public async Task GetResumableSessionsAsync_EnrichesWithMetadata()
    {
        // Arrange
        var workingDirectory = "/test/path";
        var sessionId = Guid.NewGuid().ToString();
        var discoveredSession = new DiscoveredSession(
            SessionId: sessionId,
            FilePath: "/path/to/session.jsonl",
            LastModified: DateTime.UtcNow,
            FileSize: 1000
        );
        var metadata = new SessionMetadata(
            SessionId: sessionId,
            EntityId: "entity-123",
            ProjectId: "project-456",
            WorkingDirectory: workingDirectory,
            Mode: SessionMode.Plan,
            Model: "claude-opus-4",
            SystemPrompt: null,
            CreatedAt: DateTime.UtcNow.AddHours(-1)
        );

        _discoveryMock.Setup(d => d.DiscoverSessionsAsync(workingDirectory, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DiscoveredSession> { discoveredSession });
        _metadataStoreMock.Setup(m => m.GetBySessionIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(metadata);
        _discoveryMock.Setup(d => d.GetMessageCountAsync(sessionId, workingDirectory, It.IsAny<CancellationToken>()))
            .ReturnsAsync(42);

        // Act
        var result = await _service.GetResumableSessionsAsync("entity-123", workingDirectory);

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        var resumable = result[0];
        Assert.Multiple(() =>
        {
            Assert.That(resumable.SessionId, Is.EqualTo(sessionId));
            Assert.That(resumable.Mode, Is.EqualTo(SessionMode.Plan));
            Assert.That(resumable.Model, Is.EqualTo("claude-opus-4"));
            Assert.That(resumable.MessageCount, Is.EqualTo(42));
        });
    }

    [Test]
    public async Task GetResumableSessionsAsync_NoSessions_ReturnsEmptyList()
    {
        // Arrange
        _discoveryMock.Setup(d => d.DiscoverSessionsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DiscoveredSession>());

        // Act
        var result = await _service.GetResumableSessionsAsync("entity-123", "/test/path");

        // Assert
        Assert.That(result, Is.Empty);
    }
}

/// <summary>
/// Tests for plan execution and context clearing behavior (UUXI6e).
/// </summary>
[TestFixture]
public class ClaudeSessionServicePlanExecutionTests
{
    private ClaudeSessionService _service = null!;
    private IClaudeSessionStore _sessionStore = null!;
    private SessionOptionsFactory _optionsFactory = null!;
    private Mock<ILogger<ClaudeSessionService>> _loggerMock = null!;
    private Mock<ILogger<SessionOptionsFactory>> _factoryLoggerMock = null!;
    private Mock<IHubContext<Homespun.Features.ClaudeCode.Hubs.ClaudeCodeHub>> _hubContextMock = null!;
    private Mock<IClaudeSessionDiscovery> _discoveryMock = null!;
    private Mock<ISessionMetadataStore> _metadataStoreMock = null!;
    private Mock<IMessageCacheStore> _messageCacheMock = null!;
    private IToolResultParser _toolResultParser = null!;
    private Mock<IHooksService> _hooksServiceMock = null!;
    private Mock<IAgentExecutionService> _agentExecutionServiceMock = null!;

    [SetUp]
    public void SetUp()
    {
        _sessionStore = new ClaudeSessionStore();
        _factoryLoggerMock = new Mock<ILogger<SessionOptionsFactory>>();
        _optionsFactory = new SessionOptionsFactory(_factoryLoggerMock.Object);
        _loggerMock = new Mock<ILogger<ClaudeSessionService>>();
        _hubContextMock = new Mock<IHubContext<Homespun.Features.ClaudeCode.Hubs.ClaudeCodeHub>>();
        _discoveryMock = new Mock<IClaudeSessionDiscovery>();
        _metadataStoreMock = new Mock<ISessionMetadataStore>();
        _messageCacheMock = new Mock<IMessageCacheStore>();
        _toolResultParser = new ToolResultParser();
        _hooksServiceMock = new Mock<IHooksService>();
        _agentExecutionServiceMock = new Mock<IAgentExecutionService>();

        var clientsMock = new Mock<IHubClients>();
        var clientProxyMock = new Mock<IClientProxy>();
        clientsMock.Setup(c => c.All).Returns(clientProxyMock.Object);
        clientsMock.Setup(c => c.Group(It.IsAny<string>())).Returns(clientProxyMock.Object);
        _hubContextMock.Setup(h => h.Clients).Returns(clientsMock.Object);

        _service = new ClaudeSessionService(
            _sessionStore,
            _optionsFactory,
            _loggerMock.Object,
            _hubContextMock.Object,
            _discoveryMock.Object,
            _metadataStoreMock.Object,
            _toolResultParser,
            _hooksServiceMock.Object,
            _messageCacheMock.Object,
            _agentExecutionServiceMock.Object);
    }

    /// <summary>
    /// Helper to create an async enumerable that yields the given SDK messages.
    /// </summary>
    private static async IAsyncEnumerable<SdkMessage> CreateSdkMessageStream(params SdkMessage[] messages)
    {
        foreach (var msg in messages)
        {
            yield return msg;
        }
        await Task.CompletedTask; // Ensure the method is async
    }

    [Test]
    public async Task ExecutePlanAsync_MessageDoesNotContainFilePath()
    {
        // Arrange - Create a session with plan content and a file path
        var session = await _service.StartSessionAsync(
            "entity-1", "project-1", "/test/path", SessionMode.Plan, "sonnet");

        session.PlanContent = "# Test Plan\n\n1. Step one\n2. Step two";
        session.PlanFilePath = "/home/homespun/.claude/plans/cheeky-stirring-stonebraker.md";

        // Setup mock to return a valid SDK message stream that completes immediately
        _agentExecutionServiceMock
            .Setup(s => s.StartSessionAsync(It.IsAny<AgentStartRequest>(), It.IsAny<CancellationToken>()))
            .Returns(CreateSdkMessageStream(
                new SdkSystemMessage("agent-1", null, "session_started", null, null),
                new SdkResultMessage("agent-1", null, null, 0, 0, false, 0, 0, null)));

        // Act
        await _service.ExecutePlanAsync(session.Id, clearContext: false);

        // Assert - verify the message sent does NOT contain the file path
        // The last user message should be the execution message
        var lastUserMessage = session.Messages.LastOrDefault(m => m.Role == ClaudeMessageRole.User);
        Assert.That(lastUserMessage, Is.Not.Null, "Should have a user message");

        var messageText = lastUserMessage!.Content.FirstOrDefault()?.Text ?? "";
        Assert.Multiple(() =>
        {
            // Should NOT reference the file path (this was the bug)
            Assert.That(messageText, Does.Not.Contain("cheeky-stirring-stonebraker"),
                "Execution message should not reference the plan file name");
            Assert.That(messageText, Does.Not.Contain(".claude/plans/"),
                "Execution message should not reference the plans directory");

            // Should contain the actual plan content
            Assert.That(messageText, Does.Contain("# Test Plan"),
                "Execution message should contain the plan content");
            Assert.That(messageText, Does.Contain("Step one"),
                "Execution message should contain plan steps");
        });
    }

    [Test]
    public async Task ExecutePlanAsync_WithClearContext_ClearsAndResetsConversationId()
    {
        // Arrange
        var session = await _service.StartSessionAsync(
            "entity-1", "project-1", "/test/path", SessionMode.Plan, "sonnet");

        session.ConversationId = "original-conversation-id";
        session.PlanContent = "# Test Plan";

        // Setup mock to return a valid SDK message stream
        // The result message carries the new conversation ID from the fresh session
        _agentExecutionServiceMock
            .Setup(s => s.StartSessionAsync(It.IsAny<AgentStartRequest>(), It.IsAny<CancellationToken>()))
            .Returns(CreateSdkMessageStream(
                new SdkSystemMessage("new-agent-1", null, "session_started", null, null),
                new SdkResultMessage("new-conversation-id", null, null, 0, 0, false, 0, 0, null)));

        // Act
        await _service.ExecutePlanAsync(session.Id, clearContext: true);

        // Assert - ConversationId should be updated from the new result message
        // Context clear nulls it, but the new session's result sets a fresh conversation ID
        Assert.That(session.ConversationId, Is.Not.EqualTo("original-conversation-id"),
            "ConversationId should not be the original value after context clearing");
        Assert.That(session.ConversationId, Is.EqualTo("new-conversation-id"),
            "ConversationId should be set from the new session's result message");
    }

    [Test]
    public async Task ExecutePlanAsync_WithClearContext_AddsContextClearMarker()
    {
        // Arrange
        var session = await _service.StartSessionAsync(
            "entity-1", "project-1", "/test/path", SessionMode.Plan, "sonnet");

        session.PlanContent = "# Test Plan";

        // Setup mock to return a valid SDK message stream
        _agentExecutionServiceMock
            .Setup(s => s.StartSessionAsync(It.IsAny<AgentStartRequest>(), It.IsAny<CancellationToken>()))
            .Returns(CreateSdkMessageStream(
                new SdkSystemMessage("agent-1", null, "session_started", null, null),
                new SdkResultMessage("agent-1", null, null, 0, 0, false, 0, 0, null)));

        // Act
        await _service.ExecutePlanAsync(session.Id, clearContext: true);

        // Assert - Should have a context clear marker
        Assert.That(session.ContextClearMarkers, Has.Count.EqualTo(1),
            "Should have exactly one context clear marker");
    }

    [Test]
    public async Task ExecutePlanAsync_WithoutPlanContent_DoesNotSendMessage()
    {
        // Arrange
        var session = await _service.StartSessionAsync(
            "entity-1", "project-1", "/test/path", SessionMode.Plan, "sonnet");

        // PlanContent is null

        var initialMessageCount = session.Messages.Count;

        // Act
        await _service.ExecutePlanAsync(session.Id, clearContext: true);

        // Assert - no new messages should be added
        Assert.That(session.Messages.Count, Is.EqualTo(initialMessageCount),
            "Should not add messages when there's no plan content");
    }

    [Test]
    public async Task ClearContextAsync_PreservesPlanContent()
    {
        // Arrange
        var session = await _service.StartSessionAsync(
            "entity-1", "project-1", "/test/path", SessionMode.Plan, "sonnet");

        session.PlanContent = "# My Plan\n\nPlan details here.";
        session.PlanFilePath = "/home/homespun/.claude/plans/test-plan.md";

        // Act
        await _service.ClearContextAsync(session.Id);

        // Assert - Plan content should be preserved even after context clear
        Assert.Multiple(() =>
        {
            Assert.That(session.PlanContent, Is.EqualTo("# My Plan\n\nPlan details here."),
                "PlanContent should be preserved after context clear");
            Assert.That(session.PlanFilePath, Is.EqualTo("/home/homespun/.claude/plans/test-plan.md"),
                "PlanFilePath should be preserved after context clear");
        });
    }
}

/// <summary>
/// Tests for question_pending control event and AnswerQuestionAsync routing.
/// </summary>
[TestFixture]
public class ClaudeSessionServiceQuestionPendingTests
{
    private ClaudeSessionService _service = null!;
    private IClaudeSessionStore _sessionStore = null!;
    private SessionOptionsFactory _optionsFactory = null!;
    private Mock<ILogger<ClaudeSessionService>> _loggerMock = null!;
    private Mock<ILogger<SessionOptionsFactory>> _factoryLoggerMock = null!;
    private Mock<IHubContext<Homespun.Features.ClaudeCode.Hubs.ClaudeCodeHub>> _hubContextMock = null!;
    private Mock<IClaudeSessionDiscovery> _discoveryMock = null!;
    private Mock<ISessionMetadataStore> _metadataStoreMock = null!;
    private Mock<IMessageCacheStore> _messageCacheMock = null!;
    private IToolResultParser _toolResultParser = null!;
    private Mock<IHooksService> _hooksServiceMock = null!;
    private Mock<IAgentExecutionService> _agentExecutionServiceMock = null!;

    [SetUp]
    public void SetUp()
    {
        _sessionStore = new ClaudeSessionStore();
        _factoryLoggerMock = new Mock<ILogger<SessionOptionsFactory>>();
        _optionsFactory = new SessionOptionsFactory(_factoryLoggerMock.Object);
        _loggerMock = new Mock<ILogger<ClaudeSessionService>>();
        _hubContextMock = new Mock<IHubContext<Homespun.Features.ClaudeCode.Hubs.ClaudeCodeHub>>();
        _discoveryMock = new Mock<IClaudeSessionDiscovery>();
        _metadataStoreMock = new Mock<ISessionMetadataStore>();
        _messageCacheMock = new Mock<IMessageCacheStore>();
        _toolResultParser = new ToolResultParser();
        _hooksServiceMock = new Mock<IHooksService>();
        _agentExecutionServiceMock = new Mock<IAgentExecutionService>();

        var clientsMock = new Mock<IHubClients>();
        var clientProxyMock = new Mock<IClientProxy>();
        clientsMock.Setup(c => c.All).Returns(clientProxyMock.Object);
        clientsMock.Setup(c => c.Group(It.IsAny<string>())).Returns(clientProxyMock.Object);
        _hubContextMock.Setup(h => h.Clients).Returns(clientsMock.Object);

        _service = new ClaudeSessionService(
            _sessionStore,
            _optionsFactory,
            _loggerMock.Object,
            _hubContextMock.Object,
            _discoveryMock.Object,
            _metadataStoreMock.Object,
            _toolResultParser,
            _hooksServiceMock.Object,
            _messageCacheMock.Object,
            _agentExecutionServiceMock.Object);
    }

    private static async IAsyncEnumerable<SdkMessage> CreateSdkMessageStream(params SdkMessage[] messages)
    {
        foreach (var msg in messages)
        {
            yield return msg;
        }
        await Task.CompletedTask;
    }

    [Test]
    public async Task ProcessSdkMessage_QuestionPending_SetsWaitingStatus()
    {
        // Arrange
        var session = await _service.StartSessionAsync(
            "entity-1", "project-1", "/test/path", SessionMode.Build, "sonnet");

        var questionsJson = "{\"questions\":[{\"question\":\"Which option?\",\"header\":\"Choice\",\"options\":[{\"label\":\"A\",\"description\":\"Option A\"}],\"multiSelect\":false}]}";

        // Simulate a question_pending event followed by a result
        _agentExecutionServiceMock
            .Setup(s => s.StartSessionAsync(It.IsAny<AgentStartRequest>(), It.IsAny<CancellationToken>()))
            .Returns(CreateSdkMessageStream(
                new SdkSystemMessage("agent-1", null, "session_started", null, null),
                new SdkQuestionPendingMessage("agent-1", questionsJson),
                new SdkResultMessage("agent-1", null, null, 0, 0, false, 0, 0, null)));

        // Act
        await _service.SendMessageAsync(session.Id, "Do something");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(session.Status, Is.EqualTo(ClaudeSessionStatus.WaitingForQuestionAnswer));
            Assert.That(session.PendingQuestion, Is.Not.Null);
            Assert.That(session.PendingQuestion!.Questions, Has.Count.EqualTo(1));
            Assert.That(session.PendingQuestion.Questions[0].Question, Is.EqualTo("Which option?"));
        });
    }

    [Test]
    public async Task AnswerQuestionAsync_DockerMode_CallsExecutionService()
    {
        // Arrange
        var session = await _service.StartSessionAsync(
            "entity-1", "project-1", "/test/path", SessionMode.Build, "sonnet");

        // Set up agent session mapping via a StartSession call
        _agentExecutionServiceMock
            .Setup(s => s.StartSessionAsync(It.IsAny<AgentStartRequest>(), It.IsAny<CancellationToken>()))
            .Returns(CreateSdkMessageStream(
                new SdkSystemMessage("agent-1", null, "session_started", null, null),
                new SdkResultMessage("agent-1", null, null, 0, 0, false, 0, 0, null)));

        // Send a message to establish the agent session mapping
        await _service.SendMessageAsync(session.Id, "Hello");

        // Set up the question pending state
        session.PendingQuestion = new PendingQuestion
        {
            Id = "q1",
            ToolUseId = "",
            Questions = new List<UserQuestion>
            {
                new() { Question = "Which?", Header = "Choice", Options = new List<QuestionOption>() }
            }
        };
        session.Status = ClaudeSessionStatus.WaitingForQuestionAnswer;

        // Mock the execution service to resolve the question
        _agentExecutionServiceMock
            .Setup(s => s.AnswerQuestionAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var answers = new Dictionary<string, string> { { "Which?", "Option A" } };
        await _service.AnswerQuestionAsync(session.Id, answers);

        // Assert
        _agentExecutionServiceMock.Verify(
            s => s.AnswerQuestionAsync(It.IsAny<string>(), answers, It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.That(session.Status, Is.EqualTo(ClaudeSessionStatus.Running));
        Assert.That(session.PendingQuestion, Is.Null);
    }

    [Test]
    public async Task AnswerQuestionAsync_LocalMode_FallsBackToSendMessage()
    {
        // Arrange
        var session = await _service.StartSessionAsync(
            "entity-1", "project-1", "/test/path", SessionMode.Build, "sonnet");

        // Set up the question pending state (no agent session mapping = local mode fallback)
        session.PendingQuestion = new PendingQuestion
        {
            Id = "q1",
            ToolUseId = "",
            Questions = new List<UserQuestion>
            {
                new() { Question = "Which?", Header = "Choice", Options = new List<QuestionOption>() }
            }
        };
        session.Status = ClaudeSessionStatus.WaitingForQuestionAnswer;

        // Mock the execution service to start a session (for the fallback SendMessage)
        _agentExecutionServiceMock
            .Setup(s => s.StartSessionAsync(It.IsAny<AgentStartRequest>(), It.IsAny<CancellationToken>()))
            .Returns(CreateSdkMessageStream(
                new SdkSystemMessage("agent-1", null, "session_started", null, null),
                new SdkResultMessage("agent-1", null, null, 0, 0, false, 0, 0, null)));

        // Act
        var answers = new Dictionary<string, string> { { "Which?", "Option A" } };
        await _service.AnswerQuestionAsync(session.Id, answers);

        // Assert - should NOT call AnswerQuestionAsync on execution service (no agent mapping)
        _agentExecutionServiceMock.Verify(
            s => s.AnswerQuestionAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // Should have started a new session (via SendMessageAsync fallback)
        _agentExecutionServiceMock.Verify(
            s => s.StartSessionAsync(It.IsAny<AgentStartRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}

/// <summary>
/// Tests for plan approval (ApprovePlanAsync) in ClaudeSessionService.
/// </summary>
[TestFixture]
public class ClaudeSessionServicePlanApprovalTests
{
    private ClaudeSessionService _service = null!;
    private IClaudeSessionStore _sessionStore = null!;
    private SessionOptionsFactory _optionsFactory = null!;
    private Mock<ILogger<ClaudeSessionService>> _loggerMock = null!;
    private Mock<ILogger<SessionOptionsFactory>> _factoryLoggerMock = null!;
    private Mock<IHubContext<Homespun.Features.ClaudeCode.Hubs.ClaudeCodeHub>> _hubContextMock = null!;
    private Mock<IClaudeSessionDiscovery> _discoveryMock = null!;
    private Mock<ISessionMetadataStore> _metadataStoreMock = null!;
    private Mock<IMessageCacheStore> _messageCacheMock = null!;
    private IToolResultParser _toolResultParser = null!;
    private Mock<IHooksService> _hooksServiceMock = null!;
    private Mock<IAgentExecutionService> _agentExecutionServiceMock = null!;

    [SetUp]
    public void SetUp()
    {
        _sessionStore = new ClaudeSessionStore();
        _factoryLoggerMock = new Mock<ILogger<SessionOptionsFactory>>();
        _optionsFactory = new SessionOptionsFactory(_factoryLoggerMock.Object);
        _loggerMock = new Mock<ILogger<ClaudeSessionService>>();
        _hubContextMock = new Mock<IHubContext<Homespun.Features.ClaudeCode.Hubs.ClaudeCodeHub>>();
        _discoveryMock = new Mock<IClaudeSessionDiscovery>();
        _metadataStoreMock = new Mock<ISessionMetadataStore>();
        _messageCacheMock = new Mock<IMessageCacheStore>();
        _toolResultParser = new ToolResultParser();
        _hooksServiceMock = new Mock<IHooksService>();
        _agentExecutionServiceMock = new Mock<IAgentExecutionService>();

        var clientsMock = new Mock<IHubClients>();
        var clientProxyMock = new Mock<IClientProxy>();
        clientsMock.Setup(c => c.All).Returns(clientProxyMock.Object);
        clientsMock.Setup(c => c.Group(It.IsAny<string>())).Returns(clientProxyMock.Object);
        _hubContextMock.Setup(h => h.Clients).Returns(clientsMock.Object);

        _service = new ClaudeSessionService(
            _sessionStore,
            _optionsFactory,
            _loggerMock.Object,
            _hubContextMock.Object,
            _discoveryMock.Object,
            _metadataStoreMock.Object,
            _toolResultParser,
            _hooksServiceMock.Object,
            _messageCacheMock.Object,
            _agentExecutionServiceMock.Object);
    }

    private static async IAsyncEnumerable<SdkMessage> CreateSdkMessageStream(params SdkMessage[] messages)
    {
        foreach (var msg in messages)
        {
            yield return msg;
        }
        await Task.CompletedTask;
    }

    [Test]
    public void ApprovePlanAsync_NotWaitingForPlan_ThrowsInvalidOperationException()
    {
        // Arrange
        var session = new ClaudeSession
        {
            Id = "test-session",
            EntityId = "entity-123",
            ProjectId = "project-456",
            WorkingDirectory = "/test/path",
            Model = "model",
            Mode = SessionMode.Plan,
            Status = ClaudeSessionStatus.Running,
            CreatedAt = DateTime.UtcNow
        };
        _sessionStore.Add(session);

        // Act & Assert
        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _service.ApprovePlanAsync("test-session", true, true));
        Assert.That(ex!.Message, Does.Contain("not waiting for plan approval"));
    }

    [Test]
    public void ApprovePlanAsync_NonExistentSession_ThrowsInvalidOperationException()
    {
        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _service.ApprovePlanAsync("non-existent-session", true, true));
    }

    [Test]
    public async Task ApprovePlanAsync_Approved_KeepContext_CallsExecutionService()
    {
        // Arrange
        var session = await _service.StartSessionAsync(
            "entity-1", "project-1", "/test/path", SessionMode.Plan, "sonnet");

        session.PlanContent = "# Test Plan";
        session.Status = ClaudeSessionStatus.WaitingForPlanExecution;

        // Set up agent session mapping
        _agentExecutionServiceMock
            .Setup(s => s.StartSessionAsync(It.IsAny<AgentStartRequest>(), It.IsAny<CancellationToken>()))
            .Returns(CreateSdkMessageStream(
                new SdkSystemMessage("agent-1", null, "session_started", null, null),
                new SdkResultMessage("agent-1", null, null, 0, 0, false, 0, 0, null)));

        await _service.SendMessageAsync(session.Id, "Hello");
        session.Status = ClaudeSessionStatus.WaitingForPlanExecution;

        // Mock the execution service to resolve the plan approval
        _agentExecutionServiceMock
            .Setup(s => s.ApprovePlanAsync(It.IsAny<string>(), true, true, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _service.ApprovePlanAsync(session.Id, approved: true, keepContext: true);

        // Assert
        _agentExecutionServiceMock.Verify(
            s => s.ApprovePlanAsync(It.IsAny<string>(), true, true, null, It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.That(session.Status, Is.EqualTo(ClaudeSessionStatus.Running));
    }

    [Test]
    public async Task ApprovePlanAsync_Approved_ClearContext_ExecutesPlan()
    {
        // Arrange
        var session = await _service.StartSessionAsync(
            "entity-1", "project-1", "/test/path", SessionMode.Plan, "sonnet");

        session.PlanContent = "# Test Plan\n\n1. Step one";
        session.Status = ClaudeSessionStatus.WaitingForPlanExecution;

        // Set up agent session mapping
        _agentExecutionServiceMock
            .Setup(s => s.StartSessionAsync(It.IsAny<AgentStartRequest>(), It.IsAny<CancellationToken>()))
            .Returns(CreateSdkMessageStream(
                new SdkSystemMessage("agent-1", null, "session_started", null, null),
                new SdkResultMessage("agent-1", null, null, 0, 0, false, 0, 0, null)));

        // Mock the execution service for plan notification
        _agentExecutionServiceMock
            .Setup(s => s.ApprovePlanAsync(It.IsAny<string>(), true, false, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _service.ApprovePlanAsync(session.Id, approved: true, keepContext: false);

        // Assert - should clear context and send plan as message
        Assert.That(session.ContextClearMarkers, Has.Count.EqualTo(1),
            "Should have a context clear marker");

        // Verify the plan content was sent as a message
        var lastUserMessage = session.Messages.LastOrDefault(m => m.Role == ClaudeMessageRole.User);
        Assert.That(lastUserMessage, Is.Not.Null);
        Assert.That(lastUserMessage!.Content[0].Text, Does.Contain("# Test Plan"));
    }

    [Test]
    public async Task ApprovePlanAsync_Approved_ClearContext_BroadcastsStatusImmediately()
    {
        // Arrange
        var session = await _service.StartSessionAsync(
            "entity-1", "project-1", "/test/path", SessionMode.Plan, "sonnet");

        session.PlanContent = "# Test Plan\n\n1. Step one";
        session.Status = ClaudeSessionStatus.WaitingForPlanExecution;

        // Set up agent session mapping
        _agentExecutionServiceMock
            .Setup(s => s.StartSessionAsync(It.IsAny<AgentStartRequest>(), It.IsAny<CancellationToken>()))
            .Returns(CreateSdkMessageStream(
                new SdkSystemMessage("agent-1", null, "session_started", null, null),
                new SdkResultMessage("agent-1", null, null, 0, 0, false, 0, 0, null)));

        // Send message first to establish agent session mapping
        await _service.SendMessageAsync(session.Id, "Hello");
        session.Status = ClaudeSessionStatus.WaitingForPlanExecution;

        // Mock the execution service for plan notification
        _agentExecutionServiceMock
            .Setup(s => s.ApprovePlanAsync(It.IsAny<string>(), true, false, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Track the status broadcast calls
        var statusBroadcasts = new List<(string sessionId, ClaudeSessionStatus status)>();
        _hubContextMock.Setup(h => h.Clients.All.SendCoreAsync(
            "SessionStatusChanged",
            It.IsAny<object?[]>(),
            It.IsAny<CancellationToken>()))
            .Callback<string, object?[], CancellationToken>((method, args, ct) =>
            {
                if (args.Length >= 2 && args[0] is string sid && args[1] is ClaudeSessionStatus st)
                {
                    statusBroadcasts.Add((sid, st));
                }
            })
            .Returns(Task.CompletedTask);

        // Act
        await _service.ApprovePlanAsync(session.Id, approved: true, keepContext: false);

        // Assert - the first broadcast should be Running (immediate status change)
        Assert.That(statusBroadcasts, Has.Count.GreaterThanOrEqualTo(1),
            "Should have broadcast at least one status change");
        Assert.That(statusBroadcasts[0].sessionId, Is.EqualTo(session.Id),
            "First broadcast should be for the correct session");
        Assert.That(statusBroadcasts[0].status, Is.EqualTo(ClaudeSessionStatus.Running),
            "First broadcast should be Running status (immediate feedback)");
    }

    [Test]
    public async Task ApprovePlanAsync_Rejected_CallsExecutionServiceWithFeedback()
    {
        // Arrange
        var session = await _service.StartSessionAsync(
            "entity-1", "project-1", "/test/path", SessionMode.Plan, "sonnet");

        session.PlanContent = "# Test Plan";
        session.Status = ClaudeSessionStatus.WaitingForPlanExecution;

        // Set up agent session mapping
        _agentExecutionServiceMock
            .Setup(s => s.StartSessionAsync(It.IsAny<AgentStartRequest>(), It.IsAny<CancellationToken>()))
            .Returns(CreateSdkMessageStream(
                new SdkSystemMessage("agent-1", null, "session_started", null, null),
                new SdkResultMessage("agent-1", null, null, 0, 0, false, 0, 0, null)));

        await _service.SendMessageAsync(session.Id, "Hello");
        session.Status = ClaudeSessionStatus.WaitingForPlanExecution;

        // Mock the execution service to resolve the rejection
        _agentExecutionServiceMock
            .Setup(s => s.ApprovePlanAsync(It.IsAny<string>(), false, false, "Please add tests", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _service.ApprovePlanAsync(session.Id, approved: false, keepContext: false, feedback: "Please add tests");

        // Assert
        _agentExecutionServiceMock.Verify(
            s => s.ApprovePlanAsync(It.IsAny<string>(), false, false, "Please add tests", It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.That(session.Status, Is.EqualTo(ClaudeSessionStatus.Running));
    }

    [Test]
    public async Task ApprovePlanAsync_Rejected_LocalFallback_SendsFeedbackAsMessage()
    {
        // Arrange
        var session = await _service.StartSessionAsync(
            "entity-1", "project-1", "/test/path", SessionMode.Plan, "sonnet");

        session.PlanContent = "# Test Plan";
        session.Status = ClaudeSessionStatus.WaitingForPlanExecution;

        // No agent session mapping = local mode fallback

        // Mock SendMessage to work (via StartSessionAsync)
        _agentExecutionServiceMock
            .Setup(s => s.StartSessionAsync(It.IsAny<AgentStartRequest>(), It.IsAny<CancellationToken>()))
            .Returns(CreateSdkMessageStream(
                new SdkSystemMessage("agent-1", null, "session_started", null, null),
                new SdkResultMessage("agent-1", null, null, 0, 0, false, 0, 0, null)));

        // Act
        await _service.ApprovePlanAsync(session.Id, approved: false, keepContext: false, feedback: "Add more detail");

        // Assert - should send feedback as a message (local fallback)
        var lastUserMessage = session.Messages.LastOrDefault(m => m.Role == ClaudeMessageRole.User);
        Assert.That(lastUserMessage, Is.Not.Null);
        Assert.That(lastUserMessage!.Content[0].Text, Does.Contain("Add more detail"));
    }

    [Test]
    public async Task ApprovePlanAsync_Approved_LocalFallback_ExecutesPlan()
    {
        // Arrange
        var session = await _service.StartSessionAsync(
            "entity-1", "project-1", "/test/path", SessionMode.Plan, "sonnet");

        session.PlanContent = "# Test Plan\n\n1. Do things";
        session.Status = ClaudeSessionStatus.WaitingForPlanExecution;

        // No agent session mapping = local mode fallback

        // Mock StartSessionAsync for the ExecutePlan call
        _agentExecutionServiceMock
            .Setup(s => s.StartSessionAsync(It.IsAny<AgentStartRequest>(), It.IsAny<CancellationToken>()))
            .Returns(CreateSdkMessageStream(
                new SdkSystemMessage("agent-1", null, "session_started", null, null),
                new SdkResultMessage("agent-1", null, null, 0, 0, false, 0, 0, null)));

        // Act
        await _service.ApprovePlanAsync(session.Id, approved: true, keepContext: true);

        // Assert - should fall back to ExecutePlanAsync
        var lastUserMessage = session.Messages.LastOrDefault(m => m.Role == ClaudeMessageRole.User);
        Assert.That(lastUserMessage, Is.Not.Null);
        Assert.That(lastUserMessage!.Content[0].Text, Does.Contain("# Test Plan"));
    }

    [Test]
    public async Task ProcessSdkMessage_PlanPending_SetsWaitingForPlanExecution()
    {
        // Arrange
        var session = await _service.StartSessionAsync(
            "entity-1", "project-1", "/test/path", SessionMode.Plan, "sonnet");

        var planJson = "{\"plan\":\"# My Plan\\n\\n1. Step one\\n2. Step two\"}";

        // Simulate a plan_pending event followed by a result
        _agentExecutionServiceMock
            .Setup(s => s.StartSessionAsync(It.IsAny<AgentStartRequest>(), It.IsAny<CancellationToken>()))
            .Returns(CreateSdkMessageStream(
                new SdkSystemMessage("agent-1", null, "session_started", null, null),
                new SdkPlanPendingMessage("agent-1", planJson),
                new SdkResultMessage("agent-1", null, null, 0, 0, false, 0, 0, null)));

        // Act
        await _service.SendMessageAsync(session.Id, "Plan this feature");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(session.Status, Is.EqualTo(ClaudeSessionStatus.WaitingForPlanExecution));
            Assert.That(session.PlanContent, Is.Not.Null.And.Not.Empty);
        });
    }
}

/// <summary>
/// Tests for tool result detection in user messages (agent mode).
/// In agent mode, tool_use blocks are not streamed — only tool results arrive as user messages.
/// These tests verify that Write and ExitPlanMode are detected from tool results.
/// </summary>
[TestFixture]
public class ClaudeSessionServiceToolResultDetectionTests
{
    private ClaudeSessionService _service = null!;
    private IClaudeSessionStore _sessionStore = null!;
    private SessionOptionsFactory _optionsFactory = null!;
    private Mock<ILogger<ClaudeSessionService>> _loggerMock = null!;
    private Mock<ILogger<SessionOptionsFactory>> _factoryLoggerMock = null!;
    private Mock<IHubContext<Homespun.Features.ClaudeCode.Hubs.ClaudeCodeHub>> _hubContextMock = null!;
    private Mock<IClaudeSessionDiscovery> _discoveryMock = null!;
    private Mock<ISessionMetadataStore> _metadataStoreMock = null!;
    private Mock<IMessageCacheStore> _messageCacheMock = null!;
    private IToolResultParser _toolResultParser = null!;
    private Mock<IHooksService> _hooksServiceMock = null!;
    private Mock<IAgentExecutionService> _agentExecutionServiceMock = null!;

    [SetUp]
    public void SetUp()
    {
        _sessionStore = new ClaudeSessionStore();
        _factoryLoggerMock = new Mock<ILogger<SessionOptionsFactory>>();
        _optionsFactory = new SessionOptionsFactory(_factoryLoggerMock.Object);
        _loggerMock = new Mock<ILogger<ClaudeSessionService>>();
        _hubContextMock = new Mock<IHubContext<Homespun.Features.ClaudeCode.Hubs.ClaudeCodeHub>>();
        _discoveryMock = new Mock<IClaudeSessionDiscovery>();
        _metadataStoreMock = new Mock<ISessionMetadataStore>();
        _messageCacheMock = new Mock<IMessageCacheStore>();
        _toolResultParser = new ToolResultParser();
        _hooksServiceMock = new Mock<IHooksService>();
        _agentExecutionServiceMock = new Mock<IAgentExecutionService>();

        var clientsMock = new Mock<IHubClients>();
        var clientProxyMock = new Mock<IClientProxy>();
        clientsMock.Setup(c => c.All).Returns(clientProxyMock.Object);
        clientsMock.Setup(c => c.Group(It.IsAny<string>())).Returns(clientProxyMock.Object);
        _hubContextMock.Setup(h => h.Clients).Returns(clientsMock.Object);

        _service = new ClaudeSessionService(
            _sessionStore,
            _optionsFactory,
            _loggerMock.Object,
            _hubContextMock.Object,
            _discoveryMock.Object,
            _metadataStoreMock.Object,
            _toolResultParser,
            _hooksServiceMock.Object,
            _messageCacheMock.Object,
            _agentExecutionServiceMock.Object);
    }

    private static async IAsyncEnumerable<SdkMessage> CreateSdkMessageStream(params SdkMessage[] messages)
    {
        foreach (var msg in messages)
        {
            yield return msg;
        }
        await Task.CompletedTask;
    }

    /// <summary>
    /// Helper to create an SdkToolResultBlock from text content.
    /// </summary>
    private static SdkToolResultBlock CreateToolResult(string toolUseId, string? textContent, bool isError = false)
    {
        var contentElement = textContent != null
            ? System.Text.Json.JsonDocument.Parse($"\"{textContent}\"").RootElement
            : default;
        return new SdkToolResultBlock(toolUseId, contentElement, isError ? true : null);
    }

    [Test]
    public async Task WriteToolResult_WithPlanFilePath_CapturesPlanFilePath()
    {
        // Arrange
        var session = await _service.StartSessionAsync(
            "entity-1", "project-1", "/test/path", SessionMode.Plan, "sonnet");

        var planFilePath = "/home/user/.claude/plans/fluffy-aurora.md";

        // Simulate agent mode: Write tool result arrives as a user message
        _agentExecutionServiceMock
            .Setup(s => s.StartSessionAsync(It.IsAny<AgentStartRequest>(), It.IsAny<CancellationToken>()))
            .Returns(CreateSdkMessageStream(
                new SdkSystemMessage("agent-1", null, "session_started", null, null),
                // Assistant message with text
                new SdkAssistantMessage("agent-1", null,
                    new SdkApiMessage("assistant", new List<SdkContentBlock>
                    {
                        new SdkTextBlock("Writing plan file..."),
                        new SdkToolUseBlock("tool-use-1", "Write",
                            System.Text.Json.JsonDocument.Parse($"{{\"file_path\":\"{planFilePath}\",\"content\":\"# Plan\"}}").RootElement)
                    }), null),
                // Write tool result as a user message
                new SdkUserMessage("agent-1", null,
                    new SdkApiMessage("user", new List<SdkContentBlock>
                    {
                        CreateToolResult("tool-use-1", $"File created: {planFilePath}")
                    }), null),
                new SdkResultMessage("agent-1", null, null, 0, 0, false, 0, 0, null)));

        // Act
        await _service.SendMessageAsync(session.Id, "Create a plan");

        // Assert
        Assert.That(session.PlanFilePath, Is.EqualTo(planFilePath),
            "PlanFilePath should be captured from Write tool result");
    }

    [Test]
    public async Task WriteToolResult_WithNonPlanFilePath_DoesNotCapture()
    {
        // Arrange
        var session = await _service.StartSessionAsync(
            "entity-1", "project-1", "/test/path", SessionMode.Plan, "sonnet");

        // Simulate agent mode: Write tool result for a non-plan file
        _agentExecutionServiceMock
            .Setup(s => s.StartSessionAsync(It.IsAny<AgentStartRequest>(), It.IsAny<CancellationToken>()))
            .Returns(CreateSdkMessageStream(
                new SdkSystemMessage("agent-1", null, "session_started", null, null),
                new SdkAssistantMessage("agent-1", null,
                    new SdkApiMessage("assistant", new List<SdkContentBlock>
                    {
                        new SdkTextBlock("Writing file..."),
                        new SdkToolUseBlock("tool-use-1", "Write",
                            System.Text.Json.JsonDocument.Parse("{\"file_path\":\"/home/user/project/src/handler.ts\",\"content\":\"code\"}").RootElement)
                    }), null),
                new SdkUserMessage("agent-1", null,
                    new SdkApiMessage("user", new List<SdkContentBlock>
                    {
                        CreateToolResult("tool-use-1", "File created: /home/user/project/src/handler.ts")
                    }), null),
                new SdkResultMessage("agent-1", null, null, 0, 0, false, 0, 0, null)));

        // Act
        await _service.SendMessageAsync(session.Id, "Write a handler");

        // Assert
        Assert.That(session.PlanFilePath, Is.Null,
            "PlanFilePath should not be captured for non-plan files");
    }

    [Test]
    public async Task ExitPlanModeToolResult_TransitionsToWaitingForPlanExecution()
    {
        // Arrange
        var session = await _service.StartSessionAsync(
            "entity-1", "project-1", "/test/path", SessionMode.Plan, "sonnet");

        // Pre-populate plan content as if Write tool had already captured it
        session.PlanContent = "# Implementation Plan\n\n1. Step one\n2. Step two";
        session.PlanFilePath = "/home/user/.claude/plans/fluffy-aurora.md";

        // Simulate agent mode: ExitPlanMode tool result as a user message
        _agentExecutionServiceMock
            .Setup(s => s.StartSessionAsync(It.IsAny<AgentStartRequest>(), It.IsAny<CancellationToken>()))
            .Returns(CreateSdkMessageStream(
                new SdkSystemMessage("agent-1", null, "session_started", null, null),
                new SdkAssistantMessage("agent-1", null,
                    new SdkApiMessage("assistant", new List<SdkContentBlock>
                    {
                        new SdkTextBlock("I've created a plan."),
                        new SdkToolUseBlock("tool-use-2", "ExitPlanMode",
                            System.Text.Json.JsonDocument.Parse("{}").RootElement)
                    }), null),
                new SdkUserMessage("agent-1", null,
                    new SdkApiMessage("user", new List<SdkContentBlock>
                    {
                        CreateToolResult("tool-use-2", "Plan mode exited successfully.")
                    }), null),
                new SdkResultMessage("agent-1", null, null, 0, 0, false, 0, 0, null)));

        // Act
        await _service.SendMessageAsync(session.Id, "Plan this feature");

        // Assert
        Assert.That(session.Status, Is.EqualTo(ClaudeSessionStatus.WaitingForPlanExecution),
            "Session should transition to WaitingForPlanExecution when ExitPlanMode is detected from tool result");
    }

    [Test]
    public async Task ExitPlanModeToolResult_UsesPreviouslyCapturedPlanContent()
    {
        // Arrange
        var session = await _service.StartSessionAsync(
            "entity-1", "project-1", "/test/path", SessionMode.Plan, "sonnet");

        var planContent = "# My Plan\n\n## Steps\n1. Do this\n2. Do that";

        // Pre-populate plan content (simulates Write tool having captured it earlier)
        session.PlanContent = planContent;
        session.PlanFilePath = "/home/user/.claude/plans/test-plan.md";

        // Simulate agent mode: ExitPlanMode arrives
        _agentExecutionServiceMock
            .Setup(s => s.StartSessionAsync(It.IsAny<AgentStartRequest>(), It.IsAny<CancellationToken>()))
            .Returns(CreateSdkMessageStream(
                new SdkSystemMessage("agent-1", null, "session_started", null, null),
                new SdkAssistantMessage("agent-1", null,
                    new SdkApiMessage("assistant", new List<SdkContentBlock>
                    {
                        new SdkTextBlock("Plan complete."),
                        new SdkToolUseBlock("tool-use-2", "ExitPlanMode",
                            System.Text.Json.JsonDocument.Parse("{}").RootElement)
                    }), null),
                new SdkUserMessage("agent-1", null,
                    new SdkApiMessage("user", new List<SdkContentBlock>
                    {
                        CreateToolResult("tool-use-2", "Plan mode exited.")
                    }), null),
                new SdkResultMessage("agent-1", null, null, 0, 0, false, 0, 0, null)));

        // Act
        await _service.SendMessageAsync(session.Id, "Plan this");

        // Assert - plan message should be in session messages with the stored plan content
        var planMessage = session.Messages.FirstOrDefault(m =>
            m.Role == ClaudeMessageRole.Assistant &&
            m.Content.Any(c => c.Text?.Contains("Implementation Plan") == true));

        Assert.That(planMessage, Is.Not.Null, "Should have a plan display message");
        Assert.That(planMessage!.Content[0].Text, Does.Contain(planContent),
            "Plan display should contain the captured plan content");
    }

    [Test]
    public async Task WriteFollowedByExitPlanMode_ProducesCorrectPlanFlow()
    {
        // Arrange
        var session = await _service.StartSessionAsync(
            "entity-1", "project-1", "/test/path", SessionMode.Plan, "sonnet");

        var planFilePath = "/home/user/.claude/plans/fluffy-aurora.md";
        var planContent = "# Plan\n\n1. Step one\n2. Step two";

        // Mock agent container read - in real scenario, the file exists inside the agent container
        _agentExecutionServiceMock
            .Setup(s => s.ReadFileFromAgentAsync("agent-1", planFilePath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(planContent);

        // Simulate full agent mode flow: Write plan file, then ExitPlanMode
        _agentExecutionServiceMock
            .Setup(s => s.StartSessionAsync(It.IsAny<AgentStartRequest>(), It.IsAny<CancellationToken>()))
            .Returns(CreateSdkMessageStream(
                new SdkSystemMessage("agent-1", null, "session_started", null, null),
                // Assistant writes plan
                new SdkAssistantMessage("agent-1", null,
                    new SdkApiMessage("assistant", new List<SdkContentBlock>
                    {
                        new SdkTextBlock("Creating plan..."),
                        new SdkToolUseBlock("tool-use-1", "Write",
                            System.Text.Json.JsonDocument.Parse($"{{\"file_path\":\"{planFilePath}\",\"content\":\"{planContent.Replace("\n", "\\n")}\"}}").RootElement)
                    }), null),
                // Write tool result
                new SdkUserMessage("agent-1", null,
                    new SdkApiMessage("user", new List<SdkContentBlock>
                    {
                        CreateToolResult("tool-use-1", $"File created: {planFilePath}")
                    }), null),
                // Assistant announces plan mode exit
                new SdkAssistantMessage("agent-1", null,
                    new SdkApiMessage("assistant", new List<SdkContentBlock>
                    {
                        new SdkTextBlock("Plan ready for review."),
                        new SdkToolUseBlock("tool-use-2", "ExitPlanMode",
                            System.Text.Json.JsonDocument.Parse("{}").RootElement)
                    }), null),
                // ExitPlanMode tool result
                new SdkUserMessage("agent-1", null,
                    new SdkApiMessage("user", new List<SdkContentBlock>
                    {
                        CreateToolResult("tool-use-2", "Exited plan mode.")
                    }), null),
                new SdkResultMessage("agent-1", null, null, 0, 0, false, 0, 0, null)));

        // Act
        await _service.SendMessageAsync(session.Id, "Plan this feature");

        // Assert
        Assert.Multiple(() =>
        {
            // PlanFilePath should be captured from Write tool
            Assert.That(session.PlanFilePath, Is.EqualTo(planFilePath),
                "PlanFilePath should be captured from Write tool");

            // Status should be WaitingForPlanExecution from ExitPlanMode
            Assert.That(session.Status, Is.EqualTo(ClaudeSessionStatus.WaitingForPlanExecution),
                "Session should be in WaitingForPlanExecution status");

            // Plan content should be available (captured from Write tool input or fetched from agent)
            Assert.That(session.PlanContent, Is.Not.Null.And.Not.Empty,
                "PlanContent should be available");
        });
    }

    [Test]
    public async Task ExitPlanModeToolResult_WithoutPlanContent_UsesAgentContainerFallback()
    {
        // Arrange
        var session = await _service.StartSessionAsync(
            "entity-1", "project-1", "/test/path", SessionMode.Plan, "sonnet");

        var planFilePath = "/home/user/.claude/plans/test-plan.md";
        var planContent = "# Plan from container\n\nFetched remotely.";

        // Set up the plan file path but no content (simulates Docker mode)
        session.PlanFilePath = planFilePath;

        // Mock ReadFileFromAgentAsync to return plan content
        _agentExecutionServiceMock
            .Setup(s => s.ReadFileFromAgentAsync("agent-1", planFilePath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(planContent);

        // Simulate ExitPlanMode arriving
        _agentExecutionServiceMock
            .Setup(s => s.StartSessionAsync(It.IsAny<AgentStartRequest>(), It.IsAny<CancellationToken>()))
            .Returns(CreateSdkMessageStream(
                new SdkSystemMessage("agent-1", null, "session_started", null, null),
                new SdkAssistantMessage("agent-1", null,
                    new SdkApiMessage("assistant", new List<SdkContentBlock>
                    {
                        new SdkTextBlock("Plan ready."),
                        new SdkToolUseBlock("tool-use-1", "ExitPlanMode",
                            System.Text.Json.JsonDocument.Parse("{}").RootElement)
                    }), null),
                new SdkUserMessage("agent-1", null,
                    new SdkApiMessage("user", new List<SdkContentBlock>
                    {
                        CreateToolResult("tool-use-1", "Exited plan mode.")
                    }), null),
                new SdkResultMessage("agent-1", null, null, 0, 0, false, 0, 0, null)));

        // Act
        await _service.SendMessageAsync(session.Id, "Plan this");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(session.Status, Is.EqualTo(ClaudeSessionStatus.WaitingForPlanExecution),
                "Should transition to WaitingForPlanExecution");
            Assert.That(session.PlanContent, Is.EqualTo(planContent),
                "Should have fetched plan content from agent container");
        });
    }
}

/// <summary>
/// Tests for CheckCloneStateAsync and StartSessionWithTerminationAsync methods.
/// These methods support the simplified agent worker container management.
/// </summary>
[TestFixture]
public class ClaudeSessionServiceCloneStateTests
{
    private ClaudeSessionService _service = null!;
    private IClaudeSessionStore _sessionStore = null!;
    private SessionOptionsFactory _optionsFactory = null!;
    private Mock<ILogger<ClaudeSessionService>> _loggerMock = null!;
    private Mock<ILogger<SessionOptionsFactory>> _factoryLoggerMock = null!;
    private Mock<IHubContext<Homespun.Features.ClaudeCode.Hubs.ClaudeCodeHub>> _hubContextMock = null!;
    private Mock<IClaudeSessionDiscovery> _discoveryMock = null!;
    private Mock<ISessionMetadataStore> _metadataStoreMock = null!;
    private Mock<IMessageCacheStore> _messageCacheMock = null!;
    private IToolResultParser _toolResultParser = null!;
    private Mock<IHooksService> _hooksServiceMock = null!;
    private Mock<IAgentExecutionService> _agentExecutionServiceMock = null!;

    [SetUp]
    public void SetUp()
    {
        _sessionStore = new ClaudeSessionStore();
        _factoryLoggerMock = new Mock<ILogger<SessionOptionsFactory>>();
        _optionsFactory = new SessionOptionsFactory(_factoryLoggerMock.Object);
        _loggerMock = new Mock<ILogger<ClaudeSessionService>>();
        _hubContextMock = new Mock<IHubContext<Homespun.Features.ClaudeCode.Hubs.ClaudeCodeHub>>();
        _discoveryMock = new Mock<IClaudeSessionDiscovery>();
        _metadataStoreMock = new Mock<ISessionMetadataStore>();
        _messageCacheMock = new Mock<IMessageCacheStore>();
        _toolResultParser = new ToolResultParser();
        _hooksServiceMock = new Mock<IHooksService>();
        _agentExecutionServiceMock = new Mock<IAgentExecutionService>();

        var clientsMock = new Mock<IHubClients>();
        var clientProxyMock = new Mock<IClientProxy>();
        clientsMock.Setup(c => c.All).Returns(clientProxyMock.Object);
        clientsMock.Setup(c => c.Group(It.IsAny<string>())).Returns(clientProxyMock.Object);
        _hubContextMock.Setup(h => h.Clients).Returns(clientsMock.Object);

        _service = new ClaudeSessionService(
            _sessionStore,
            _optionsFactory,
            _loggerMock.Object,
            _hubContextMock.Object,
            _discoveryMock.Object,
            _metadataStoreMock.Object,
            _toolResultParser,
            _hooksServiceMock.Object,
            _messageCacheMock.Object,
            _agentExecutionServiceMock.Object);
    }

    [Test]
    public async Task CheckCloneStateAsync_NoContainer_ReturnsStartNew()
    {
        // Arrange
        _agentExecutionServiceMock
            .Setup(s => s.GetCloneContainerStateAsync("/test/path", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CloneContainerState?)null);

        // Act
        var result = await _service.CheckCloneStateAsync("/test/path");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Action, Is.EqualTo(AgentStartAction.StartNew));
            Assert.That(result.ExistingState, Is.Null);
            Assert.That(result.Message, Is.Null);
        });
    }

    [Test]
    public async Task CheckCloneStateAsync_RunningSession_ReturnsNotifyActive()
    {
        // Arrange
        var containerState = new CloneContainerState(
            "/test/path",
            "container-123",
            "session-123",
            "worker-session-123",
            ClaudeSessionStatus.Running,
            DateTime.UtcNow,
            false,
            false);

        _agentExecutionServiceMock
            .Setup(s => s.GetCloneContainerStateAsync("/test/path", It.IsAny<CancellationToken>()))
            .ReturnsAsync(containerState);

        // Act
        var result = await _service.CheckCloneStateAsync("/test/path");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Action, Is.EqualTo(AgentStartAction.NotifyActive));
            Assert.That(result.ExistingState, Is.EqualTo(containerState));
            Assert.That(result.Message, Does.Contain("currently working"));
        });
    }

    [Test]
    public async Task CheckCloneStateAsync_WaitingForQuestion_ReturnsNotifyActive()
    {
        // Arrange
        var containerState = new CloneContainerState(
            "/test/path",
            "container-123",
            "session-123",
            "worker-session-123",
            ClaudeSessionStatus.WaitingForQuestionAnswer,
            DateTime.UtcNow,
            true,
            false);

        _agentExecutionServiceMock
            .Setup(s => s.GetCloneContainerStateAsync("/test/path", It.IsAny<CancellationToken>()))
            .ReturnsAsync(containerState);

        // Act
        var result = await _service.CheckCloneStateAsync("/test/path");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Action, Is.EqualTo(AgentStartAction.NotifyActive));
            Assert.That(result.Message, Does.Contain("answer a question"));
        });
    }

    [Test]
    public async Task CheckCloneStateAsync_WaitingForPlan_ReturnsNotifyActive()
    {
        // Arrange
        var containerState = new CloneContainerState(
            "/test/path",
            "container-123",
            "session-123",
            "worker-session-123",
            ClaudeSessionStatus.WaitingForPlanExecution,
            DateTime.UtcNow,
            false,
            true);

        _agentExecutionServiceMock
            .Setup(s => s.GetCloneContainerStateAsync("/test/path", It.IsAny<CancellationToken>()))
            .ReturnsAsync(containerState);

        // Act
        var result = await _service.CheckCloneStateAsync("/test/path");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Action, Is.EqualTo(AgentStartAction.NotifyActive));
            Assert.That(result.Message, Does.Contain("plan waiting"));
        });
    }

    [Test]
    public async Task CheckCloneStateAsync_IdleSession_ReturnsConfirmTerminate()
    {
        // Arrange
        var containerState = new CloneContainerState(
            "/test/path",
            "container-123",
            "session-123",
            "worker-session-123",
            ClaudeSessionStatus.WaitingForInput,
            DateTime.UtcNow,
            false,
            false);

        _agentExecutionServiceMock
            .Setup(s => s.GetCloneContainerStateAsync("/test/path", It.IsAny<CancellationToken>()))
            .ReturnsAsync(containerState);

        // Act
        var result = await _service.CheckCloneStateAsync("/test/path");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Action, Is.EqualTo(AgentStartAction.ConfirmTerminate));
            Assert.That(result.ExistingState, Is.EqualTo(containerState));
            Assert.That(result.Message, Does.Contain("terminate"));
        });
    }

    [Test]
    public async Task CheckCloneStateAsync_StoppedSession_ReturnsReuseContainer()
    {
        // Arrange
        var containerState = new CloneContainerState(
            "/test/path",
            "container-123",
            null,
            null,
            ClaudeSessionStatus.Stopped,
            null,
            false,
            false);

        _agentExecutionServiceMock
            .Setup(s => s.GetCloneContainerStateAsync("/test/path", It.IsAny<CancellationToken>()))
            .ReturnsAsync(containerState);

        // Act
        var result = await _service.CheckCloneStateAsync("/test/path");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Action, Is.EqualTo(AgentStartAction.ReuseContainer));
            Assert.That(result.ExistingState, Is.EqualTo(containerState));
            Assert.That(result.Message, Is.Null);
        });
    }

    [Test]
    public async Task StartSessionWithTerminationAsync_WithTerminateExisting_CallsTerminate()
    {
        // Arrange
        _agentExecutionServiceMock
            .Setup(s => s.TerminateCloneSessionAsync("/test/path", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var session = await _service.StartSessionWithTerminationAsync(
            "entity-123",
            "project-456",
            "/test/path",
            SessionMode.Build,
            "sonnet",
            terminateExisting: true);

        // Assert
        _agentExecutionServiceMock.Verify(
            s => s.TerminateCloneSessionAsync("/test/path", It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.That(session, Is.Not.Null);
        Assert.That(session.EntityId, Is.EqualTo("entity-123"));
    }

    [Test]
    public async Task StartSessionWithTerminationAsync_WithoutTerminateExisting_DoesNotCallTerminate()
    {
        // Act
        var session = await _service.StartSessionWithTerminationAsync(
            "entity-123",
            "project-456",
            "/test/path",
            SessionMode.Build,
            "sonnet",
            terminateExisting: false);

        // Assert
        _agentExecutionServiceMock.Verify(
            s => s.TerminateCloneSessionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.That(session, Is.Not.Null);
    }

    [Test]
    public async Task StartSessionWithTerminationAsync_WithSystemPrompt_PassesPromptToStartSession()
    {
        // Act
        var session = await _service.StartSessionWithTerminationAsync(
            "entity-123",
            "project-456",
            "/test/path",
            SessionMode.Plan,
            "opus",
            terminateExisting: false,
            systemPrompt: "You are a helpful assistant.");

        // Assert
        Assert.That(session.SystemPrompt, Is.EqualTo("You are a helpful assistant."));
    }
}

/// <summary>
/// Tests for SignalR status broadcasts in SendMessageAsync (Issue 4wBwBc).
/// These tests verify that status changes are properly broadcast to clients via SignalR.
/// </summary>
[TestFixture]
public class ClaudeSessionServiceStatusBroadcastTests
{
    private ClaudeSessionService _service = null!;
    private IClaudeSessionStore _sessionStore = null!;
    private SessionOptionsFactory _optionsFactory = null!;
    private Mock<ILogger<ClaudeSessionService>> _loggerMock = null!;
    private Mock<ILogger<SessionOptionsFactory>> _factoryLoggerMock = null!;
    private Mock<IHubContext<Homespun.Features.ClaudeCode.Hubs.ClaudeCodeHub>> _hubContextMock = null!;
    private Mock<IClaudeSessionDiscovery> _discoveryMock = null!;
    private Mock<ISessionMetadataStore> _metadataStoreMock = null!;
    private Mock<IMessageCacheStore> _messageCacheMock = null!;
    private IToolResultParser _toolResultParser = null!;
    private Mock<IHooksService> _hooksServiceMock = null!;
    private Mock<IAgentExecutionService> _agentExecutionServiceMock = null!;
    private Mock<IClientProxy> _clientProxyMock = null!;
    private List<(string Method, object?[] Args)> _broadcastCalls = null!;

    [SetUp]
    public void SetUp()
    {
        _sessionStore = new ClaudeSessionStore();
        _factoryLoggerMock = new Mock<ILogger<SessionOptionsFactory>>();
        _optionsFactory = new SessionOptionsFactory(_factoryLoggerMock.Object);
        _loggerMock = new Mock<ILogger<ClaudeSessionService>>();
        _hubContextMock = new Mock<IHubContext<Homespun.Features.ClaudeCode.Hubs.ClaudeCodeHub>>();
        _discoveryMock = new Mock<IClaudeSessionDiscovery>();
        _metadataStoreMock = new Mock<ISessionMetadataStore>();
        _messageCacheMock = new Mock<IMessageCacheStore>();
        _toolResultParser = new ToolResultParser();
        _hooksServiceMock = new Mock<IHooksService>();
        _agentExecutionServiceMock = new Mock<IAgentExecutionService>();
        _broadcastCalls = new List<(string Method, object?[] Args)>();

        var clientsMock = new Mock<IHubClients>();
        _clientProxyMock = new Mock<IClientProxy>();

        // Track all SendAsync calls to verify broadcasts
        _clientProxyMock
            .Setup(c => c.SendCoreAsync(
                It.IsAny<string>(),
                It.IsAny<object?[]>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, object?[], CancellationToken>((method, args, _) =>
            {
                _broadcastCalls.Add((method, args));
            })
            .Returns(Task.CompletedTask);

        clientsMock.Setup(c => c.All).Returns(_clientProxyMock.Object);
        clientsMock.Setup(c => c.Group(It.IsAny<string>())).Returns(_clientProxyMock.Object);
        _hubContextMock.Setup(h => h.Clients).Returns(clientsMock.Object);

        _service = new ClaudeSessionService(
            _sessionStore,
            _optionsFactory,
            _loggerMock.Object,
            _hubContextMock.Object,
            _discoveryMock.Object,
            _metadataStoreMock.Object,
            _toolResultParser,
            _hooksServiceMock.Object,
            _messageCacheMock.Object,
            _agentExecutionServiceMock.Object);
    }

    private static async IAsyncEnumerable<SdkMessage> CreateSdkMessageStream(params SdkMessage[] messages)
    {
        foreach (var msg in messages)
        {
            yield return msg;
        }
        await Task.CompletedTask;
    }

    [Test]
    public async Task SendMessageAsync_BroadcastsRunningStatus_WhenProcessingStarts()
    {
        // Arrange
        var session = await _service.StartSessionAsync(
            "entity-1", "project-1", "/test/path", SessionMode.Build, "sonnet");

        // Clear broadcasts from StartSessionAsync
        _broadcastCalls.Clear();

        // Setup mock to return a valid SDK message stream
        _agentExecutionServiceMock
            .Setup(s => s.StartSessionAsync(It.IsAny<AgentStartRequest>(), It.IsAny<CancellationToken>()))
            .Returns(CreateSdkMessageStream(
                new SdkSystemMessage("agent-1", null, "session_started", null, null),
                new SdkResultMessage("agent-1", null, null, 0, 0, false, 0, 0, null)));

        // Act
        await _service.SendMessageAsync(session.Id, "Hello");

        // Assert - Should have broadcast Running status when processing started
        var statusBroadcasts = _broadcastCalls
            .Where(c => c.Method == "SessionStatusChanged")
            .ToList();

        Assert.That(statusBroadcasts.Any(c =>
            c.Args.Length >= 2 &&
            c.Args[1] is ClaudeSessionStatus status &&
            status == ClaudeSessionStatus.Running),
            Is.True,
            "Should broadcast Running status when message processing starts");
    }

    [Test]
    public async Task SendMessageAsync_BroadcastsWaitingForInputStatus_WhenProcessingCompletes()
    {
        // Arrange
        var session = await _service.StartSessionAsync(
            "entity-1", "project-1", "/test/path", SessionMode.Build, "sonnet");

        // Clear broadcasts from StartSessionAsync
        _broadcastCalls.Clear();

        // Setup mock to return a valid SDK message stream
        _agentExecutionServiceMock
            .Setup(s => s.StartSessionAsync(It.IsAny<AgentStartRequest>(), It.IsAny<CancellationToken>()))
            .Returns(CreateSdkMessageStream(
                new SdkSystemMessage("agent-1", null, "session_started", null, null),
                new SdkResultMessage("agent-1", null, null, 0, 0, false, 0, 0, null)));

        // Act
        await _service.SendMessageAsync(session.Id, "Hello");

        // Assert - Should have broadcast WaitingForInput status when processing completed
        var statusBroadcasts = _broadcastCalls
            .Where(c => c.Method == "SessionStatusChanged")
            .ToList();

        Assert.That(statusBroadcasts.Any(c =>
            c.Args.Length >= 2 &&
            c.Args[1] is ClaudeSessionStatus status &&
            status == ClaudeSessionStatus.WaitingForInput),
            Is.True,
            "Should broadcast WaitingForInput status when message processing completes");
    }

    [Test]
    public async Task SendMessageAsync_BroadcastsStatusInCorrectOrder()
    {
        // Arrange
        var session = await _service.StartSessionAsync(
            "entity-1", "project-1", "/test/path", SessionMode.Build, "sonnet");

        // Clear broadcasts from StartSessionAsync
        _broadcastCalls.Clear();

        // Setup mock to return a valid SDK message stream
        _agentExecutionServiceMock
            .Setup(s => s.StartSessionAsync(It.IsAny<AgentStartRequest>(), It.IsAny<CancellationToken>()))
            .Returns(CreateSdkMessageStream(
                new SdkSystemMessage("agent-1", null, "session_started", null, null),
                new SdkResultMessage("agent-1", null, null, 0, 0, false, 0, 0, null)));

        // Act
        await _service.SendMessageAsync(session.Id, "Hello");

        // Assert - Get status broadcasts in order (BroadcastSessionStatusChanged sends to both All and Group)
        var statusBroadcasts = _broadcastCalls
            .Where(c => c.Method == "SessionStatusChanged")
            .Select(c => c.Args.Length >= 2 ? c.Args[1] as ClaudeSessionStatus? : null)
            .Where(s => s.HasValue)
            .Select(s => s!.Value)
            .ToList();

        // Should have at least 2 broadcasts (Running then WaitingForInput), each sent twice (All and Group)
        // So we expect Running, Running, WaitingForInput, WaitingForInput
        var distinctStatuses = statusBroadcasts.Distinct().ToList();
        Assert.That(distinctStatuses, Has.Count.GreaterThanOrEqualTo(2),
            "Should have at least Running and WaitingForInput status broadcasts");

        // Verify Running comes before WaitingForInput
        var firstRunningIndex = statusBroadcasts.IndexOf(ClaudeSessionStatus.Running);
        var firstWaitingIndex = statusBroadcasts.IndexOf(ClaudeSessionStatus.WaitingForInput);

        Assert.That(firstRunningIndex, Is.LessThan(firstWaitingIndex),
            "Running status should be broadcast before WaitingForInput status");
    }

    [Test]
    public async Task ExecutePlanAsync_WithClearContext_BroadcastsStatusChanges()
    {
        // Arrange
        var session = await _service.StartSessionAsync(
            "entity-1", "project-1", "/test/path", SessionMode.Plan, "sonnet");

        session.PlanContent = "# Test Plan\n\n1. Step one";
        session.Status = ClaudeSessionStatus.WaitingForPlanExecution;

        // Clear broadcasts from setup
        _broadcastCalls.Clear();

        // Setup mock to return a valid SDK message stream
        _agentExecutionServiceMock
            .Setup(s => s.StartSessionAsync(It.IsAny<AgentStartRequest>(), It.IsAny<CancellationToken>()))
            .Returns(CreateSdkMessageStream(
                new SdkSystemMessage("agent-1", null, "session_started", null, null),
                new SdkResultMessage("agent-1", null, null, 0, 0, false, 0, 0, null)));

        // Act
        await _service.ExecutePlanAsync(session.Id, clearContext: true);

        // Assert - Should have broadcast status changes during plan execution
        var statusBroadcasts = _broadcastCalls
            .Where(c => c.Method == "SessionStatusChanged")
            .Select(c => c.Args.Length >= 2 ? c.Args[1] as ClaudeSessionStatus? : null)
            .Where(s => s.HasValue)
            .Select(s => s!.Value)
            .ToList();

        // Should have Running broadcast (from ExecutePlanAsync setting status before SendMessageAsync)
        // and then Running again from SendMessageAsync start, then WaitingForInput at end
        Assert.That(statusBroadcasts.Any(s => s == ClaudeSessionStatus.Running),
            Is.True,
            "Should broadcast Running status during plan execution");

        Assert.That(statusBroadcasts.Any(s => s == ClaudeSessionStatus.WaitingForInput),
            Is.True,
            "Should broadcast WaitingForInput status when plan execution completes");
    }

    [Test]
    public async Task SendMessageAsync_AgentStartRequest_IncludesIssueIdAndProjectId()
    {
        // Arrange
        var entityId = "issue-42";
        var projectId = "project-abc";
        var session = await _service.StartSessionAsync(
            entityId, projectId, "/test/path", SessionMode.Build, "sonnet");

        AgentStartRequest? capturedRequest = null;
        _agentExecutionServiceMock
            .Setup(s => s.StartSessionAsync(It.IsAny<AgentStartRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AgentStartRequest, CancellationToken>((req, _) => capturedRequest = req)
            .Returns(CreateSdkMessageStream(
                new SdkSystemMessage("agent-1", null, "session_started", null, null),
                new SdkResultMessage("agent-1", null, null, 0, 0, false, 0, 0, null)));

        // Act
        await _service.SendMessageAsync(session.Id, "hello");

        // Assert
        Assert.That(capturedRequest, Is.Not.Null, "AgentStartRequest should have been captured");
        Assert.Multiple(() =>
        {
            Assert.That(capturedRequest!.IssueId, Is.EqualTo(entityId),
                "AgentStartRequest.IssueId should be set to the session's EntityId");
            Assert.That(capturedRequest.ProjectId, Is.EqualTo(projectId),
                "AgentStartRequest.ProjectId should be set to the session's ProjectId");
        });
    }

    [Test]
    public async Task SendMessageAsync_AgentStartRequest_IncludesWorkingDirectoryAndModel()
    {
        // Arrange
        var session = await _service.StartSessionAsync(
            "entity-1", "project-1", "/work/dir", SessionMode.Plan, "opus");

        AgentStartRequest? capturedRequest = null;
        _agentExecutionServiceMock
            .Setup(s => s.StartSessionAsync(It.IsAny<AgentStartRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AgentStartRequest, CancellationToken>((req, _) => capturedRequest = req)
            .Returns(CreateSdkMessageStream(
                new SdkSystemMessage("agent-1", null, "session_started", null, null),
                new SdkResultMessage("agent-1", null, null, 0, 0, false, 0, 0, null)));

        // Act
        await _service.SendMessageAsync(session.Id, "test message");

        // Assert
        Assert.That(capturedRequest, Is.Not.Null, "AgentStartRequest should have been captured");
        Assert.Multiple(() =>
        {
            Assert.That(capturedRequest!.WorkingDirectory, Is.EqualTo("/work/dir"));
            Assert.That(capturedRequest.Mode, Is.EqualTo(SessionMode.Plan));
            Assert.That(capturedRequest.Prompt, Is.EqualTo("test message"));
        });
    }
}
