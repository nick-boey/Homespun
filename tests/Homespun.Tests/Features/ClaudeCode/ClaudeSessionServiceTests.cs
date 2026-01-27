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
    private Mock<IHubContext<Homespun.Features.ClaudeCode.Hubs.ClaudeCodeHub>> _hubContextMock = null!;
    private Mock<IClaudeSessionDiscovery> _discoveryMock = null!;
    private Mock<ISessionMetadataStore> _metadataStoreMock = null!;
    private IToolResultParser _toolResultParser = null!;

    [SetUp]
    public void SetUp()
    {
        _sessionStore = new ClaudeSessionStore();
        _optionsFactory = new SessionOptionsFactory();
        _loggerMock = new Mock<ILogger<ClaudeSessionService>>();
        _hubContextMock = new Mock<IHubContext<Homespun.Features.ClaudeCode.Hubs.ClaudeCodeHub>>();
        _discoveryMock = new Mock<IClaudeSessionDiscovery>();
        _metadataStoreMock = new Mock<ISessionMetadataStore>();
        _toolResultParser = new ToolResultParser();

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
            _toolResultParser);
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
    private Mock<IHubContext<Homespun.Features.ClaudeCode.Hubs.ClaudeCodeHub>> _hubContextMock = null!;
    private Mock<IClaudeSessionDiscovery> _discoveryMock = null!;
    private Mock<ISessionMetadataStore> _metadataStoreMock = null!;
    private IToolResultParser _toolResultParser = null!;

    [SetUp]
    public void SetUp()
    {
        _sessionStore = new ClaudeSessionStore();
        _optionsFactory = new SessionOptionsFactory();
        _loggerMock = new Mock<ILogger<ClaudeSessionService>>();
        _hubContextMock = new Mock<IHubContext<Homespun.Features.ClaudeCode.Hubs.ClaudeCodeHub>>();
        _discoveryMock = new Mock<IClaudeSessionDiscovery>();
        _metadataStoreMock = new Mock<ISessionMetadataStore>();
        _toolResultParser = new ToolResultParser();

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
            _toolResultParser);
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
    private Mock<IHubContext<Homespun.Features.ClaudeCode.Hubs.ClaudeCodeHub>> _hubContextMock = null!;
    private Mock<IClaudeSessionDiscovery> _discoveryMock = null!;
    private Mock<ISessionMetadataStore> _metadataStoreMock = null!;
    private IToolResultParser _toolResultParser = null!;

    [SetUp]
    public void SetUp()
    {
        _sessionStore = new ClaudeSessionStore();
        _optionsFactory = new SessionOptionsFactory();
        _loggerMock = new Mock<ILogger<ClaudeSessionService>>();
        _hubContextMock = new Mock<IHubContext<Homespun.Features.ClaudeCode.Hubs.ClaudeCodeHub>>();
        _discoveryMock = new Mock<IClaudeSessionDiscovery>();
        _metadataStoreMock = new Mock<ISessionMetadataStore>();
        _toolResultParser = new ToolResultParser();

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
            _toolResultParser);
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
/// Tests for session resumption functionality.
/// </summary>
[TestFixture]
public class ClaudeSessionServiceResumeTests
{
    private ClaudeSessionService _service = null!;
    private IClaudeSessionStore _sessionStore = null!;
    private SessionOptionsFactory _optionsFactory = null!;
    private Mock<ILogger<ClaudeSessionService>> _loggerMock = null!;
    private Mock<IHubContext<Homespun.Features.ClaudeCode.Hubs.ClaudeCodeHub>> _hubContextMock = null!;
    private Mock<IClaudeSessionDiscovery> _discoveryMock = null!;
    private Mock<ISessionMetadataStore> _metadataStoreMock = null!;
    private IToolResultParser _toolResultParser = null!;
    private string _testClaudeDir = null!;

    [SetUp]
    public void SetUp()
    {
        _testClaudeDir = Path.Combine(Path.GetTempPath(), $"claude-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testClaudeDir);

        _sessionStore = new ClaudeSessionStore();
        _optionsFactory = new SessionOptionsFactory();
        _loggerMock = new Mock<ILogger<ClaudeSessionService>>();
        _hubContextMock = new Mock<IHubContext<Homespun.Features.ClaudeCode.Hubs.ClaudeCodeHub>>();
        _discoveryMock = new Mock<IClaudeSessionDiscovery>();
        _metadataStoreMock = new Mock<ISessionMetadataStore>();
        _toolResultParser = new ToolResultParser();

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
            _toolResultParser);
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
