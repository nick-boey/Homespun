using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.Testing;
using Homespun.Shared.Models.Sessions;

namespace Homespun.Tests.Features.ClaudeCode.Services;

[TestFixture]
public class AgentPromptServiceTests
{
    private MockDataStore _dataStore = null!;
    private AgentPromptService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _dataStore = new MockDataStore();
        _service = new AgentPromptService(_dataStore);
    }

    [Test]
    public async Task GetAllPrompts_ExcludesIssueModifyPrompts()
    {
        // Arrange - create standard prompts and an IssueModify prompt
        await _service.EnsureDefaultPromptsAsync();

        // Act
        var prompts = _service.GetAllPrompts();

        // Assert - should include Plan, Build, Rebase but not IssueModify
        Assert.Multiple(() =>
        {
            Assert.That(prompts, Has.Count.EqualTo(3));
            Assert.That(prompts.Any(p => p.Name == "Plan"), Is.True);
            Assert.That(prompts.Any(p => p.Name == "Build"), Is.True);
            Assert.That(prompts.Any(p => p.Name == "Rebase"), Is.True);
            Assert.That(prompts.Any(p => p.Name == "IssueModify"), Is.False);
        });
    }

    [Test]
    public async Task GetPromptsForProject_ExcludesIssueModifyPrompts()
    {
        // Arrange
        await _service.EnsureDefaultPromptsAsync();

        // Act
        var prompts = _service.GetPromptsForProject("test-project");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(prompts, Has.Count.EqualTo(3));
            Assert.That(prompts.Any(p => p.Name == "IssueModify"), Is.False);
        });
    }

    [Test]
    public async Task GetPromptBySessionType_ReturnsIssueModifyPrompt()
    {
        // Arrange
        await _service.EnsureDefaultPromptsAsync();

        // Act
        var prompt = _service.GetPromptBySessionType(SessionType.IssueModify);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(prompt, Is.Not.Null);
            Assert.That(prompt!.Name, Is.EqualTo("IssueModify"));
            Assert.That(prompt.SessionType, Is.EqualTo(SessionType.IssueModify));
            Assert.That(prompt.Mode, Is.EqualTo(SessionMode.Build));
        });
    }

    [Test]
    public void GetPromptBySessionType_ReturnsNullForUnknownType()
    {
        // Act
        var prompt = _service.GetPromptBySessionType(SessionType.Standard);

        // Assert
        Assert.That(prompt, Is.Null);
    }

    [Test]
    public async Task EnsureDefaultPromptsAsync_SetsSessionTypeOnIssueModifyPrompt()
    {
        // Act
        await _service.EnsureDefaultPromptsAsync();

        // Get IssueModify prompt directly from datastore (bypassing filter)
        var issueModifyPrompt = _dataStore.AgentPrompts
            .FirstOrDefault(p => p.Name == "IssueModify");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(issueModifyPrompt, Is.Not.Null);
            Assert.That(issueModifyPrompt!.SessionType, Is.EqualTo(SessionType.IssueModify));
        });
    }

    [Test]
    public async Task EnsureDefaultPromptsAsync_DoesNotRecreateExistingPrompts()
    {
        // Arrange
        await _service.EnsureDefaultPromptsAsync();
        var initialCount = _dataStore.AgentPrompts.Count;

        // Act - call again
        await _service.EnsureDefaultPromptsAsync();

        // Assert - count should not change
        Assert.That(_dataStore.AgentPrompts, Has.Count.EqualTo(initialCount));
    }

    [Test]
    public async Task GetProjectPrompts_ExcludesSessionTypePrompts()
    {
        // Arrange - add a project-level prompt with a session type (unusual but should be filtered)
        var projectId = "test-project";
        var prompt = new AgentPrompt
        {
            Id = "proj-issue-modify",
            Name = "ProjectIssueModify",
            Mode = SessionMode.Build,
            ProjectId = projectId,
            SessionType = SessionType.IssueModify
        };
        await _dataStore.AddAgentPromptAsync(prompt);

        // Also add a regular project prompt
        var regularPrompt = new AgentPrompt
        {
            Id = "proj-plan",
            Name = "ProjectPlan",
            Mode = SessionMode.Plan,
            ProjectId = projectId,
            SessionType = null
        };
        await _dataStore.AddAgentPromptAsync(regularPrompt);

        // Act
        var prompts = _service.GetProjectPrompts(projectId);

        // Assert - should only include regular prompt
        Assert.Multiple(() =>
        {
            Assert.That(prompts, Has.Count.EqualTo(1));
            Assert.That(prompts[0].Name, Is.EqualTo("ProjectPlan"));
        });
    }

    [Test]
    public async Task RenderTemplate_ReplacesPlaceholders()
    {
        // Arrange
        var template = "Issue: {{title}} (ID: {{id}})\nBranch: {{branch}}\nType: {{type}}\n{{description}}";
        var context = new PromptContext
        {
            Title = "Test Issue",
            Id = "abc123",
            Branch = "feature/test",
            Type = "bug",
            Description = "This is a test description"
        };

        // Act
        var result = _service.RenderTemplate(template, context);

        // Assert
        Assert.That(result, Is.EqualTo("Issue: Test Issue (ID: abc123)\nBranch: feature/test\nType: bug\nThis is a test description"));
    }

    [Test]
    public void RenderTemplate_ReturnsNullForNullTemplate()
    {
        var context = new PromptContext { Title = "Test", Id = "123", Branch = "main", Type = "task" };
        var result = _service.RenderTemplate(null, context);
        Assert.That(result, Is.Null);
    }

    [Test]
    public void RenderTemplate_ReplacesContextPlaceholder()
    {
        // Arrange
        var template = "## Issue Hierarchy\n{{context}}\n\n## Description\n{{description}}";
        var treeContext = """
            - parent1 [feature] [open] Parent Issue
              - child1 [task] [progress] Current Issue
            """;
        var context = new PromptContext
        {
            Title = "Current Issue",
            Id = "child1",
            Branch = "feature/test",
            Type = "task",
            Description = "Test description",
            Context = treeContext
        };

        // Act
        var result = _service.RenderTemplate(template, context);

        // Assert
        var expected = $"## Issue Hierarchy\n{treeContext}\n\n## Description\nTest description";
        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public void RenderTemplate_ContextPlaceholder_ReturnsEmptyStringWhenNull()
    {
        // Arrange
        var template = "Context: {{context}}";
        var context = new PromptContext
        {
            Title = "Test",
            Id = "123",
            Branch = "main",
            Type = "task",
            Context = null
        };

        // Act
        var result = _service.RenderTemplate(template, context);

        // Assert
        Assert.That(result, Is.EqualTo("Context: "));
    }

    [Test]
    public async Task CreatePromptAsync_CreatesGlobalPrompt()
    {
        // Act
        var prompt = await _service.CreatePromptAsync("TestPrompt", "Test message", SessionMode.Plan);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(prompt.Name, Is.EqualTo("TestPrompt"));
            Assert.That(prompt.InitialMessage, Is.EqualTo("Test message"));
            Assert.That(prompt.Mode, Is.EqualTo(SessionMode.Plan));
            Assert.That(prompt.ProjectId, Is.Null);
            Assert.That(prompt.SessionType, Is.Null);
        });
    }

    [Test]
    public async Task CreatePromptAsync_CreatesProjectPrompt()
    {
        // Act
        var prompt = await _service.CreatePromptAsync("TestPrompt", "Test message", SessionMode.Build, "project-123");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(prompt.Name, Is.EqualTo("TestPrompt"));
            Assert.That(prompt.ProjectId, Is.EqualTo("project-123"));
        });
    }

    [Test]
    public async Task GetPrompt_ReturnsByIdIncludingSessionTypePrompts()
    {
        // Arrange - create IssueModify prompt
        await _service.EnsureDefaultPromptsAsync();
        var issueModifyPrompt = _service.GetPromptBySessionType(SessionType.IssueModify);

        // Act - get by ID (should still work even for session-type prompts)
        var prompt = _service.GetPrompt(issueModifyPrompt!.Id);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(prompt, Is.Not.Null);
            Assert.That(prompt!.Name, Is.EqualTo("IssueModify"));
        });
    }
}
