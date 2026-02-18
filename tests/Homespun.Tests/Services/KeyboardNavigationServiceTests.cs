using Fleece.Core.Models;
using Homespun.Client.Components;
using Homespun.Client.Services;
using Homespun.Shared.Models.Fleece;
using Moq;

namespace Homespun.Tests.Services;

[TestFixture]
public class KeyboardNavigationServiceTests
{
    private KeyboardNavigationService _service = null!;
    private Mock<HttpIssueApiService> _mockIssueApi = null!;
    private List<TaskGraphIssueRenderLine> _sampleRenderLines = null!;

    [SetUp]
    public void Setup()
    {
        _mockIssueApi = new Mock<HttpIssueApiService>(MockBehavior.Loose, (HttpClient)null!);
        _service = new KeyboardNavigationService(_mockIssueApi.Object);

        _sampleRenderLines =
        [
            CreateIssueLine("ISSUE-001", "First issue", 0, TaskGraphMarkerType.Actionable),
            CreateIssueLine("ISSUE-002", "Second issue", 0, TaskGraphMarkerType.Open),
            CreateIssueLine("ISSUE-003", "Third issue", 1, TaskGraphMarkerType.Open, parentLane: 2),
            CreateIssueLine("ISSUE-004", "Fourth issue (parent)", 2, TaskGraphMarkerType.Open)
        ];
    }

    private static TaskGraphIssueRenderLine CreateIssueLine(
        string issueId, string title, int lane, TaskGraphMarkerType marker,
        int? parentLane = null, bool isFirstChild = false, bool isSeriesChild = false)
    {
        return new TaskGraphIssueRenderLine(
            IssueId: issueId,
            Title: title,
            Lane: lane,
            Marker: marker,
            ParentLane: parentLane,
            IsFirstChild: isFirstChild,
            IsSeriesChild: isSeriesChild,
            DrawTopLine: false,
            DrawBottomLine: false,
            SeriesConnectorFromLane: null,
            IssueType: IssueType.Task,
            HasDescription: false,
            LinkedPr: null,
            AgentStatus: null
        );
    }

    #region Initialization Tests

    [Test]
    public void Initialize_WithRenderLines_SetsSelectedIndexToMinusOne()
    {
        _service.Initialize(_sampleRenderLines);

        Assert.That(_service.SelectedIndex, Is.EqualTo(-1));
        Assert.That(_service.SelectedIssueId, Is.Null);
    }

    [Test]
    public void SelectFirstActionable_SelectsFirstActionableIssue()
    {
        _service.Initialize(_sampleRenderLines);

        _service.SelectFirstActionable();

        Assert.That(_service.SelectedIndex, Is.EqualTo(0));
        Assert.That(_service.SelectedIssueId, Is.EqualTo("ISSUE-001"));
    }

    [Test]
    public void SelectFirstActionable_NoActionableIssues_SelectsFirstIssue()
    {
        var nonActionableLines = new List<TaskGraphIssueRenderLine>
        {
            CreateIssueLine("ISSUE-001", "First", 0, TaskGraphMarkerType.Open),
            CreateIssueLine("ISSUE-002", "Second", 0, TaskGraphMarkerType.Open)
        };
        _service.Initialize(nonActionableLines);

        _service.SelectFirstActionable();

        Assert.That(_service.SelectedIndex, Is.EqualTo(0));
        Assert.That(_service.SelectedIssueId, Is.EqualTo("ISSUE-001"));
    }

    [Test]
    public void SelectFirstActionable_EmptyList_DoesNotThrow()
    {
        _service.Initialize([]);

        Assert.DoesNotThrow(() => _service.SelectFirstActionable());
        Assert.That(_service.SelectedIndex, Is.EqualTo(-1));
    }

    [Test]
    public void SelectIssue_ValidId_SelectsCorrectIndex()
    {
        _service.Initialize(_sampleRenderLines);

        _service.SelectIssue("ISSUE-003");

        Assert.That(_service.SelectedIndex, Is.EqualTo(2));
        Assert.That(_service.SelectedIssueId, Is.EqualTo("ISSUE-003"));
    }

    [Test]
    public void SelectIssue_InvalidId_DoesNotChangeSelection()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();
        var originalIndex = _service.SelectedIndex;

        _service.SelectIssue("NONEXISTENT");

