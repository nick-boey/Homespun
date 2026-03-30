using Homespun.Features.Testing;

namespace Homespun.Tests.Features.ClaudeCode;

[TestFixture]
public class AgentPromptDataTests
{
    private MockDataStore _dataStore = null!;

    [SetUp]
    public void SetUp()
    {
        _dataStore = new MockDataStore();
    }

    [Test]
    public void AgentPrompts_InitiallyEmpty()
    {
        Assert.That(_dataStore.AgentPrompts, Is.Empty);
    }

    [Test]
    public async Task AddAgentPromptAsync_AddsPromptToStore()
    {
        var prompt = new AgentPrompt
        {
            Name = "Test Prompt",
            InitialMessage = "Test message",
            Mode = SessionMode.Build
        };

        await _dataStore.AddAgentPromptAsync(prompt);

        Assert.Multiple(() =>
        {
            Assert.That(_dataStore.AgentPrompts, Has.Count.EqualTo(1));
            Assert.That(_dataStore.AgentPrompts[0].Name, Is.EqualTo("Test Prompt"));
        });
    }

    [Test]
    public async Task GetAgentPrompt_ReturnsByNameAndProjectId()
    {
        var prompt = new AgentPrompt
        {
            Name = "Test Prompt"
        };

        await _dataStore.AddAgentPromptAsync(prompt);

        var result = _dataStore.GetAgentPrompt("Test Prompt", null);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Name, Is.EqualTo("Test Prompt"));
    }

    [Test]
    public void GetAgentPrompt_ReturnsNullWhenNotFound()
    {
        var result = _dataStore.GetAgentPrompt("nonexistent", null);

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetAgentPrompt_DistinguishesBetweenGlobalAndProject()
    {
        var globalPrompt = new AgentPrompt { Name = "Build", ProjectId = null };
        var projectPrompt = new AgentPrompt { Name = "Build", ProjectId = "project-1" };

        await _dataStore.AddAgentPromptAsync(globalPrompt);
        await _dataStore.AddAgentPromptAsync(projectPrompt);

        var globalResult = _dataStore.GetAgentPrompt("Build", null);
        var projectResult = _dataStore.GetAgentPrompt("Build", "project-1");

        Assert.Multiple(() =>
        {
            Assert.That(globalResult, Is.Not.Null);
            Assert.That(globalResult!.ProjectId, Is.Null);
            Assert.That(projectResult, Is.Not.Null);
            Assert.That(projectResult!.ProjectId, Is.EqualTo("project-1"));
        });
    }

    [Test]
    public async Task GetAgentPrompt_IsCaseInsensitive()
    {
        var prompt = new AgentPrompt { Name = "Build" };
        await _dataStore.AddAgentPromptAsync(prompt);

        var result = _dataStore.GetAgentPrompt("build", null);
        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public async Task UpdateAgentPromptAsync_UpdatesExistingPrompt()
    {
        var prompt = new AgentPrompt
        {
            Name = "Original Name"
        };

        await _dataStore.AddAgentPromptAsync(prompt);

        prompt.InitialMessage = "Updated message";
        await _dataStore.UpdateAgentPromptAsync(prompt);

        var result = _dataStore.GetAgentPrompt("Original Name", null);
        Assert.That(result!.InitialMessage, Is.EqualTo("Updated message"));
    }

    [Test]
    public async Task RemoveAgentPromptAsync_RemovesPromptFromStore()
    {
        var prompt = new AgentPrompt
        {
            Name = "Test Prompt"
        };

        await _dataStore.AddAgentPromptAsync(prompt);
        await _dataStore.RemoveAgentPromptAsync("Test Prompt", null);

        Assert.Multiple(() =>
        {
            Assert.That(_dataStore.AgentPrompts, Is.Empty);
            Assert.That(_dataStore.GetAgentPrompt("Test Prompt", null), Is.Null);
        });
    }

    [Test]
    public async Task RemoveAgentPromptAsync_OnlyRemovesMatchingScope()
    {
        var globalPrompt = new AgentPrompt { Name = "Build", ProjectId = null };
        var projectPrompt = new AgentPrompt { Name = "Build", ProjectId = "project-1" };

        await _dataStore.AddAgentPromptAsync(globalPrompt);
        await _dataStore.AddAgentPromptAsync(projectPrompt);

        await _dataStore.RemoveAgentPromptAsync("Build", null);

        Assert.Multiple(() =>
        {
            Assert.That(_dataStore.AgentPrompts, Has.Count.EqualTo(1));
            Assert.That(_dataStore.GetAgentPrompt("Build", null), Is.Null);
            Assert.That(_dataStore.GetAgentPrompt("Build", "project-1"), Is.Not.Null);
        });
    }

    [Test]
    public async Task AddAgentPromptAsync_CanAddMultiplePrompts()
    {
        var prompt1 = new AgentPrompt { Name = "Plan", Mode = SessionMode.Plan };
        var prompt2 = new AgentPrompt { Name = "Build", Mode = SessionMode.Build };

        await _dataStore.AddAgentPromptAsync(prompt1);
        await _dataStore.AddAgentPromptAsync(prompt2);

        Assert.Multiple(() =>
        {
            Assert.That(_dataStore.AgentPrompts, Has.Count.EqualTo(2));
            Assert.That(_dataStore.GetAgentPrompt("Plan", null)!.Mode, Is.EqualTo(SessionMode.Plan));
            Assert.That(_dataStore.GetAgentPrompt("Build", null)!.Mode, Is.EqualTo(SessionMode.Build));
        });
    }
}
