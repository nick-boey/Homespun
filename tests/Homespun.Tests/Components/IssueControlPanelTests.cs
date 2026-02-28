using Bunit;
using Homespun.Client.Components;
using Homespun.Client.Services;
using Homespun.Shared.Models.Fleece;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Homespun.Tests.Components;

/// <summary>
/// bUnit tests for the IssueControlPanel component.
/// </summary>
[TestFixture]
public class IssueControlPanelTests : BunitTestContext
{
    private Mock<IKeyboardNavigationService> _mockNavService = null!;

    [SetUp]
    public new void Setup()
    {
        base.Setup();
        _mockNavService = new Mock<IKeyboardNavigationService>();
        _mockNavService.Setup(s => s.EditMode).Returns(KeyboardEditMode.Viewing);
        Services.AddSingleton(_mockNavService.Object);
    }

    [Test]
    public void Renders_CreateAboveButton()
    {
        var cut = Render<IssueControlPanel>();

        var createAboveBtn = cut.Find("[data-testid='create-issue-above-btn']");
        Assert.That(createAboveBtn, Is.Not.Null);
        // LucideIcon renders SVG or placeholder - verify button has label text
        Assert.That(createAboveBtn.InnerHtml, Does.Contain("Above"));
    }

    [Test]
    public void Renders_CreateBelowButton()
    {
        var cut = Render<IssueControlPanel>();

        var createBelowBtn = cut.Find("[data-testid='create-issue-below-btn']");
        Assert.That(createBelowBtn, Is.Not.Null);
        // LucideIcon renders SVG or placeholder - verify button has label text
        Assert.That(createBelowBtn.InnerHtml, Does.Contain("Below"));
    }

    [Test]
    public void CreateAboveButton_CallsNavServiceCreateIssueAbove()
    {
        var cut = Render<IssueControlPanel>();

        cut.Find("[data-testid='create-issue-above-btn']").Click();

        _mockNavService.Verify(s => s.CreateIssueAbove(), Times.Once);
    }

    [Test]
    public void CreateBelowButton_CallsNavServiceCreateIssueBelow()
    {
        var cut = Render<IssueControlPanel>();

        cut.Find("[data-testid='create-issue-below-btn']").Click();

        _mockNavService.Verify(s => s.CreateIssueBelow(), Times.Once);
    }

    [Test]
    public void Buttons_DisabledWhenInEditMode()
    {
        _mockNavService.Setup(s => s.EditMode).Returns(KeyboardEditMode.EditingExisting);

        var cut = Render<IssueControlPanel>();

        var createAboveBtn = cut.Find("[data-testid='create-issue-above-btn']");
        var createBelowBtn = cut.Find("[data-testid='create-issue-below-btn']");

        Assert.That(createAboveBtn.HasAttribute("disabled"), Is.True);
        Assert.That(createBelowBtn.HasAttribute("disabled"), Is.True);
    }

    [Test]
    public void Buttons_DisabledWhenInCreatingNewMode()
    {
        _mockNavService.Setup(s => s.EditMode).Returns(KeyboardEditMode.CreatingNew);

        var cut = Render<IssueControlPanel>();

        var createAboveBtn = cut.Find("[data-testid='create-issue-above-btn']");
        var createBelowBtn = cut.Find("[data-testid='create-issue-below-btn']");

        Assert.That(createAboveBtn.HasAttribute("disabled"), Is.True);
        Assert.That(createBelowBtn.HasAttribute("disabled"), Is.True);
    }

    [Test]
    public void Buttons_EnabledWhenInViewingMode()
    {
        _mockNavService.Setup(s => s.EditMode).Returns(KeyboardEditMode.Viewing);

        var cut = Render<IssueControlPanel>();

        var createAboveBtn = cut.Find("[data-testid='create-issue-above-btn']");
        var createBelowBtn = cut.Find("[data-testid='create-issue-below-btn']");

        Assert.That(createAboveBtn.HasAttribute("disabled"), Is.False);
        Assert.That(createBelowBtn.HasAttribute("disabled"), Is.False);
    }