        Assert.That(_service.SelectedIndex, Is.EqualTo(originalIndex));
    }

    #endregion

    #region Navigation Tests

    [Test]
    public void MoveDown_FromFirstIssue_SelectsSecondIssue()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();

        _service.MoveDown();

        Assert.That(_service.SelectedIndex, Is.EqualTo(1));
        Assert.That(_service.SelectedIssueId, Is.EqualTo("ISSUE-002"));
    }

    [Test]
    public void MoveDown_AtLastIssue_StaysAtLastIssue()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectIssue("ISSUE-004");

        _service.MoveDown();

        Assert.That(_service.SelectedIndex, Is.EqualTo(3));
        Assert.That(_service.SelectedIssueId, Is.EqualTo("ISSUE-004"));
    }

    [Test]
    public void MoveUp_FromSecondIssue_SelectsFirstIssue()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectIssue("ISSUE-002");

        _service.MoveUp();

        Assert.That(_service.SelectedIndex, Is.EqualTo(0));
        Assert.That(_service.SelectedIssueId, Is.EqualTo("ISSUE-001"));
    }

    [Test]
    public void MoveUp_AtFirstIssue_StaysAtFirstIssue()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();

        _service.MoveUp();

        Assert.That(_service.SelectedIndex, Is.EqualTo(0));
        Assert.That(_service.SelectedIssueId, Is.EqualTo("ISSUE-001"));
    }

    [Test]
    public void MoveDown_RaisesOnStateChanged()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();
        var stateChanged = false;
        _service.OnStateChanged += () => stateChanged = true;

        _service.MoveDown();

        Assert.That(stateChanged, Is.True);
    }

    [Test]
    public void MoveUp_RaisesOnStateChanged()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectIssue("ISSUE-002");
        var stateChanged = false;
        _service.OnStateChanged += () => stateChanged = true;

        _service.MoveUp();

        Assert.That(stateChanged, Is.True);
    }

    #endregion

    #region Parent/Child Navigation Tests

    [Test]
    public void MoveToParent_WithParentLane_NavigatesToParent()
    {
        // ISSUE-003 has parentLane=2, ISSUE-004 is at lane=2
        _service.Initialize(_sampleRenderLines);
        _service.SelectIssue("ISSUE-003");

        _service.MoveToParent();

        Assert.That(_service.SelectedIssueId, Is.EqualTo("ISSUE-004"));
    }

    [Test]
    public void MoveToParent_NoParent_StaysAtCurrentIssue()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectIssue("ISSUE-001");

        _service.MoveToParent();

        Assert.That(_service.SelectedIssueId, Is.EqualTo("ISSUE-001"));
    }

    [Test]
    public void MoveToChild_WithChild_NavigatesToChild()
    {
        // ISSUE-004 is at lane=2, ISSUE-003 has parentLane=2
        _service.Initialize(_sampleRenderLines);
        _service.SelectIssue("ISSUE-004");

        _service.MoveToChild();

        Assert.That(_service.SelectedIssueId, Is.EqualTo("ISSUE-003"));
    }

    [Test]
    public void MoveToChild_NoChild_StaysAtCurrentIssue()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectIssue("ISSUE-001");

        _service.MoveToChild();

        Assert.That(_service.SelectedIssueId, Is.EqualTo("ISSUE-001"));
    }

    #endregion

    #region Edit Mode Tests

    [Test]
    public void StartEditingAtStart_SetsEditModeAndPendingEdit()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();

        _service.StartEditingAtStart();

        Assert.That(_service.EditMode, Is.EqualTo(KeyboardEditMode.EditingExisting));
        Assert.That(_service.PendingEdit, Is.Not.Null);
        Assert.That(_service.PendingEdit!.IssueId, Is.EqualTo("ISSUE-001"));
        Assert.That(_service.PendingEdit.Title, Is.EqualTo("First issue"));
        Assert.That(_service.PendingEdit.CursorPosition, Is.EqualTo(EditCursorPosition.Start));
    }

    [Test]
    public void StartEditingAtEnd_SetsCursorPositionToEnd()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();

        _service.StartEditingAtEnd();

        Assert.That(_service.EditMode, Is.EqualTo(KeyboardEditMode.EditingExisting));
        Assert.That(_service.PendingEdit!.CursorPosition, Is.EqualTo(EditCursorPosition.End));
    }

    [Test]
    public void StartReplacingTitle_ClearsTitleAndSetsReplaceMode()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();

        _service.StartReplacingTitle();

        Assert.That(_service.EditMode, Is.EqualTo(KeyboardEditMode.EditingExisting));
        Assert.That(_service.PendingEdit!.Title, Is.Empty);
        Assert.That(_service.PendingEdit.OriginalTitle, Is.EqualTo("First issue"));
        Assert.That(_service.PendingEdit.CursorPosition, Is.EqualTo(EditCursorPosition.Replace));
    }

    [Test]
    public void UpdateEditTitle_UpdatesPendingEditTitle()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();
        _service.StartEditingAtStart();

        _service.UpdateEditTitle("New title");

        Assert.That(_service.PendingEdit!.Title, Is.EqualTo("New title"));
    }

    [Test]
    public void CancelEdit_ReturnsToViewingMode()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();
        _service.StartEditingAtStart();

        _service.CancelEdit();

        Assert.That(_service.EditMode, Is.EqualTo(KeyboardEditMode.Viewing));
        Assert.That(_service.PendingEdit, Is.Null);
    }

    [Test]
    public void CancelEdit_PreservesSelection()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();
        _service.StartEditingAtStart();

        _service.CancelEdit();

        Assert.That(_service.SelectedIssueId, Is.EqualTo("ISSUE-001"));
    }

    [Test]
    public void StartEditing_NoSelection_DoesNothing()
    {
        _service.Initialize(_sampleRenderLines);
        // No selection made

        _service.StartEditingAtStart();

        Assert.That(_service.EditMode, Is.EqualTo(KeyboardEditMode.Viewing));
        Assert.That(_service.PendingEdit, Is.Null);
    }

    #endregion

    #region Create Issue Tests

    [Test]
    public void CreateIssueBelow_CreatesPendingNewIssue_WithoutApiCall()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();

        _service.CreateIssueBelow();

        Assert.That(_service.EditMode, Is.EqualTo(KeyboardEditMode.CreatingNew));
        Assert.That(_service.PendingNewIssue, Is.Not.Null);
        Assert.That(_service.PendingNewIssue!.InsertAtIndex, Is.EqualTo(1)); // After first issue
        Assert.That(_service.PendingNewIssue.IsAbove, Is.False);
        Assert.That(_service.PendingNewIssue.Title, Is.Empty);
        _mockIssueApi.VerifyNoOtherCalls();
    }

    [Test]
    public void CreateIssueAbove_CreatesPendingNewIssue_AtCorrectIndex()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectIssue("ISSUE-002");

        _service.CreateIssueAbove();

        Assert.That(_service.EditMode, Is.EqualTo(KeyboardEditMode.CreatingNew));
        Assert.That(_service.PendingNewIssue, Is.Not.Null);
        Assert.That(_service.PendingNewIssue!.InsertAtIndex, Is.EqualTo(1)); // At ISSUE-002's position
        Assert.That(_service.PendingNewIssue.IsAbove, Is.True);
        _mockIssueApi.VerifyNoOtherCalls();
    }

    [Test]
    public void CreateIssueBelow_AtRoot_CreatesAtCorrectPosition()
    {
        var singleIssue = new List<TaskGraphIssueRenderLine>
        {
            CreateIssueLine("ROOT-001", "Root", 0, TaskGraphMarkerType.Actionable)
        };
        _service.Initialize(singleIssue);
        _service.SelectFirstActionable();

        _service.CreateIssueBelow();

        Assert.That(_service.PendingNewIssue!.InsertAtIndex, Is.EqualTo(1));
    }

    [Test]
    public void CreateIssue_SetsReferenceIssueId()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();

        _service.CreateIssueBelow();

        Assert.That(_service.PendingNewIssue!.ReferenceIssueId, Is.EqualTo("ISSUE-001"));
    }

    [Test]
    public void CancelEdit_DiscardsPendingNewIssue()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();
        _service.CreateIssueBelow();

        _service.CancelEdit();

        Assert.That(_service.EditMode, Is.EqualTo(KeyboardEditMode.Viewing));
        Assert.That(_service.PendingNewIssue, Is.Null);
    }

    [Test]
    public void UpdateEditTitle_UpdatesPendingNewIssueTitle()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();
        _service.CreateIssueBelow();

        _service.UpdateEditTitle("New issue title");

        Assert.That(_service.PendingNewIssue!.Title, Is.EqualTo("New issue title"));
    }

    #endregion

    #region Indent/Unindent Tests

    [Test]
    public void IndentAsChild_WhileCreatingNew_SetsPendingParentId()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectIssue("ISSUE-002");
        _service.CreateIssueBelow();

        _service.IndentAsChild();

        Assert.That(_service.PendingNewIssue!.PendingParentId, Is.EqualTo("ISSUE-002"));
    }

    [Test]
    public void IndentAsChild_AtFirstPosition_DoesNotSetParent()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();
        _service.CreateIssueAbove(); // Creating above first issue

        _service.IndentAsChild();

        // No issue above, so no parent should be set
        Assert.That(_service.PendingNewIssue!.PendingParentId, Is.Null);
    }

    [Test]
    public void UnindentAsSibling_WhileCreatingNew_ClearsPendingParentId()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectIssue("ISSUE-002");
        _service.CreateIssueBelow();
        _service.IndentAsChild(); // First set a parent

        _service.UnindentAsSibling();

        Assert.That(_service.PendingNewIssue!.PendingParentId, Is.Null);
    }

    [Test]
    public void IndentAsChild_NotInEditMode_DoesNothing()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();

        _service.IndentAsChild();

        Assert.That(_service.EditMode, Is.EqualTo(KeyboardEditMode.Viewing));
    }

    #endregion

    #region AcceptEdit Tests

    [Test]
    public async Task AcceptEditAsync_WithEmptyTitle_DoesNotSave()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();
        _service.StartReplacingTitle(); // Clears title

        await _service.AcceptEditAsync();

        // Should still be in edit mode since title is empty
        Assert.That(_service.EditMode, Is.EqualTo(KeyboardEditMode.EditingExisting));
        _mockIssueApi.VerifyNoOtherCalls();
    }

    [Test]
    public async Task AcceptEditAsync_WithWhitespaceTitle_DoesNotSave()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();
        _service.StartReplacingTitle();
        _service.UpdateEditTitle("   ");

        await _service.AcceptEditAsync();

        Assert.That(_service.EditMode, Is.EqualTo(KeyboardEditMode.EditingExisting));
        _mockIssueApi.VerifyNoOtherCalls();
    }

    [Test]
    public async Task AcceptEditAsync_ForNewIssue_WithEmptyTitle_DoesNotSave()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();
        _service.CreateIssueBelow();
        // Title is empty by default

        await _service.AcceptEditAsync();

        Assert.That(_service.EditMode, Is.EqualTo(KeyboardEditMode.CreatingNew));
        _mockIssueApi.VerifyNoOtherCalls();
    }

    #endregion

    #region Event Tests

    [Test]
    public void StartEditingAtStart_RaisesOnStateChanged()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();
        var stateChanged = false;
        _service.OnStateChanged += () => stateChanged = true;

        _service.StartEditingAtStart();

        Assert.That(stateChanged, Is.True);
    }

    [Test]
    public void CreateIssueBelow_RaisesOnStateChanged()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();
        var stateChanged = false;
        _service.OnStateChanged += () => stateChanged = true;

        _service.CreateIssueBelow();

        Assert.That(stateChanged, Is.True);
    }

    [Test]
    public void CancelEdit_RaisesOnStateChanged()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();
        _service.StartEditingAtStart();
        var stateChanged = false;
        _service.OnStateChanged += () => stateChanged = true;

        _service.CancelEdit();

        Assert.That(stateChanged, Is.True);
    }

    #endregion

    #region Edge Cases

    [Test]
    public void Navigation_DuringEditMode_IsIgnored()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();
        _service.StartEditingAtStart();

        _service.MoveDown();

        // Selection should not change during edit mode
        Assert.That(_service.SelectedIssueId, Is.EqualTo("ISSUE-001"));
    }

    [Test]
    public void CreateIssue_DuringEditMode_IsIgnored()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();
        _service.StartEditingAtStart();

        _service.CreateIssueBelow();

        // Should still be in EditingExisting mode
        Assert.That(_service.EditMode, Is.EqualTo(KeyboardEditMode.EditingExisting));
        Assert.That(_service.PendingNewIssue, Is.Null);
    }

    [Test]
    public void StartEditing_DuringCreateMode_IsIgnored()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();
        _service.CreateIssueBelow();

        _service.StartEditingAtStart();

        // Should still be in CreatingNew mode
        Assert.That(_service.EditMode, Is.EqualTo(KeyboardEditMode.CreatingNew));
    }

    #endregion
}
