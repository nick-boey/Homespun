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
    public async Task GetAllPrompts_ExcludesIssueAgentModificationPrompts()
    {
        // Arrange - create standard prompts and an IssueAgentModification prompt
        await _service.EnsureDefaultPromptsAsync();

        // Act
        var prompts = _service.GetAllPrompts();

        // Assert - should include Plan, Build, Rebase but not IssueAgentModification
        Assert.Multiple(() =>
        {
            Assert.That(prompts, Has.Count.EqualTo(3));
            Assert.That(prompts.Any(p => p.Name == "Plan"), Is.True);
            Assert.That(prompts.Any(p => p.Name == "Build"), Is.True);
            Assert.That(prompts.Any(p => p.Name == "Rebase"), Is.True);
            Assert.That(prompts.Any(p => p.Name == "IssueAgentModification"), Is.False);
        });
    }

    [Test]
    public async Task GetPromptsForProject_ExcludesIssueAgentModificationPrompts()
    {
        // Arrange
        await _service.EnsureDefaultPromptsAsync();

        // Act
        var prompts = _service.GetPromptsForProject("test-project");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(prompts, Has.Count.EqualTo(3));
            Assert.That(prompts.Any(p => p.Name == "IssueAgentModification"), Is.False);
        });
    }

    [Test]
    public async Task GetPromptBySessionType_ReturnsIssueAgentModificationPrompt()
    {
        // Arrange
        await _service.EnsureDefaultPromptsAsync();

        // Act
        var prompt = _service.GetPromptBySessionType(SessionType.IssueAgentModification);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(prompt, Is.Not.Null);
            Assert.That(prompt!.Name, Is.EqualTo("IssueAgentModification"));
            Assert.That(prompt.SessionType, Is.EqualTo(SessionType.IssueAgentModification));
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
    public async Task EnsureDefaultPromptsAsync_SetsSessionTypeOnIssueAgentModificationPrompt()
    {
        // Act
        await _service.EnsureDefaultPromptsAsync();

        // Get IssueAgentModification prompt directly from datastore (bypassing filter)
        var issueAgentModificationPrompt = _dataStore.AgentPrompts
            .FirstOrDefault(p => p.Name == "IssueAgentModification");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(issueAgentModificationPrompt, Is.Not.Null);
            Assert.That(issueAgentModificationPrompt!.SessionType, Is.EqualTo(SessionType.IssueAgentModification));
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
            Id = "proj-issue-agent-modification",
            Name = "ProjectIssueAgentModification",
            Mode = SessionMode.Build,
            ProjectId = projectId,
            SessionType = SessionType.IssueAgentModification
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
        // Arrange - create IssueAgentModification prompt
        await _service.EnsureDefaultPromptsAsync();
        var issueAgentModificationPrompt = _service.GetPromptBySessionType(SessionType.IssueAgentModification);

        // Act - get by ID (should still work even for session-type prompts)
        var prompt = _service.GetPrompt(issueAgentModificationPrompt!.Id);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(prompt, Is.Not.Null);
            Assert.That(prompt!.Name, Is.EqualTo("IssueAgentModification"));
        });
    }

    [Test]
    public async Task GetIssueAgentPrompts_ReturnsBothIssueAgentPrompts()
    {
        // Arrange
        await _service.EnsureDefaultPromptsAsync();

        // Act
        var prompts = _service.GetIssueAgentPrompts();

        // Assert - should include both IssueAgentModification and IssueAgentSystem
        Assert.Multiple(() =>
        {
            Assert.That(prompts, Has.Count.EqualTo(2));
            Assert.That(prompts.Any(p => p.Name == "IssueAgentModification"), Is.True);
            Assert.That(prompts.Any(p => p.Name == "IssueAgentSystem"), Is.True);
            Assert.That(prompts.All(p => p.SessionType != null), Is.True);
        });
    }

    [Test]
    public async Task GetPromptBySessionType_ReturnsIssueAgentSystemPrompt()
    {
        // Arrange
        await _service.EnsureDefaultPromptsAsync();

        // Act
        var prompt = _service.GetPromptBySessionType(SessionType.IssueAgentSystem);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(prompt, Is.Not.Null);
            Assert.That(prompt!.Name, Is.EqualTo("IssueAgentSystem"));
            Assert.That(prompt.SessionType, Is.EqualTo(SessionType.IssueAgentSystem));
            Assert.That(prompt.Mode, Is.EqualTo(SessionMode.Build));
        });
    }

    [Test]
    public async Task EnsureDefaultPromptsAsync_CreatesIssueAgentSystemPrompt()
    {
        // Act
        await _service.EnsureDefaultPromptsAsync();

        // Get IssueAgentSystem prompt directly from datastore
        var issueAgentSystemPrompt = _dataStore.AgentPrompts
            .FirstOrDefault(p => p.Name == "IssueAgentSystem");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(issueAgentSystemPrompt, Is.Not.Null);
            Assert.That(issueAgentSystemPrompt!.SessionType, Is.EqualTo(SessionType.IssueAgentSystem));
            Assert.That(issueAgentSystemPrompt.InitialMessage, Is.Not.Null.And.Not.Empty);
        });
    }

    [Test]
    public void RenderTemplate_ReplacesSelectedIssueIdPlaceholder()
    {
        // Arrange
        var template = "Selected Issue: {{selectedIssueId}}";
        var context = new PromptContext
        {
            Title = "Test",
            Id = "123",
            Branch = "main",
            Type = "task",
            SelectedIssueId = "abc123"
        };

        // Act
        var result = _service.RenderTemplate(template, context);

        // Assert
        Assert.That(result, Is.EqualTo("Selected Issue: abc123"));
    }

    [Test]
    public void RenderTemplate_ReplacesUserPromptPlaceholder()
    {
        // Arrange
        var template = "User Request: {{userPrompt}}";
        var context = new PromptContext
        {
            Title = "Test",
            Id = "123",
            Branch = "main",
            Type = "task",
            UserPrompt = "Please organize the issues by type"
        };

        // Act
        var result = _service.RenderTemplate(template, context);

        // Assert
        Assert.That(result, Is.EqualTo("User Request: Please organize the issues by type"));
    }

    [Test]
    public void RenderTemplate_ReplacesAllNewPlaceholders()
    {
        // Arrange
        var template = "Issue: {{selectedIssueId}}\nInstructions: {{userPrompt}}";
        var context = new PromptContext
        {
            Title = "Test",
            Id = "123",
            Branch = "main",
            Type = "task",
            SelectedIssueId = "xyz789",
            UserPrompt = "Update the status to complete"
        };

        // Act
        var result = _service.RenderTemplate(template, context);

        // Assert
        Assert.That(result, Is.EqualTo("Issue: xyz789\nInstructions: Update the status to complete"));
    }

    [Test]
    public void RenderTemplate_NewPlaceholders_ReturnsEmptyStringWhenNull()
    {
        // Arrange
        var template = "Issue: {{selectedIssueId}}\nInstructions: {{userPrompt}}";
        var context = new PromptContext
        {
            Title = "Test",
            Id = "123",
            Branch = "main",
            Type = "task",
            SelectedIssueId = null,
            UserPrompt = null
        };

        // Act
        var result = _service.RenderTemplate(template, context);

        // Assert
        Assert.That(result, Is.EqualTo("Issue: \nInstructions: "));
    }

    [Test]
    public void RenderTemplate_ConditionalBlock_IncludesContentWhenValuePresent()
    {
        // Arrange
        var template = "{{#if selectedIssueId}}**Selected Issue:** {{selectedIssueId}}{{/if}}";
        var context = new PromptContext
        {
            Title = "Test",
            Id = "123",
            Branch = "main",
            Type = "task",
            SelectedIssueId = "abc123"
        };

        // Act
        var result = _service.RenderTemplate(template, context);

        // Assert
        Assert.That(result, Is.EqualTo("**Selected Issue:** abc123"));
    }

    [Test]
    public void RenderTemplate_ConditionalBlock_RemovesBlockWhenValueNull()
    {
        // Arrange
        var template = "Before{{#if selectedIssueId}}\n**Selected Issue:** {{selectedIssueId}}{{/if}}\nAfter";
        var context = new PromptContext
        {
            Title = "Test",
            Id = "123",
            Branch = "main",
            Type = "task",
            SelectedIssueId = null
        };

        // Act
        var result = _service.RenderTemplate(template, context);

        // Assert
        Assert.That(result, Is.EqualTo("Before\nAfter"));
    }

    [Test]
    public void RenderTemplate_ConditionalBlock_RemovesBlockWhenValueEmpty()
    {
        // Arrange
        var template = "Before{{#if selectedIssueId}}\n**Selected Issue:** {{selectedIssueId}}{{/if}}\nAfter";
        var context = new PromptContext
        {
            Title = "Test",
            Id = "123",
            Branch = "main",
            Type = "task",
            SelectedIssueId = ""
        };

        // Act
        var result = _service.RenderTemplate(template, context);

        // Assert
        Assert.That(result, Is.EqualTo("Before\nAfter"));
    }

    [Test]
    public void RenderTemplate_ConditionalBlock_WithMultipleLines()
    {
        // Arrange
        var template = """
            {{#if selectedIssueId}}
            **Selected Issue:** {{selectedIssueId}}

            First, use `fleece show {{selectedIssueId}} --json` to understand the current state.
            {{/if}}

            User request: {{userPrompt}}
            """;
        var context = new PromptContext
        {
            Title = "Test",
            Id = "123",
            Branch = "main",
            Type = "task",
            SelectedIssueId = "xyz789",
            UserPrompt = "Update the status"
        };

        // Act
        var result = _service.RenderTemplate(template, context);

        // Assert
        var expected = """

            **Selected Issue:** xyz789

            First, use `fleece show xyz789 --json` to understand the current state.


            User request: Update the status
            """;
        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public async Task IssueAgentModificationPrompt_ContainsExpectedContent()
    {
        // Arrange
        await _service.EnsureDefaultPromptsAsync();

        // Act
        var prompt = _service.GetPromptBySessionType(SessionType.IssueAgentModification);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(prompt, Is.Not.Null);
            Assert.That(prompt!.InitialMessage, Does.Contain("Issue Modification Request"));
            Assert.That(prompt.InitialMessage, Does.Contain("IMPORTANT CONSTRAINTS"));
            Assert.That(prompt.InitialMessage, Does.Contain("fleece"));
            Assert.That(prompt.InitialMessage, Does.Contain("{{userPrompt}}"));
            Assert.That(prompt.InitialMessage, Does.Contain("{{#if selectedIssueId}}"));
        });
    }

    [Test]
    public async Task GetPromptsForProject_IsOverrideTrue_WhenProjectPromptShadowsGlobalPrompt()
    {
        // Arrange - create global prompts first
        await _service.EnsureDefaultPromptsAsync();

        // Create a project prompt with the same name as a global prompt (Build)
        var projectId = "test-project";
        var projectPrompt = new AgentPrompt
        {
            Id = "proj-build",
            Name = "Build",
            InitialMessage = "Custom project build message",
            Mode = SessionMode.Build,
            ProjectId = projectId
        };
        await _dataStore.AddAgentPromptAsync(projectPrompt);

        // Act
        var prompts = _service.GetPromptsForProject(projectId);

        // Assert - the project's Build prompt should have IsOverride = true
        var buildPrompt = prompts.FirstOrDefault(p => p.Name == "Build");
        Assert.Multiple(() =>
        {
            Assert.That(buildPrompt, Is.Not.Null);
            Assert.That(buildPrompt!.ProjectId, Is.EqualTo(projectId));
            Assert.That(buildPrompt.IsOverride, Is.True);
        });
    }

    [Test]
    public async Task GetPromptsForProject_IsOverrideFalse_ForGlobalPrompts()
    {
        // Arrange
        await _service.EnsureDefaultPromptsAsync();
        var projectId = "test-project";

        // Act
        var prompts = _service.GetPromptsForProject(projectId);

        // Assert - all prompts should have IsOverride = false (no project prompts exist)
        Assert.Multiple(() =>
        {
            Assert.That(prompts, Has.Count.EqualTo(3)); // Plan, Build, Rebase
            Assert.That(prompts.All(p => p.IsOverride == false), Is.True);
        });
    }

    [Test]
    public async Task GetPromptsForProject_IsOverrideFalse_ForProjectPromptsWithUniqueNames()
    {
        // Arrange
        await _service.EnsureDefaultPromptsAsync();
        var projectId = "test-project";

        // Create a project prompt with a unique name
        var projectPrompt = new AgentPrompt
        {
            Id = "proj-custom",
            Name = "CustomProjectPrompt",
            InitialMessage = "Custom project message",
            Mode = SessionMode.Build,
            ProjectId = projectId
        };
        await _dataStore.AddAgentPromptAsync(projectPrompt);

        // Act
        var prompts = _service.GetPromptsForProject(projectId);

        // Assert - custom prompt should have IsOverride = false (no global prompt with same name)
        var customPrompt = prompts.FirstOrDefault(p => p.Name == "CustomProjectPrompt");
        Assert.Multiple(() =>
        {
            Assert.That(customPrompt, Is.Not.Null);
            Assert.That(customPrompt!.IsOverride, Is.False);
        });
    }

    [Test]
    public async Task GetAllPrompts_IsOverrideAlwaysFalse()
    {
        // Arrange
        await _service.EnsureDefaultPromptsAsync();

        // Act
        var prompts = _service.GetAllPrompts();

        // Assert - global prompts should never have IsOverride = true
        Assert.That(prompts.All(p => p.IsOverride == false), Is.True);
    }

    [Test]
    public async Task GetProjectPrompts_IsOverrideAlwaysFalse()
    {
        // Arrange
        var projectId = "test-project";
        await _service.EnsureDefaultPromptsAsync();

        // Create a project prompt that shadows a global prompt
        var projectPrompt = new AgentPrompt
        {
            Id = "proj-build",
            Name = "Build",
            Mode = SessionMode.Build,
            ProjectId = projectId
        };
        await _dataStore.AddAgentPromptAsync(projectPrompt);

        // Act - GetProjectPrompts only returns project-scoped prompts, not merged list
        var prompts = _service.GetProjectPrompts(projectId);

        // Assert - IsOverride is only set in GetPromptsForProject context
        Assert.Multiple(() =>
        {
            Assert.That(prompts, Has.Count.EqualTo(1));
            Assert.That(prompts[0].IsOverride, Is.False);
        });
    }
}
