using Fleece.Core.Models;
using Homespun.Client.Services;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.Testing;
using Homespun.Shared.Models.Fleece;
using static Homespun.Shared.Models.PullRequests.BranchNameGenerator;

namespace Homespun.Tests.Features.ClaudeCode;

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
    public void GetAllPrompts_ReturnsEmptyListWhenNoPrompts()
    {
        var result = _service.GetAllPrompts();

        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetAllPrompts_ReturnsAllPrompts()
    {
        await _dataStore.AddAgentPromptAsync(new AgentPrompt { Id = "p1", Name = "Plan" });
        await _dataStore.AddAgentPromptAsync(new AgentPrompt { Id = "p2", Name = "Build" });

        var result = _service.GetAllPrompts();

        Assert.That(result, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task GetPrompt_ReturnsPromptById()
    {
        await _dataStore.AddAgentPromptAsync(new AgentPrompt { Id = "test1", Name = "Test" });

        var result = _service.GetPrompt("test1");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Name, Is.EqualTo("Test"));
    }

    [Test]
    public void GetPrompt_ReturnsNullForNonExistent()
    {
        var result = _service.GetPrompt("nonexistent");

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task CreatePromptAsync_CreatesNewPrompt()
    {
        var result = await _service.CreatePromptAsync("New Prompt", "Initial message", SessionMode.Build);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Name, Is.EqualTo("New Prompt"));
            Assert.That(result.InitialMessage, Is.EqualTo("Initial message"));
            Assert.That(result.Mode, Is.EqualTo(SessionMode.Build));
            Assert.That(_dataStore.AgentPrompts, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task UpdatePromptAsync_UpdatesExistingPrompt()
    {
        var original = await _service.CreatePromptAsync("Original", "Original message", SessionMode.Plan);

        var updated = await _service.UpdatePromptAsync(original.Id, "Updated", "Updated message", SessionMode.Build);

        Assert.Multiple(() =>
        {
            Assert.That(updated.Name, Is.EqualTo("Updated"));
            Assert.That(updated.InitialMessage, Is.EqualTo("Updated message"));
            Assert.That(updated.Mode, Is.EqualTo(SessionMode.Build));
        });
    }

    [Test]
    public async Task DeletePromptAsync_RemovesPrompt()
    {
        var prompt = await _service.CreatePromptAsync("ToDelete", null, SessionMode.Build);

        await _service.DeletePromptAsync(prompt.Id);

        Assert.That(_service.GetPrompt(prompt.Id), Is.Null);
    }

    #region Template Rendering Tests

    [Test]
    public void RenderTemplate_WithIssueContext_ReplacesAllPlaceholders()
    {
        // Simulates building a PromptContext from IssueResponse data,
        // as IssueDetailPanel should do before sending the initial message
        var issue = new IssueResponse
        {
            Id = "PtJ11e",
            Title = "Fix template rendering bug",
            Description = "Templates should have variables replaced",
            Type = IssueType.Bug
        };
        var branchName = GenerateBranchName(issue);

        var context = new PromptContext
        {
            Title = issue.Title,
            Id = issue.Id,
            Description = issue.Description,
            Branch = branchName,
            Type = issue.Type.ToString()
        };

        var template = "## Issue: {{title}}\n**ID:** {{id}}\n**Type:** {{type}}\n**Branch:** {{branch}}\n### Description\n{{description}}";

        var result = _service.RenderTemplate(template, context);

        Assert.Multiple(() =>
        {
            Assert.That(result, Does.Contain("Fix template rendering bug"));
            Assert.That(result, Does.Contain("PtJ11e"));
            Assert.That(result, Does.Contain("Bug"));
            Assert.That(result, Does.Contain(branchName));
            Assert.That(result, Does.Contain("Templates should have variables replaced"));
            Assert.That(result, Does.Not.Contain("{{title}}"));
            Assert.That(result, Does.Not.Contain("{{id}}"));
            Assert.That(result, Does.Not.Contain("{{type}}"));
            Assert.That(result, Does.Not.Contain("{{branch}}"));
            Assert.That(result, Does.Not.Contain("{{description}}"));
        });
    }

    [Test]
    public void ClientSideRenderTemplate_WithIssueContext_ReplacesAllPlaceholders()
    {
        // Tests the client-side static RenderTemplate (used in Blazor components)
        var issue = new IssueResponse
        {
            Id = "ABC123",
            Title = "Add authentication",
            Description = "Implement OAuth2",
            Type = IssueType.Feature
        };
        var branchName = GenerateBranchName(issue);

        var context = new PromptContext
        {
            Title = issue.Title,
            Id = issue.Id,
            Description = issue.Description,
            Branch = branchName,
            Type = issue.Type.ToString()
        };

        var template = "Working on {{title}} ({{id}}) - {{type}}";

        var result = HttpAgentPromptApiService.RenderTemplate(template, context);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo($"Working on Add authentication (ABC123) - Feature"));
            Assert.That(result, Does.Not.Contain("{{"));
        });
    }

    [Test]
    public void RenderTemplate_ReplacesPlaceholders()
    {
        var template = "Working on {{title}} ({{id}})";
        var context = new PromptContext
        {
            Title = "My Feature",
            Id = "abc123",
            Description = "A description",
            Branch = "feature/my-feature",
            Type = "Feature"
        };

        var result = _service.RenderTemplate(template, context);

        Assert.That(result, Is.EqualTo("Working on My Feature (abc123)"));
    }

    [Test]
    public void RenderTemplate_ReplacesAllPlaceholders()
    {
        var template = "{{title}}\nID: {{id}}\nType: {{type}}\nBranch: {{branch}}\nDescription: {{description}}";
        var context = new PromptContext
        {
            Title = "Test Feature",
            Id = "XYZ789",
            Description = "This is a test",
            Branch = "test-branch",
            Type = "Bug"
        };

        var result = _service.RenderTemplate(template, context);

        Assert.That(result, Does.Contain("Test Feature"));
        Assert.That(result, Does.Contain("XYZ789"));
        Assert.That(result, Does.Contain("Bug"));
        Assert.That(result, Does.Contain("test-branch"));
        Assert.That(result, Does.Contain("This is a test"));
    }

    [Test]
    public void RenderTemplate_HandlesNullDescription()
    {
        var template = "Title: {{title}}, Description: {{description}}";
        var context = new PromptContext
        {
            Title = "Test",
            Id = "123",
            Description = null,
            Branch = "branch",
            Type = "Task"
        };

        var result = _service.RenderTemplate(template, context);

        Assert.That(result, Is.EqualTo("Title: Test, Description: "));
    }

    [Test]
    public void RenderTemplate_HandlesNullTemplate()
    {
        var context = new PromptContext
        {
            Title = "Test",
            Id = "123",
            Description = null,
            Branch = "branch",
            Type = "Task"
        };

        var result = _service.RenderTemplate(null, context);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void RenderTemplate_IsCaseInsensitive()
    {
        var template = "{{TITLE}} and {{Title}} and {{title}}";
        var context = new PromptContext
        {
            Title = "Test",
            Id = "123",
            Description = null,
            Branch = "branch",
            Type = "Task"
        };

        var result = _service.RenderTemplate(template, context);

        Assert.That(result, Is.EqualTo("Test and Test and Test"));
    }

    [Test]
    public void RenderTemplate_WithFullEntityContext_MatchesIssueDetailPanelBehavior()
    {
        // This test verifies that AgentControlPanel now renders templates identically
        // to IssueDetailPanel when provided with full entity context
        var template = "## Issue: {{title}}\n\n**ID:** {{id}}\n**Type:** {{type}}\n**Branch:** {{branch}}\n\n### Description\n{{description}}";

        // Simulates the full context that IssueDetailPanel passes
        var context = new PromptContext
        {
            Title = "Fix inline agent run panel",
            Id = "WtnUDM",
            Description = "When using the inline agent run panel, selecting a prompt and running it successfully creates a new session but it doesn't inject the first prompt.",
            Branch = "task/fix-inline-agent+WtnUDM",
            Type = "Task"
        };

        var result = _service.RenderTemplate(template, context);

        Assert.Multiple(() =>
        {
            Assert.That(result, Does.Contain("Fix inline agent run panel"));
            Assert.That(result, Does.Contain("WtnUDM"));
            Assert.That(result, Does.Contain("Task"));
            Assert.That(result, Does.Contain("task/fix-inline-agent+WtnUDM"));
            Assert.That(result, Does.Contain("doesn't inject the first prompt"));
            Assert.That(result, Does.Not.Contain("{{"));
        });
    }

    [Test]
    public void RenderTemplate_WithPartialContext_HandlesEmptyFields()
    {
        // This test verifies that AgentControlPanel handles partial context gracefully
        // (e.g., when only branch and ID are known, but not title/description/type)
        var template = "Working on {{title}} ({{id}}) on branch {{branch}}";

        // Simulates context from AgentControlPanel when entity details aren't available
        var context = new PromptContext
        {
            Id = "clone:feature/my-branch",
            Title = string.Empty,  // Not available
            Description = null,    // Not available
            Branch = "feature/my-branch",
            Type = string.Empty    // Not available
        };

        var result = _service.RenderTemplate(template, context);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo("Working on  (clone:feature/my-branch) on branch feature/my-branch"));
            Assert.That(result, Does.Not.Contain("{{"));
        });
    }

    #endregion

    #region Default Prompts Tests

    [Test]
    public async Task EnsureDefaultPromptsAsync_CreatesDefaultsWhenEmpty()
    {
        await _service.EnsureDefaultPromptsAsync();

        var prompts = _service.GetAllPrompts();
        Assert.That(prompts, Has.Count.EqualTo(3)); // Plan, Build, Rebase
        Assert.That(prompts.Any(p => p.Name == "Plan"), Is.True);
        Assert.That(prompts.Any(p => p.Name == "Build"), Is.True);
        Assert.That(prompts.Any(p => p.Name == "Rebase"), Is.True);
    }

    [Test]
    public async Task EnsureDefaultPromptsAsync_DoesNotDuplicateExisting()
    {
        await _service.CreatePromptAsync("CustomPrompt", "Custom message", SessionMode.Build);

        await _service.EnsureDefaultPromptsAsync();

        var prompts = _service.GetAllPrompts();
        Assert.That(prompts, Has.Count.EqualTo(4)); // Custom + 3 defaults (Plan, Build, Rebase)
    }

    [Test]
    public async Task EnsureDefaultPromptsAsync_SetsCorrectModes()
    {
        await _service.EnsureDefaultPromptsAsync();

        var planPrompt = _service.GetAllPrompts().FirstOrDefault(p => p.Name == "Plan");
        var buildPrompt = _service.GetAllPrompts().FirstOrDefault(p => p.Name == "Build");
        var rebasePrompt = _service.GetAllPrompts().FirstOrDefault(p => p.Name == "Rebase");

        Assert.Multiple(() =>
        {
            Assert.That(planPrompt!.Mode, Is.EqualTo(SessionMode.Plan));
            Assert.That(buildPrompt!.Mode, Is.EqualTo(SessionMode.Build));
            Assert.That(rebasePrompt!.Mode, Is.EqualTo(SessionMode.Build));
        });
    }

    #endregion

    #region Project-Specific Prompt Tests

    [Test]
    public async Task GetAllPrompts_ReturnsOnlyGlobalPrompts()
    {
        await _service.CreatePromptAsync("Global Prompt", "message", SessionMode.Build);
        await _service.CreatePromptAsync("Project Prompt", "message", SessionMode.Build, "project-1");

        var result = _service.GetAllPrompts();

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Name, Is.EqualTo("Global Prompt"));
    }

    [Test]
    public async Task GetProjectPrompts_ReturnsOnlyProjectPrompts()
    {
        await _service.CreatePromptAsync("Global Prompt", "message", SessionMode.Build);
        await _service.CreatePromptAsync("Project Prompt", "message", SessionMode.Build, "project-1");
        await _service.CreatePromptAsync("Other Project Prompt", "message", SessionMode.Plan, "project-2");

        var result = _service.GetProjectPrompts("project-1");

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Name, Is.EqualTo("Project Prompt"));
    }

    [Test]
    public async Task GetPromptsForProject_ReturnsCombinedProjectAndGlobalPromptsWithDifferentNames()
    {
        await _service.CreatePromptAsync("Global Plan", "message", SessionMode.Plan);
        await _service.CreatePromptAsync("Global Rebase", "message", SessionMode.Build);
        await _service.CreatePromptAsync("Project Build", "message", SessionMode.Build, "project-1");

        var result = _service.GetPromptsForProject("project-1");

        // All 3 have different names so no override occurs
        Assert.That(result, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task GetPromptsForProject_ProjectPromptsAppearFirst()
    {
        await _service.CreatePromptAsync("Global Plan", "message", SessionMode.Plan);
        await _service.CreatePromptAsync("Project Build", "message", SessionMode.Build, "project-1");

        var result = _service.GetPromptsForProject("project-1");

        Assert.That(result[0].Name, Is.EqualTo("Project Build"));
        Assert.That(result[1].Name, Is.EqualTo("Global Plan"));
    }

    [Test]
    public async Task CreatePromptAsync_WithProjectId_SetsProjectId()
    {
        var result = await _service.CreatePromptAsync("Project Prompt", "message", SessionMode.Build, "project-1");

        Assert.That(result.ProjectId, Is.EqualTo("project-1"));
    }

    [Test]
    public async Task CreatePromptAsync_WithoutProjectId_HasNullProjectId()
    {
        var result = await _service.CreatePromptAsync("Global Prompt", "message", SessionMode.Build);

        Assert.That(result.ProjectId, Is.Null);
    }

    [Test]
    public async Task EnsureDefaultPromptsAsync_DoesNotCreateProjectSpecificDefaults()
    {
        // Create a project-specific prompt named "Plan" - should not prevent global default creation
        await _service.CreatePromptAsync("Plan", "project plan", SessionMode.Plan, "project-1");

        await _service.EnsureDefaultPromptsAsync();

        // Should still have created the global Plan default since the existing one is project-scoped
        var globalPrompts = _service.GetAllPrompts();
        Assert.That(globalPrompts.Any(p => p.Name == "Plan"), Is.True);
    }

    [Test]
    public async Task GetPromptsForProject_ReturnsEmptyWhenNoPrompts()
    {
        var result = _service.GetPromptsForProject("nonexistent-project");

        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task RemoveProject_CascadeDeletesProjectPrompts()
    {
        await _dataStore.AddProjectAsync(new Project
        {
            Id = "project-1",
            Name = "Test Project",
            LocalPath = "/tmp/test",
            DefaultBranch = "main"
        });
        await _service.CreatePromptAsync("Project Prompt", "message", SessionMode.Build, "project-1");
        await _service.CreatePromptAsync("Global Prompt", "message", SessionMode.Build);

        await _dataStore.RemoveProjectAsync("project-1");

        Assert.That(_service.GetProjectPrompts("project-1"), Is.Empty);
        Assert.That(_service.GetAllPrompts(), Has.Count.EqualTo(1));
        Assert.That(_service.GetAllPrompts()[0].Name, Is.EqualTo("Global Prompt"));
    }

    #endregion

    #region Prompt Override Tests

    [Test]
    public async Task GetPromptsForProject_ExcludesGlobalPromptsOverriddenByProject()
    {
        // Create global "Build" prompt
        await _service.CreatePromptAsync("Build", "global build message", SessionMode.Build);
        // Create project "Build" prompt that should override the global
        await _service.CreatePromptAsync("Build", "project build message", SessionMode.Build, "project-1");

        var result = _service.GetPromptsForProject("project-1");

        // Should only have one "Build" prompt - the project one
        Assert.That(result.Count(p => p.Name == "Build"), Is.EqualTo(1));
        Assert.That(result.First(p => p.Name == "Build").InitialMessage, Is.EqualTo("project build message"));
        Assert.That(result.First(p => p.Name == "Build").ProjectId, Is.EqualTo("project-1"));
    }

    [Test]
    public async Task GetPromptsForProject_ReturnsGlobalPromptsNotOverridden()
    {
        // Create global "Build" and "Plan" prompts
        await _service.CreatePromptAsync("Build", "global build", SessionMode.Build);
        await _service.CreatePromptAsync("Plan", "global plan", SessionMode.Plan);
        // Create project "Build" prompt only
        await _service.CreatePromptAsync("Build", "project build", SessionMode.Build, "project-1");

        var result = _service.GetPromptsForProject("project-1");

        // Should have project "Build" + global "Plan"
        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result.Any(p => p.Name == "Build" && p.ProjectId == "project-1"), Is.True);
        Assert.That(result.Any(p => p.Name == "Plan" && p.ProjectId == null), Is.True);
    }

    [Test]
    public async Task GetPromptsForProject_CaseInsensitiveNameMatching()
    {
        // Create global "Build" prompt
        await _service.CreatePromptAsync("Build", "global build", SessionMode.Build);
        // Create project "build" prompt (lowercase) that should override
        await _service.CreatePromptAsync("build", "project build", SessionMode.Build, "project-1");

        var result = _service.GetPromptsForProject("project-1");

        // Should only have one Build prompt - override works case-insensitively
        Assert.That(result.Count(p => p.Name.Equals("build", StringComparison.OrdinalIgnoreCase)), Is.EqualTo(1));
        Assert.That(result.First(p => p.Name.Equals("build", StringComparison.OrdinalIgnoreCase)).ProjectId, Is.EqualTo("project-1"));
    }

    [Test]
    public async Task GetGlobalPromptsNotOverridden_ReturnsOnlyNonOverriddenGlobals()
    {
        // Create global prompts
        await _service.CreatePromptAsync("Build", "global build", SessionMode.Build);
        await _service.CreatePromptAsync("Plan", "global plan", SessionMode.Plan);
        await _service.CreatePromptAsync("Rebase", "global rebase", SessionMode.Build);
        // Create project "Build" prompt that overrides the global
        await _service.CreatePromptAsync("Build", "project build", SessionMode.Build, "project-1");

        var result = _service.GetGlobalPromptsNotOverridden("project-1");

        // Should return only "Plan" and "Rebase" (not "Build" which is overridden)
        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result.Any(p => p.Name == "Plan"), Is.True);
        Assert.That(result.Any(p => p.Name == "Rebase"), Is.True);
        Assert.That(result.Any(p => p.Name == "Build"), Is.False);
    }

    [Test]
    public async Task GetGlobalPromptsNotOverridden_ReturnsAllGlobalsWhenNoOverrides()
    {
        // Create global prompts only
        await _service.CreatePromptAsync("Build", "global build", SessionMode.Build);
        await _service.CreatePromptAsync("Plan", "global plan", SessionMode.Plan);

        var result = _service.GetGlobalPromptsNotOverridden("project-1");

        // Should return all globals since none are overridden
        Assert.That(result, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task GetGlobalPromptsNotOverridden_CaseInsensitiveMatching()
    {
        await _service.CreatePromptAsync("Build", "global build", SessionMode.Build);
        await _service.CreatePromptAsync("BUILD", "project build", SessionMode.Build, "project-1");

        var result = _service.GetGlobalPromptsNotOverridden("project-1");

        // Should be empty since "BUILD" overrides "Build" (case-insensitive)
        Assert.That(result, Is.Empty);
    }

    #endregion
}
