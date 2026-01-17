using Homespun.Features.ClaudeCode.Data;
using Homespun.Features.ClaudeCode.Services;

namespace Homespun.Tests.Features.ClaudeCode;

[TestFixture]
public class ClaudeSessionStoreTests
{
    private ClaudeSessionStore _store = null!;

    [SetUp]
    public void SetUp()
    {
        _store = new ClaudeSessionStore();
    }

    [Test]
    public void AddSession_ValidSession_AddsToStore()
    {
        // Arrange
        var session = CreateTestSession("session-1", "entity-1");

        // Act
        _store.Add(session);

        // Assert
        var retrieved = _store.GetById(session.Id);
        Assert.That(retrieved, Is.EqualTo(session));
    }

    [Test]
    public void GetById_NonExistentSession_ReturnsNull()
    {
        // Act
        var result = _store.GetById("non-existent");

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void GetByEntityId_ExistingEntity_ReturnsSession()
    {
        // Arrange
        var session = CreateTestSession("session-1", "entity-1");
        _store.Add(session);

        // Act
        var result = _store.GetByEntityId("entity-1");

        // Assert
        Assert.That(result, Is.EqualTo(session));
    }

    [Test]
    public void GetByEntityId_NonExistentEntity_ReturnsNull()
    {
        // Arrange
        var session = CreateTestSession("session-1", "entity-1");
        _store.Add(session);

        // Act
        var result = _store.GetByEntityId("entity-2");

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void Remove_ExistingSession_RemovesFromStore()
    {
        // Arrange
        var session = CreateTestSession("session-1", "entity-1");
        _store.Add(session);

        // Act
        var removed = _store.Remove(session.Id);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(removed, Is.True);
            Assert.That(_store.GetById(session.Id), Is.Null);
        });
    }

    [Test]
    public void Remove_NonExistentSession_ReturnsFalse()
    {
        // Act
        var removed = _store.Remove("non-existent");

        // Assert
        Assert.That(removed, Is.False);
    }

    [Test]
    public void GetAll_ReturnsCopyOfSessions()
    {
        // Arrange
        var session1 = CreateTestSession("session-1", "entity-1");
        var session2 = CreateTestSession("session-2", "entity-2");
        _store.Add(session1);
        _store.Add(session2);

        // Act
        var all = _store.GetAll();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(all, Has.Count.EqualTo(2));
            Assert.That(all, Does.Contain(session1));
            Assert.That(all, Does.Contain(session2));
        });
    }

    [Test]
    public void GetByProjectId_ReturnsSessionsForProject()
    {
        // Arrange
        var session1 = CreateTestSession("session-1", "entity-1", "project-1");
        var session2 = CreateTestSession("session-2", "entity-2", "project-1");
        var session3 = CreateTestSession("session-3", "entity-3", "project-2");
        _store.Add(session1);
        _store.Add(session2);
        _store.Add(session3);

        // Act
        var result = _store.GetByProjectId("project-1");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result, Does.Contain(session1));
            Assert.That(result, Does.Contain(session2));
            Assert.That(result, Does.Not.Contain(session3));
        });
    }

    [Test]
    public void Update_ExistingSession_UpdatesInStore()
    {
        // Arrange
        var session = CreateTestSession("session-1", "entity-1");
        _store.Add(session);

        session.Status = ClaudeSessionStatus.Running;

        // Act
        var updated = _store.Update(session);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(updated, Is.True);
            var retrieved = _store.GetById(session.Id);
            Assert.That(retrieved?.Status, Is.EqualTo(ClaudeSessionStatus.Running));
        });
    }

    [Test]
    public void Update_NonExistentSession_ReturnsFalse()
    {
        // Arrange
        var session = CreateTestSession("session-1", "entity-1");

        // Act
        var updated = _store.Update(session);

        // Assert
        Assert.That(updated, Is.False);
    }

    [Test]
    public void AddSession_DuplicateId_OverwritesExisting()
    {
        // Arrange
        var session1 = CreateTestSession("session-1", "entity-1");
        var session2 = CreateTestSession("session-1", "entity-2");
        _store.Add(session1);

        // Act
        _store.Add(session2);

        // Assert
        var retrieved = _store.GetById("session-1");
        Assert.That(retrieved?.EntityId, Is.EqualTo("entity-2"));
    }

    private static ClaudeSession CreateTestSession(string id, string entityId, string projectId = "project-1")
    {
        return new ClaudeSession
        {
            Id = id,
            EntityId = entityId,
            ProjectId = projectId,
            Mode = SessionMode.Build,
            WorkingDirectory = "/test/path",
            Model = "claude-sonnet-4-20250514",
            Status = ClaudeSessionStatus.Starting,
            CreatedAt = DateTime.UtcNow
        };
    }
}
