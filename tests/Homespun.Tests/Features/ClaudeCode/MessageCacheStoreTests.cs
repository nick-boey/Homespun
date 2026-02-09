using Homespun.Features.ClaudeCode.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.ClaudeCode;

[TestFixture]
public class MessageCacheStoreTests
{
    private string _testDir = null!;
    private MessageCacheStore _store = null!;
    private Mock<ILogger<MessageCacheStore>> _loggerMock = null!;

    [SetUp]
    public void SetUp()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"message-cache-{Guid.NewGuid()}");
        _loggerMock = new Mock<ILogger<MessageCacheStore>>();
        _store = new MessageCacheStore(_testDir, _loggerMock.Object);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }
    }

    #region InitializeSessionAsync Tests

    [Test]
    public async Task InitializeSessionAsync_CreatesDirectoryStructure()
    {
        // Act
        await _store.InitializeSessionAsync("session-1", "entity-1", "project-1", SessionMode.Build, "sonnet");

        // Assert - directory exists
        Assert.That(Directory.Exists(Path.Combine(_testDir, "project-1")), Is.True);
    }

    [Test]
    public async Task InitializeSessionAsync_CreatesMetaFile()
    {
        // Act
        await _store.InitializeSessionAsync("session-1", "entity-1", "project-1", SessionMode.Build, "sonnet");

        // Assert
        var metaPath = Path.Combine(_testDir, "project-1", "session-1.meta.json");
        Assert.That(File.Exists(metaPath), Is.True);
    }

    [Test]
    public async Task InitializeSessionAsync_StoresCorrectMetadata()
    {
        // Arrange
        var mode = SessionMode.Plan;
        var model = "claude-opus-4";

        // Act
        await _store.InitializeSessionAsync("session-1", "entity-1", "project-1", mode, model);

        // Assert
        var summary = await _store.GetSessionSummaryAsync("session-1");
        Assert.That(summary, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(summary!.SessionId, Is.EqualTo("session-1"));
            Assert.That(summary.EntityId, Is.EqualTo("entity-1"));
            Assert.That(summary.ProjectId, Is.EqualTo("project-1"));
            Assert.That(summary.Mode, Is.EqualTo(mode));
            Assert.That(summary.Model, Is.EqualTo(model));
            Assert.That(summary.MessageCount, Is.EqualTo(0));
        });
    }

    #endregion

    #region AppendMessageAsync Tests

    [Test]
    public async Task AppendMessageAsync_AppendsToJsonlFile()
    {
        // Arrange
        await _store.InitializeSessionAsync("session-1", "entity-1", "project-1", SessionMode.Build, "sonnet");
        var message = CreateTestMessage("session-1", ClaudeMessageRole.User, "Hello");

        // Act
        await _store.AppendMessageAsync("session-1", message);

        // Assert
        var jsonlPath = Path.Combine(_testDir, "project-1", "session-1.jsonl");
        Assert.That(File.Exists(jsonlPath), Is.True);
        var lines = await File.ReadAllLinesAsync(jsonlPath);
        Assert.That(lines, Has.Length.EqualTo(1));
    }

    [Test]
    public async Task AppendMessageAsync_MultipleMessages_AppendsCorrectly()
    {
        // Arrange
        await _store.InitializeSessionAsync("session-1", "entity-1", "project-1", SessionMode.Build, "sonnet");
        var message1 = CreateTestMessage("session-1", ClaudeMessageRole.User, "Hello");
        var message2 = CreateTestMessage("session-1", ClaudeMessageRole.Assistant, "Hi there!");
        var message3 = CreateTestMessage("session-1", ClaudeMessageRole.User, "How are you?");

        // Act
        await _store.AppendMessageAsync("session-1", message1);
        await _store.AppendMessageAsync("session-1", message2);
        await _store.AppendMessageAsync("session-1", message3);

        // Assert
        var jsonlPath = Path.Combine(_testDir, "project-1", "session-1.jsonl");
        var lines = await File.ReadAllLinesAsync(jsonlPath);
        Assert.That(lines, Has.Length.EqualTo(3));
    }

    [Test]
    public async Task AppendMessageAsync_UpdatesMessageCount()
    {
        // Arrange
        await _store.InitializeSessionAsync("session-1", "entity-1", "project-1", SessionMode.Build, "sonnet");
        var message1 = CreateTestMessage("session-1", ClaudeMessageRole.User, "Hello");
        var message2 = CreateTestMessage("session-1", ClaudeMessageRole.Assistant, "Hi!");

        // Act
        await _store.AppendMessageAsync("session-1", message1);
        await _store.AppendMessageAsync("session-1", message2);

        // Assert
        var summary = await _store.GetSessionSummaryAsync("session-1");
        Assert.That(summary, Is.Not.Null);
        Assert.That(summary!.MessageCount, Is.EqualTo(2));
    }

    [Test]
    public async Task AppendMessageAsync_UpdatesLastMessageAt()
    {
        // Arrange
        await _store.InitializeSessionAsync("session-1", "entity-1", "project-1", SessionMode.Build, "sonnet");
        var summaryBefore = await _store.GetSessionSummaryAsync("session-1");
        var message = CreateTestMessage("session-1", ClaudeMessageRole.User, "Hello");

        // Act
        await Task.Delay(10); // Ensure time passes
        await _store.AppendMessageAsync("session-1", message);

        // Assert
        var summaryAfter = await _store.GetSessionSummaryAsync("session-1");
        Assert.That(summaryAfter!.LastMessageAt, Is.GreaterThan(summaryBefore!.LastMessageAt));
    }

    [Test]
    public async Task AppendMessageAsync_WithoutInitialize_ThrowsInvalidOperation()
    {
        // Arrange
        var message = CreateTestMessage("session-1", ClaudeMessageRole.User, "Hello");

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _store.AppendMessageAsync("session-1", message));
    }

    #endregion

    #region GetMessagesAsync Tests

    [Test]
    public async Task GetMessagesAsync_NoSession_ReturnsEmptyList()
    {
        // Act
        var result = await _store.GetMessagesAsync("nonexistent");

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetMessagesAsync_ReturnsMessagesInOrder()
    {
        // Arrange
        await _store.InitializeSessionAsync("session-1", "entity-1", "project-1", SessionMode.Build, "sonnet");
        var message1 = CreateTestMessage("session-1", ClaudeMessageRole.User, "First");
        var message2 = CreateTestMessage("session-1", ClaudeMessageRole.Assistant, "Second");
        var message3 = CreateTestMessage("session-1", ClaudeMessageRole.User, "Third");
        await _store.AppendMessageAsync("session-1", message1);
        await _store.AppendMessageAsync("session-1", message2);
        await _store.AppendMessageAsync("session-1", message3);

        // Act
        var result = await _store.GetMessagesAsync("session-1");

        // Assert
        Assert.That(result, Has.Count.EqualTo(3));
        Assert.Multiple(() =>
        {
            Assert.That(result[0].Content[0].Text, Is.EqualTo("First"));
            Assert.That(result[1].Content[0].Text, Is.EqualTo("Second"));
            Assert.That(result[2].Content[0].Text, Is.EqualTo("Third"));
        });
    }

    [Test]
    public async Task GetMessagesAsync_PreservesAllMessageFields()
    {
        // Arrange
        await _store.InitializeSessionAsync("session-1", "entity-1", "project-1", SessionMode.Build, "sonnet");
        var message = new ClaudeMessage
        {
            Id = "msg-123",
            SessionId = "session-1",
            Role = ClaudeMessageRole.Assistant,
            CreatedAt = new DateTime(2025, 1, 31, 12, 0, 0, DateTimeKind.Utc),
            Content = [
                new ClaudeMessageContent
                {
                    Type = ClaudeContentType.Text,
                    Text = "Hello world"
                },
                new ClaudeMessageContent
                {
                    Type = ClaudeContentType.ToolUse,
                    ToolName = "Read",
                    ToolInput = "{\"path\": \"/test\"}",
                    ToolUseId = "tool-1"
                }
            ]
        };
        await _store.AppendMessageAsync("session-1", message);

        // Act
        var result = await _store.GetMessagesAsync("session-1");

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        var retrieved = result[0];
        Assert.Multiple(() =>
        {
            Assert.That(retrieved.Id, Is.EqualTo("msg-123"));
            Assert.That(retrieved.SessionId, Is.EqualTo("session-1"));
            Assert.That(retrieved.Role, Is.EqualTo(ClaudeMessageRole.Assistant));
            Assert.That(retrieved.CreatedAt, Is.EqualTo(message.CreatedAt));
            Assert.That(retrieved.Content, Has.Count.EqualTo(2));
            Assert.That(retrieved.Content[0].Type, Is.EqualTo(ClaudeContentType.Text));
            Assert.That(retrieved.Content[0].Text, Is.EqualTo("Hello world"));
            Assert.That(retrieved.Content[1].Type, Is.EqualTo(ClaudeContentType.ToolUse));
            Assert.That(retrieved.Content[1].ToolName, Is.EqualTo("Read"));
            Assert.That(retrieved.Content[1].ToolUseId, Is.EqualTo("tool-1"));
        });
    }

    #endregion

    #region GetSessionSummaryAsync Tests

    [Test]
    public async Task GetSessionSummaryAsync_NoSession_ReturnsNull()
    {
        // Act
        var result = await _store.GetSessionSummaryAsync("nonexistent");

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetSessionSummaryAsync_ExistingSession_ReturnsSummary()
    {
        // Arrange
        await _store.InitializeSessionAsync("session-1", "entity-1", "project-1", SessionMode.Plan, "opus");

        // Act
        var result = await _store.GetSessionSummaryAsync("session-1");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!.SessionId, Is.EqualTo("session-1"));
            Assert.That(result.EntityId, Is.EqualTo("entity-1"));
            Assert.That(result.ProjectId, Is.EqualTo("project-1"));
            Assert.That(result.Mode, Is.EqualTo(SessionMode.Plan));
            Assert.That(result.Model, Is.EqualTo("opus"));
        });
    }

    #endregion

    #region ListSessionsAsync Tests

    [Test]
    public async Task ListSessionsAsync_NoSessions_ReturnsEmptyList()
    {
        // Act
        var result = await _store.ListSessionsAsync("project-1");

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task ListSessionsAsync_MultipleSessions_ReturnsAll()
    {
        // Arrange
        await _store.InitializeSessionAsync("session-1", "entity-1", "project-1", SessionMode.Build, "sonnet");
        await _store.InitializeSessionAsync("session-2", "entity-2", "project-1", SessionMode.Plan, "opus");
        await _store.InitializeSessionAsync("session-3", "entity-1", "project-2", SessionMode.Build, "sonnet"); // Different project

        // Act
        var result = await _store.ListSessionsAsync("project-1");

        // Assert
        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result.Select(s => s.SessionId), Contains.Item("session-1"));
        Assert.That(result.Select(s => s.SessionId), Contains.Item("session-2"));
        Assert.That(result.Select(s => s.SessionId), Does.Not.Contain("session-3"));
    }

    [Test]
    public async Task ListSessionsAsync_OrderedByLastMessageAtDescending()
    {
        // Arrange
        await _store.InitializeSessionAsync("session-1", "entity-1", "project-1", SessionMode.Build, "sonnet");
        await _store.AppendMessageAsync("session-1", CreateTestMessage("session-1", ClaudeMessageRole.User, "Old"));

        await Task.Delay(10);
        await _store.InitializeSessionAsync("session-2", "entity-2", "project-1", SessionMode.Build, "sonnet");
        await _store.AppendMessageAsync("session-2", CreateTestMessage("session-2", ClaudeMessageRole.User, "New"));

        // Act
        var result = await _store.ListSessionsAsync("project-1");

        // Assert - session-2 should be first (more recent)
        Assert.That(result[0].SessionId, Is.EqualTo("session-2"));
        Assert.That(result[1].SessionId, Is.EqualTo("session-1"));
    }

    #endregion

    #region GetSessionIdsForEntityAsync Tests

    [Test]
    public async Task GetSessionIdsForEntityAsync_NoMatches_ReturnsEmptyList()
    {
        // Act
        var result = await _store.GetSessionIdsForEntityAsync("project-1", "nonexistent");

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetSessionIdsForEntityAsync_MultipleSessions_ReturnsAll()
    {
        // Arrange
        await _store.InitializeSessionAsync("session-1", "entity-1", "project-1", SessionMode.Build, "sonnet");
        await _store.InitializeSessionAsync("session-2", "entity-1", "project-1", SessionMode.Plan, "opus");
        await _store.InitializeSessionAsync("session-3", "entity-2", "project-1", SessionMode.Build, "sonnet");

        // Act
        var result = await _store.GetSessionIdsForEntityAsync("project-1", "entity-1");

        // Assert
        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result, Contains.Item("session-1"));
        Assert.That(result, Contains.Item("session-2"));
        Assert.That(result, Does.Not.Contain("session-3"));
    }

    #endregion

    #region ExistsAsync Tests

    [Test]
    public async Task ExistsAsync_NoSession_ReturnsFalse()
    {
        // Act
        var result = await _store.ExistsAsync("nonexistent");

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task ExistsAsync_ExistingSession_ReturnsTrue()
    {
        // Arrange
        await _store.InitializeSessionAsync("session-1", "entity-1", "project-1", SessionMode.Build, "sonnet");

        // Act
        var result = await _store.ExistsAsync("session-1");

        // Assert
        Assert.That(result, Is.True);
    }

    #endregion

    #region Persistence Tests

    [Test]
    public async Task NewInstance_LoadsExistingSessionIndex()
    {
        // Arrange
        await _store.InitializeSessionAsync("session-1", "entity-1", "project-1", SessionMode.Build, "sonnet");
        await _store.AppendMessageAsync("session-1", CreateTestMessage("session-1", ClaudeMessageRole.User, "Hello"));

        // Act - create new instance pointing to same directory
        var newStore = new MessageCacheStore(_testDir, _loggerMock.Object);
        var result = await newStore.GetMessagesAsync("session-1");

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Content[0].Text, Is.EqualTo("Hello"));
    }

    [Test]
    public async Task NewInstance_LoadsSessionSummary()
    {
        // Arrange
        await _store.InitializeSessionAsync("session-1", "entity-1", "project-1", SessionMode.Plan, "opus");
        await _store.AppendMessageAsync("session-1", CreateTestMessage("session-1", ClaudeMessageRole.User, "Test"));

        // Act - create new instance
        var newStore = new MessageCacheStore(_testDir, _loggerMock.Object);
        var summary = await newStore.GetSessionSummaryAsync("session-1");

        // Assert
        Assert.That(summary, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(summary!.EntityId, Is.EqualTo("entity-1"));
            Assert.That(summary.Mode, Is.EqualTo(SessionMode.Plan));
            Assert.That(summary.MessageCount, Is.EqualTo(1));
        });
    }

    #endregion

    #region Thread Safety Tests

    [Test]
    public async Task ConcurrentAppends_AllMessagesStored()
    {
        // Arrange
        await _store.InitializeSessionAsync("session-1", "entity-1", "project-1", SessionMode.Build, "sonnet");
        var tasks = new List<Task>();

        // Act - append 10 messages concurrently
        for (int i = 0; i < 10; i++)
        {
            var message = CreateTestMessage("session-1", ClaudeMessageRole.User, $"Message {i}");
            tasks.Add(_store.AppendMessageAsync("session-1", message));
        }
        await Task.WhenAll(tasks);

        // Assert
        var result = await _store.GetMessagesAsync("session-1");
        Assert.That(result, Has.Count.EqualTo(10));
    }

    #endregion

    #region Helper Methods

    private static ClaudeMessage CreateTestMessage(string sessionId, ClaudeMessageRole role, string text)
    {
        return new ClaudeMessage
        {
            SessionId = sessionId,
            Role = role,
            Content = [new ClaudeMessageContent { Type = ClaudeContentType.Text, Text = text }]
        };
    }

    #endregion
}