    [Test]
    public void HasStickyPositioningClass()
    {
        var cut = Render<IssueControlPanel>();

        var panel = cut.Find(".issue-control-panel");
        Assert.That(panel, Is.Not.Null);
    }

    [Test]
    public void Renders_WithCorrectButtonTitles()
    {
        var cut = Render<IssueControlPanel>();

        var createAboveBtn = cut.Find("[data-testid='create-issue-above-btn']");
        var createBelowBtn = cut.Find("[data-testid='create-issue-below-btn']");

        Assert.That(createAboveBtn.GetAttribute("title"), Does.Contain("above"));
        Assert.That(createBelowBtn.GetAttribute("title"), Does.Contain("below"));
    }

    [Test]
    public void DoesNotCallCreateIssueAbove_WhenDisabled()
    {
        _mockNavService.Setup(s => s.EditMode).Returns(KeyboardEditMode.EditingExisting);

        var cut = Render<IssueControlPanel>();

        // The button is disabled so click should not trigger the action
        // bUnit will not fire onclick on disabled buttons by default
        var createAboveBtn = cut.Find("[data-testid='create-issue-above-btn']");
        Assert.That(createAboveBtn.HasAttribute("disabled"), Is.True);

        _mockNavService.Verify(s => s.CreateIssueAbove(), Times.Never);
    }

    #region Make Child Of / Make Parent Of Button Tests

    [Test]
    public void Renders_MakeChildOfButton()
    {
        var cut = Render<IssueControlPanel>();

        var makeChildOfBtn = cut.Find("[data-testid='make-child-of-btn']");
        Assert.That(makeChildOfBtn, Is.Not.Null);
    }

    [Test]
    public void Renders_MakeParentOfButton()
    {
        var cut = Render<IssueControlPanel>();

        var makeParentOfBtn = cut.Find("[data-testid='make-parent-of-btn']");
        Assert.That(makeParentOfBtn, Is.Not.Null);
    }

    [Test]
    public void MakeChildOfButton_Disabled_WhenNoIssueSelected()
    {
        _mockNavService.Setup(s => s.SelectedIndex).Returns(-1);

        var cut = Render<IssueControlPanel>();

        var makeChildOfBtn = cut.Find("[data-testid='make-child-of-btn']");
        Assert.That(makeChildOfBtn.HasAttribute("disabled"), Is.True);
    }

    [Test]
    public void MakeParentOfButton_Disabled_WhenNoIssueSelected()
    {
        _mockNavService.Setup(s => s.SelectedIndex).Returns(-1);

        var cut = Render<IssueControlPanel>();

        var makeParentOfBtn = cut.Find("[data-testid='make-parent-of-btn']");
        Assert.That(makeParentOfBtn.HasAttribute("disabled"), Is.True);
    }

    [Test]
    public void MakeChildOfButton_Disabled_WhenInEditMode()
    {
        _mockNavService.Setup(s => s.SelectedIndex).Returns(0);
        _mockNavService.Setup(s => s.EditMode).Returns(KeyboardEditMode.EditingExisting);

        var cut = Render<IssueControlPanel>();

        var makeChildOfBtn = cut.Find("[data-testid='make-child-of-btn']");
        Assert.That(makeChildOfBtn.HasAttribute("disabled"), Is.True);
    }

    [Test]
    public void MakeParentOfButton_Disabled_WhenInEditMode()
    {
        _mockNavService.Setup(s => s.SelectedIndex).Returns(0);
        _mockNavService.Setup(s => s.EditMode).Returns(KeyboardEditMode.EditingExisting);

        var cut = Render<IssueControlPanel>();

        var makeParentOfBtn = cut.Find("[data-testid='make-parent-of-btn']");
        Assert.That(makeParentOfBtn.HasAttribute("disabled"), Is.True);
    }

