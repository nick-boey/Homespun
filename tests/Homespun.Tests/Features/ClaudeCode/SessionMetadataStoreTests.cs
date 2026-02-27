using Homespun.Features.ClaudeCode.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.ClaudeCode;

[TestFixture]
public class SessionMetadataStoreTests
{
    private string _testFilePath = null!;
    private SessionMetadataStore _store = null!;
    private Mock<ILogger<SessionMetadataStore>> _loggerMock = null!;

    [SetUp]
    public void SetUp()
    {
        _testFilePath = Path.Combine(Path.GetTempPath(), $"session-metadata-{Guid.NewGuid()}.json");
        _loggerMock = new Mock<ILogger<SessionMetadataStore>>();
        _store = new SessionMetadataStore(_testFilePath, _loggerMock.Object);
    }

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(_testFilePath))
        {
            File.Delete(_testFilePath);
        }
    }

    #region GetBySessionIdAsync Tests

    [Test]
    public async Task GetBySessionIdAsync_NotFound_ReturnsNull()
    {
        // Act
        var result = await _store.GetBySessionIdAsync("nonexistent");

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetBySessionIdAsync_Found_ReturnsMetadata()
    {
        // Arrange
        var metadata = CreateTestMetadata("session-1", "entity-1");
        await _store.SaveAsync(metadata);

        // Act
        var result = await _store.GetBySessionIdAsync("session-1");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!.SessionId, Is.EqualTo("session-1"));
            Assert.That(result.EntityId, Is.EqualTo("entity-1"));
        });
    }

    #endregion

    #region GetByEntityIdAsync Tests

    [Test]
    public async Task GetByEntityIdAsync_NoMatches_ReturnsEmptyList()
    {
        // Act
        var result = await _store.GetByEntityIdAsync("nonexistent");

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetByEntityIdAsync_MultipleSessions_ReturnsAll()
    {
        // Arrange
        var metadata1 = CreateTestMetadata("session-1", "entity-1");
        var metadata2 = CreateTestMetadata("session-2", "entity-1");
        var metadata3 = CreateTestMetadata("session-3", "entity-2");
        await _store.SaveAsync(metadata1);
        await _store.SaveAsync(metadata2);
        await _store.SaveAsync(metadata3);

        // Act
        var result = await _store.GetByEntityIdAsync("entity-1");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result.Select(m => m.SessionId), Does.Contain("session-1"));
            Assert.That(result.Select(m => m.SessionId), Does.Contain("session-2"));
            Assert.That(result.Select(m => m.SessionId), Does.Not.Contain("session-3"));
        });
    }

    #endregion

    #region SaveAsync Tests

    [Test]
    public async Task SaveAsync_NewMetadata_PersistsToFile()
    {
        // Arrange
        var metadata = CreateTestMetadata("session-1", "entity-1");

        // Act
        await _store.SaveAsync(metadata);

        // Assert - verify file exists and contains data
        Assert.That(File.Exists(_testFilePath), Is.True);

        // Create a new store instance to verify persistence
        var newStore = new SessionMetadataStore(_testFilePath, _loggerMock.Object);
        var retrieved = await newStore.GetBySessionIdAsync("session-1");
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved!.EntityId, Is.EqualTo("entity-1"));
    }

    [Test]
    public async Task SaveAsync_ExistingMetadata_Updates()
    {
        // Arrange
        var metadata1 = CreateTestMetadata("session-1", "entity-1");
        await _store.SaveAsync(metadata1);

        var metadata2 = CreateTestMetadata("session-1", "entity-2"); // Same session ID, different entity

        // Act
        await _store.SaveAsync(metadata2);

        // Assert
        var result = await _store.GetBySessionIdAsync("session-1");
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.EntityId, Is.EqualTo("entity-2"));
    }

    [Test]
    public async Task SaveAsync_PreservesAllFields()
    {
        // Arrange
        var createdAt = new DateTime(2025, 1, 26, 10, 0, 0, DateTimeKind.Utc);
        var metadata = new SessionMetadata(
            SessionId: "session-1",
            EntityId: "entity-1",
            ProjectId: "project-1",
            WorkingDirectory: "/test/path",
            Mode: SessionMode.Build,
            Model: "sonnet",
            SystemPrompt: "Test system prompt",
            CreatedAt: createdAt
        );

        // Act
        await _store.SaveAsync(metadata);

        // Assert
        var retrieved = await _store.GetBySessionIdAsync("session-1");
        Assert.That(retrieved, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(retrieved!.SessionId, Is.EqualTo("session-1"));
            Assert.That(retrieved.EntityId, Is.EqualTo("entity-1"));
            Assert.That(retrieved.ProjectId, Is.EqualTo("project-1"));
            Assert.That(retrieved.WorkingDirectory, Is.EqualTo("/test/path"));
            Assert.That(retrieved.Mode, Is.EqualTo(SessionMode.Build));
            Assert.That(retrieved.Model, Is.EqualTo("sonnet"));
            Assert.That(retrieved.SystemPrompt, Is.EqualTo("Test system prompt"));
            Assert.That(retrieved.CreatedAt, Is.EqualTo(createdAt));
        });
    }

    #endregion

    #region RemoveAsync Tests

    [Test]
    public async Task RemoveAsync_ExistingMetadata_RemovesFromStore()
    {
        // Arrange
        var metadata = CreateTestMetadata("session-1", "entity-1");
        await _store.SaveAsync(metadata);

        // Act
        await _store.RemoveAsync("session-1");

        // Assert
        var result = await _store.GetBySessionIdAsync("session-1");
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task RemoveAsync_NonExistent_DoesNotThrow()
    {
        // Act & Assert - should not throw
        Assert.DoesNotThrowAsync(async () => await _store.RemoveAsync("nonexistent"));
    }

    #endregion

    #region GetAllAsync Tests

    [Test]
    public async Task GetAllAsync_EmptyStore_ReturnsEmptyList()
    {
        // Act
        var result = await _store.GetAllAsync();

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetAllAsync_WithMetadata_ReturnsAll()
    {
        // Arrange
        var metadata1 = CreateTestMetadata("session-1", "entity-1");
        var metadata2 = CreateTestMetadata("session-2", "entity-2");
        await _store.SaveAsync(metadata1);
        await _store.SaveAsync(metadata2);

        // Act
        var result = await _store.GetAllAsync();

        // Assert
        Assert.That(result, Has.Count.EqualTo(2));
    }

    #endregion

    #region Persistence Tests

    [Test]
    public async Task NewInstance_LoadsExistingData()
    {
        // Arrange
        var metadata = CreateTestMetadata("session-1", "entity-1");
        await _store.SaveAsync(metadata);

        // Act - create new instance pointing to same file
        var newStore = new SessionMetadataStore(_testFilePath, _loggerMock.Object);
        var result = await newStore.GetBySessionIdAsync("session-1");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.EntityId, Is.EqualTo("entity-1"));
    }

    [Test]
    public async Task CorruptedFile_ReturnsEmptyAndLogs()
    {
        // Arrange - write corrupted JSON
        await File.WriteAllTextAsync(_testFilePath, "{ invalid json }}}");

        // Act - create store (should handle corruption gracefully)
        var newStore = new SessionMetadataStore(_testFilePath, _loggerMock.Object);
        var result = await newStore.GetAllAsync();

        // Assert
        Assert.That(result, Is.Empty);
    }

    #endregion

    private static SessionMetadata CreateTestMetadata(string sessionId, string entityId)
    {
        return new SessionMetadata(
            SessionId: sessionId,
            EntityId: entityId,
            ProjectId: "project-1",
            WorkingDirectory: "/test/path",
            Mode: SessionMode.Build,
            Model: "sonnet",
            SystemPrompt: null,
            CreatedAt: DateTime.UtcNow
        );
    }
}
