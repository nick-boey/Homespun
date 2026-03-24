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
    public async Task GetAllPrompts_ExcludesIssueAgentModificationAndCategoryPrompts()
    {
        // Arrange - create standard prompts and an IssueAgentModification prompt
        await _service.EnsureDefaultPromptsAsync();

        // Act
        var prompts = _service.GetAllPrompts();

        // Assert - should include all standard prompts but not session-type or IssueAgent category prompts
        Assert.Multiple(() =>
        {
            Assert.That(prompts, Has.Count.EqualTo(8));
            Assert.That(prompts.Any(p => p.Name == "Plan"), Is.True);
            Assert.That(prompts.Any(p => p.Name == "Build"), Is.True);
            Assert.That(prompts.Any(p => p.Name == "Rebase"), Is.True);
            Assert.That(prompts.Any(p => p.Name == "Rebase, Test and Merge"), Is.True);
            Assert.That(prompts.Any(p => p.Name == "Create a PR"), Is.True);
            Assert.That(prompts.Any(p => p.Name == "Fix tests"), Is.True);
            Assert.That(prompts.Any(p => p.Name == "Build and Merge"), Is.True);
            Assert.That(prompts.Any(p => p.Name == "Review PR comments"), Is.True);
            Assert.That(prompts.Any(p => p.Name == "IssueModify"), Is.False);
            Assert.That(prompts.Any(p => p.Name == "IssueAgentModification"), Is.False);
            Assert.That(prompts.Any(p => p.Name == "IssueAgentSystem"), Is.False);
        });
    }

    [Test]
    public async Task GetPromptsForProject_ExcludesSessionTypeAndNonStandardCategoryPrompts()
    {
        // Arrange
        await _service.EnsureDefaultPromptsAsync();

        // Act
        var prompts = _service.GetPromptsForProject("test-project");

        // Assert - excludes session-type prompts and IssueAgent category prompts
        Assert.Multiple(() =>
        {
            Assert.That(prompts, Has.Count.EqualTo(8));
            Assert.That(prompts.Any(p => p.SessionType != null), Is.False);
            Assert.That(prompts.Any(p => p.Category != PromptCategory.Standard), Is.False);
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
            Assert.That(prompts, Has.Count.EqualTo(8));
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

    [Test]
    public async Task CreateOverrideAsync_CreatesProjectPromptFromGlobalPrompt()
    {
        // Arrange
        await _service.EnsureDefaultPromptsAsync();
        var globalBuildPrompt = _service.GetAllPrompts().First(p => p.Name == "Build");
        var projectId = "test-project";

        // Act
        var overridePrompt = await _service.CreateOverrideAsync(globalBuildPrompt.Id, projectId, null);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(overridePrompt, Is.Not.Null);
            Assert.That(overridePrompt.Name, Is.EqualTo("Build"));
            Assert.That(overridePrompt.ProjectId, Is.EqualTo(projectId));
            Assert.That(overridePrompt.Mode, Is.EqualTo(globalBuildPrompt.Mode));
            Assert.That(overridePrompt.InitialMessage, Is.EqualTo(globalBuildPrompt.InitialMessage));
            Assert.That(overridePrompt.Id, Is.Not.EqualTo(globalBuildPrompt.Id)); // Should be a new ID
        });
    }

    [Test]
    public async Task CreateOverrideAsync_UsesProvidedInitialMessage()
    {
        // Arrange
        await _service.EnsureDefaultPromptsAsync();
        var globalBuildPrompt = _service.GetAllPrompts().First(p => p.Name == "Build");
        var projectId = "test-project";
        var customMessage = "Custom override message for this project";

        // Act
        var overridePrompt = await _service.CreateOverrideAsync(globalBuildPrompt.Id, projectId, customMessage);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(overridePrompt.InitialMessage, Is.EqualTo(customMessage));
            Assert.That(overridePrompt.Name, Is.EqualTo("Build"));
            Assert.That(overridePrompt.Mode, Is.EqualTo(globalBuildPrompt.Mode));
        });
    }

    [Test]
    public async Task CreateOverrideAsync_ThrowsWhenGlobalPromptNotFound()
    {
        // Arrange
        var projectId = "test-project";

        // Act & Assert
        var ex = Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.CreateOverrideAsync("non-existent-id", projectId, null));

        Assert.That(ex!.Message, Does.Contain("not found"));
    }

    [Test]
    public async Task CreateOverrideAsync_ThrowsWhenPromptIsNotGlobal()
    {
        // Arrange
        var projectId = "test-project";
        var projectPrompt = await _service.CreatePromptAsync("ProjectPrompt", "Test", SessionMode.Build, projectId);

        // Act & Assert
        var ex = Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.CreateOverrideAsync(projectPrompt.Id, "another-project", null));

        Assert.That(ex!.Message, Does.Contain("global prompt"));
    }

    [Test]
    public async Task CreateOverrideAsync_ProjectPromptAppearsInGetPromptsForProject()
    {
        // Arrange
        await _service.EnsureDefaultPromptsAsync();
        var globalBuildPrompt = _service.GetAllPrompts().First(p => p.Name == "Build");
        var projectId = "test-project";

        // Act
        await _service.CreateOverrideAsync(globalBuildPrompt.Id, projectId, "Custom message");
        var projectPrompts = _service.GetPromptsForProject(projectId);

        // Assert
        var buildPrompt = projectPrompts.FirstOrDefault(p => p.Name == "Build");
        Assert.Multiple(() =>
        {
            Assert.That(buildPrompt, Is.Not.Null);
            Assert.That(buildPrompt!.ProjectId, Is.EqualTo(projectId));
            Assert.That(buildPrompt.IsOverride, Is.True);
            Assert.That(buildPrompt.InitialMessage, Is.EqualTo("Custom message"));
        });
    }

    [Test]
    public async Task RemoveOverrideAsync_DeletesProjectPromptAndReturnsGlobalPrompt()
    {
        // Arrange
        await _service.EnsureDefaultPromptsAsync();
        var globalBuildPrompt = _service.GetAllPrompts().First(p => p.Name == "Build");
        var projectId = "test-project";

        // Create an override
        var overridePrompt = await _service.CreateOverrideAsync(globalBuildPrompt.Id, projectId, "Custom message");

        // Act
        var result = await _service.RemoveOverrideAsync(overridePrompt.Id);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Id, Is.EqualTo(globalBuildPrompt.Id));
            Assert.That(result.Name, Is.EqualTo("Build"));
            Assert.That(result.ProjectId, Is.Null); // Global prompt
        });

        // Verify the override was deleted
        var deletedPrompt = _service.GetPrompt(overridePrompt.Id);
        Assert.That(deletedPrompt, Is.Null);
    }

    [Test]
    public async Task RemoveOverrideAsync_ThrowsWhenPromptNotFound()
    {
        // Act & Assert
        var ex = Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.RemoveOverrideAsync("non-existent-id"));

        Assert.That(ex!.Message, Does.Contain("not found"));
    }

    [Test]
    public async Task RemoveOverrideAsync_ThrowsWhenPromptIsNotProjectScoped()
    {
        // Arrange
        await _service.EnsureDefaultPromptsAsync();
        var globalPrompt = _service.GetAllPrompts().First();

        // Act & Assert
        var ex = Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.RemoveOverrideAsync(globalPrompt.Id));

        Assert.That(ex!.Message, Does.Contain("project prompt"));
    }

    [Test]
    public async Task RemoveOverrideAsync_ThrowsWhenNoGlobalPromptWithSameName()
    {
        // Arrange - Create a project-only prompt (no global equivalent)
        var projectId = "test-project";
        var projectOnlyPrompt = await _service.CreatePromptAsync(
            "UniqueProjectPrompt",
            "Test message",
            SessionMode.Build,
            projectId);

        // Act & Assert
        var ex = Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.RemoveOverrideAsync(projectOnlyPrompt.Id));

        Assert.That(ex!.Message, Does.Contain("not an override"));
    }

    [Test]
    public async Task RemoveOverrideAsync_ProjectPromptNoLongerAppearsInGetPromptsForProject()
    {
        // Arrange
        await _service.EnsureDefaultPromptsAsync();
        var globalBuildPrompt = _service.GetAllPrompts().First(p => p.Name == "Build");
        var projectId = "test-project";

        // Create an override
        var overridePrompt = await _service.CreateOverrideAsync(globalBuildPrompt.Id, projectId, "Custom message");

        // Act
        await _service.RemoveOverrideAsync(overridePrompt.Id);
        var projectPrompts = _service.GetPromptsForProject(projectId);

        // Assert - Global Build prompt should now be included, not the override
        var buildPrompt = projectPrompts.FirstOrDefault(p => p.Name == "Build");
        Assert.Multiple(() =>
        {
            Assert.That(buildPrompt, Is.Not.Null);
            Assert.That(buildPrompt!.ProjectId, Is.Null); // Global prompt
            Assert.That(buildPrompt.IsOverride, Is.False);
        });
    }

    [Test]
    public void LoadDefaultPromptDefinitions_ReturnsAllPrompts()
    {
        // Act
        var definitions = AgentPromptService.LoadDefaultPromptDefinitions();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(definitions, Has.Count.EqualTo(13));
            Assert.That(definitions.Any(d => d.Name == "Plan" && d.Mode == "plan"), Is.True);
            Assert.That(definitions.Any(d => d.Name == "Build" && d.Mode == "build"), Is.True);
            Assert.That(definitions.Any(d => d.Name == "Rebase"), Is.True);
            Assert.That(definitions.Any(d => d.Name == "Rebase, Test and Merge"), Is.True);
            Assert.That(definitions.Any(d => d.Name == "Create a PR"), Is.True);
            Assert.That(definitions.Any(d => d.Name == "Fix tests"), Is.True);
            Assert.That(definitions.Any(d => d.Name == "Build and Merge"), Is.True);
            Assert.That(definitions.Any(d => d.Name == "IssueModify" && d.Category == "IssueAgent"), Is.True);
            Assert.That(definitions.Any(d => d.Name == "Review PR comments"), Is.True);
            Assert.That(definitions.Any(d => d.Name == "IssueAgentSystem" && d.SessionType == "issueAgentSystem"), Is.True);
            Assert.That(definitions.Any(d => d.Name == "IssueAgentModification" && d.SessionType == "issueAgentModification"), Is.True);
            Assert.That(definitions.Any(d => d.Name == "Verify" && d.Category == "IssueAgent" && d.Mode == "build"), Is.True);
            Assert.That(definitions.Any(d => d.Name == "Expand feature" && d.Category == "IssueAgent" && d.Mode == "plan"), Is.True);
        });
    }

    [Test]
    public async Task EnsureDefaultPromptsAsync_CreatesAllNewPrompts()
    {
        // Act
        await _service.EnsureDefaultPromptsAsync();

        // Assert - verify all 13 prompts were created
        var allPrompts = _dataStore.AgentPrompts;
        Assert.Multiple(() =>
        {
            Assert.That(allPrompts, Has.Count.EqualTo(13));
            Assert.That(allPrompts.Any(p => p.Name == "Rebase, Test and Merge" && p.Mode == SessionMode.Build), Is.True);
            Assert.That(allPrompts.Any(p => p.Name == "Create a PR" && p.Mode == SessionMode.Build), Is.True);
            Assert.That(allPrompts.Any(p => p.Name == "Fix tests" && p.Mode == SessionMode.Build), Is.True);
            Assert.That(allPrompts.Any(p => p.Name == "Build and Merge" && p.Mode == SessionMode.Build), Is.True);
            Assert.That(allPrompts.Any(p => p.Name == "IssueModify" && p.Mode == SessionMode.Build), Is.True);
            Assert.That(allPrompts.Any(p => p.Name == "Review PR comments" && p.Mode == SessionMode.Build), Is.True);
        });
    }

    [Test]
    public async Task EnsureDefaultPromptsAsync_SetsCategoryOnIssueModifyPrompt()
    {
        // Act
        await _service.EnsureDefaultPromptsAsync();

        // Assert - IssueModify should have IssueAgent category
        var issueModifyPrompt = _dataStore.AgentPrompts
            .FirstOrDefault(p => p.Name == "IssueModify");

        Assert.Multiple(() =>
        {
            Assert.That(issueModifyPrompt, Is.Not.Null);
            Assert.That(issueModifyPrompt!.Category, Is.EqualTo(PromptCategory.IssueAgent));
            Assert.That(issueModifyPrompt.SessionType, Is.Null);
        });
    }

    [Test]
    public async Task EnsureDefaultPromptsAsync_StandardPromptsHaveStandardCategory()
    {
        // Act
        await _service.EnsureDefaultPromptsAsync();

        // Assert - standard prompts should have Standard category
        var buildPrompt = _dataStore.AgentPrompts.First(p => p.Name == "Build");
        var planPrompt = _dataStore.AgentPrompts.First(p => p.Name == "Plan");

        Assert.Multiple(() =>
        {
            Assert.That(buildPrompt.Category, Is.EqualTo(PromptCategory.Standard));
            Assert.That(planPrompt.Category, Is.EqualTo(PromptCategory.Standard));
        });
    }

    [Test]
    public async Task EnsureDefaultPromptsAsync_MigratesExistingPromptCategory()
    {
        // Arrange - create IssueModify prompt with Standard category (pre-migration state)
        var prompt = new AgentPrompt
        {
            Id = "old-im",
            Name = "IssueModify",
            InitialMessage = "old message",
            Mode = SessionMode.Build,
            Category = PromptCategory.Standard
        };
        await _dataStore.AddAgentPromptAsync(prompt);

        // Act - EnsureDefaults should migrate category
        await _service.EnsureDefaultPromptsAsync();

        // Assert
        var updated = _dataStore.AgentPrompts.First(p => p.Name == "IssueModify");
        Assert.That(updated.Category, Is.EqualTo(PromptCategory.IssueAgent));
    }

    [Test]
    public async Task GetAllPrompts_ExcludesIssueAgentCategoryPrompts()
    {
        // Arrange
        await _service.EnsureDefaultPromptsAsync();

        // Act
        var prompts = _service.GetAllPrompts();

        // Assert - IssueModify has IssueAgent category and should be excluded
        Assert.Multiple(() =>
        {
            Assert.That(prompts, Has.Count.EqualTo(8));
            Assert.That(prompts.Any(p => p.Name == "IssueModify"), Is.False);
            Assert.That(prompts.Any(p => p.Name == "Plan"), Is.True);
            Assert.That(prompts.Any(p => p.Name == "Build"), Is.True);
        });
    }

    [Test]
    public async Task GetIssueAgentUserPrompts_ReturnsOnlyIssueAgentCategoryPrompts()
    {
        // Arrange
        await _service.EnsureDefaultPromptsAsync();

        // Act
        var prompts = _service.GetIssueAgentUserPrompts();

        // Assert - should include IssueModify, Verify, and Expand feature (IssueAgent category, no SessionType)
        Assert.Multiple(() =>
        {
            Assert.That(prompts, Has.Count.EqualTo(3));
            Assert.That(prompts.Any(p => p.Name == "IssueModify"), Is.True);
            Assert.That(prompts.Any(p => p.Name == "Verify"), Is.True);
            Assert.That(prompts.Any(p => p.Name == "Expand feature"), Is.True);
            Assert.That(prompts.All(p => p.Category == PromptCategory.IssueAgent), Is.True);
            Assert.That(prompts.All(p => p.SessionType == null), Is.True);
        });
    }

    [Test]
    public async Task GetIssueAgentPromptsForProject_ReturnsMergedPromptsWithOverrideDetection()
    {
        // Arrange
        await _service.EnsureDefaultPromptsAsync();
        var projectId = "test-project";

        // Create a project-level IssueAgent prompt that overrides IssueModify
        var projectPrompt = new AgentPrompt
        {
            Id = "proj-im",
            Name = "IssueModify",
            InitialMessage = "Custom project issue modify message",
            Mode = SessionMode.Build,
            ProjectId = projectId,
            Category = PromptCategory.IssueAgent
        };
        await _dataStore.AddAgentPromptAsync(projectPrompt);

        // Act
        var prompts = _service.GetIssueAgentPromptsForProject(projectId);

        // Assert - should include the project override plus non-overridden global prompts
        Assert.Multiple(() =>
        {
            Assert.That(prompts, Has.Count.EqualTo(3));
            var issueModify = prompts.First(p => p.Name == "IssueModify");
            Assert.That(issueModify.ProjectId, Is.EqualTo(projectId));
            Assert.That(issueModify.IsOverride, Is.True);
            Assert.That(issueModify.InitialMessage, Is.EqualTo("Custom project issue modify message"));
            Assert.That(prompts.Any(p => p.Name == "Verify"), Is.True);
            Assert.That(prompts.Any(p => p.Name == "Expand feature"), Is.True);
        });
    }

    [Test]
    public async Task GetIssueAgentPromptsForProject_ReturnsGlobalPromptsWhenNoProjectOverride()
    {
        // Arrange
        await _service.EnsureDefaultPromptsAsync();

        // Act
        var prompts = _service.GetIssueAgentPromptsForProject("test-project");

        // Assert - should include the global IssueAgent prompts
        Assert.Multiple(() =>
        {
            Assert.That(prompts, Has.Count.EqualTo(3));
            Assert.That(prompts.Any(p => p.Name == "IssueModify"), Is.True);
            Assert.That(prompts.Any(p => p.Name == "Verify"), Is.True);
            Assert.That(prompts.Any(p => p.Name == "Expand feature"), Is.True);
            Assert.That(prompts.All(p => p.ProjectId == null), Is.True);
            Assert.That(prompts.All(p => p.IsOverride == false), Is.True);
        });
    }

    [Test]
    public async Task GetPromptsForProject_ExcludesIssueAgentCategoryPrompts()
    {
        // Arrange
        await _service.EnsureDefaultPromptsAsync();

        // Act
        var prompts = _service.GetPromptsForProject("test-project");

        // Assert - IssueModify (IssueAgent category) should not appear
        Assert.Multiple(() =>
        {
            Assert.That(prompts, Has.Count.EqualTo(8));
            Assert.That(prompts.Any(p => p.Name == "IssueModify"), Is.False);
        });
    }
}
