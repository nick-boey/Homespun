using Homespun.Features.ClaudeCode.Data;
using Homespun.Features.Testing.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.Testing;

[TestFixture]
public class JsonlSessionLoaderTests
{
    private string _testDir = null!;
    private JsonlSessionLoader _loader = null!;
    private Mock<ILogger<JsonlSessionLoader>> _loggerMock = null!;

    [SetUp]
    public void SetUp()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"jsonl-session-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDir);
        _loggerMock = new Mock<ILogger<JsonlSessionLoader>>();
        _loader = new JsonlSessionLoader(_loggerMock.Object);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }
    }

    #region LoadMessagesAsync Tests

    [Test]
    public async Task LoadMessagesAsync_EmptyFile_ReturnsEmptyList()
    {
        // Arrange
        var filePath = Path.Combine(_testDir, "empty.jsonl");
        await File.WriteAllTextAsync(filePath, "");

        // Act
        var result = await _loader.LoadMessagesAsync(filePath);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task LoadMessagesAsync_SingleMessage_ReturnsOneMessage()
    {
        // Arrange
        var filePath = Path.Combine(_testDir, "single.jsonl");
        var json = """{"id":"msg-1","sessionId":"session-1","role":0,"content":[{"type":0,"text":"Hello"}],"createdAt":"2026-01-01T00:00:00Z","isStreaming":false}""";
        await File.WriteAllTextAsync(filePath, json);

        // Act
        var result = await _loader.LoadMessagesAsync(filePath);

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Id, Is.EqualTo("msg-1"));
        Assert.That(result[0].Role, Is.EqualTo(ClaudeMessageRole.User));
    }

    [Test]
    public async Task LoadMessagesAsync_MultipleMessages_ReturnsAll()
    {
        // Arrange
        var filePath = Path.Combine(_testDir, "multiple.jsonl");
        var lines = new[]
        {
            """{"id":"msg-1","sessionId":"s1","role":0,"content":[{"type":0,"text":"Hello"}],"createdAt":"2026-01-01T00:00:00Z","isStreaming":false}""",
            """{"id":"msg-2","sessionId":"s1","role":1,"content":[{"type":0,"text":"Hi there!"}],"createdAt":"2026-01-01T00:00:01Z","isStreaming":false}""",
            """{"id":"msg-3","sessionId":"s1","role":0,"content":[{"type":0,"text":"How are you?"}],"createdAt":"2026-01-01T00:00:02Z","isStreaming":false}"""
        };
        await File.WriteAllLinesAsync(filePath, lines);

        // Act
        var result = await _loader.LoadMessagesAsync(filePath);

        // Assert
        Assert.That(result, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task LoadMessagesAsync_SkipsEmptyLines()
    {
        // Arrange
        var filePath = Path.Combine(_testDir, "with-blanks.jsonl");
        var content = """
            {"id":"msg-1","sessionId":"s1","role":0,"content":[{"type":0,"text":"Hello"}],"createdAt":"2026-01-01T00:00:00Z","isStreaming":false}

            {"id":"msg-2","sessionId":"s1","role":1,"content":[{"type":0,"text":"Hi!"}],"createdAt":"2026-01-01T00:00:01Z","isStreaming":false}

            """;
        await File.WriteAllTextAsync(filePath, content);

        // Act
        var result = await _loader.LoadMessagesAsync(filePath);

        // Assert
        Assert.That(result, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task LoadMessagesAsync_SkipsInvalidJson()
    {
        // Arrange
        var filePath = Path.Combine(_testDir, "with-invalid.jsonl");
        var lines = new[]
        {
            """{"id":"msg-1","sessionId":"s1","role":0,"content":[{"type":0,"text":"Valid"}],"createdAt":"2026-01-01T00:00:00Z","isStreaming":false}""",
            """not valid json at all""",
            """{"id":"msg-2","sessionId":"s1","role":1,"content":[{"type":0,"text":"Also valid"}],"createdAt":"2026-01-01T00:00:01Z","isStreaming":false}"""
        };
        await File.WriteAllLinesAsync(filePath, lines);

        // Act
        var result = await _loader.LoadMessagesAsync(filePath);

        // Assert
        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result[0].Id, Is.EqualTo("msg-1"));
        Assert.That(result[1].Id, Is.EqualTo("msg-2"));
    }

    [Test]
    public async Task LoadMessagesAsync_ParsesAllContentTypes()
    {
        // Arrange
        var filePath = Path.Combine(_testDir, "content-types.jsonl");
        var json = """{"id":"msg-1","sessionId":"s1","role":1,"content":[{"type":1,"text":"Thinking..."},{"type":0,"text":"Response"},{"type":2,"toolName":"Read","toolUseId":"tool-1"}],"createdAt":"2026-01-01T00:00:00Z","isStreaming":false}""";
        await File.WriteAllTextAsync(filePath, json);

        // Act
        var result = await _loader.LoadMessagesAsync(filePath);

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Content, Has.Count.EqualTo(3));
        Assert.Multiple(() =>
        {
            Assert.That(result[0].Content[0].Type, Is.EqualTo(ClaudeContentType.Thinking));
            Assert.That(result[0].Content[1].Type, Is.EqualTo(ClaudeContentType.Text));
            Assert.That(result[0].Content[2].Type, Is.EqualTo(ClaudeContentType.ToolUse));
            Assert.That(result[0].Content[2].ToolName, Is.EqualTo("Read"));
        });
    }

    [Test]
    public async Task LoadMessagesAsync_ParsesToolResultWithParsedData()
    {
        // Arrange
        var filePath = Path.Combine(_testDir, "tool-result.jsonl");
        var json = """{"id":"msg-1","sessionId":"s1","role":0,"content":[{"type":3,"toolName":"Read","toolUseId":"tool-1","toolSuccess":true,"text":"file content"}],"createdAt":"2026-01-01T00:00:00Z","isStreaming":false}""";
        await File.WriteAllTextAsync(filePath, json);

        // Act
        var result = await _loader.LoadMessagesAsync(filePath);

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        var content = result[0].Content[0];
        Assert.Multiple(() =>
        {
            Assert.That(content.Type, Is.EqualTo(ClaudeContentType.ToolResult));
            Assert.That(content.ToolName, Is.EqualTo("Read"));
            Assert.That(content.ToolSuccess, Is.True);
            Assert.That(content.Text, Is.EqualTo("file content"));
        });
    }

    #endregion

    #region LoadSessionFromDirectoryAsync Tests

    [Test]
    public async Task LoadSessionFromDirectoryAsync_NoJsonlFiles_ReturnsNull()
    {
        // Arrange
        var sessionDir = Path.Combine(_testDir, "empty-session");
        Directory.CreateDirectory(sessionDir);

        // Act
        var result = await _loader.LoadSessionFromDirectoryAsync(sessionDir);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task LoadSessionFromDirectoryAsync_WithJsonlFile_ReturnsSession()
    {
        // Arrange
        var sessionDir = Path.Combine(_testDir, "test-project");
        Directory.CreateDirectory(sessionDir);
        var jsonlPath = Path.Combine(sessionDir, "session-123.jsonl");
        var lines = new[]
        {
            """{"id":"msg-1","sessionId":"session-123","role":0,"content":[{"type":0,"text":"Hello"}],"createdAt":"2026-01-01T00:00:00Z","isStreaming":false}""",
            """{"id":"msg-2","sessionId":"session-123","role":1,"content":[{"type":0,"text":"Hi!"}],"createdAt":"2026-01-01T00:00:01Z","isStreaming":false}"""
        };
        await File.WriteAllLinesAsync(jsonlPath, lines);

        // Act
        var result = await _loader.LoadSessionFromDirectoryAsync(sessionDir);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Id, Is.EqualTo("session-123"));
        Assert.That(result.Messages, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task LoadSessionFromDirectoryAsync_WithMetaFile_LoadsMetadata()
    {
        // Arrange
        var sessionDir = Path.Combine(_testDir, "test-project");
        Directory.CreateDirectory(sessionDir);
        var sessionId = "session-abc";

        // Create JSONL file
        var jsonlPath = Path.Combine(sessionDir, $"{sessionId}.jsonl");
        await File.WriteAllTextAsync(jsonlPath,
            """{"id":"msg-1","sessionId":"session-abc","role":0,"content":[{"type":0,"text":"Test"}],"createdAt":"2026-01-01T00:00:00Z","isStreaming":false}""");

        // Create meta file
        var metaPath = Path.Combine(sessionDir, $"{sessionId}.meta.json");
        var metaJson = """
        {
            "sessionId": "session-abc",
            "entityId": "entity-1",
            "projectId": "project-1",
            "messageCount": 1,
            "createdAt": "2026-01-01T00:00:00Z",
            "lastMessageAt": "2026-01-01T00:00:00Z",
            "mode": 1,
            "model": "opus"
        }
        """;
        await File.WriteAllTextAsync(metaPath, metaJson);

        // Act
        var result = await _loader.LoadSessionFromDirectoryAsync(sessionDir);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!.Id, Is.EqualTo("session-abc"));
            Assert.That(result.EntityId, Is.EqualTo("entity-1"));
            Assert.That(result.ProjectId, Is.EqualTo("project-1"));
            Assert.That(result.Mode, Is.EqualTo(SessionMode.Build));
            Assert.That(result.Model, Is.EqualTo("opus"));
        });
    }

    #endregion

    #region LoadAllSessionsAsync Tests

    [Test]
    public async Task LoadAllSessionsAsync_NoProjectDirectories_ReturnsEmptyList()
    {
        // Act
        var result = await _loader.LoadAllSessionsAsync(_testDir);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task LoadAllSessionsAsync_MultipleProjects_ReturnsAllSessions()
    {
        // Arrange
        var project1Dir = Path.Combine(_testDir, "project-1");
        var project2Dir = Path.Combine(_testDir, "project-2");
        Directory.CreateDirectory(project1Dir);
        Directory.CreateDirectory(project2Dir);

        // Create session in project 1
        await File.WriteAllTextAsync(
            Path.Combine(project1Dir, "session-1.jsonl"),
            """{"id":"msg-1","sessionId":"session-1","role":0,"content":[{"type":0,"text":"P1 message"}],"createdAt":"2026-01-01T00:00:00Z","isStreaming":false}""");

        // Create session in project 2
        await File.WriteAllTextAsync(
            Path.Combine(project2Dir, "session-2.jsonl"),
            """{"id":"msg-2","sessionId":"session-2","role":0,"content":[{"type":0,"text":"P2 message"}],"createdAt":"2026-01-01T00:00:01Z","isStreaming":false}""");

        // Act
        var result = await _loader.LoadAllSessionsAsync(_testDir);

        // Assert
        Assert.That(result, Has.Count.EqualTo(2));
        var sessionIds = result.Select(s => s.Id).ToList();
        Assert.That(sessionIds, Contains.Item("session-1"));
        Assert.That(sessionIds, Contains.Item("session-2"));
    }

    [Test]
    public async Task LoadAllSessionsAsync_ProjectWithMultipleSessions_ReturnsAll()
    {
        // Arrange
        var projectDir = Path.Combine(_testDir, "multi-session-project");
        Directory.CreateDirectory(projectDir);

        // Create multiple session files
        await File.WriteAllTextAsync(
            Path.Combine(projectDir, "session-a.jsonl"),
            """{"id":"msg-a","sessionId":"session-a","role":0,"content":[{"type":0,"text":"A"}],"createdAt":"2026-01-01T00:00:00Z","isStreaming":false}""");

        await File.WriteAllTextAsync(
            Path.Combine(projectDir, "session-b.jsonl"),
            """{"id":"msg-b","sessionId":"session-b","role":0,"content":[{"type":0,"text":"B"}],"createdAt":"2026-01-01T00:00:01Z","isStreaming":false}""");

        // Act
        var result = await _loader.LoadAllSessionsAsync(_testDir);

        // Assert
        Assert.That(result, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task LoadAllSessionsAsync_IgnoresNonJsonlFiles()
    {
        // Arrange
        var projectDir = Path.Combine(_testDir, "project-with-extras");
        Directory.CreateDirectory(projectDir);

        // Create valid session
        await File.WriteAllTextAsync(
            Path.Combine(projectDir, "session-1.jsonl"),
            """{"id":"msg-1","sessionId":"session-1","role":0,"content":[{"type":0,"text":"Valid"}],"createdAt":"2026-01-01T00:00:00Z","isStreaming":false}""");

        // Create non-JSONL files that should be ignored
        await File.WriteAllTextAsync(Path.Combine(projectDir, "session-1.meta.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(projectDir, "README.md"), "# Notes");
        await File.WriteAllTextAsync(Path.Combine(projectDir, "index.json"), "{}");

        // Act
        var result = await _loader.LoadAllSessionsAsync(_testDir);

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Id, Is.EqualTo("session-1"));
    }

    #endregion

    #region Session Creation Tests

    [Test]
    public async Task LoadSessionFromDirectoryAsync_SetsDefaultsWhenNoMetaFile()
    {
        // Arrange
        var sessionDir = Path.Combine(_testDir, "no-meta-project");
        Directory.CreateDirectory(sessionDir);
        await File.WriteAllTextAsync(
            Path.Combine(sessionDir, "session-xyz.jsonl"),
            """{"id":"msg-1","sessionId":"session-xyz","role":0,"content":[{"type":0,"text":"Test"}],"createdAt":"2026-01-01T00:00:00Z","isStreaming":false}""");

        // Act
        var result = await _loader.LoadSessionFromDirectoryAsync(sessionDir);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!.Id, Is.EqualTo("session-xyz"));
            Assert.That(result.Mode, Is.EqualTo(SessionMode.Build)); // Default
            Assert.That(result.Status, Is.EqualTo(ClaudeSessionStatus.WaitingForInput));
        });
    }

    [Test]
    public async Task LoadSessionFromDirectoryAsync_InfersProjectIdFromDirectoryName()
    {
        // Arrange
        var projectDir = Path.Combine(_testDir, "my-cool-project");
        Directory.CreateDirectory(projectDir);
        await File.WriteAllTextAsync(
            Path.Combine(projectDir, "session-1.jsonl"),
            """{"id":"msg-1","sessionId":"session-1","role":0,"content":[{"type":0,"text":"Test"}],"createdAt":"2026-01-01T00:00:00Z","isStreaming":false}""");

        // Act
        var result = await _loader.LoadSessionFromDirectoryAsync(projectDir);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ProjectId, Is.EqualTo("my-cool-project"));
    }

    #endregion
}
