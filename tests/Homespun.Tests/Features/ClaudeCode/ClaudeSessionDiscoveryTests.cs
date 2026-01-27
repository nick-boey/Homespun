using Homespun.Features.ClaudeCode.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.ClaudeCode;

[TestFixture]
public class ClaudeSessionDiscoveryTests
{
    private string _testClaudeDir = null!;
    private ClaudeSessionDiscovery _discovery = null!;
    private Mock<ILogger<ClaudeSessionDiscovery>> _loggerMock = null!;

    [SetUp]
    public void SetUp()
    {
        _testClaudeDir = Path.Combine(Path.GetTempPath(), $"claude-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testClaudeDir);

        _loggerMock = new Mock<ILogger<ClaudeSessionDiscovery>>();
        _discovery = new ClaudeSessionDiscovery(_testClaudeDir, _loggerMock.Object);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testClaudeDir))
        {
            Directory.Delete(_testClaudeDir, recursive: true);
        }
    }

    #region EncodePath Tests

    [Test]
    public void EncodePath_UnixPath_ReplacesSlashesWithHyphens()
    {
        // Arrange
        var path = "/home/user/project";

        // Act
        var encoded = ClaudeSessionDiscovery.EncodePath(path);

        // Assert
        Assert.That(encoded, Is.EqualTo("-home-user-project"));
    }

    [Test]
    public void EncodePath_WindowsPath_ReplacesBackslashesWithHyphens()
    {
        // Arrange
        var path = @"C:\Users\user\project";

        // Act
        var encoded = ClaudeSessionDiscovery.EncodePath(path);

        // Assert
        // Note: Claude only replaces path separators (/ and \), not colons
        Assert.That(encoded, Is.EqualTo("C:-Users-user-project"));
    }

    [Test]
    public void EncodePath_MixedPath_ReplacesAllSeparators()
    {
        // Arrange
        var path = @"/home/user\mixed/path";

        // Act
        var encoded = ClaudeSessionDiscovery.EncodePath(path);

        // Assert
        Assert.That(encoded, Is.EqualTo("-home-user-mixed-path"));
    }

    [Test]
    public void EncodePath_EmptyPath_ReturnsEmpty()
    {
        // Act
        var encoded = ClaudeSessionDiscovery.EncodePath("");

        // Assert
        Assert.That(encoded, Is.EqualTo(""));
    }

    #endregion

    #region DiscoverSessionsAsync Tests

    [Test]
    public async Task DiscoverSessionsAsync_NoProjectDirectory_ReturnsEmptyList()
    {
        // Arrange - don't create the project directory

        // Act
        var sessions = await _discovery.DiscoverSessionsAsync("/home/user/nonexistent");

        // Assert
        Assert.That(sessions, Is.Empty);
    }

    [Test]
    public async Task DiscoverSessionsAsync_EmptyDirectory_ReturnsEmptyList()
    {
        // Arrange
        var workingDir = "/home/user/project";
        var projectDir = Path.Combine(_testClaudeDir, ClaudeSessionDiscovery.EncodePath(workingDir));
        Directory.CreateDirectory(projectDir);

        // Act
        var sessions = await _discovery.DiscoverSessionsAsync(workingDir);

        // Assert
        Assert.That(sessions, Is.Empty);
    }

    [Test]
    public async Task DiscoverSessionsAsync_WithSessions_ReturnsAllJsonlFiles()
    {
        // Arrange
        var workingDir = "/home/user/project";
        var projectDir = Path.Combine(_testClaudeDir, ClaudeSessionDiscovery.EncodePath(workingDir));
        Directory.CreateDirectory(projectDir);

        var session1 = Guid.NewGuid().ToString();
        var session2 = Guid.NewGuid().ToString();
        await File.WriteAllTextAsync(Path.Combine(projectDir, $"{session1}.jsonl"), "{}");
        await File.WriteAllTextAsync(Path.Combine(projectDir, $"{session2}.jsonl"), "{}");
        // Add a non-jsonl file that should be ignored
        await File.WriteAllTextAsync(Path.Combine(projectDir, "other.txt"), "ignored");

        // Act
        var sessions = await _discovery.DiscoverSessionsAsync(workingDir);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(sessions, Has.Count.EqualTo(2));
            Assert.That(sessions.Select(s => s.SessionId), Does.Contain(session1));
            Assert.That(sessions.Select(s => s.SessionId), Does.Contain(session2));
        });
    }

    [Test]
    public async Task DiscoverSessionsAsync_OrdersByLastModifiedDescending()
    {
        // Arrange
        var workingDir = "/home/user/project";
        var projectDir = Path.Combine(_testClaudeDir, ClaudeSessionDiscovery.EncodePath(workingDir));
        Directory.CreateDirectory(projectDir);

        var olderSession = Guid.NewGuid().ToString();
        var newerSession = Guid.NewGuid().ToString();

        var olderPath = Path.Combine(projectDir, $"{olderSession}.jsonl");
        var newerPath = Path.Combine(projectDir, $"{newerSession}.jsonl");

        await File.WriteAllTextAsync(olderPath, "{}");
        await Task.Delay(50); // Ensure different timestamps
        await File.WriteAllTextAsync(newerPath, "{}");

        // Act
        var sessions = await _discovery.DiscoverSessionsAsync(workingDir);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(sessions, Has.Count.EqualTo(2));
            Assert.That(sessions[0].SessionId, Is.EqualTo(newerSession), "Newest should be first");
            Assert.That(sessions[1].SessionId, Is.EqualTo(olderSession), "Oldest should be last");
        });
    }

    [Test]
    public async Task DiscoverSessionsAsync_PopulatesAllFields()
    {
        // Arrange
        var workingDir = "/home/user/project";
        var projectDir = Path.Combine(_testClaudeDir, ClaudeSessionDiscovery.EncodePath(workingDir));
        Directory.CreateDirectory(projectDir);

        var sessionId = Guid.NewGuid().ToString();
        var filePath = Path.Combine(projectDir, $"{sessionId}.jsonl");
        var content = "{\"message\": \"test\"}";
        await File.WriteAllTextAsync(filePath, content);

        // Act
        var sessions = await _discovery.DiscoverSessionsAsync(workingDir);

        // Assert
        Assert.That(sessions, Has.Count.EqualTo(1));
        var session = sessions[0];
        Assert.Multiple(() =>
        {
            Assert.That(session.SessionId, Is.EqualTo(sessionId));
            Assert.That(session.FilePath, Is.EqualTo(filePath));
            Assert.That(session.FileSize, Is.EqualTo(content.Length));
            Assert.That(session.LastModified, Is.EqualTo(File.GetLastWriteTimeUtc(filePath)));
        });
    }

    #endregion

    #region SessionExists Tests

    [Test]
    public void SessionExists_ExistingSession_ReturnsTrue()
    {
        // Arrange
        var workingDir = "/home/user/project";
        var projectDir = Path.Combine(_testClaudeDir, ClaudeSessionDiscovery.EncodePath(workingDir));
        Directory.CreateDirectory(projectDir);

        var sessionId = Guid.NewGuid().ToString();
        File.WriteAllText(Path.Combine(projectDir, $"{sessionId}.jsonl"), "{}");

        // Act
        var exists = _discovery.SessionExists(sessionId, workingDir);

        // Assert
        Assert.That(exists, Is.True);
    }

    [Test]
    public void SessionExists_NonExistentSession_ReturnsFalse()
    {
        // Arrange
        var workingDir = "/home/user/project";
        var projectDir = Path.Combine(_testClaudeDir, ClaudeSessionDiscovery.EncodePath(workingDir));
        Directory.CreateDirectory(projectDir);

        // Act
        var exists = _discovery.SessionExists("nonexistent", workingDir);

        // Assert
        Assert.That(exists, Is.False);
    }

    [Test]
    public void SessionExists_NonExistentDirectory_ReturnsFalse()
    {
        // Act
        var exists = _discovery.SessionExists("any-session", "/nonexistent/path");

        // Assert
        Assert.That(exists, Is.False);
    }

    #endregion

    #region GetSessionFilePath Tests

    [Test]
    public void GetSessionFilePath_ExistingSession_ReturnsPath()
    {
        // Arrange
        var workingDir = "/home/user/project";
        var projectDir = Path.Combine(_testClaudeDir, ClaudeSessionDiscovery.EncodePath(workingDir));
        Directory.CreateDirectory(projectDir);

        var sessionId = Guid.NewGuid().ToString();
        var expectedPath = Path.Combine(projectDir, $"{sessionId}.jsonl");
        File.WriteAllText(expectedPath, "{}");

        // Act
        var path = _discovery.GetSessionFilePath(sessionId, workingDir);

        // Assert
        Assert.That(path, Is.EqualTo(expectedPath));
    }

    [Test]
    public void GetSessionFilePath_NonExistentSession_ReturnsNull()
    {
        // Arrange
        var workingDir = "/home/user/project";
        var projectDir = Path.Combine(_testClaudeDir, ClaudeSessionDiscovery.EncodePath(workingDir));
        Directory.CreateDirectory(projectDir);

        // Act
        var path = _discovery.GetSessionFilePath("nonexistent", workingDir);

        // Assert
        Assert.That(path, Is.Null);
    }

    #endregion

    #region GetMessageCountAsync Tests

    [Test]
    public async Task GetMessageCountAsync_ExistingFile_ReturnsLineCount()
    {
        // Arrange
        var workingDir = "/home/user/project";
        var projectDir = Path.Combine(_testClaudeDir, ClaudeSessionDiscovery.EncodePath(workingDir));
        Directory.CreateDirectory(projectDir);

        var sessionId = Guid.NewGuid().ToString();
        var content = "{\"line\": 1}\n{\"line\": 2}\n{\"line\": 3}";
        await File.WriteAllTextAsync(Path.Combine(projectDir, $"{sessionId}.jsonl"), content);

        // Act
        var count = await _discovery.GetMessageCountAsync(sessionId, workingDir);

        // Assert
        Assert.That(count, Is.EqualTo(3));
    }

    [Test]
    public async Task GetMessageCountAsync_NonExistentFile_ReturnsNull()
    {
        // Act
        var count = await _discovery.GetMessageCountAsync("nonexistent", "/home/user/project");

        // Assert
        Assert.That(count, Is.Null);
    }

    [Test]
    public async Task GetMessageCountAsync_EmptyFile_ReturnsZero()
    {
        // Arrange
        var workingDir = "/home/user/project";
        var projectDir = Path.Combine(_testClaudeDir, ClaudeSessionDiscovery.EncodePath(workingDir));
        Directory.CreateDirectory(projectDir);

        var sessionId = Guid.NewGuid().ToString();
        await File.WriteAllTextAsync(Path.Combine(projectDir, $"{sessionId}.jsonl"), "");

        // Act
        var count = await _discovery.GetMessageCountAsync(sessionId, workingDir);

        // Assert
        Assert.That(count, Is.EqualTo(0));
    }

    #endregion
}
