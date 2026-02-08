using Homespun.ClaudeAgentSdk;
using Homespun.Features.ClaudeCode.Data;
using Homespun.Features.ClaudeCode.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.ClaudeCode;

[TestFixture]
public class SessionOptionsFactoryTests
{
    private SessionOptionsFactory _factory = null!;
    private Mock<ILogger<SessionOptionsFactory>> _loggerMock = null!;

    [SetUp]
    public void SetUp()
    {
        _loggerMock = new Mock<ILogger<SessionOptionsFactory>>();
        _factory = new SessionOptionsFactory(_loggerMock.Object);
    }

    [Test]
    public void Create_PlanMode_ReturnsReadOnlyTools()
    {
        // Arrange
        var workingDirectory = "/test/path";
        var model = "claude-sonnet-4-20250514";

        // Act
        var options = _factory.Create(SessionMode.Plan, workingDirectory, model);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(options.Cwd, Is.EqualTo(workingDirectory));
            Assert.That(options.Model, Is.EqualTo(model));
            Assert.That(options.AllowedTools, Is.Not.Null);
            Assert.That(options.AllowedTools, Does.Contain("Read"));
            Assert.That(options.AllowedTools, Does.Contain("Glob"));
            Assert.That(options.AllowedTools, Does.Contain("Grep"));
            Assert.That(options.AllowedTools, Does.Contain("WebFetch"));
            Assert.That(options.AllowedTools, Does.Contain("WebSearch"));
            Assert.That(options.AllowedTools, Does.Contain("ExitPlanMode"));
            Assert.That(options.AllowedTools, Does.Contain("mcp__homespun__ask_user"));
            Assert.That(options.AllowedTools, Does.Not.Contain("AskUserQuestion"),
                "Plan mode should use mcp__homespun__ask_user instead of built-in AskUserQuestion");
            Assert.That(options.AllowedTools, Does.Not.Contain("Write"));
            Assert.That(options.AllowedTools, Does.Not.Contain("Edit"));
            Assert.That(options.AllowedTools, Does.Not.Contain("Bash"));
        });
    }

    [Test]
    public void Create_BuildMode_ReturnsAllTools()
    {
        // Arrange
        var workingDirectory = "/test/path";
        var model = "claude-sonnet-4-20250514";

        // Act
        var options = _factory.Create(SessionMode.Build, workingDirectory, model);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(options.Cwd, Is.EqualTo(workingDirectory));
            Assert.That(options.Model, Is.EqualTo(model));
            // Build mode should have all tools or null (meaning all tools allowed)
            Assert.That(options.AllowedTools, Is.Null.Or.Empty,
                "Build mode should allow all tools (null or empty means all tools)");
        });
    }

    [Test]
    public void Create_PlanMode_DoesNotIncludeWriteTools()
    {
        // Arrange
        var workingDirectory = "/test/path";
        var model = "claude-opus-4-20250514";

        // Act
        var options = _factory.Create(SessionMode.Plan, workingDirectory, model);

        // Assert - Plan mode should be read-only
        var writeTools = new[] { "Write", "Edit", "Bash", "NotebookEdit" };
        foreach (var tool in writeTools)
        {
            Assert.That(options.AllowedTools, Does.Not.Contain(tool),
                $"Plan mode should not include write tool: {tool}");
        }
    }

    [Test]
    public void Create_WithSystemPrompt_IncludesSystemPrompt()
    {
        // Arrange
        var workingDirectory = "/test/path";
        var model = "claude-sonnet-4-20250514";
        var systemPrompt = "You are a helpful assistant.";

        // Act
        var options = _factory.Create(SessionMode.Build, workingDirectory, model, systemPrompt);

        // Assert
        Assert.That(options.SystemPrompt, Is.EqualTo(systemPrompt));
    }

    [Test]
    public void Create_WithNullSystemPrompt_DoesNotThrow()
    {
        // Arrange
        var workingDirectory = "/test/path";
        var model = "claude-sonnet-4-20250514";

        // Act & Assert
        Assert.DoesNotThrow(() => _factory.Create(SessionMode.Plan, workingDirectory, model, null));
    }

    [Test]
    public void Create_ConfiguresPlaywrightMcpServer()
    {
        // Arrange
        var workingDirectory = "/test/path";
        var model = "claude-sonnet-4-20250514";

        // Act
        var options = _factory.Create(SessionMode.Build, workingDirectory, model);

        // Assert
        Assert.That(options.McpServers, Is.Not.Null);
        Assert.That(options.McpServers, Is.InstanceOf<Dictionary<string, object>>());

        var mcpServers = (Dictionary<string, object>)options.McpServers!;
        Assert.That(mcpServers.ContainsKey("playwright"), Is.True, "McpServers should contain 'playwright' key");

        // Config uses lowercase keys to match Claude CLI's expected JSON format
        var playwrightConfig = mcpServers["playwright"] as Dictionary<string, object>;
        Assert.That(playwrightConfig, Is.Not.Null);
        Assert.That(playwrightConfig!["type"], Is.EqualTo("stdio"));
        Assert.That(playwrightConfig["command"], Is.EqualTo("npx"));

        var args = playwrightConfig["args"] as string[];
        Assert.That(args, Is.Not.Null);
        Assert.That(args, Does.Contain("@playwright/mcp@latest"));
        Assert.That(args, Does.Contain("--headless"));
    }

    [Test]
    public void Create_ConfiguresLargeBufferSize()
    {
        // Arrange
        var workingDirectory = "/test/path";
        var model = "claude-sonnet-4-20250514";

        // Act
        var options = _factory.Create(SessionMode.Build, workingDirectory, model);

        // Assert - Buffer size should be 10MB (10 * 1024 * 1024 = 10485760)
        Assert.That(options.MaxBufferSize, Is.EqualTo(10 * 1024 * 1024),
            "Buffer size should be 10MB to accommodate large Playwright MCP responses");
    }

    [Test]
    public void Create_ConfiguresSkipMessageBehavior()
    {
        // Arrange
        var workingDirectory = "/test/path";
        var model = "claude-sonnet-4-20250514";

        // Act
        var options = _factory.Create(SessionMode.Build, workingDirectory, model);

        // Assert - Should use SkipMessage behavior for graceful degradation
        Assert.That(options.BufferOverflowBehavior, Is.EqualTo(BufferOverflowBehavior.SkipMessage),
            "Should use SkipMessage behavior to gracefully handle large messages");
    }

    [Test]
    public void Create_ConfiguresBufferOverflowCallback()
    {
        // Arrange
        var workingDirectory = "/test/path";
        var model = "claude-sonnet-4-20250514";

        // Act
        var options = _factory.Create(SessionMode.Build, workingDirectory, model);

        // Assert - Callback should be configured for logging
        Assert.That(options.OnBufferOverflow, Is.Not.Null,
            "Buffer overflow callback should be configured for logging");
    }

    [Test]
    public void Create_BufferOverflowCallback_LogsWarning()
    {
        // Arrange
        var workingDirectory = "/test/path";
        var model = "claude-sonnet-4-20250514";
        var options = _factory.Create(SessionMode.Build, workingDirectory, model);

        // Act - Simulate buffer overflow by invoking the callback
        options.OnBufferOverflow?.Invoke("playwright_mcp_result", 15_000_000, 10_485_760);

        // Assert - Verify logger was called with warning
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Buffer overflow detected")),
                It.IsAny<Exception?>(),
                It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
            Times.Once);
    }

    [Test]
    public void Create_WithAskUserFunction_AddsHomespunMcpServer()
    {
        // Arrange
        var askUserFunction = AskUserQuestionFunction.Create(
            (questions, ct) => Task.FromResult(new Dictionary<string, string>()));

        // Act
        var options = _factory.Create(SessionMode.Build, "/test/path", "claude-sonnet-4-20250514",
            askUserFunction: askUserFunction);

        // Assert
        Assert.That(options.McpServers, Is.InstanceOf<Dictionary<string, object>>());
        var mcpServers = (Dictionary<string, object>)options.McpServers!;
        Assert.That(mcpServers.ContainsKey("homespun"), Is.True,
            "McpServers should contain 'homespun' key when askUserFunction is provided");

        var homespunConfig = mcpServers["homespun"] as McpSdkServerConfig;
        Assert.That(homespunConfig, Is.Not.Null);
        Assert.That(homespunConfig!.Name, Is.EqualTo("homespun"));
        Assert.That(homespunConfig.Instance, Is.InstanceOf<DynamicAIFunctionMcpServer>());
    }

    [Test]
    public void Create_WithAskUserFunction_DisallowsBuiltInAskUserQuestion()
    {
        // Arrange
        var askUserFunction = AskUserQuestionFunction.Create(
            (questions, ct) => Task.FromResult(new Dictionary<string, string>()));

        // Act
        var options = _factory.Create(SessionMode.Build, "/test/path", "claude-sonnet-4-20250514",
            askUserFunction: askUserFunction);

        // Assert
        Assert.That(options.DisallowedTools, Does.Contain("AskUserQuestion"),
            "Built-in AskUserQuestion should be disallowed when using custom MCP tool");
    }

    [Test]
    public void Create_PlanMode_WithAskUserFunction_AllowsMcpToolName()
    {
        // Arrange
        var askUserFunction = AskUserQuestionFunction.Create(
            (questions, ct) => Task.FromResult(new Dictionary<string, string>()));

        // Act
        var options = _factory.Create(SessionMode.Plan, "/test/path", "claude-sonnet-4-20250514",
            askUserFunction: askUserFunction);

        // Assert
        Assert.That(options.AllowedTools, Does.Contain("mcp__homespun__ask_user"),
            "Plan mode should allow the custom MCP tool name");
        Assert.That(options.AllowedTools, Does.Not.Contain("AskUserQuestion"),
            "Plan mode should not allow built-in AskUserQuestion");
    }

    [Test]
    public void Create_WithoutAskUserFunction_NoHomespunMcpServer()
    {
        // Arrange & Act
        var options = _factory.Create(SessionMode.Build, "/test/path", "claude-sonnet-4-20250514");

        // Assert - backward compatibility: no homespun MCP server
        var mcpServers = (Dictionary<string, object>)options.McpServers!;
        Assert.That(mcpServers.ContainsKey("homespun"), Is.False,
            "McpServers should not contain 'homespun' when no askUserFunction is provided");
        Assert.That(options.DisallowedTools, Does.Not.Contain("AskUserQuestion"),
            "Built-in AskUserQuestion should not be disallowed without custom MCP tool");
    }

    [Test]
    public void Create_WithAskUserFunction_PlaywrightMcpServerStillPresent()
    {
        // Arrange
        var askUserFunction = AskUserQuestionFunction.Create(
            (questions, ct) => Task.FromResult(new Dictionary<string, string>()));

        // Act
        var options = _factory.Create(SessionMode.Build, "/test/path", "claude-sonnet-4-20250514",
            askUserFunction: askUserFunction);

        // Assert - Playwright should still be configured
        var mcpServers = (Dictionary<string, object>)options.McpServers!;
        Assert.That(mcpServers.ContainsKey("playwright"), Is.True,
            "Playwright MCP server should still be present alongside homespun");
    }
}