    [Test]
    public void MoveButtons_Enabled_WhenIssueSelectedAndViewing()
    {
        _mockNavService.Setup(s => s.SelectedIndex).Returns(0);
        _mockNavService.Setup(s => s.EditMode).Returns(KeyboardEditMode.Viewing);

        var cut = Render<IssueControlPanel>();

        var makeChildOfBtn = cut.Find("[data-testid='make-child-of-btn']");
        var makeParentOfBtn = cut.Find("[data-testid='make-parent-of-btn']");

        Assert.That(makeChildOfBtn.HasAttribute("disabled"), Is.False);
        Assert.That(makeParentOfBtn.HasAttribute("disabled"), Is.False);
    }

    [Test]
    public void MakeChildOfButton_CallsStartMakeChildOf()
    {
        _mockNavService.Setup(s => s.SelectedIndex).Returns(0);
        _mockNavService.Setup(s => s.EditMode).Returns(KeyboardEditMode.Viewing);

        var cut = Render<IssueControlPanel>();

        cut.Find("[data-testid='make-child-of-btn']").Click();

        _mockNavService.Verify(s => s.StartMakeChildOf(), Times.Once);
    }

    [Test]
    public void MakeParentOfButton_CallsStartMakeParentOf()
    {
        _mockNavService.Setup(s => s.SelectedIndex).Returns(0);
        _mockNavService.Setup(s => s.EditMode).Returns(KeyboardEditMode.Viewing);

        var cut = Render<IssueControlPanel>();

        cut.Find("[data-testid='make-parent-of-btn']").Click();

        _mockNavService.Verify(s => s.StartMakeParentOf(), Times.Once);
    }

    [Test]
    public void MakeChildOfButton_ShowsActiveState_WhenInSelectingMoveTargetMode_AsChildOf()
    {
        _mockNavService.Setup(s => s.SelectedIndex).Returns(0);
        _mockNavService.Setup(s => s.EditMode).Returns(KeyboardEditMode.SelectingMoveTarget);
        _mockNavService.Setup(s => s.CurrentMoveOperation).Returns(MoveOperationType.AsChildOf);

        var cut = Render<IssueControlPanel>();

        var makeChildOfBtn = cut.Find("[data-testid='make-child-of-btn']");
        // The button should have an active/highlighted class
        Assert.That(makeChildOfBtn.ClassList.Contains("btn-active") || makeChildOfBtn.ClassList.Contains("ring-2"), Is.True,
            "Make Child Of button should be highlighted when in AsChildOf selection mode");
    }

    [Test]
    public void MakeParentOfButton_ShowsActiveState_WhenInSelectingMoveTargetMode_AsParentOf()
    {
        _mockNavService.Setup(s => s.SelectedIndex).Returns(0);
        _mockNavService.Setup(s => s.EditMode).Returns(KeyboardEditMode.SelectingMoveTarget);
        _mockNavService.Setup(s => s.CurrentMoveOperation).Returns(MoveOperationType.AsParentOf);

        var cut = Render<IssueControlPanel>();

        var makeParentOfBtn = cut.Find("[data-testid='make-parent-of-btn']");
        // The button should have an active/highlighted class
        Assert.That(makeParentOfBtn.ClassList.Contains("btn-active") || makeParentOfBtn.ClassList.Contains("ring-2"), Is.True,
            "Make Parent Of button should be highlighted when in AsParentOf selection mode");
    }

    [Test]
    public void MoveButtons_Disabled_WhenInSelectingMoveTargetMode()
    {
        _mockNavService.Setup(s => s.SelectedIndex).Returns(0);
        _mockNavService.Setup(s => s.EditMode).Returns(KeyboardEditMode.SelectingMoveTarget);
        _mockNavService.Setup(s => s.CurrentMoveOperation).Returns(MoveOperationType.AsChildOf);

        var cut = Render<IssueControlPanel>();

        // The other button (not the active one) should be disabled
        var makeParentOfBtn = cut.Find("[data-testid='make-parent-of-btn']");
        Assert.That(makeParentOfBtn.HasAttribute("disabled"), Is.True);
    }

