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
        Assert.That(createAboveBtn.InnerHtml, Does.Contain("bi-plus"));
    }

    [Test]
    public void Renders_CreateBelowButton()
    {
        var cut = Render<IssueControlPanel>();

        var createBelowBtn = cut.Find("[data-testid='create-issue-below-btn']");
        Assert.That(createBelowBtn, Is.Not.Null);
        Assert.That(createBelowBtn.InnerHtml, Does.Contain("bi-plus"));
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
}
