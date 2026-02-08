using Homespun.ClaudeAgentSdk;
using Homespun.Features.ClaudeCode.Data;
using Homespun.Features.ClaudeCode.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

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
    public async Task StopAllSessionsForEntityAsync_StopsAllSessionsForEntity()
    {
        // Arrange - create multiple sessions for the same entity
        var entityId = "entity-123";
        _sessionStore.Add(new ClaudeSession
        {
            Id = "session-1",
            EntityId = entityId,
            ProjectId = "project-456",
            WorkingDirectory = "/path1",
            Model = "model",
            Mode = SessionMode.Plan,
            Status = ClaudeSessionStatus.Running,
            CreatedAt = DateTime.UtcNow
        });
        _sessionStore.Add(new ClaudeSession
        {
            Id = "session-2",
            EntityId = entityId,
            ProjectId = "project-456",
            WorkingDirectory = "/path2",
            Model = "model",
            Mode = SessionMode.Build,
            Status = ClaudeSessionStatus.Running,
            CreatedAt = DateTime.UtcNow
        });
        _sessionStore.Add(new ClaudeSession
        {
            Id = "session-3",
            EntityId = "other-entity",
            ProjectId = "project-456",
            WorkingDirectory = "/path3",
            Model = "model",
            Mode = SessionMode.Plan,
            Status = ClaudeSessionStatus.Running,
            CreatedAt = DateTime.UtcNow
        });

        // Act
        await _service.StopAllSessionsForEntityAsync(entityId);

        // Assert - sessions for the entity should be removed, other session preserved
        Assert.Multiple(() =>
        {
            Assert.That(_sessionStore.GetById("session-1"), Is.Null, "Session 1 should be removed");
            Assert.That(_sessionStore.GetById("session-2"), Is.Null, "Session 2 should be removed");
            Assert.That(_sessionStore.GetById("session-3"), Is.Not.Null, "Session 3 should be preserved");
        });
    }

    [Test]
    public async Task StopAllSessionsForEntityAsync_NoSessions_DoesNotThrow()
    {
        // Act & Assert
        Assert.DoesNotThrowAsync(async () =>
            await _service.StopAllSessionsForEntityAsync("non-existent-entity"));
    }

    [Test]
    public async Task StopAllSessionsForEntityAsync_SingleSession_StopsIt()
    {
        // Arrange
        var entityId = "entity-123";
        _sessionStore.Add(new ClaudeSession
        {
            Id = "session-1",
            EntityId = entityId,
            ProjectId = "project-456",
            WorkingDirectory = "/path1",
            Model = "model",
            Mode = SessionMode.Build,
            Status = ClaudeSessionStatus.Running,
            CreatedAt = DateTime.UtcNow
        });

        // Act
        await _service.StopAllSessionsForEntityAsync(entityId);

        // Assert
        Assert.That(_sessionStore.GetById("session-1"), Is.Null);
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
            await _service.SendMessageAsync("non-existent-session", "Hello", PermissionMode.AcceptEdits));
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
            await _service.SendMessageAsync("stopped-session", "Hello", PermissionMode.Plan));
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

    [TestCase(PermissionMode.Default)]
    [TestCase(PermissionMode.AcceptEdits)]
    [TestCase(PermissionMode.Plan)]
    [TestCase(PermissionMode.BypassPermissions)]
    public void SendMessageAsync_WithPermissionMode_AcceptsAllModes(PermissionMode permissionMode)
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
    /// Helper to create an async enumerable that yields the given events.
    /// </summary>
    private static async IAsyncEnumerable<AgentEvent> CreateAgentEventStream(params AgentEvent[] events)
    {
        foreach (var evt in events)
        {
            yield return evt;
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

        // Setup mock to return a valid event stream that completes immediately
        _agentExecutionServiceMock
            .Setup(s => s.StartSessionAsync(It.IsAny<AgentStartRequest>(), It.IsAny<CancellationToken>()))
            .Returns(CreateAgentEventStream(
                new AgentSessionStartedEvent("agent-1", null),
                new AgentResultEvent("agent-1", 0, 0, null)));

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
    public async Task ExecutePlanAsync_WithClearContext_ClearsConversationId()
    {
        // Arrange
        var session = await _service.StartSessionAsync(
            "entity-1", "project-1", "/test/path", SessionMode.Plan, "sonnet");

        session.ConversationId = "original-conversation-id";
        session.PlanContent = "# Test Plan";

        // Setup mock to return a valid event stream
        _agentExecutionServiceMock
            .Setup(s => s.StartSessionAsync(It.IsAny<AgentStartRequest>(), It.IsAny<CancellationToken>()))
            .Returns(CreateAgentEventStream(
                new AgentSessionStartedEvent("agent-1", null),
                new AgentResultEvent("agent-1", 0, 0, null)));

        // Act
        await _service.ExecutePlanAsync(session.Id, clearContext: true);

        // Assert - ConversationId should be cleared after context clear
        Assert.That(session.ConversationId, Is.Null,
            "ConversationId should be null after context clearing");
    }

    [Test]
    public async Task ExecutePlanAsync_WithClearContext_AddsContextClearMarker()
    {
        // Arrange
        var session = await _service.StartSessionAsync(
            "entity-1", "project-1", "/test/path", SessionMode.Plan, "sonnet");

        session.PlanContent = "# Test Plan";

        // Setup mock to return a valid event stream
        _agentExecutionServiceMock
            .Setup(s => s.StartSessionAsync(It.IsAny<AgentStartRequest>(), It.IsAny<CancellationToken>()))
            .Returns(CreateAgentEventStream(
                new AgentSessionStartedEvent("agent-1", null),
                new AgentResultEvent("agent-1", 0, 0, null)));

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

    private static async IAsyncEnumerable<AgentEvent> CreateAgentEventStream(params AgentEvent[] events)
    {
        foreach (var evt in events)
        {
            yield return evt;
        }
        await Task.CompletedTask;
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
            .Returns(CreateAgentEventStream(
                new AgentSessionStartedEvent("agent-1", null),
                // Assistant message with text
                new AgentMessageEvent("agent-1", ClaudeMessageRole.Assistant, new List<AgentContentBlockEvent>
                {
                    new("agent-1", ClaudeContentType.Text, "Writing plan file...", null, null, null, null, 0)
                }),
                // Write tool result as a user message
                new AgentMessageEvent("agent-1", ClaudeMessageRole.User, new List<AgentContentBlockEvent>
                {
                    new("agent-1", ClaudeContentType.ToolResult, $"File created: {planFilePath}",
                        "Write", null, "tool-use-1", true, 0)
                }),
                new AgentResultEvent("agent-1", 0, 0, null)));

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
            .Returns(CreateAgentEventStream(
                new AgentSessionStartedEvent("agent-1", null),
                new AgentMessageEvent("agent-1", ClaudeMessageRole.Assistant, new List<AgentContentBlockEvent>
                {
                    new("agent-1", ClaudeContentType.Text, "Writing file...", null, null, null, null, 0)
                }),
                new AgentMessageEvent("agent-1", ClaudeMessageRole.User, new List<AgentContentBlockEvent>
                {
                    new("agent-1", ClaudeContentType.ToolResult, "File created: /home/user/project/src/handler.ts",
                        "Write", null, "tool-use-1", true, 0)
                }),
                new AgentResultEvent("agent-1", 0, 0, null)));

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
            .Returns(CreateAgentEventStream(
                new AgentSessionStartedEvent("agent-1", null),
                new AgentMessageEvent("agent-1", ClaudeMessageRole.Assistant, new List<AgentContentBlockEvent>
                {
                    new("agent-1", ClaudeContentType.Text, "I've created a plan.", null, null, null, null, 0)
                }),
                new AgentMessageEvent("agent-1", ClaudeMessageRole.User, new List<AgentContentBlockEvent>
                {
                    new("agent-1", ClaudeContentType.ToolResult, "Plan mode exited successfully.",
                        "ExitPlanMode", null, "tool-use-2", true, 0)
                }),
                new AgentResultEvent("agent-1", 0, 0, null)));

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
            .Returns(CreateAgentEventStream(
                new AgentSessionStartedEvent("agent-1", null),
                new AgentMessageEvent("agent-1", ClaudeMessageRole.Assistant, new List<AgentContentBlockEvent>
                {
                    new("agent-1", ClaudeContentType.Text, "Plan complete.", null, null, null, null, 0)
                }),
                new AgentMessageEvent("agent-1", ClaudeMessageRole.User, new List<AgentContentBlockEvent>
                {
                    new("agent-1", ClaudeContentType.ToolResult, "Plan mode exited.",
                        "ExitPlanMode", null, "tool-use-2", true, 0)
                }),
                new AgentResultEvent("agent-1", 0, 0, null)));

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
            .Returns(CreateAgentEventStream(
                new AgentSessionStartedEvent("agent-1", null),
                // Assistant writes plan
                new AgentMessageEvent("agent-1", ClaudeMessageRole.Assistant, new List<AgentContentBlockEvent>
                {
                    new("agent-1", ClaudeContentType.Text, "Creating plan...", null, null, null, null, 0)
                }),
                // Write tool result
                new AgentMessageEvent("agent-1", ClaudeMessageRole.User, new List<AgentContentBlockEvent>
                {
                    new("agent-1", ClaudeContentType.ToolResult, $"File created: {planFilePath}",
                        "Write", null, "tool-use-1", true, 0)
                }),
                // Assistant announces plan mode exit
                new AgentMessageEvent("agent-1", ClaudeMessageRole.Assistant, new List<AgentContentBlockEvent>
                {
                    new("agent-1", ClaudeContentType.Text, "Plan ready for review.", null, null, null, null, 0)
                }),
                // ExitPlanMode tool result
                new AgentMessageEvent("agent-1", ClaudeMessageRole.User, new List<AgentContentBlockEvent>
                {
                    new("agent-1", ClaudeContentType.ToolResult, "Exited plan mode.",
                        "ExitPlanMode", null, "tool-use-2", true, 0)
                }),
                new AgentResultEvent("agent-1", 0, 0, null)));

        // Act
        await _service.SendMessageAsync(session.Id, "Plan this feature");

        // Assert
        Assert.Multiple(() =>
        {
            // PlanFilePath should be captured from Write tool result
            Assert.That(session.PlanFilePath, Is.EqualTo(planFilePath),
                "PlanFilePath should be captured from Write tool result");

            // Status should be WaitingForPlanExecution from ExitPlanMode
            Assert.That(session.Status, Is.EqualTo(ClaudeSessionStatus.WaitingForPlanExecution),
                "Session should be in WaitingForPlanExecution status");

            // Plan content should be fetched from agent container
            Assert.That(session.PlanContent, Is.EqualTo(planContent),
                "PlanContent should be fetched from agent container");
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

        // Set up the plan file path but no content (simulates Docker/Azure mode)
        session.PlanFilePath = planFilePath;

        // Mock ReadFileFromAgentAsync to return plan content
        _agentExecutionServiceMock
            .Setup(s => s.ReadFileFromAgentAsync("agent-1", planFilePath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(planContent);

        // Simulate ExitPlanMode arriving
        _agentExecutionServiceMock
            .Setup(s => s.StartSessionAsync(It.IsAny<AgentStartRequest>(), It.IsAny<CancellationToken>()))
            .Returns(CreateAgentEventStream(
                new AgentSessionStartedEvent("agent-1", null),
                new AgentMessageEvent("agent-1", ClaudeMessageRole.Assistant, new List<AgentContentBlockEvent>
                {
                    new("agent-1", ClaudeContentType.Text, "Plan ready.", null, null, null, null, 0)
                }),
                new AgentMessageEvent("agent-1", ClaudeMessageRole.User, new List<AgentContentBlockEvent>
                {
                    new("agent-1", ClaudeContentType.ToolResult, "Exited plan mode.",
                        "ExitPlanMode", null, "tool-use-1", true, 0)
                }),
                new AgentResultEvent("agent-1", 0, 0, null)));

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