    #endregion

    #region Move Up/Down Button Tests

    [Test]
    public void Renders_MoveUpButton()
    {
        var cut = Render<IssueControlPanel>();

        var moveUpBtn = cut.Find("[data-testid='move-up-btn']");
        Assert.That(moveUpBtn, Is.Not.Null);
    }

    [Test]
    public void Renders_MoveDownButton()
    {
        var cut = Render<IssueControlPanel>();

        var moveDownBtn = cut.Find("[data-testid='move-down-btn']");
        Assert.That(moveDownBtn, Is.Not.Null);
    }

    [Test]
    public void MoveUpButton_Disabled_WhenNoIssueSelected()
    {
        _mockNavService.Setup(s => s.SelectedIndex).Returns(-1);
        _mockNavService.Setup(s => s.GetSiblingMoveInfo()).Returns((false, false, false));

        var cut = Render<IssueControlPanel>();

        var moveUpBtn = cut.Find("[data-testid='move-up-btn']");
        Assert.That(moveUpBtn.HasAttribute("disabled"), Is.True);
    }

    [Test]
    public void MoveDownButton_Disabled_WhenNoIssueSelected()
    {
        _mockNavService.Setup(s => s.SelectedIndex).Returns(-1);
        _mockNavService.Setup(s => s.GetSiblingMoveInfo()).Returns((false, false, false));

        var cut = Render<IssueControlPanel>();

        var moveDownBtn = cut.Find("[data-testid='move-down-btn']");
        Assert.That(moveDownBtn.HasAttribute("disabled"), Is.True);
    }

    [Test]
    public void MoveUpButton_Disabled_WhenInEditMode()
    {
        _mockNavService.Setup(s => s.SelectedIndex).Returns(0);
        _mockNavService.Setup(s => s.EditMode).Returns(KeyboardEditMode.EditingExisting);
        _mockNavService.Setup(s => s.GetSiblingMoveInfo()).Returns((true, true, true));

        var cut = Render<IssueControlPanel>();

        var moveUpBtn = cut.Find("[data-testid='move-up-btn']");
        Assert.That(moveUpBtn.HasAttribute("disabled"), Is.True);
    }

    [Test]
    public void MoveDownButton_Disabled_WhenInEditMode()
    {
        _mockNavService.Setup(s => s.SelectedIndex).Returns(0);
        _mockNavService.Setup(s => s.EditMode).Returns(KeyboardEditMode.EditingExisting);
        _mockNavService.Setup(s => s.GetSiblingMoveInfo()).Returns((true, true, true));

        var cut = Render<IssueControlPanel>();

        var moveDownBtn = cut.Find("[data-testid='move-down-btn']");
        Assert.That(moveDownBtn.HasAttribute("disabled"), Is.True);
    }

    [Test]
    public void MoveUpButton_Disabled_WhenIssueHasNoParent()
    {
        _mockNavService.Setup(s => s.SelectedIndex).Returns(0);
        _mockNavService.Setup(s => s.EditMode).Returns(KeyboardEditMode.Viewing);
        _mockNavService.Setup(s => s.GetSiblingMoveInfo()).Returns((false, false, false)); // No single parent

        var cut = Render<IssueControlPanel>();

        var moveUpBtn = cut.Find("[data-testid='move-up-btn']");
        Assert.That(moveUpBtn.HasAttribute("disabled"), Is.True);
    }

    [Test]
    public void MoveDownButton_Disabled_WhenIssueHasNoParent()
    {
        _mockNavService.Setup(s => s.SelectedIndex).Returns(0);
        _mockNavService.Setup(s => s.EditMode).Returns(KeyboardEditMode.Viewing);
        _mockNavService.Setup(s => s.GetSiblingMoveInfo()).Returns((false, false, false)); // No single parent

        var cut = Render<IssueControlPanel>();

        var moveDownBtn = cut.Find("[data-testid='move-down-btn']");
        Assert.That(moveDownBtn.HasAttribute("disabled"), Is.True);
    }

