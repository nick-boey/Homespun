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
/// bUnit tests for the IssueRowActions component.
/// Tests basic rendering and the edit button functionality.
/// Note: Agent launching tests are covered in integration tests since
/// the component now uses UnifiedAgentLauncher which has many dependencies.
/// </summary>
[TestFixture]
public class IssueRowActionsTests : BunitTestContext
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
    public void Renders_EditButton_WithPencilIcon()
    {
        var cut = Render<IssueRowActions>(p =>
        {
            p.Add(x => x.IssueId, "TEST-001");
            p.Add(x => x.ProjectId, "project-1");
            p.Add(x => x.BranchName, "task/test-issue+TEST-001");
        });

        var editButton = cut.Find("[data-testid='edit-issue-button']");
        Assert.That(editButton, Is.Not.Null);
        Assert.That(editButton.InnerHtml, Does.Contain("bi-pencil"));
    }

    [Test]
    public void Renders_RunAgentButton_WithPlayIcon()
    {
        var cut = Render<IssueRowActions>(p =>
        {
            p.Add(x => x.IssueId, "TEST-001");
            p.Add(x => x.ProjectId, "project-1");
            p.Add(x => x.BranchName, "task/test-issue+TEST-001");
        });

        var runButton = cut.Find("[data-testid='run-agent-button']");
        Assert.That(runButton, Is.Not.Null);
        Assert.That(runButton.InnerHtml, Does.Contain("bi-play"));
    }

    [Test]
    public void HasActionsWrapper_WithCorrectClass()
    {
        var cut = Render<IssueRowActions>(p =>
        {
            p.Add(x => x.IssueId, "TEST-001");
            p.Add(x => x.ProjectId, "project-1");
            p.Add(x => x.BranchName, "task/test-issue+TEST-001");
        });

        var wrapper = cut.Find(".issue-row-actions");
        Assert.That(wrapper, Is.Not.Null);
    }

    [Test]
    public void Invokes_OnEditClick_WhenEditButtonClicked()
    {
        string? clickedIssueId = null;
        var cut = Render<IssueRowActions>(p =>
        {
            p.Add(x => x.IssueId, "TEST-001");
            p.Add(x => x.ProjectId, "project-1");
            p.Add(x => x.BranchName, "task/test-issue+TEST-001");
            p.Add(x => x.OnEditClick, EventCallback.Factory.Create<string>(this, id => clickedIssueId = id));
        });

        cut.Find("[data-testid='edit-issue-button']").Click();

        Assert.That(clickedIssueId, Is.EqualTo("TEST-001"));
    }

    [Test]
    public void ShowsAgentDropdown_WhenRunAgentButtonClicked()
    {
        var cut = Render<IssueRowActions>(p =>
        {
            p.Add(x => x.IssueId, "TEST-001");
            p.Add(x => x.ProjectId, "project-1");
            p.Add(x => x.BranchName, "task/test-issue+TEST-001");
        });

        // Initially dropdown should not be visible
        Assert.That(cut.FindAll(".issue-row-agent-dropdown"), Is.Empty);

        // Click run agent button
        cut.Find("[data-testid='run-agent-button']").Click();

        // Dropdown should now be visible
        var dropdown = cut.Find(".issue-row-agent-dropdown");
        Assert.That(dropdown, Is.Not.Null);
    }

    [Test]
    public void HidesAgentDropdown_WhenClickedAgain()
    {
        var cut = Render<IssueRowActions>(p =>
        {
            p.Add(x => x.IssueId, "TEST-001");
            p.Add(x => x.ProjectId, "project-1");
            p.Add(x => x.BranchName, "task/test-issue+TEST-001");
        });

        // Open dropdown
        cut.Find("[data-testid='run-agent-button']").Click();
        Assert.That(cut.FindAll(".issue-row-agent-dropdown"), Has.Count.EqualTo(1));

        // Click the run agent button again to close
        cut.Find("[data-testid='run-agent-button']").Click();

        // Dropdown should be hidden
        Assert.That(cut.FindAll(".issue-row-agent-dropdown"), Is.Empty);
    }

    [Test]
    public void HasCorrectCssClasses_ForVisibilityControl()
    {
        var cut = Render<IssueRowActions>(p =>
        {
            p.Add(x => x.IssueId, "TEST-001");
            p.Add(x => x.ProjectId, "project-1");
            p.Add(x => x.BranchName, "task/test-issue+TEST-001");
        });

        var wrapper = cut.Find(".issue-row-actions");

        // Should have the base class for visibility control
        Assert.That(wrapper.ClassList, Does.Contain("issue-row-actions"));
    }

    [Test]
    public void AppliesVisibleClass_WhenIsVisibleIsTrue()
    {
        var cut = Render<IssueRowActions>(p =>
        {
            p.Add(x => x.IssueId, "TEST-001");
            p.Add(x => x.ProjectId, "project-1");
            p.Add(x => x.BranchName, "task/test-issue+TEST-001");
            p.Add(x => x.IsVisible, true);
        });

        var wrapper = cut.Find(".issue-row-actions");
        Assert.That(wrapper.ClassList, Does.Contain("visible"));
    }

    [Test]
    public void DoesNotApplyVisibleClass_WhenIsVisibleIsFalse()
    {
        var cut = Render<IssueRowActions>(p =>
        {
            p.Add(x => x.IssueId, "TEST-001");
            p.Add(x => x.ProjectId, "project-1");
            p.Add(x => x.BranchName, "task/test-issue+TEST-001");
            p.Add(x => x.IsVisible, false);
        });

        var wrapper = cut.Find(".issue-row-actions");
        Assert.That(wrapper.ClassList, Does.Not.Contain("visible"));
    }

    [Test]
    public void Renders_ButtonsWithCorrectTitles()
    {
        var cut = Render<IssueRowActions>(p =>
        {
            p.Add(x => x.IssueId, "TEST-001");
            p.Add(x => x.ProjectId, "project-1");
            p.Add(x => x.BranchName, "task/test-issue+TEST-001");
        });

        var editButton = cut.Find("[data-testid='edit-issue-button']");
        var runButton = cut.Find("[data-testid='run-agent-button']");

        Assert.That(editButton.GetAttribute("title"), Is.EqualTo("Edit issue"));
        Assert.That(runButton.GetAttribute("title"), Is.EqualTo("Run agent"));
    }

    [Test]
    public void DisablesRunAgentButton_WhenDisabledIsTrue()
    {
        var cut = Render<IssueRowActions>(p =>
        {
            p.Add(x => x.IssueId, "TEST-001");
            p.Add(x => x.ProjectId, "project-1");
            p.Add(x => x.BranchName, "task/test-issue+TEST-001");
            p.Add(x => x.Disabled, true);
        });

        var runButton = cut.Find("[data-testid='run-agent-button']");
        Assert.That(runButton.HasAttribute("disabled"), Is.True);
    }

    [Test]
    public void DisablesEditButton_WhenDisabledIsTrue()
    {
        var cut = Render<IssueRowActions>(p =>
        {
            p.Add(x => x.IssueId, "TEST-001");
            p.Add(x => x.ProjectId, "project-1");
            p.Add(x => x.BranchName, "task/test-issue+TEST-001");
            p.Add(x => x.Disabled, true);
        });

        var editButton = cut.Find("[data-testid='edit-issue-button']");
        Assert.That(editButton.HasAttribute("disabled"), Is.True);
    }


    [Test]
    public void ShowsDropdown_WhenShowAgentDropdownIsTrue()
    {
        var cut = Render<IssueRowActions>(p =>
        {
            p.Add(x => x.IssueId, "TEST-001");
            p.Add(x => x.ProjectId, "project-1");
            p.Add(x => x.ShowAgentDropdown, true);
        });

        // Dropdown should be visible due to ShowAgentDropdown=true
        var dropdown = cut.Find(".issue-row-agent-dropdown");
        Assert.That(dropdown, Is.Not.Null);
    }

    [Test]
    public void HidesDropdown_WhenShowAgentDropdownIsFalse()
    {
        var cut = Render<IssueRowActions>(p =>
        {
            p.Add(x => x.IssueId, "TEST-001");
            p.Add(x => x.ProjectId, "project-1");
            p.Add(x => x.ShowAgentDropdown, false);
        });

        // Dropdown should not be visible
        Assert.That(cut.FindAll(".issue-row-agent-dropdown"), Is.Empty);
    }

    [Test]
    public void ShowsDropdown_WhenEitherClickOrKeyboardShowsIt()
    {
        var cut = Render<IssueRowActions>(p =>
        {
            p.Add(x => x.IssueId, "TEST-001");
            p.Add(x => x.ProjectId, "project-1");
            p.Add(x => x.ShowAgentDropdown, false);
        });

        // Click the button to show dropdown via _showDropdown
        cut.Find("[data-testid='run-agent-button']").Click();

        // Dropdown should be visible due to _showDropdown
        var dropdown = cut.Find(".issue-row-agent-dropdown");
        Assert.That(dropdown, Is.Not.Null);
    }
}
