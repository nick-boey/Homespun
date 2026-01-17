using Homespun.Features.ClaudeCode.Data;
using Homespun.Features.ClaudeCode.Services;

namespace Homespun.Tests.Features.ClaudeCode;

[TestFixture]
public class SessionOptionsFactoryTests
{
    private SessionOptionsFactory _factory = null!;

    [SetUp]
    public void SetUp()
    {
        _factory = new SessionOptionsFactory();
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
}
