using Homespun.Features.Testing.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.Testing;

/// <summary>
/// Tests that verify the JsonlSessionLoader correctly loads real session data from /data/sessions/.
/// These tests help ensure the mock container can display realistic session data correctly.
/// </summary>
[TestFixture]
public class JsonlSessionLoaderRealDataTests
{
    private const string RealSessionDataPath = "/data/sessions";
    private JsonlSessionLoader _loader = null!;
    private Mock<ILogger<JsonlSessionLoader>> _loggerMock = null!;

    [SetUp]
    public void SetUp()
    {
        _loggerMock = new Mock<ILogger<JsonlSessionLoader>>();
        _loader = new JsonlSessionLoader(_loggerMock.Object);
    }

    [Test]
    [Category("Integration")]
    public async Task LoadAllSessionsAsync_RealData_LoadsSessions()
    {
        // Skip if test data path doesn't exist (e.g., running in CI)
        if (!Directory.Exists(RealSessionDataPath))
        {
            Assert.Ignore($"Real session data path not found: {RealSessionDataPath}");
            return;
        }

        // Act
        var sessions = await _loader.LoadAllSessionsAsync(RealSessionDataPath);

        // Assert
        Assert.That(sessions, Is.Not.Empty, "Should load at least one session from real data");

        foreach (var session in sessions)
        {
            Assert.Multiple(() =>
            {
                Assert.That(session.Id, Is.Not.Null.And.Not.Empty, "Session ID should not be empty");
                Assert.That(session.Messages, Is.Not.Empty, "Session should have messages");
            });
        }
    }

    [Test]
    [Category("Integration")]
    public async Task LoadAllSessionsAsync_RealData_MessagesHaveCorrectRoles()
    {
        // Skip if test data path doesn't exist
        if (!Directory.Exists(RealSessionDataPath))
        {
            Assert.Ignore($"Real session data path not found: {RealSessionDataPath}");
            return;
        }

        // Act
        var sessions = await _loader.LoadAllSessionsAsync(RealSessionDataPath);

        // Assert - verify message role distribution makes sense
        foreach (var session in sessions)
        {
            var userMessages = session.Messages.Count(m => m.Role == ClaudeMessageRole.User);
            var assistantMessages = session.Messages.Count(m => m.Role == ClaudeMessageRole.Assistant);

            Assert.That(userMessages, Is.GreaterThan(0),
                $"Session {session.Id} should have at least one user message");

            // In real sessions, we expect assistant messages with tool uses
            TestContext.WriteLine($"Session {session.Id}: {userMessages} user, {assistantMessages} assistant messages");
        }
    }

    [Test]
    [Category("Integration")]
    public async Task LoadAllSessionsAsync_RealData_AssistantMessagesHaveToolUseOrText()
    {
        // Skip if test data path doesn't exist
        if (!Directory.Exists(RealSessionDataPath))
        {
            Assert.Ignore($"Real session data path not found: {RealSessionDataPath}");
            return;
        }

        // Act
        var sessions = await _loader.LoadAllSessionsAsync(RealSessionDataPath);

        // Assert - assistant messages should have meaningful content
        foreach (var session in sessions)
        {
            var assistantMessages = session.Messages.Where(m => m.Role == ClaudeMessageRole.Assistant).ToList();

            foreach (var msg in assistantMessages)
            {
                var hasText = msg.Content.Any(c => c.Type == ClaudeContentType.Text && !string.IsNullOrEmpty(c.Text));
                var hasThinking = msg.Content.Any(c => c.Type == ClaudeContentType.Thinking);
                var hasToolUse = msg.Content.Any(c => c.Type == ClaudeContentType.ToolUse);

                Assert.That(hasText || hasThinking || hasToolUse, Is.True,
                    $"Assistant message {msg.Id} in session {session.Id} should have text, thinking, or tool_use content");
            }
        }
    }

    [Test]
    [Category("Integration")]
    public async Task LoadAllSessionsAsync_RealData_ToolResultMessagesAreUserRole()
    {
        // Skip if test data path doesn't exist
        if (!Directory.Exists(RealSessionDataPath))
        {
            Assert.Ignore($"Real session data path not found: {RealSessionDataPath}");
            return;
        }

        // Act
        var sessions = await _loader.LoadAllSessionsAsync(RealSessionDataPath);

        // Assert - messages containing only tool results should have role=user
        foreach (var session in sessions)
        {
            var toolResultOnlyMessages = session.Messages
                .Where(m => m.Content.Count > 0 && m.Content.All(c => c.Type == ClaudeContentType.ToolResult))
                .ToList();

            foreach (var msg in toolResultOnlyMessages)
            {
                Assert.That(msg.Role, Is.EqualTo(ClaudeMessageRole.User),
                    $"Tool result message {msg.Id} in session {session.Id} should have role=User");
            }
        }
    }

    [Test]
    [Category("Integration")]
    public async Task LoadAllSessionsAsync_RealData_ContentTypesAreParsedCorrectly()
    {
        // Skip if test data path doesn't exist
        if (!Directory.Exists(RealSessionDataPath))
        {
            Assert.Ignore($"Real session data path not found: {RealSessionDataPath}");
            return;
        }

        // Act
        var sessions = await _loader.LoadAllSessionsAsync(RealSessionDataPath);

        // Assert - count content types to ensure they're being parsed
        var contentTypeCounts = new Dictionary<ClaudeContentType, int>();

        foreach (var session in sessions)
        {
            foreach (var msg in session.Messages)
            {
                foreach (var content in msg.Content)
                {
                    if (!contentTypeCounts.ContainsKey(content.Type))
                        contentTypeCounts[content.Type] = 0;
                    contentTypeCounts[content.Type]++;
                }
            }
        }

        TestContext.WriteLine("Content type distribution across all sessions:");
        foreach (var kvp in contentTypeCounts)
        {
            TestContext.WriteLine($"  {kvp.Key}: {kvp.Value}");
        }

        // Verify we have the expected content types
        Assert.That(contentTypeCounts.ContainsKey(ClaudeContentType.Text), Is.True, "Should have Text content");
        Assert.That(contentTypeCounts.ContainsKey(ClaudeContentType.ToolUse), Is.True, "Should have ToolUse content");
        Assert.That(contentTypeCounts.ContainsKey(ClaudeContentType.ToolResult), Is.True, "Should have ToolResult content");
    }

    [Test]
    [Category("Integration")]
    public async Task LoadAllSessionsAsync_RealData_SessionMetadataLoaded()
    {
        // Skip if test data path doesn't exist
        if (!Directory.Exists(RealSessionDataPath))
        {
            Assert.Ignore($"Real session data path not found: {RealSessionDataPath}");
            return;
        }

        // Act
        var sessions = await _loader.LoadAllSessionsAsync(RealSessionDataPath);

        // Assert - sessions should have metadata loaded
        foreach (var session in sessions)
        {
            TestContext.WriteLine($"Session {session.Id}:");
            TestContext.WriteLine($"  ProjectId: {session.ProjectId}");
            TestContext.WriteLine($"  EntityId: {session.EntityId}");
            TestContext.WriteLine($"  Mode: {session.Mode}");
            TestContext.WriteLine($"  Model: {session.Model}");
            TestContext.WriteLine($"  MessageCount: {session.Messages.Count}");
            TestContext.WriteLine("");
        }

        // At least one session should have metadata
        var sessionsWithMetadata = sessions.Where(s => !string.IsNullOrEmpty(s.ProjectId)).ToList();
        Assert.That(sessionsWithMetadata, Is.Not.Empty, "At least one session should have metadata loaded");
    }
}
