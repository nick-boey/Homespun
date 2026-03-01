using Bunit;
using Homespun.Client.Features.Toolbar.Components;
using Homespun.Client.Services;
using Homespun.Shared.Models.Sessions;
using Homespun.Tests.Helpers;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Homespun.Tests.Components;

/// <summary>
/// bUnit tests for the ProjectToolbar component.
/// Tests rendering, button states, and callback invocations for all button groups.
/// </summary>
[TestFixture]
public class ProjectToolbarTests : BunitTestContext
{
    private MockHttpMessageHandler _mockHandler = null!;
    private Mock<IKeyboardNavigationService> _mockNavService = null!;

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

        _mockNavService = new Mock<IKeyboardNavigationService>();

        var httpClient = _mockHandler.CreateClient();
        Services.AddSingleton(new HttpAgentPromptApiService(httpClient));
        Services.AddSingleton(new HttpSessionApiService(httpClient));
        Services.AddSingleton(new HttpCloneApiService(httpClient));
        Services.AddSingleton<IAgentStartupTracker>(new AgentStartupTracker());
        Services.AddSingleton(_mockNavService.Object);
        Services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
    }

    #region Container and Layout Tests

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
    public void Renders_ToolbarSeparators()
    {
        var cut = Render<ProjectToolbar>(p =>
        {
            p.Add(x => x.ProjectId, "project-1");
        });

        var separators = cut.FindAll("[data-testid='toolbar-separator']");
        Assert.That(separators, Has.Count.EqualTo(3), "Should have 3 separators between 4 button groups");
    }

    #endregion

    #region Edit and Run Button Tests (Original)

    [Test]
    public void DisablesEditAndRunButtons_WhenNoIssueSelected()
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
    public void EnablesEditAndRunButtons_WhenIssueSelected()
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
    public void RendersEditButtonWithPencilIcon()
    {
        var cut = Render<ProjectToolbar>(p =>
        {
            p.Add(x => x.ProjectId, "project-1");
        });

        var editButton = cut.Find("[data-testid='toolbar-edit-button']");
        // ToolbarButton uses LucideIcon which renders as SVG
        Assert.That(editButton.QuerySelector("svg"), Is.Not.Null);
    }

    [Test]
    public void RendersRunButtonWithPlayIcon()
    {
        var cut = Render<ProjectToolbar>(p =>
        {
            p.Add(x => x.ProjectId, "project-1");
        });

        var runButton = cut.Find("[data-testid='toolbar-run-button']");
        // ToolbarButton uses LucideIcon which renders as SVG
        Assert.That(runButton.QuerySelector("svg"), Is.Not.Null);
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

    #endregion

    #region Create Above/Below Button Tests

    [Test]
    public void Renders_CreateAboveButton()
    {
        var cut = Render<ProjectToolbar>(p =>
        {
            p.Add(x => x.ProjectId, "project-1");
        });

        var createAboveBtn = cut.Find("[data-testid='toolbar-create-above-button']");
        Assert.That(createAboveBtn, Is.Not.Null);
        // Uses LucideIcon which renders as SVG
        Assert.That(createAboveBtn.QuerySelector("svg"), Is.Not.Null);
    }

    [Test]
    public void Renders_CreateBelowButton()
    {
        var cut = Render<ProjectToolbar>(p =>
        {
            p.Add(x => x.ProjectId, "project-1");
        });

        var createBelowBtn = cut.Find("[data-testid='toolbar-create-below-button']");
        Assert.That(createBelowBtn, Is.Not.Null);
        // Uses LucideIcon which renders as SVG
        Assert.That(createBelowBtn.QuerySelector("svg"), Is.Not.Null);
    }

    [Test]
    public void CreateButtons_DisabledWhenCreateDisabled()
    {
        var cut = Render<ProjectToolbar>(p =>
        {
            p.Add(x => x.ProjectId, "project-1");
            p.Add(x => x.CreateDisabled, true);
        });

        var createAboveBtn = cut.Find("[data-testid='toolbar-create-above-button']");
        var createBelowBtn = cut.Find("[data-testid='toolbar-create-below-button']");

        Assert.That(createAboveBtn.HasAttribute("disabled"), Is.True);
        Assert.That(createBelowBtn.HasAttribute("disabled"), Is.True);
    }

    [Test]
    public void CreateAboveButton_CallsNavServiceCreateIssueAbove_WhenNoCallback()
    {
        var cut = Render<ProjectToolbar>(p =>
        {
            p.Add(x => x.ProjectId, "project-1");
            p.Add(x => x.CreateDisabled, false);
        });

        cut.Find("[data-testid='toolbar-create-above-button']").Click();

        _mockNavService.Verify(s => s.CreateIssueAbove(), Times.Once);
    }

    [Test]
    public void CreateBelowButton_CallsNavServiceCreateIssueBelow_WhenNoCallback()
    {
        var cut = Render<ProjectToolbar>(p =>
        {
            p.Add(x => x.ProjectId, "project-1");
            p.Add(x => x.CreateDisabled, false);
        });

        cut.Find("[data-testid='toolbar-create-below-button']").Click();

        _mockNavService.Verify(s => s.CreateIssueBelow(), Times.Once);
    }

    [Test]
    public void CreateAboveButton_InvokesCallback_WhenProvided()
    {
        var callbackInvoked = false;
        var cut = Render<ProjectToolbar>(p =>
        {
            p.Add(x => x.ProjectId, "project-1");
            p.Add(x => x.CreateDisabled, false);
            p.Add(x => x.OnCreateAbove, EventCallback.Factory.Create(this, () => callbackInvoked = true));
        });

        cut.Find("[data-testid='toolbar-create-above-button']").Click();

        Assert.That(callbackInvoked, Is.True);
        _mockNavService.Verify(s => s.CreateIssueAbove(), Times.Never);
    }

    [Test]
    public void CreateBelowButton_InvokesCallback_WhenProvided()
    {
        var callbackInvoked = false;
        var cut = Render<ProjectToolbar>(p =>
        {
            p.Add(x => x.ProjectId, "project-1");
            p.Add(x => x.CreateDisabled, false);
            p.Add(x => x.OnCreateBelow, EventCallback.Factory.Create(this, () => callbackInvoked = true));
        });

        cut.Find("[data-testid='toolbar-create-below-button']").Click();

        Assert.That(callbackInvoked, Is.True);
        _mockNavService.Verify(s => s.CreateIssueBelow(), Times.Never);
    }

    #endregion

    #region Hierarchy Button Tests (Child Of / Parent Of)

    [Test]
    public void Renders_MakeChildOfButton()
    {
        var cut = Render<ProjectToolbar>(p =>
        {
            p.Add(x => x.ProjectId, "project-1");
        });

        var makeChildOfBtn = cut.Find("[data-testid='toolbar-child-of-button']");
        Assert.That(makeChildOfBtn, Is.Not.Null);
        // Uses LucideIcon which renders as SVG
        Assert.That(makeChildOfBtn.QuerySelector("svg"), Is.Not.Null);
    }

    [Test]
    public void Renders_MakeParentOfButton()
    {
        var cut = Render<ProjectToolbar>(p =>
        {
            p.Add(x => x.ProjectId, "project-1");
        });

        var makeParentOfBtn = cut.Find("[data-testid='toolbar-parent-of-button']");
        Assert.That(makeParentOfBtn, Is.Not.Null);
        // Uses LucideIcon which renders as SVG
        Assert.That(makeParentOfBtn.QuerySelector("svg"), Is.Not.Null);
    }

    [Test]
    public void MakeChildOfButton_Disabled_WhenChildOfDisabled()
    {
        var cut = Render<ProjectToolbar>(p =>
        {
            p.Add(x => x.ProjectId, "project-1");
            p.Add(x => x.ChildOfDisabled, true);
        });

        var makeChildOfBtn = cut.Find("[data-testid='toolbar-child-of-button']");
        Assert.That(makeChildOfBtn.HasAttribute("disabled"), Is.True);
    }

    [Test]
    public void MakeParentOfButton_Disabled_WhenParentOfDisabled()
    {
        var cut = Render<ProjectToolbar>(p =>
        {
            p.Add(x => x.ProjectId, "project-1");
            p.Add(x => x.ParentOfDisabled, true);
        });

        var makeParentOfBtn = cut.Find("[data-testid='toolbar-parent-of-button']");
        Assert.That(makeParentOfBtn.HasAttribute("disabled"), Is.True);
    }

    [Test]
    public void MakeChildOfButton_ShowsActiveState_WhenChildOfActive()
    {
        var cut = Render<ProjectToolbar>(p =>
        {
            p.Add(x => x.ProjectId, "project-1");
            p.Add(x => x.ChildOfActive, true);
        });

        var makeChildOfBtn = cut.Find("[data-testid='toolbar-child-of-button']");
        Assert.That(makeChildOfBtn.ClassList.Contains("toolbar-btn-active"), Is.True,
            "Make Child Of button should be highlighted when active");
    }

    [Test]
    public void MakeParentOfButton_ShowsActiveState_WhenParentOfActive()
    {
        var cut = Render<ProjectToolbar>(p =>
        {
            p.Add(x => x.ProjectId, "project-1");
            p.Add(x => x.ParentOfActive, true);
        });

        var makeParentOfBtn = cut.Find("[data-testid='toolbar-parent-of-button']");
        Assert.That(makeParentOfBtn.ClassList.Contains("toolbar-btn-active"), Is.True,
            "Make Parent Of button should be highlighted when active");
    }

    [Test]
    public void MakeChildOfButton_CallsNavServiceStartMakeChildOf_WhenNoCallback()
    {
        var cut = Render<ProjectToolbar>(p =>
        {
            p.Add(x => x.ProjectId, "project-1");
            p.Add(x => x.ChildOfDisabled, false);
        });

        cut.Find("[data-testid='toolbar-child-of-button']").Click();

        _mockNavService.Verify(s => s.StartMakeChildOf(), Times.Once);
    }

    [Test]
    public void MakeParentOfButton_CallsNavServiceStartMakeParentOf_WhenNoCallback()
    {
        var cut = Render<ProjectToolbar>(p =>
        {
            p.Add(x => x.ProjectId, "project-1");
            p.Add(x => x.ParentOfDisabled, false);
        });

        cut.Find("[data-testid='toolbar-parent-of-button']").Click();

        _mockNavService.Verify(s => s.StartMakeParentOf(), Times.Once);
    }

    [Test]
    public void MakeChildOfButton_InvokesCallback_WhenProvided()
    {
        var callbackInvoked = false;
        var cut = Render<ProjectToolbar>(p =>
        {
            p.Add(x => x.ProjectId, "project-1");
            p.Add(x => x.ChildOfDisabled, false);
            p.Add(x => x.OnMakeChildOf, EventCallback.Factory.Create(this, () => callbackInvoked = true));
        });

        cut.Find("[data-testid='toolbar-child-of-button']").Click();

        Assert.That(callbackInvoked, Is.True);
        _mockNavService.Verify(s => s.StartMakeChildOf(), Times.Never);
    }

    [Test]
    public void MakeParentOfButton_InvokesCallback_WhenProvided()
    {
        var callbackInvoked = false;
        var cut = Render<ProjectToolbar>(p =>
        {
            p.Add(x => x.ProjectId, "project-1");
            p.Add(x => x.ParentOfDisabled, false);
            p.Add(x => x.OnMakeParentOf, EventCallback.Factory.Create(this, () => callbackInvoked = true));
        });

        cut.Find("[data-testid='toolbar-parent-of-button']").Click();

        Assert.That(callbackInvoked, Is.True);
        _mockNavService.Verify(s => s.StartMakeParentOf(), Times.Never);
    }

    #endregion

    #region Undo/Redo Button Tests

    [Test]
    public void Renders_UndoButton()
    {
        var cut = Render<ProjectToolbar>(p =>
        {
            p.Add(x => x.ProjectId, "project-1");
        });

        var undoBtn = cut.Find("[data-testid='toolbar-undo-button']");
        Assert.That(undoBtn, Is.Not.Null);
        // Uses LucideIcon which renders as SVG
        Assert.That(undoBtn.QuerySelector("svg"), Is.Not.Null);
    }

    [Test]
    public void Renders_RedoButton()
    {
        var cut = Render<ProjectToolbar>(p =>
        {
            p.Add(x => x.ProjectId, "project-1");
        });

        var redoBtn = cut.Find("[data-testid='toolbar-redo-button']");
        Assert.That(redoBtn, Is.Not.Null);
        // Uses LucideIcon which renders as SVG
        Assert.That(redoBtn.QuerySelector("svg"), Is.Not.Null);
    }

    [Test]
    public void UndoButton_Disabled_WhenUndoDisabled()
    {
        var cut = Render<ProjectToolbar>(p =>
        {
            p.Add(x => x.ProjectId, "project-1");
            p.Add(x => x.UndoDisabled, true);
        });

        var undoBtn = cut.Find("[data-testid='toolbar-undo-button']");
        Assert.That(undoBtn.HasAttribute("disabled"), Is.True);
    }

    [Test]
    public void RedoButton_Disabled_WhenRedoDisabled()
    {
        var cut = Render<ProjectToolbar>(p =>
        {
            p.Add(x => x.ProjectId, "project-1");
            p.Add(x => x.RedoDisabled, true);
        });

        var redoBtn = cut.Find("[data-testid='toolbar-redo-button']");
        Assert.That(redoBtn.HasAttribute("disabled"), Is.True);
    }

    [Test]
    public void UndoButton_InvokesOnUndo()
    {
        var callbackInvoked = false;
        var cut = Render<ProjectToolbar>(p =>
        {
            p.Add(x => x.ProjectId, "project-1");
            p.Add(x => x.UndoDisabled, false);
            p.Add(x => x.OnUndo, EventCallback.Factory.Create(this, () => callbackInvoked = true));
        });

        cut.Find("[data-testid='toolbar-undo-button']").Click();

        Assert.That(callbackInvoked, Is.True);
    }

    [Test]
    public void RedoButton_InvokesOnRedo()
    {
        var callbackInvoked = false;
        var cut = Render<ProjectToolbar>(p =>
        {
            p.Add(x => x.ProjectId, "project-1");
            p.Add(x => x.RedoDisabled, false);
            p.Add(x => x.OnRedo, EventCallback.Factory.Create(this, () => callbackInvoked = true));
        });

        cut.Find("[data-testid='toolbar-redo-button']").Click();

        Assert.That(callbackInvoked, Is.True);
    }

    [Test]
    public void UndoButton_AcceptsCustomTooltip()
    {
        // This test verifies the component accepts the UndoTooltip parameter without error.
        // The BbTooltip component may render tooltip content to a portal which isn't
        // captured in the bUnit markup. We verify the component renders successfully
        // and the undo button exists.
        var cut = Render<ProjectToolbar>(p =>
        {
            p.Add(x => x.ProjectId, "project-1");
            p.Add(x => x.UndoTooltip, "Undo: Create issue 'Add feature'");
        });

        var undoBtn = cut.Find("[data-testid='toolbar-undo-button']");
        Assert.That(undoBtn, Is.Not.Null);
    }

    [Test]
    public void RedoButton_AcceptsCustomTooltip()
    {
        // This test verifies the component accepts the RedoTooltip parameter without error.
        // The BbTooltip component may render tooltip content to a portal which isn't
        // captured in the bUnit markup. We verify the component renders successfully
        // and the redo button exists.
        var cut = Render<ProjectToolbar>(p =>
        {
            p.Add(x => x.ProjectId, "project-1");
            p.Add(x => x.RedoTooltip, "Redo: Delete issue 'Bug fix'");
        });

        var redoBtn = cut.Find("[data-testid='toolbar-redo-button']");
        Assert.That(redoBtn, Is.Not.Null);
    }

    [Test]
    public void UndoButton_DoesNotInvokeCallback_WhenDisabled()
    {
        var callbackInvoked = false;
        var cut = Render<ProjectToolbar>(p =>
        {
            p.Add(x => x.ProjectId, "project-1");
            p.Add(x => x.UndoDisabled, true);
            p.Add(x => x.OnUndo, EventCallback.Factory.Create(this, () => callbackInvoked = true));
        });

        // bUnit doesn't fire click on disabled buttons, but we can verify the button is disabled
        var undoBtn = cut.Find("[data-testid='toolbar-undo-button']");
        Assert.That(undoBtn.HasAttribute("disabled"), Is.True);
        Assert.That(callbackInvoked, Is.False);
    }

    [Test]
    public void RedoButton_DoesNotInvokeCallback_WhenDisabled()
    {
        var callbackInvoked = false;
        var cut = Render<ProjectToolbar>(p =>
        {
            p.Add(x => x.ProjectId, "project-1");
            p.Add(x => x.RedoDisabled, true);
            p.Add(x => x.OnRedo, EventCallback.Factory.Create(this, () => callbackInvoked = true));
        });

        var redoBtn = cut.Find("[data-testid='toolbar-redo-button']");
        Assert.That(redoBtn.HasAttribute("disabled"), Is.True);
        Assert.That(callbackInvoked, Is.False);
    }

    #endregion
}