    [Test]
    public void MoveUpButton_Disabled_WhenAlreadyFirst()
    {
        _mockNavService.Setup(s => s.SelectedIndex).Returns(0);
        _mockNavService.Setup(s => s.EditMode).Returns(KeyboardEditMode.Viewing);
        _mockNavService.Setup(s => s.GetSiblingMoveInfo()).Returns((false, true, true)); // Can't move up, can move down

        var cut = Render<IssueControlPanel>();

        var moveUpBtn = cut.Find("[data-testid='move-up-btn']");
        Assert.That(moveUpBtn.HasAttribute("disabled"), Is.True);
    }

    [Test]
    public void MoveDownButton_Disabled_WhenAlreadyLast()
    {
        _mockNavService.Setup(s => s.SelectedIndex).Returns(0);
        _mockNavService.Setup(s => s.EditMode).Returns(KeyboardEditMode.Viewing);
        _mockNavService.Setup(s => s.GetSiblingMoveInfo()).Returns((true, false, true)); // Can move up, can't move down

        var cut = Render<IssueControlPanel>();

        var moveDownBtn = cut.Find("[data-testid='move-down-btn']");
        Assert.That(moveDownBtn.HasAttribute("disabled"), Is.True);
    }

    [Test]
    public void MoveUpButton_Enabled_WhenCanMoveUp()
    {
        _mockNavService.Setup(s => s.SelectedIndex).Returns(0);
        _mockNavService.Setup(s => s.EditMode).Returns(KeyboardEditMode.Viewing);
        _mockNavService.Setup(s => s.GetSiblingMoveInfo()).Returns((true, true, true)); // Can move both ways

        var cut = Render<IssueControlPanel>();

        var moveUpBtn = cut.Find("[data-testid='move-up-btn']");
        Assert.That(moveUpBtn.HasAttribute("disabled"), Is.False);
    }

    [Test]
    public void MoveDownButton_Enabled_WhenCanMoveDown()
    {
        _mockNavService.Setup(s => s.SelectedIndex).Returns(0);
        _mockNavService.Setup(s => s.EditMode).Returns(KeyboardEditMode.Viewing);
        _mockNavService.Setup(s => s.GetSiblingMoveInfo()).Returns((true, true, true)); // Can move both ways

        var cut = Render<IssueControlPanel>();

        var moveDownBtn = cut.Find("[data-testid='move-down-btn']");
        Assert.That(moveDownBtn.HasAttribute("disabled"), Is.False);
    }

    [Test]
    public void MoveUpButton_CallsMoveSelectedUpAsync()
    {
        _mockNavService.Setup(s => s.SelectedIndex).Returns(0);
        _mockNavService.Setup(s => s.EditMode).Returns(KeyboardEditMode.Viewing);
        _mockNavService.Setup(s => s.GetSiblingMoveInfo()).Returns((true, true, true));
        _mockNavService.Setup(s => s.MoveSelectedUpAsync()).Returns(Task.CompletedTask);

        var cut = Render<IssueControlPanel>();

        cut.Find("[data-testid='move-up-btn']").Click();

        _mockNavService.Verify(s => s.MoveSelectedUpAsync(), Times.Once);
    }

    [Test]
    public void MoveDownButton_CallsMoveSelectedDownAsync()
    {
        _mockNavService.Setup(s => s.SelectedIndex).Returns(0);
        _mockNavService.Setup(s => s.EditMode).Returns(KeyboardEditMode.Viewing);
        _mockNavService.Setup(s => s.GetSiblingMoveInfo()).Returns((true, true, true));
        _mockNavService.Setup(s => s.MoveSelectedDownAsync()).Returns(Task.CompletedTask);

        var cut = Render<IssueControlPanel>();

        cut.Find("[data-testid='move-down-btn']").Click();

        _mockNavService.Verify(s => s.MoveSelectedDownAsync(), Times.Once);
    }

    #endregion
}
