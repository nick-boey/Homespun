using Bunit;
using Homespun.Client.Components;
using Homespun.Client.Services;
using Homespun.Shared.Models.Sessions;
using Homespun.Tests.Helpers;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Homespun.Tests.Components;

/// <summary>
/// bUnit tests for the ProjectToolbar component.
/// Tests basic rendering and button state management.
/// Note: Agent launching tests are covered in integration tests since
/// the component now uses UnifiedAgentLauncher which has many dependencies.
/// </summary>
[TestFixture]
public class ProjectToolbarTests : BunitTestContext
{
    private MockHttpMessageHandler _mockHandler = null!;

    private static readonly List<AgentPrompt> MockPrompts =
    [
        new AgentPrompt { Id = "prompt-1", Name = "Build", Mode = SessionMode.Build },
        new AgentPrompt { Id = "prompt-2", Name = "Plan", Mode = SessionMode.Plan }
    ];

    [SetUp]
    public new void Setup()
    {
        base.Setup();
        _mockHandler = new MockHttpMessageHandler();
        _mockHandler.RespondWith("api/agent-prompts/ensure-defaults", new { });
        _mockHandler.RespondWith("api/agent-prompts", MockPrompts);
        _mockHandler.RespondWith("api/agent-prompts/project/project-1", MockPrompts);

        var httpClient = _mockHandler.CreateClient();
        Services.AddSingleton(new HttpAgentPromptApiService(httpClient));
        Services.AddSingleton(new HttpSessionApiService(httpClient));
        Services.AddSingleton(new HttpCloneApiService(httpClient));
        Services.AddSingleton<IAgentStartupTracker>(new AgentStartupTracker());
        Services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
    }

    [Test]
    public void DisablesButtons_WhenNoIssueSelected()
    {
        var cut = Render<ProjectToolbar>(p =>
        {
            p.Add(x => x.ProjectId, "project-1");
            p.Add(x => x.SelectedIssueId, null);
        });

        var editButton = cut.Find("[data-testid='toolbar-edit-button']");
        var runButton = cut.Find("[data-testid='toolbar-run-button']");

        Assert.That(editButton.HasAttribute("disabled"), Is.True);
        Assert.That(runButton.HasAttribute("disabled"), Is.True);
    }

    [Test]
    public void EnablesButtons_WhenIssueSelected()
    {
        var cut = Render<ProjectToolbar>(p =>
        {
            p.Add(x => x.ProjectId, "project-1");
            p.Add(x => x.SelectedIssueId, "TEST-001");
        });

        var editButton = cut.Find("[data-testid='toolbar-edit-button']");
        var runButton = cut.Find("[data-testid='toolbar-run-button']");

        Assert.That(editButton.HasAttribute("disabled"), Is.False);
        Assert.That(runButton.HasAttribute("disabled"), Is.False);
    }

    [Test]
    public void DisablesRunButton_WhenAgentIsRunning()
    {
        var cut = Render<ProjectToolbar>(p =>
        {
            p.Add(x => x.ProjectId, "project-1");
            p.Add(x => x.SelectedIssueId, "TEST-001");
            p.Add(x => x.IsAgentRunning, true);
        });

        var editButton = cut.Find("[data-testid='toolbar-edit-button']");
        var runButton = cut.Find("[data-testid='toolbar-run-button']");

        // Edit should still be enabled
        Assert.That(editButton.HasAttribute("disabled"), Is.False);
        // Run should be disabled when agent is running
        Assert.That(runButton.HasAttribute("disabled"), Is.True);
    }

    [Test]
    public void InvokesOnEditClick_WithSelectedIssueId()
    {
        string? clickedIssueId = null;
        var cut = Render<ProjectToolbar>(p =>
        {
            p.Add(x => x.ProjectId, "project-1");
            p.Add(x => x.SelectedIssueId, "TEST-001");
            p.Add(x => x.OnEditClick, EventCallback.Factory.Create<string>(this, id => clickedIssueId = id));
        });

        cut.Find("[data-testid='toolbar-edit-button']").Click();

        Assert.That(clickedIssueId, Is.EqualTo("TEST-001"));
    }

    [Test]
    public void ShowsDropdown_WhenRunButtonClicked()
    {
        var cut = Render<ProjectToolbar>(p =>
        {
            p.Add(x => x.ProjectId, "project-1");
            p.Add(x => x.SelectedIssueId, "TEST-001");
        });

        // Initially dropdown should not be visible
        Assert.That(cut.FindAll(".toolbar-agent-dropdown"), Is.Empty);

        // Click run button
        cut.Find("[data-testid='toolbar-run-button']").Click();

        // Dropdown should now be visible
        var dropdown = cut.Find(".toolbar-agent-dropdown");
        Assert.That(dropdown, Is.Not.Null);
    }

    [Test]
    public void HasCorrectContainerClass()
    {
        var cut = Render<ProjectToolbar>(p =>
        {
            p.Add(x => x.ProjectId, "project-1");
        });

        var toolbar = cut.Find(".project-toolbar");
        Assert.That(toolbar, Is.Not.Null);
    }

    [Test]
    public void RendersEditButtonWithPencilIcon()
    {
        var cut = Render<ProjectToolbar>(p =>
        {
            p.Add(x => x.ProjectId, "project-1");
        });

        var editButton = cut.Find("[data-testid='toolbar-edit-button']");
        Assert.That(editButton.InnerHtml, Does.Contain("bi-pencil"));
    }

    [Test]
    public void RendersRunButtonWithPlayIcon()
    {
        var cut = Render<ProjectToolbar>(p =>
        {
            p.Add(x => x.ProjectId, "project-1");
        });

        var runButton = cut.Find("[data-testid='toolbar-run-button']");
        Assert.That(runButton.InnerHtml, Does.Contain("bi-play-fill"));
    }

    [Test]
    public void HidesDropdown_WhenRunButtonClickedAgain()
    {
        var cut = Render<ProjectToolbar>(p =>
        {
            p.Add(x => x.ProjectId, "project-1");
            p.Add(x => x.SelectedIssueId, "TEST-001");
        });

        // Open dropdown
        cut.Find("[data-testid='toolbar-run-button']").Click();
        Assert.That(cut.FindAll(".toolbar-agent-dropdown"), Has.Count.EqualTo(1));

        // Click again to close
        cut.Find("[data-testid='toolbar-run-button']").Click();

        // Dropdown should be closed
        Assert.That(cut.FindAll(".toolbar-agent-dropdown"), Is.Empty);
    }
}
