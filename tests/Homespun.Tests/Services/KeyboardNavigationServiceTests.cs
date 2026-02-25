using Fleece.Core.Models;
using Homespun.Client.Components;
using Homespun.Client.Services;
using Homespun.Shared.Models.Fleece;
using Homespun.Tests.Helpers;
using Moq;

namespace Homespun.Tests.Services;

[TestFixture]
public class KeyboardNavigationServiceTests
{
    private KeyboardNavigationService _service = null!;
    private Mock<HttpIssueApiService> _mockIssueApi = null!;
    private MockHttpMessageHandler _mockHandler = null!;
    private List<TaskGraphIssueRenderLine> _sampleRenderLines = null!;

    [SetUp]
    public void Setup()
    {
        _mockHandler = new MockHttpMessageHandler();
        _mockHandler.RespondWith("issues", new IssueResponse
        {
            Id = "new-issue",
            Title = "test",
            Type = IssueType.Task,
            Status = IssueStatus.Open
        });
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

    #region MoveToFirst/MoveToLast Tests

    [Test]
    public void MoveToFirst_SelectsFirstIssue()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectIssue("ISSUE-004"); // Start at last

        _service.MoveToFirst();

        Assert.That(_service.SelectedIndex, Is.EqualTo(0));
        Assert.That(_service.SelectedIssueId, Is.EqualTo("ISSUE-001"));
    }

    [Test]
    public void MoveToFirst_FromMiddle_SelectsFirstIssue()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectIssue("ISSUE-002"); // Start in middle

        _service.MoveToFirst();

        Assert.That(_service.SelectedIndex, Is.EqualTo(0));
        Assert.That(_service.SelectedIssueId, Is.EqualTo("ISSUE-001"));
    }

    [Test]
    public void MoveToFirst_EmptyList_DoesNotThrow()
    {
        _service.Initialize([]);

        Assert.DoesNotThrow(() => _service.MoveToFirst());
        Assert.That(_service.SelectedIndex, Is.EqualTo(-1));
    }

    [Test]
    public void MoveToFirst_DuringEditMode_IsIgnored()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectIssue("ISSUE-003");
        _service.StartEditingAtStart();

        _service.MoveToFirst();

        Assert.That(_service.SelectedIssueId, Is.EqualTo("ISSUE-003")); // Unchanged
    }

    [Test]
    public void MoveToFirst_RaisesOnStateChanged()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectIssue("ISSUE-003");
        var stateChanged = false;
        _service.OnStateChanged += () => stateChanged = true;

        _service.MoveToFirst();

        Assert.That(stateChanged, Is.True);
    }

    [Test]
    public void MoveToLast_SelectsLastIssue()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable(); // Start at first

        _service.MoveToLast();

        Assert.That(_service.SelectedIndex, Is.EqualTo(3));
        Assert.That(_service.SelectedIssueId, Is.EqualTo("ISSUE-004"));
    }

    [Test]
    public void MoveToLast_FromMiddle_SelectsLastIssue()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectIssue("ISSUE-002"); // Start in middle

        _service.MoveToLast();

        Assert.That(_service.SelectedIndex, Is.EqualTo(3));
        Assert.That(_service.SelectedIssueId, Is.EqualTo("ISSUE-004"));
    }

    [Test]
    public void MoveToLast_EmptyList_DoesNotThrow()
    {
        _service.Initialize([]);

        Assert.DoesNotThrow(() => _service.MoveToLast());
        Assert.That(_service.SelectedIndex, Is.EqualTo(-1));
    }

    [Test]
    public void MoveToLast_DuringEditMode_IsIgnored()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();
        _service.StartEditingAtStart();

        _service.MoveToLast();

        Assert.That(_service.SelectedIssueId, Is.EqualTo("ISSUE-001")); // Unchanged
    }

    [Test]
    public void MoveToLast_RaisesOnStateChanged()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();
        var stateChanged = false;
        _service.OnStateChanged += () => stateChanged = true;

        _service.MoveToLast();

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
    public void IndentAsChild_WhileCreatingNew_SetsPendingChildId()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectIssue("ISSUE-002");
        _service.CreateIssueBelow();

        _service.IndentAsChild();

        // Tab: new issue becomes parent of reference issue (ISSUE-002)
        Assert.That(_service.PendingNewIssue!.PendingChildId, Is.EqualTo("ISSUE-002"));
        Assert.That(_service.PendingNewIssue!.PendingParentId, Is.Null);
    }

    [Test]
    public void IndentAsChild_ClearsInheritedParent()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectIssue("ISSUE-002");
        _service.CreateIssueBelow();

        _service.IndentAsChild();

        // Tab clears inherited parent since new issue is becoming a parent
        Assert.That(_service.PendingNewIssue!.InheritedParentIssueId, Is.Null);
        Assert.That(_service.PendingNewIssue!.InheritedParentSortOrder, Is.Null);
    }

    [Test]
    public void UnindentAsSibling_WhileCreatingNew_SetsPendingParentId()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectIssue("ISSUE-002");
        _service.CreateIssueBelow();

        _service.UnindentAsSibling();

        // Shift+Tab: new issue becomes child of reference issue (ISSUE-002)
        Assert.That(_service.PendingNewIssue!.PendingParentId, Is.EqualTo("ISSUE-002"));
        Assert.That(_service.PendingNewIssue!.PendingChildId, Is.Null);
    }

    [Test]
    public void UnindentAsSibling_AfterIndent_SwitchesToChild()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectIssue("ISSUE-002");
        _service.CreateIssueBelow();
        _service.IndentAsChild(); // First set as parent (Tab)

        _service.UnindentAsSibling(); // Then set as child (Shift+Tab)

        Assert.That(_service.PendingNewIssue!.PendingParentId, Is.EqualTo("ISSUE-002"));
        Assert.That(_service.PendingNewIssue!.PendingChildId, Is.Null);
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

    [Test]
    public async Task AcceptEditAsync_EditExisting_WithValidTitle_TransitionsToViewing()
    {
        var service = CreateServiceWithHttpClient();
        service.Initialize(_sampleRenderLines);
        service.SetProjectId("test-project");
        service.SelectFirstActionable();
        service.StartEditingAtStart();
        service.UpdateEditTitle("Updated title");

        await service.AcceptEditAsync();

        Assert.That(service.EditMode, Is.EqualTo(KeyboardEditMode.Viewing));
        Assert.That(service.PendingEdit, Is.Null);
        Assert.That(service.SelectedIssueId, Is.EqualTo("ISSUE-001")); // Selection preserved
    }

    [Test]
    public async Task AcceptEditAsync_EditExisting_RaisesOnStateChanged()
    {
        var service = CreateServiceWithHttpClient();
        service.Initialize(_sampleRenderLines);
        service.SetProjectId("test-project");
        service.SelectFirstActionable();
        service.StartEditingAtStart();
        service.UpdateEditTitle("Updated title");
        var stateChanged = false;
        service.OnStateChanged += () => stateChanged = true;

        await service.AcceptEditAsync();

        Assert.That(stateChanged, Is.True);
    }

    [Test]
    public async Task AcceptEditAsync_CreateNew_WithValidTitle_TransitionsToViewing()
    {
        var service = CreateServiceWithHttpClient();
        service.Initialize(_sampleRenderLines);
        service.SetProjectId("test-project");
        service.SelectFirstActionable();
        service.CreateIssueBelow();
        service.UpdateEditTitle("New issue");

        await service.AcceptEditAsync();

        Assert.That(service.EditMode, Is.EqualTo(KeyboardEditMode.Viewing));
        Assert.That(service.PendingNewIssue, Is.Null);
    }

    [Test]
    public async Task AcceptEditAsync_CreateNew_RaisesOnStateChanged()
    {
        var service = CreateServiceWithHttpClient();
        service.Initialize(_sampleRenderLines);
        service.SetProjectId("test-project");
        service.SelectFirstActionable();
        service.CreateIssueBelow();
        service.UpdateEditTitle("New issue");
        var stateChanged = false;
        service.OnStateChanged += () => stateChanged = true;

        await service.AcceptEditAsync();

        Assert.That(stateChanged, Is.True);
    }

    [Test]
    public async Task AcceptEditAsync_WithNoProjectId_DoesNotTransition()
    {
        _service.Initialize(_sampleRenderLines);
        // No SetProjectId call
        _service.SelectFirstActionable();
        _service.StartEditingAtStart();
        _service.UpdateEditTitle("Updated title");

        await _service.AcceptEditAsync();

        // Should remain in edit mode because no project ID is set
        Assert.That(_service.EditMode, Is.EqualTo(KeyboardEditMode.EditingExisting));
    }

    /// <summary>
    /// Creates a KeyboardNavigationService backed by a real HttpIssueApiService
    /// with a mock HTTP handler, for tests that exercise AcceptEditAsync API calls.
    /// </summary>
    private KeyboardNavigationService CreateServiceWithHttpClient()
    {
        var issueApi = new HttpIssueApiService(_mockHandler.CreateClient());
        return new KeyboardNavigationService(issueApi);
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

    #region Hierarchy Creation Helper Methods

    /// <summary>
    /// Creates a hierarchy with three children under one parent.
    /// Graph: Child1 -> Child2 -> Child3 -> Parent
    /// Render order (top to bottom): Child1, Child2, Child3, Parent
    /// </summary>
    private (List<TaskGraphIssueRenderLine> Lines, List<TaskGraphNodeResponse> Nodes) CreateThreeChildrenWithParent()
    {
        var lines = new List<TaskGraphIssueRenderLine>
        {
            CreateIssueLine("CHILD-001", "Child issue 1", 0, TaskGraphMarkerType.Actionable, parentLane: 1, isSeriesChild: true),
            CreateIssueLine("CHILD-002", "Child issue 2", 0, TaskGraphMarkerType.Open, parentLane: 1, isSeriesChild: true),
            CreateIssueLine("CHILD-003", "Child issue 3", 0, TaskGraphMarkerType.Open, parentLane: 1, isSeriesChild: true, isFirstChild: true),
            CreateIssueLine("PARENT-001", "Parent issue", 1, TaskGraphMarkerType.Open)
        };

        var nodes = new List<TaskGraphNodeResponse>
        {
            new()
            {
                Issue = new IssueResponse
                {
                    Id = "CHILD-001",
                    Title = "Child issue 1",
                    Type = IssueType.Task,
                    ParentIssues = [new ParentIssueRefResponse { ParentIssue = "PARENT-001", SortOrder = "a" }]
                },
                Lane = 0,
                Row = 0,
                IsActionable = true
            },
            new()
            {
                Issue = new IssueResponse
                {
                    Id = "CHILD-002",
                    Title = "Child issue 2",
                    Type = IssueType.Task,
                    ParentIssues = [new ParentIssueRefResponse { ParentIssue = "PARENT-001", SortOrder = "b" }]
                },
                Lane = 0,
                Row = 1,
                IsActionable = false
            },
            new()
            {
                Issue = new IssueResponse
                {
                    Id = "CHILD-003",
                    Title = "Child issue 3",
                    Type = IssueType.Task,
                    ParentIssues = [new ParentIssueRefResponse { ParentIssue = "PARENT-001", SortOrder = "c" }]
                },
                Lane = 0,
                Row = 2,
                IsActionable = false
            },
            new()
            {
                Issue = new IssueResponse
                {
                    Id = "PARENT-001",
                    Title = "Parent issue",
                    Type = IssueType.Feature,
                    ParentIssues = []
                },
                Lane = 1,
                Row = 3,
                IsActionable = false
            }
        };

        return (lines, nodes);
    }

    /// <summary>
    /// Creates a hierarchy with two children under one parent.
    /// Graph: Child1 -> Child2 -> Parent
    /// Render order (top to bottom): Child1, Child2, Parent
    /// </summary>
    private (List<TaskGraphIssueRenderLine> Lines, List<TaskGraphNodeResponse> Nodes) CreateTwoChildrenWithParent()
    {
        var lines = new List<TaskGraphIssueRenderLine>
        {
            CreateIssueLine("CHILD-001", "Child issue 1", 0, TaskGraphMarkerType.Actionable, parentLane: 1, isSeriesChild: true),
            CreateIssueLine("CHILD-002", "Child issue 2", 0, TaskGraphMarkerType.Open, parentLane: 1, isSeriesChild: true, isFirstChild: true),
            CreateIssueLine("PARENT-001", "Parent issue", 1, TaskGraphMarkerType.Open)
        };

        var nodes = new List<TaskGraphNodeResponse>
        {
            new()
            {
                Issue = new IssueResponse
                {
                    Id = "CHILD-001",
                    Title = "Child issue 1",
                    Type = IssueType.Task,
                    ParentIssues = [new ParentIssueRefResponse { ParentIssue = "PARENT-001", SortOrder = "a" }]
                },
                Lane = 0,
                Row = 0,
                IsActionable = true
            },
            new()
            {
                Issue = new IssueResponse
                {
                    Id = "CHILD-002",
                    Title = "Child issue 2",
                    Type = IssueType.Task,
                    ParentIssues = [new ParentIssueRefResponse { ParentIssue = "PARENT-001", SortOrder = "b" }]
                },
                Lane = 0,
                Row = 1,
                IsActionable = false
            },
            new()
            {
                Issue = new IssueResponse
                {
                    Id = "PARENT-001",
                    Title = "Parent issue",
                    Type = IssueType.Feature,
                    ParentIssues = []
                },
                Lane = 1,
                Row = 2,
                IsActionable = false
            }
        };

        return (lines, nodes);
    }

    /// <summary>
    /// Creates an orphan above a child hierarchy.
    /// Graph: Orphan (no parent), Child1 -> Child2 -> Parent
    /// Render order (top to bottom): Orphan, Child1, Child2, Parent
    /// </summary>
    private (List<TaskGraphIssueRenderLine> Lines, List<TaskGraphNodeResponse> Nodes) CreateOrphanAboveChildHierarchy()
    {
        var lines = new List<TaskGraphIssueRenderLine>
        {
            CreateIssueLine("ORPHAN-001", "Next issue (orphan)", 0, TaskGraphMarkerType.Open),
            CreateIssueLine("CHILD-001", "Child issue 1", 0, TaskGraphMarkerType.Actionable, parentLane: 1, isSeriesChild: true),
            CreateIssueLine("CHILD-002", "Child issue 2", 0, TaskGraphMarkerType.Open, parentLane: 1, isSeriesChild: true, isFirstChild: true),
            CreateIssueLine("PARENT-001", "Parent issue", 1, TaskGraphMarkerType.Open)
        };

        var nodes = new List<TaskGraphNodeResponse>
        {
            new()
            {
                Issue = new IssueResponse
                {
                    Id = "ORPHAN-001",
                    Title = "Next issue (orphan)",
                    Type = IssueType.Task,
                    ParentIssues = []
                },
                Lane = 0,
                Row = 0,
                IsActionable = false
            },
            new()
            {
                Issue = new IssueResponse
                {
                    Id = "CHILD-001",
                    Title = "Child issue 1",
                    Type = IssueType.Task,
                    ParentIssues = [new ParentIssueRefResponse { ParentIssue = "PARENT-001", SortOrder = "a" }]
                },
                Lane = 0,
                Row = 1,
                IsActionable = true
            },
            new()
            {
                Issue = new IssueResponse
                {
                    Id = "CHILD-002",
                    Title = "Child issue 2",
                    Type = IssueType.Task,
                    ParentIssues = [new ParentIssueRefResponse { ParentIssue = "PARENT-001", SortOrder = "b" }]
                },
                Lane = 0,
                Row = 2,
                IsActionable = false
            },
            new()
            {
                Issue = new IssueResponse
                {
                    Id = "PARENT-001",
                    Title = "Parent issue",
                    Type = IssueType.Feature,
                    ParentIssues = []
                },
                Lane = 1,
                Row = 3,
                IsActionable = false
            }
        };

        return (lines, nodes);
    }

    /// <summary>
    /// Creates a child hierarchy above an orphan.
    /// Graph: Child1 -> Child2 -> Parent, Orphan (no parent)
    /// Render order (top to bottom): Child1, Child2, Parent, Orphan
    /// </summary>
    private (List<TaskGraphIssueRenderLine> Lines, List<TaskGraphNodeResponse> Nodes) CreateChildHierarchyAboveOrphan()
    {
        var lines = new List<TaskGraphIssueRenderLine>
        {
            CreateIssueLine("CHILD-001", "Child issue 1", 0, TaskGraphMarkerType.Actionable, parentLane: 1, isSeriesChild: true),
            CreateIssueLine("CHILD-002", "Child issue 2", 0, TaskGraphMarkerType.Open, parentLane: 1, isSeriesChild: true, isFirstChild: true),
            CreateIssueLine("PARENT-001", "Parent issue", 1, TaskGraphMarkerType.Open),
            CreateIssueLine("ORPHAN-001", "Next issue (orphan)", 0, TaskGraphMarkerType.Open)
        };

        var nodes = new List<TaskGraphNodeResponse>
        {
            new()
            {
                Issue = new IssueResponse
                {
                    Id = "CHILD-001",
                    Title = "Child issue 1",
                    Type = IssueType.Task,
                    ParentIssues = [new ParentIssueRefResponse { ParentIssue = "PARENT-001", SortOrder = "a" }]
                },
                Lane = 0,
                Row = 0,
                IsActionable = true
            },
            new()
            {
                Issue = new IssueResponse
                {
                    Id = "CHILD-002",
                    Title = "Child issue 2",
                    Type = IssueType.Task,
                    ParentIssues = [new ParentIssueRefResponse { ParentIssue = "PARENT-001", SortOrder = "b" }]
                },
                Lane = 0,
                Row = 1,
                IsActionable = false
            },
            new()
            {
                Issue = new IssueResponse
                {
                    Id = "PARENT-001",
                    Title = "Parent issue",
                    Type = IssueType.Feature,
                    ParentIssues = []
                },
                Lane = 1,
                Row = 2,
                IsActionable = false
            },
            new()
            {
                Issue = new IssueResponse
                {
                    Id = "ORPHAN-001",
                    Title = "Next issue (orphan)",
                    Type = IssueType.Task,
                    ParentIssues = []
                },
                Lane = 0,
                Row = 3,
                IsActionable = false
            }
        };

        return (lines, nodes);
    }

    /// <summary>
    /// Creates two adjacent hierarchies.
    /// Graph: Child1.1 -> Child1.2 -> Parent1, Child2.1 -> Parent2
    /// Render order (top to bottom): Child1.1, Child1.2, Parent1, Child2.1, Parent2
    /// </summary>
    private (List<TaskGraphIssueRenderLine> Lines, List<TaskGraphNodeResponse> Nodes) CreateTwoAdjacentHierarchies()
    {
        var lines = new List<TaskGraphIssueRenderLine>
        {
            CreateIssueLine("CHILD-1-1", "Child issue 1.1", 0, TaskGraphMarkerType.Actionable, parentLane: 1, isSeriesChild: true),
            CreateIssueLine("CHILD-1-2", "Child issue 1.2", 0, TaskGraphMarkerType.Open, parentLane: 1, isSeriesChild: true, isFirstChild: true),
            CreateIssueLine("PARENT-001", "Parent issue 1", 1, TaskGraphMarkerType.Open),
            CreateIssueLine("CHILD-2-1", "Child issue 2.1", 0, TaskGraphMarkerType.Open, parentLane: 1, isSeriesChild: true, isFirstChild: true),
            CreateIssueLine("PARENT-002", "Parent issue 2", 1, TaskGraphMarkerType.Open)
        };

        var nodes = new List<TaskGraphNodeResponse>
        {
            new()
            {
                Issue = new IssueResponse
                {
                    Id = "CHILD-1-1",
                    Title = "Child issue 1.1",
                    Type = IssueType.Task,
                    ParentIssues = [new ParentIssueRefResponse { ParentIssue = "PARENT-001", SortOrder = "a" }]
                },
                Lane = 0,
                Row = 0,
                IsActionable = true
            },
            new()
            {
                Issue = new IssueResponse
                {
                    Id = "CHILD-1-2",
                    Title = "Child issue 1.2",
                    Type = IssueType.Task,
                    ParentIssues = [new ParentIssueRefResponse { ParentIssue = "PARENT-001", SortOrder = "b" }]
                },
                Lane = 0,
                Row = 1,
                IsActionable = false
            },
            new()
            {
                Issue = new IssueResponse
                {
                    Id = "PARENT-001",
                    Title = "Parent issue 1",
                    Type = IssueType.Feature,
                    ParentIssues = []
                },
                Lane = 1,
                Row = 2,
                IsActionable = false
            },
            new()
            {
                Issue = new IssueResponse
                {
                    Id = "CHILD-2-1",
                    Title = "Child issue 2.1",
                    Type = IssueType.Task,
                    ParentIssues = [new ParentIssueRefResponse { ParentIssue = "PARENT-002", SortOrder = "a" }]
                },
                Lane = 0,
                Row = 3,
                IsActionable = false
            },
            new()
            {
                Issue = new IssueResponse
                {
                    Id = "PARENT-002",
                    Title = "Parent issue 2",
                    Type = IssueType.Feature,
                    ParentIssues = []
                },
                Lane = 1,
                Row = 4,
                IsActionable = false
            }
        };

        return (lines, nodes);
    }

    #endregion

    #region Parent Inheritance Tests

    private List<TaskGraphNodeResponse> CreateTaskGraphNodesWithParent()
    {
        return
        [
            new TaskGraphNodeResponse
            {
                Issue = new IssueResponse
                {
                    Id = "PARENT-001",
                    Title = "Parent issue",
                    Type = IssueType.Feature,
                    ParentIssues = []
                },
                Lane = 1,
                Row = 0,
                IsActionable = false
            },
            new TaskGraphNodeResponse
            {
                Issue = new IssueResponse
                {
                    Id = "ISSUE-001",
                    Title = "First issue",
                    Type = IssueType.Task,
                    ParentIssues = [new ParentIssueRefResponse { ParentIssue = "PARENT-001", SortOrder = "a" }]
                },
                Lane = 0,
                Row = 1,
                IsActionable = true
            },
            new TaskGraphNodeResponse
            {
                Issue = new IssueResponse
                {
                    Id = "ISSUE-002",
                    Title = "Second issue",
                    Type = IssueType.Task,
                    ParentIssues = [new ParentIssueRefResponse { ParentIssue = "PARENT-001", SortOrder = "b" }]
                },
                Lane = 0,
                Row = 2,
                IsActionable = false
            }
        ];
    }

    [Test]
    public void CreateIssueBelow_InheritsParent_WhenReferenceIssueHasParent()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SetTaskGraphNodes(CreateTaskGraphNodesWithParent());
        _service.SelectFirstActionable();

        _service.CreateIssueBelow();

        Assert.That(_service.PendingNewIssue, Is.Not.Null);
        Assert.That(_service.PendingNewIssue!.InheritedParentIssueId, Is.EqualTo("PARENT-001"));
        Assert.That(_service.PendingNewIssue.InheritedParentSortOrder, Is.Not.Null);
    }

    [Test]
    public void CreateIssueAbove_InheritsParent_WhenReferenceIssueHasParent()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SetTaskGraphNodes(CreateTaskGraphNodesWithParent());
        _service.SelectIssue("ISSUE-002");

        _service.CreateIssueAbove();

        Assert.That(_service.PendingNewIssue, Is.Not.Null);
        Assert.That(_service.PendingNewIssue!.InheritedParentIssueId, Is.EqualTo("PARENT-001"));
        Assert.That(_service.PendingNewIssue.InheritedParentSortOrder, Is.Not.Null);
    }

    [Test]
    public void CreateIssueBelow_NoParent_WhenReferenceIssueIsRoot()
    {
        var rootNodes = new List<TaskGraphNodeResponse>
        {
            new()
            {
                Issue = new IssueResponse
                {
                    Id = "ISSUE-001",
                    Title = "Root issue",
                    Type = IssueType.Task,
                    ParentIssues = []
                },
                Lane = 0,
                Row = 0,
                IsActionable = true
            }
        };

        _service.Initialize(_sampleRenderLines);
        _service.SetTaskGraphNodes(rootNodes);
        _service.SelectFirstActionable();

        _service.CreateIssueBelow();

        Assert.That(_service.PendingNewIssue, Is.Not.Null);
        Assert.That(_service.PendingNewIssue!.InheritedParentIssueId, Is.Null);
        Assert.That(_service.PendingNewIssue.InheritedParentSortOrder, Is.Null);
    }

    [Test]
    public async Task AcceptEditAsync_CreatesIssueWithInheritedParent()
    {
        var handler = new MockHttpMessageHandler();
        handler.RespondWith("issues", new IssueResponse
        {
            Id = "new-issue",
            Title = "test",
            Type = IssueType.Task,
            Status = IssueStatus.Open
        });
        var issueApi = new HttpIssueApiService(handler.CreateClient());
        var service = new KeyboardNavigationService(issueApi);

        service.Initialize(_sampleRenderLines);
        service.SetProjectId("test-project");
        service.SetTaskGraphNodes(CreateTaskGraphNodesWithParent());
        service.SelectFirstActionable();
        service.CreateIssueBelow();
        service.UpdateEditTitle("New sibling issue");

        await service.AcceptEditAsync();

        Assert.That(service.EditMode, Is.EqualTo(KeyboardEditMode.Viewing));

        // Verify the request included parent info
        var createRequest = handler.CapturedRequests
            .FirstOrDefault(r => r.Method == HttpMethod.Post && r.Url.Contains("issues"));
        Assert.That(createRequest, Is.Not.Null);
        var body = createRequest!.BodyAs<CreateIssueRequest>();
        Assert.That(body, Is.Not.Null);
        Assert.That(body!.ParentIssueId, Is.EqualTo("PARENT-001"));
        Assert.That(body.ParentSortOrder, Is.Not.Null);
    }

    [Test]
    public async Task AcceptEditAsync_TabSetsChildIssueId()
    {
        var handler = new MockHttpMessageHandler();
        handler.RespondWith("issues", new IssueResponse
        {
            Id = "new-issue",
            Title = "test",
            Type = IssueType.Task,
            Status = IssueStatus.Open
        });
        var issueApi = new HttpIssueApiService(handler.CreateClient());
        var service = new KeyboardNavigationService(issueApi);

        service.Initialize(_sampleRenderLines);
        service.SetProjectId("test-project");
        service.SetTaskGraphNodes(CreateTaskGraphNodesWithParent());
        service.SelectFirstActionable();
        service.CreateIssueBelow();
        service.IndentAsChild(); // Tab sets PendingChildId (new becomes parent)
        service.UpdateEditTitle("Parent issue");

        await service.AcceptEditAsync();

        Assert.That(service.EditMode, Is.EqualTo(KeyboardEditMode.Viewing));

        // Verify Tab sets ChildIssueId (new issue becomes parent of reference issue)
        var createRequest = handler.CapturedRequests
            .FirstOrDefault(r => r.Method == HttpMethod.Post && r.Url.Contains("issues"));
        Assert.That(createRequest, Is.Not.Null);
        var body = createRequest!.BodyAs<CreateIssueRequest>();
        Assert.That(body, Is.Not.Null);
        // Tab: new issue becomes parent, so ChildIssueId = reference issue
        Assert.That(body!.ChildIssueId, Is.EqualTo("ISSUE-001"));
        Assert.That(body.ParentIssueId, Is.Null);
    }

    [Test]
    public async Task AcceptEditAsync_ShiftTabSetsParentIssueId()
    {
        var handler = new MockHttpMessageHandler();
        handler.RespondWith("issues", new IssueResponse
        {
            Id = "new-issue",
            Title = "test",
            Type = IssueType.Task,
            Status = IssueStatus.Open
        });
        var issueApi = new HttpIssueApiService(handler.CreateClient());
        var service = new KeyboardNavigationService(issueApi);

        service.Initialize(_sampleRenderLines);
        service.SetProjectId("test-project");
        service.SelectFirstActionable();
        service.CreateIssueBelow();
        service.UnindentAsSibling(); // Shift+Tab sets PendingParentId (new becomes child)
        service.UpdateEditTitle("Child issue");

        await service.AcceptEditAsync();

        Assert.That(service.EditMode, Is.EqualTo(KeyboardEditMode.Viewing));

        // Verify Shift+Tab sets ParentIssueId (new issue becomes child of reference issue)
        var createRequest = handler.CapturedRequests
            .FirstOrDefault(r => r.Method == HttpMethod.Post && r.Url.Contains("issues"));
        Assert.That(createRequest, Is.Not.Null);
        var body = createRequest!.BodyAs<CreateIssueRequest>();
        Assert.That(body, Is.Not.Null);
        // Shift+Tab: new issue becomes child, so ParentIssueId = reference issue
        Assert.That(body!.ParentIssueId, Is.EqualTo("ISSUE-001"));
        Assert.That(body.ChildIssueId, Is.Null);
    }

    #endregion

    #region Section 1 - Default (Sibling) Creation Tests

    // Section 1.1: Between two children
    [Test]
    public void CreateIssueBelow_BetweenTwoChildren_InheritsParentAndComputesMidpointSortOrder()
    {
        var (lines, nodes) = CreateThreeChildrenWithParent();
        _service.Initialize(lines);
        _service.SetTaskGraphNodes(nodes);
        _service.SelectIssue("CHILD-001"); // Select first child

        _service.CreateIssueBelow(); // Insert between Child 1 and Child 2

        Assert.That(_service.PendingNewIssue, Is.Not.Null);
        Assert.That(_service.PendingNewIssue!.InheritedParentIssueId, Is.EqualTo("PARENT-001"));
        Assert.That(_service.PendingNewIssue.InheritedParentSortOrder, Is.Not.Null);
        // Sort order should be between "a" (Child 1) and "b" (Child 2)
        Assert.That(string.Compare(_service.PendingNewIssue.InheritedParentSortOrder, "a", StringComparison.Ordinal), Is.GreaterThan(0));
        Assert.That(string.Compare(_service.PendingNewIssue.InheritedParentSortOrder, "b", StringComparison.Ordinal), Is.LessThan(0));
    }

    [Test]
    public void CreateIssueAbove_BetweenTwoChildren_InheritsParentAndComputesMidpointSortOrder()
    {
        var (lines, nodes) = CreateThreeChildrenWithParent();
        _service.Initialize(lines);
        _service.SetTaskGraphNodes(nodes);
        _service.SelectIssue("CHILD-002"); // Select second child

        _service.CreateIssueAbove(); // Insert between Child 1 and Child 2

        Assert.That(_service.PendingNewIssue, Is.Not.Null);
        Assert.That(_service.PendingNewIssue!.InheritedParentIssueId, Is.EqualTo("PARENT-001"));
        Assert.That(_service.PendingNewIssue.InheritedParentSortOrder, Is.Not.Null);
        // Sort order should be between "a" (Child 1) and "b" (Child 2)
        Assert.That(string.Compare(_service.PendingNewIssue.InheritedParentSortOrder, "a", StringComparison.Ordinal), Is.GreaterThan(0));
        Assert.That(string.Compare(_service.PendingNewIssue.InheritedParentSortOrder, "b", StringComparison.Ordinal), Is.LessThan(0));
    }

    // Section 1.2: Between a child and parent
    [Test]
    public void CreateIssueBelow_BetweenLastChildAndParent_InheritsParent()
    {
        var (lines, nodes) = CreateTwoChildrenWithParent();
        _service.Initialize(lines);
        _service.SetTaskGraphNodes(nodes);
        _service.SelectIssue("CHILD-002"); // Select last child

        _service.CreateIssueBelow(); // Insert between Child 2 and Parent

        Assert.That(_service.PendingNewIssue, Is.Not.Null);
        Assert.That(_service.PendingNewIssue!.InheritedParentIssueId, Is.EqualTo("PARENT-001"));
        Assert.That(_service.PendingNewIssue.InheritedParentSortOrder, Is.Not.Null);
        // Sort order should be after "b" (Child 2)
        Assert.That(string.Compare(_service.PendingNewIssue.InheritedParentSortOrder, "b", StringComparison.Ordinal), Is.GreaterThan(0));
    }

    [Test]
    public void CreateIssueAbove_BetweenLastChildAndParent_InheritsParent()
    {
        var (lines, nodes) = CreateTwoChildrenWithParent();
        _service.Initialize(lines);
        _service.SetTaskGraphNodes(nodes);
        _service.SelectIssue("PARENT-001"); // Select parent

        _service.CreateIssueAbove(); // Insert between Child 2 and Parent

        // Reference issue is Parent, which has no parent of its own
        Assert.That(_service.PendingNewIssue, Is.Not.Null);
        Assert.That(_service.PendingNewIssue!.InheritedParentIssueId, Is.Null);
    }

    // Section 1.3: Between an orphan and a child hierarchy
    [Test]
    public void CreateIssueAbove_AboveChildInOrphanAboveHierarchy_InheritsParent()
    {
        var (lines, nodes) = CreateOrphanAboveChildHierarchy();
        _service.Initialize(lines);
        _service.SetTaskGraphNodes(nodes);
        _service.SelectIssue("CHILD-001"); // Select Child 1 (has Parent)

        _service.CreateIssueAbove(); // Insert above Child 1

        Assert.That(_service.PendingNewIssue, Is.Not.Null);
        Assert.That(_service.PendingNewIssue!.InheritedParentIssueId, Is.EqualTo("PARENT-001"));
        Assert.That(_service.PendingNewIssue.InheritedParentSortOrder, Is.Not.Null);
        // Sort order should be before "a" (Child 1)
        Assert.That(string.Compare(_service.PendingNewIssue.InheritedParentSortOrder, "a", StringComparison.Ordinal), Is.LessThan(0));
    }

    [Test]
    public void CreateIssueBelow_BelowOrphanInOrphanAboveHierarchy_IsOrphan()
    {
        var (lines, nodes) = CreateOrphanAboveChildHierarchy();
        _service.Initialize(lines);
        _service.SetTaskGraphNodes(nodes);
        _service.SelectIssue("ORPHAN-001"); // Select Orphan (no parent)

        _service.CreateIssueBelow(); // Insert below Orphan

        Assert.That(_service.PendingNewIssue, Is.Not.Null);
        Assert.That(_service.PendingNewIssue!.InheritedParentIssueId, Is.Null);
        Assert.That(_service.PendingNewIssue.InheritedParentSortOrder, Is.Null);
    }

    // Section 1.4: After a parent and before an orphan
    [Test]
    public void CreateIssueBelow_BelowParentBeforeOrphan_IsOrphan()
    {
        var (lines, nodes) = CreateChildHierarchyAboveOrphan();
        _service.Initialize(lines);
        _service.SetTaskGraphNodes(nodes);
        _service.SelectIssue("PARENT-001"); // Select Parent (no parent of its own)

        _service.CreateIssueBelow(); // Insert below Parent

        Assert.That(_service.PendingNewIssue, Is.Not.Null);
        Assert.That(_service.PendingNewIssue!.InheritedParentIssueId, Is.Null);
        Assert.That(_service.PendingNewIssue.InheritedParentSortOrder, Is.Null);
    }

    [Test]
    public void CreateIssueAbove_AboveOrphanAfterParent_IsOrphan()
    {
        var (lines, nodes) = CreateChildHierarchyAboveOrphan();
        _service.Initialize(lines);
        _service.SetTaskGraphNodes(nodes);
        _service.SelectIssue("ORPHAN-001"); // Select Orphan (no parent)

        _service.CreateIssueAbove(); // Insert above Orphan

        Assert.That(_service.PendingNewIssue, Is.Not.Null);
        Assert.That(_service.PendingNewIssue!.InheritedParentIssueId, Is.Null);
        Assert.That(_service.PendingNewIssue.InheritedParentSortOrder, Is.Null);
    }

    // Section 1.5: Between two adjacent hierarchies
    [Test]
    public void CreateIssueBelow_BelowParent1InAdjacentHierarchies_IsOrphan()
    {
        var (lines, nodes) = CreateTwoAdjacentHierarchies();
        _service.Initialize(lines);
        _service.SetTaskGraphNodes(nodes);
        _service.SelectIssue("PARENT-001"); // Select Parent 1 (no parent of its own)

        _service.CreateIssueBelow(); // Insert below Parent 1

        Assert.That(_service.PendingNewIssue, Is.Not.Null);
        Assert.That(_service.PendingNewIssue!.InheritedParentIssueId, Is.Null);
        Assert.That(_service.PendingNewIssue.InheritedParentSortOrder, Is.Null);
    }

    [Test]
    public void CreateIssueAbove_AboveChild21InAdjacentHierarchies_InheritsParent2()
    {
        var (lines, nodes) = CreateTwoAdjacentHierarchies();
        _service.Initialize(lines);
        _service.SetTaskGraphNodes(nodes);
        _service.SelectIssue("CHILD-2-1"); // Select Child 2.1 (has Parent 2)

        _service.CreateIssueAbove(); // Insert above Child 2.1

        Assert.That(_service.PendingNewIssue, Is.Not.Null);
        Assert.That(_service.PendingNewIssue!.InheritedParentIssueId, Is.EqualTo("PARENT-002"));
        Assert.That(_service.PendingNewIssue.InheritedParentSortOrder, Is.Not.Null);
        // Sort order should be before "a" (Child 2.1's sort order)
        Assert.That(string.Compare(_service.PendingNewIssue.InheritedParentSortOrder, "a", StringComparison.Ordinal), Is.LessThan(0));
    }

    #endregion

    #region Section 2 - TAB (Become Parent) Tests

    // Section 2.1: Between two children
    [Test]
    public void Tab_BetweenTwoChildren_SetsPendingChildId()
    {
        var (lines, nodes) = CreateThreeChildrenWithParent();
        _service.Initialize(lines);
        _service.SetTaskGraphNodes(nodes);
        _service.SelectIssue("CHILD-002"); // Select Child 2

        _service.CreateIssueBelow(); // Insert between Child 2 and Child 3
        _service.IndentAsChild(); // Tab: new issue becomes parent of Child 2

        Assert.That(_service.PendingNewIssue, Is.Not.Null);
        Assert.That(_service.PendingNewIssue!.PendingChildId, Is.EqualTo("CHILD-002"));
        Assert.That(_service.PendingNewIssue.PendingParentId, Is.Null);
        Assert.That(_service.PendingNewIssue.InheritedParentIssueId, Is.Null); // Cleared
    }

    [Test]
    public void Tab_AboveChild3_SetsPendingChildIdToChild3()
    {
        var (lines, nodes) = CreateThreeChildrenWithParent();
        _service.Initialize(lines);
        _service.SetTaskGraphNodes(nodes);
        _service.SelectIssue("CHILD-003"); // Select Child 3

        _service.CreateIssueAbove(); // Insert above Child 3
        _service.IndentAsChild(); // Tab: new issue becomes parent of Child 3

        Assert.That(_service.PendingNewIssue, Is.Not.Null);
        Assert.That(_service.PendingNewIssue!.PendingChildId, Is.EqualTo("CHILD-003"));
    }

    // Section 2.2: Between a child and parent
    [Test]
    public void Tab_BelowLastChild_SetsPendingChildIdToLastChild()
    {
        var (lines, nodes) = CreateTwoChildrenWithParent();
        _service.Initialize(lines);
        _service.SetTaskGraphNodes(nodes);
        _service.SelectIssue("CHILD-002"); // Select last child

        _service.CreateIssueBelow(); // Insert between Child 2 and Parent
        _service.IndentAsChild(); // Tab: new issue becomes parent of Child 2

        Assert.That(_service.PendingNewIssue, Is.Not.Null);
        Assert.That(_service.PendingNewIssue!.PendingChildId, Is.EqualTo("CHILD-002"));
        Assert.That(_service.PendingNewIssue.InheritedParentIssueId, Is.Null);
    }

    [Test]
    public void Tab_AboveParent_SetsPendingChildIdToParent()
    {
        var (lines, nodes) = CreateTwoChildrenWithParent();
        _service.Initialize(lines);
        _service.SetTaskGraphNodes(nodes);
        _service.SelectIssue("PARENT-001"); // Select Parent

        _service.CreateIssueAbove(); // Insert above Parent
        _service.IndentAsChild(); // Tab: new issue becomes parent of Parent

        Assert.That(_service.PendingNewIssue, Is.Not.Null);
        Assert.That(_service.PendingNewIssue!.PendingChildId, Is.EqualTo("PARENT-001"));
    }

    // Section 2.3: Between an orphan and a child hierarchy
    [Test]
    public void Tab_BelowOrphan_SetsPendingChildIdToOrphan()
    {
        var (lines, nodes) = CreateOrphanAboveChildHierarchy();
        _service.Initialize(lines);
        _service.SetTaskGraphNodes(nodes);
        _service.SelectIssue("ORPHAN-001"); // Select Orphan

        _service.CreateIssueBelow(); // Insert below Orphan
        _service.IndentAsChild(); // Tab: new issue becomes parent of Orphan

        Assert.That(_service.PendingNewIssue, Is.Not.Null);
        Assert.That(_service.PendingNewIssue!.PendingChildId, Is.EqualTo("ORPHAN-001"));
    }

    [Test]
    [Ignore("NOT YET IMPLEMENTED - blocking validation for Tab above root child pending")]
    public void Tab_AboveRootChild_ShouldBeBlocked()
    {
        var (lines, nodes) = CreateOrphanAboveChildHierarchy();
        _service.Initialize(lines);
        _service.SetTaskGraphNodes(nodes);
        _service.SelectIssue("CHILD-001"); // Select Child 1

        _service.CreateIssueAbove(); // Insert above Child 1 (between Orphan and Child 1)
        var originalChildId = _service.PendingNewIssue?.PendingChildId;

        _service.IndentAsChild(); // Tab: should be blocked (no-op)

        // Should remain unchanged since Tab should be a no-op
        Assert.That(_service.PendingNewIssue!.PendingChildId, Is.EqualTo(originalChildId));
    }

    // Section 2.4: After a parent and before an orphan
    [Test]
    public void Tab_BelowParent_SetsPendingChildIdToParent()
    {
        var (lines, nodes) = CreateChildHierarchyAboveOrphan();
        _service.Initialize(lines);
        _service.SetTaskGraphNodes(nodes);
        _service.SelectIssue("PARENT-001"); // Select Parent

        _service.CreateIssueBelow(); // Insert below Parent
        _service.IndentAsChild(); // Tab: new issue becomes parent of Parent

        Assert.That(_service.PendingNewIssue, Is.Not.Null);
        Assert.That(_service.PendingNewIssue!.PendingChildId, Is.EqualTo("PARENT-001"));
    }

    [Test]
    [Ignore("NOT YET IMPLEMENTED - blocking validation for Tab above orphan pending")]
    public void Tab_AboveOrphan_ShouldBeBlocked()
    {
        var (lines, nodes) = CreateChildHierarchyAboveOrphan();
        _service.Initialize(lines);
        _service.SetTaskGraphNodes(nodes);
        _service.SelectIssue("ORPHAN-001"); // Select Orphan

        _service.CreateIssueAbove(); // Insert above Orphan
        var originalChildId = _service.PendingNewIssue?.PendingChildId;

        _service.IndentAsChild(); // Tab: should be blocked (no-op)

        // Should remain unchanged since Tab should be a no-op
        Assert.That(_service.PendingNewIssue!.PendingChildId, Is.EqualTo(originalChildId));
    }

    // Section 2.5: Between two adjacent hierarchies
    [Test]
    public void Tab_BelowParent1_InAdjacentHierarchies_SetsPendingChildIdToParent1()
    {
        var (lines, nodes) = CreateTwoAdjacentHierarchies();
        _service.Initialize(lines);
        _service.SetTaskGraphNodes(nodes);
        _service.SelectIssue("PARENT-001"); // Select Parent 1

        _service.CreateIssueBelow(); // Insert below Parent 1
        _service.IndentAsChild(); // Tab: new issue becomes parent of Parent 1

        Assert.That(_service.PendingNewIssue, Is.Not.Null);
        Assert.That(_service.PendingNewIssue!.PendingChildId, Is.EqualTo("PARENT-001"));
    }

    [Test]
    [Ignore("NOT YET IMPLEMENTED - blocking validation for Tab above child in second hierarchy pending")]
    public void Tab_AboveChild21_InAdjacentHierarchies_ShouldBeBlocked()
    {
        var (lines, nodes) = CreateTwoAdjacentHierarchies();
        _service.Initialize(lines);
        _service.SetTaskGraphNodes(nodes);
        _service.SelectIssue("CHILD-2-1"); // Select Child 2.1

        _service.CreateIssueAbove(); // Insert above Child 2.1
        var originalChildId = _service.PendingNewIssue?.PendingChildId;

        _service.IndentAsChild(); // Tab: should be blocked (no-op)

        // Should remain unchanged since Tab should be a no-op
        Assert.That(_service.PendingNewIssue!.PendingChildId, Is.EqualTo(originalChildId));
    }

    #endregion

    #region Section 3 - Shift+TAB (Become Child) Tests

    // Section 3.1: Between two children
    [Test]
    public void ShiftTab_BelowChild2_SetsPendingParentIdToChild2()
    {
        var (lines, nodes) = CreateThreeChildrenWithParent();
        _service.Initialize(lines);
        _service.SetTaskGraphNodes(nodes);
        _service.SelectIssue("CHILD-002"); // Select Child 2

        _service.CreateIssueBelow(); // Insert between Child 2 and Child 3
        _service.UnindentAsSibling(); // Shift+Tab: new issue becomes child of Child 2

        Assert.That(_service.PendingNewIssue, Is.Not.Null);
        Assert.That(_service.PendingNewIssue!.PendingParentId, Is.EqualTo("CHILD-002"));
        Assert.That(_service.PendingNewIssue.PendingChildId, Is.Null);
        Assert.That(_service.PendingNewIssue.InheritedParentIssueId, Is.Null); // Cleared
    }

    [Test]
    public void ShiftTab_AboveChild3_SetsPendingParentIdToChild3()
    {
        var (lines, nodes) = CreateThreeChildrenWithParent();
        _service.Initialize(lines);
        _service.SetTaskGraphNodes(nodes);
        _service.SelectIssue("CHILD-003"); // Select Child 3

        _service.CreateIssueAbove(); // Insert above Child 3
        _service.UnindentAsSibling(); // Shift+Tab: new issue becomes child of Child 3

        Assert.That(_service.PendingNewIssue, Is.Not.Null);
        Assert.That(_service.PendingNewIssue!.PendingParentId, Is.EqualTo("CHILD-003"));
    }

    // Section 3.2: Between a child and parent - BLOCKED
    [Test]
    [Ignore("NOT YET IMPLEMENTED - blocking validation for Shift+Tab between child and parent pending")]
    public void ShiftTab_BetweenChildAndParent_Below_ShouldBeBlocked()
    {
        var (lines, nodes) = CreateTwoChildrenWithParent();
        _service.Initialize(lines);
        _service.SetTaskGraphNodes(nodes);
        _service.SelectIssue("CHILD-002"); // Select last child

        _service.CreateIssueBelow(); // Insert between Child 2 and Parent
        var originalParentId = _service.PendingNewIssue?.PendingParentId;

        _service.UnindentAsSibling(); // Shift+Tab: should be blocked (grandchild not supported)

        // Should remain unchanged since Shift+Tab should be a no-op
        Assert.That(_service.PendingNewIssue!.PendingParentId, Is.EqualTo(originalParentId));
    }

    [Test]
    [Ignore("NOT YET IMPLEMENTED - blocking validation for Shift+Tab between child and parent pending")]
    public void ShiftTab_BetweenChildAndParent_Above_ShouldBeBlocked()
    {
        var (lines, nodes) = CreateTwoChildrenWithParent();
        _service.Initialize(lines);
        _service.SetTaskGraphNodes(nodes);
        _service.SelectIssue("PARENT-001"); // Select Parent

        _service.CreateIssueAbove(); // Insert above Parent
        var originalParentId = _service.PendingNewIssue?.PendingParentId;

        _service.UnindentAsSibling(); // Shift+Tab: should be blocked (grandchild not supported)

        // Should remain unchanged since Shift+Tab should be a no-op
        Assert.That(_service.PendingNewIssue!.PendingParentId, Is.EqualTo(originalParentId));
    }

    // Section 3.3: Between an orphan and a child hierarchy
    [Test]
    public void ShiftTab_AboveChild1_SetsPendingParentIdToChild1()
    {
        var (lines, nodes) = CreateOrphanAboveChildHierarchy();
        _service.Initialize(lines);
        _service.SetTaskGraphNodes(nodes);
        _service.SelectIssue("CHILD-001"); // Select Child 1

        _service.CreateIssueAbove(); // Insert above Child 1
        _service.UnindentAsSibling(); // Shift+Tab: new issue becomes child of Child 1

        Assert.That(_service.PendingNewIssue, Is.Not.Null);
        Assert.That(_service.PendingNewIssue!.PendingParentId, Is.EqualTo("CHILD-001"));
    }

    [Test]
    [Ignore("NOT YET IMPLEMENTED - blocking validation for Shift+Tab below orphan pending")]
    public void ShiftTab_BelowOrphan_ShouldBeBlocked()
    {
        var (lines, nodes) = CreateOrphanAboveChildHierarchy();
        _service.Initialize(lines);
        _service.SetTaskGraphNodes(nodes);
        _service.SelectIssue("ORPHAN-001"); // Select Orphan

        _service.CreateIssueBelow(); // Insert below Orphan
        var originalParentId = _service.PendingNewIssue?.PendingParentId;

        _service.UnindentAsSibling(); // Shift+Tab: should be blocked (ambiguous context)

        // Should remain unchanged since Shift+Tab should be a no-op
        Assert.That(_service.PendingNewIssue!.PendingParentId, Is.EqualTo(originalParentId));
    }

    // Section 3.4: After a parent and before an orphan
    [Test]
    [Ignore("NOT YET IMPLEMENTED - blocking validation for Shift+Tab below parent pending")]
    public void ShiftTab_BelowParent_ShouldBeBlocked()
    {
        var (lines, nodes) = CreateChildHierarchyAboveOrphan();
        _service.Initialize(lines);
        _service.SetTaskGraphNodes(nodes);
        _service.SelectIssue("PARENT-001"); // Select Parent

        _service.CreateIssueBelow(); // Insert below Parent
        var originalParentId = _service.PendingNewIssue?.PendingParentId;

        _service.UnindentAsSibling(); // Shift+Tab: should be blocked (many levels to travel down)

        // Should remain unchanged since Shift+Tab should be a no-op
        Assert.That(_service.PendingNewIssue!.PendingParentId, Is.EqualTo(originalParentId));
    }

    [Test]
    public void ShiftTab_AboveOrphan_SetsPendingParentIdToOrphan()
    {
        var (lines, nodes) = CreateChildHierarchyAboveOrphan();
        _service.Initialize(lines);
        _service.SetTaskGraphNodes(nodes);
        _service.SelectIssue("ORPHAN-001"); // Select Orphan

        _service.CreateIssueAbove(); // Insert above Orphan
        _service.UnindentAsSibling(); // Shift+Tab: new issue becomes child of Orphan

        Assert.That(_service.PendingNewIssue, Is.Not.Null);
        Assert.That(_service.PendingNewIssue!.PendingParentId, Is.EqualTo("ORPHAN-001"));
    }

    // Section 3.5: Between two adjacent hierarchies
    [Test]
    [Ignore("NOT YET IMPLEMENTED - blocking validation for Shift+Tab below parent in adjacent hierarchies pending")]
    public void ShiftTab_BelowParent1_InAdjacentHierarchies_ShouldBeBlocked()
    {
        var (lines, nodes) = CreateTwoAdjacentHierarchies();
        _service.Initialize(lines);
        _service.SetTaskGraphNodes(nodes);
        _service.SelectIssue("PARENT-001"); // Select Parent 1

        _service.CreateIssueBelow(); // Insert below Parent 1
        var originalParentId = _service.PendingNewIssue?.PendingParentId;

        _service.UnindentAsSibling(); // Shift+Tab: should be blocked (many levels to travel down)

        // Should remain unchanged since Shift+Tab should be a no-op
        Assert.That(_service.PendingNewIssue!.PendingParentId, Is.EqualTo(originalParentId));
    }

    [Test]
    public void ShiftTab_AboveChild21_InAdjacentHierarchies_SetsPendingParentIdToChild21()
    {
        var (lines, nodes) = CreateTwoAdjacentHierarchies();
        _service.Initialize(lines);
        _service.SetTaskGraphNodes(nodes);
        _service.SelectIssue("CHILD-2-1"); // Select Child 2.1

        _service.CreateIssueAbove(); // Insert above Child 2.1
        _service.UnindentAsSibling(); // Shift+Tab: new issue becomes child of Child 2.1

        Assert.That(_service.PendingNewIssue, Is.Not.Null);
        Assert.That(_service.PendingNewIssue!.PendingParentId, Is.EqualTo("CHILD-2-1"));
    }

    #endregion

    #region Section 4 - Edge Case Tests

    [Test]
    public void CreateIssue_EmptyGraph_ReturnsImmediately()
    {
        _service.Initialize([]);

        _service.CreateIssueBelow();

        Assert.That(_service.EditMode, Is.EqualTo(KeyboardEditMode.Viewing));
        Assert.That(_service.PendingNewIssue, Is.Null);
    }

    [Test]
    public void CreateIssue_SingleIssue_CreatesCorrectly()
    {
        var singleIssue = new List<TaskGraphIssueRenderLine>
        {
            CreateIssueLine("SINGLE-001", "Single issue", 0, TaskGraphMarkerType.Actionable)
        };
        _service.Initialize(singleIssue);
        _service.SelectFirstActionable();

        _service.CreateIssueBelow();

        Assert.That(_service.PendingNewIssue, Is.Not.Null);
        Assert.That(_service.PendingNewIssue!.InsertAtIndex, Is.EqualTo(1));
        Assert.That(_service.PendingNewIssue.ReferenceIssueId, Is.EqualTo("SINGLE-001"));
    }

    [Test]
    public void TabThenShiftTab_OverwritesTabState()
    {
        var (lines, nodes) = CreateTwoChildrenWithParent();
        _service.Initialize(lines);
        _service.SetTaskGraphNodes(nodes);
        _service.SelectFirstActionable();

        _service.CreateIssueBelow();
        _service.IndentAsChild(); // Tab: PendingChildId = CHILD-001

        Assert.That(_service.PendingNewIssue!.PendingChildId, Is.EqualTo("CHILD-001"));
        Assert.That(_service.PendingNewIssue.PendingParentId, Is.Null);

        _service.UnindentAsSibling(); // Shift+Tab: overwrites to PendingParentId = CHILD-001

        Assert.That(_service.PendingNewIssue.PendingParentId, Is.EqualTo("CHILD-001"));
        Assert.That(_service.PendingNewIssue.PendingChildId, Is.Null);
    }

    [Test]
    public void TabTwice_IsIdempotent()
    {
        var (lines, nodes) = CreateTwoChildrenWithParent();
        _service.Initialize(lines);
        _service.SetTaskGraphNodes(nodes);
        _service.SelectFirstActionable();

        _service.CreateIssueBelow();
        _service.IndentAsChild(); // First Tab
        var firstChildId = _service.PendingNewIssue!.PendingChildId;

        _service.IndentAsChild(); // Second Tab

        Assert.That(_service.PendingNewIssue!.PendingChildId, Is.EqualTo(firstChildId));
    }

    [Test]
    public void Escape_ClearsPendingNewIssue()
    {
        var (lines, nodes) = CreateTwoChildrenWithParent();
        _service.Initialize(lines);
        _service.SetTaskGraphNodes(nodes);
        _service.SelectFirstActionable();

        _service.CreateIssueBelow();
        Assert.That(_service.PendingNewIssue, Is.Not.Null);

        _service.CancelEdit();

        Assert.That(_service.PendingNewIssue, Is.Null);
        Assert.That(_service.EditMode, Is.EqualTo(KeyboardEditMode.Viewing));
    }

    [Test]
    public async Task AcceptEdit_BetweenTwoChildren_SetsCorrectApiFields()
    {
        var handler = new MockHttpMessageHandler();
        handler.RespondWith("issues", new IssueResponse
        {
            Id = "new-issue",
            Title = "test",
            Type = IssueType.Task,
            Status = IssueStatus.Open
        });
        var issueApi = new HttpIssueApiService(handler.CreateClient());
        var service = new KeyboardNavigationService(issueApi);

        var (lines, nodes) = CreateThreeChildrenWithParent();
        service.Initialize(lines);
        service.SetProjectId("test-project");
        service.SetTaskGraphNodes(nodes);
        service.SelectIssue("CHILD-001");
        service.CreateIssueBelow();
        service.UpdateEditTitle("New sibling");

        await service.AcceptEditAsync();

        var createRequest = handler.CapturedRequests
            .FirstOrDefault(r => r.Method == HttpMethod.Post && r.Url.Contains("issues"));
        Assert.That(createRequest, Is.Not.Null);
        var body = createRequest!.BodyAs<CreateIssueRequest>();
        Assert.That(body, Is.Not.Null);
        Assert.That(body!.ParentIssueId, Is.EqualTo("PARENT-001"));
        Assert.That(body.ParentSortOrder, Is.Not.Null);
        Assert.That(body.ChildIssueId, Is.Null);
    }

    [Test]
    public async Task AcceptEdit_WithTab_SetsChildIssueIdInRequest()
    {
        var handler = new MockHttpMessageHandler();
        handler.RespondWith("issues", new IssueResponse
        {
            Id = "new-issue",
            Title = "test",
            Type = IssueType.Task,
            Status = IssueStatus.Open
        });
        var issueApi = new HttpIssueApiService(handler.CreateClient());
        var service = new KeyboardNavigationService(issueApi);

        var (lines, nodes) = CreateTwoChildrenWithParent();
        service.Initialize(lines);
        service.SetProjectId("test-project");
        service.SetTaskGraphNodes(nodes);
        service.SelectIssue("CHILD-002");
        service.CreateIssueBelow();
        service.IndentAsChild(); // Tab
        service.UpdateEditTitle("New parent");

        await service.AcceptEditAsync();

        var createRequest = handler.CapturedRequests
            .FirstOrDefault(r => r.Method == HttpMethod.Post && r.Url.Contains("issues"));
        Assert.That(createRequest, Is.Not.Null);
        var body = createRequest!.BodyAs<CreateIssueRequest>();
        Assert.That(body, Is.Not.Null);
        Assert.That(body!.ChildIssueId, Is.EqualTo("CHILD-002"));
        Assert.That(body.ParentIssueId, Is.Null);
    }

    [Test]
    public async Task AcceptEdit_WithShiftTab_SetsParentIssueIdInRequest()
    {
        var handler = new MockHttpMessageHandler();
        handler.RespondWith("issues", new IssueResponse
        {
            Id = "new-issue",
            Title = "test",
            Type = IssueType.Task,
            Status = IssueStatus.Open
        });
        var issueApi = new HttpIssueApiService(handler.CreateClient());
        var service = new KeyboardNavigationService(issueApi);

        var (lines, nodes) = CreateThreeChildrenWithParent();
        service.Initialize(lines);
        service.SetProjectId("test-project");
        service.SetTaskGraphNodes(nodes);
        service.SelectIssue("CHILD-002");
        service.CreateIssueBelow();
        service.UnindentAsSibling(); // Shift+Tab
        service.UpdateEditTitle("New child");

        await service.AcceptEditAsync();

        var createRequest = handler.CapturedRequests
            .FirstOrDefault(r => r.Method == HttpMethod.Post && r.Url.Contains("issues"));
        Assert.That(createRequest, Is.Not.Null);
        var body = createRequest!.BodyAs<CreateIssueRequest>();
        Assert.That(body, Is.Not.Null);
        Assert.That(body!.ParentIssueId, Is.EqualTo("CHILD-002"));
        Assert.That(body.ChildIssueId, Is.Null);
    }

    #endregion

#region Open Edit Tests

    [Test]
    public void OpenSelectedIssueForEdit_WithSelection_FiresOnOpenEditRequested()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();
        string? requestedIssueId = null;
        _service.OnOpenEditRequested += (issueId) => requestedIssueId = issueId;

        _service.OpenSelectedIssueForEdit();

        Assert.That(requestedIssueId, Is.EqualTo("ISSUE-001"));
    }

    [Test]
    public void OpenSelectedIssueForEdit_NoSelection_DoesNotFire()
    {
        _service.Initialize(_sampleRenderLines);
        // No selection made (SelectedIndex = -1)
        var eventFired = false;
        _service.OnOpenEditRequested += (_) => eventFired = true;

        _service.OpenSelectedIssueForEdit();

        Assert.That(eventFired, Is.False);
    }

    [Test]
    public void OpenSelectedIssueForEdit_DuringEditMode_DoesNotFire()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();
        _service.StartEditingAtStart(); // Enter edit mode
        var eventFired = false;
        _service.OnOpenEditRequested += (_) => eventFired = true;

        _service.OpenSelectedIssueForEdit();

        Assert.That(eventFired, Is.False);
    }

    [Test]
    public void OpenSelectedIssueForEdit_DuringCreateMode_DoesNotFire()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();
        _service.CreateIssueBelow(); // Enter create mode
        var eventFired = false;
        _service.OnOpenEditRequested += (_) => eventFired = true;

        _service.OpenSelectedIssueForEdit();

        Assert.That(eventFired, Is.False);
    }

    #endregion

    #region SelectingAgentPrompt Mode Tests

    [Test]
    public void StartSelectingPrompt_SetsEditModeToSelectingAgentPrompt()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();

        _service.StartSelectingPrompt();

        Assert.That(_service.EditMode, Is.EqualTo(KeyboardEditMode.SelectingAgentPrompt));
    }

    [Test]
    public void StartSelectingPrompt_NoSelection_DoesNothing()
    {
        _service.Initialize(_sampleRenderLines);
        // No selection made (SelectedIndex is -1)

        _service.StartSelectingPrompt();

        Assert.That(_service.EditMode, Is.EqualTo(KeyboardEditMode.Viewing));
    }

    [Test]
    public void StartSelectingPrompt_DuringEditMode_IsIgnored()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();
        _service.StartEditingAtStart(); // Enter EditingExisting mode

        _service.StartSelectingPrompt();

        Assert.That(_service.EditMode, Is.EqualTo(KeyboardEditMode.EditingExisting));
    }

    [Test]
    public void StartSelectingPrompt_DuringCreatingNewMode_IsIgnored()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();
        _service.CreateIssueBelow(); // Enter CreatingNew mode

        _service.StartSelectingPrompt();

        Assert.That(_service.EditMode, Is.EqualTo(KeyboardEditMode.CreatingNew));
    }

    [Test]
    public void MovePromptSelectionDown_IncrementsIndex()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();
        _service.StartSelectingPrompt();
        var initialIndex = _service.SelectedPromptIndex;

        _service.MovePromptSelectionDown();

        Assert.That(_service.SelectedPromptIndex, Is.EqualTo(initialIndex + 1));
    }

    [Test]
    public void MovePromptSelectionUp_DecrementsIndex()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();
        _service.StartSelectingPrompt();
        _service.MovePromptSelectionDown(); // Move to index 1 first
        var indexAfterDown = _service.SelectedPromptIndex;

        _service.MovePromptSelectionUp();

        Assert.That(_service.SelectedPromptIndex, Is.EqualTo(indexAfterDown - 1));
    }

    [Test]
    public void MovePromptSelectionUp_AtZero_StaysAtZero()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();
        _service.StartSelectingPrompt();
        // SelectedPromptIndex starts at 0

        _service.MovePromptSelectionUp();

        Assert.That(_service.SelectedPromptIndex, Is.EqualTo(0));
    }

    [Test]
    public void AcceptPromptSelection_ReturnsToViewingMode()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();
        _service.StartSelectingPrompt();

        _service.AcceptPromptSelection();

        Assert.That(_service.EditMode, Is.EqualTo(KeyboardEditMode.Viewing));
    }

    [Test]
    public void AcceptPromptSelection_ResetsSelectedPromptIndex()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();
        _service.StartSelectingPrompt();
        _service.MovePromptSelectionDown();
        _service.MovePromptSelectionDown();

        _service.AcceptPromptSelection();

        Assert.That(_service.SelectedPromptIndex, Is.EqualTo(0));
    }

    [Test]
    public void CancelEdit_FromSelectingPrompt_ReturnsToViewingMode()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();
        _service.StartSelectingPrompt();

        _service.CancelEdit();

        Assert.That(_service.EditMode, Is.EqualTo(KeyboardEditMode.Viewing));
    }

    [Test]
    public void CancelEdit_FromSelectingPrompt_ResetsSelectedPromptIndex()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();
        _service.StartSelectingPrompt();
        _service.MovePromptSelectionDown();

        _service.CancelEdit();

        Assert.That(_service.SelectedPromptIndex, Is.EqualTo(0));
    }

    [Test]
    public void Navigation_DuringSelectingPrompt_IsIgnored()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();
        var originalIssueId = _service.SelectedIssueId;
        _service.StartSelectingPrompt();

        _service.MoveDown();

        // Issue selection should not change during SelectingAgentPrompt mode
        Assert.That(_service.SelectedIssueId, Is.EqualTo(originalIssueId));
    }

    [Test]
    public void StartSelectingPrompt_RaisesOnStateChanged()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();
        var stateChanged = false;
        _service.OnStateChanged += () => stateChanged = true;

        _service.StartSelectingPrompt();

        Assert.That(stateChanged, Is.True);
    }

    [Test]
    public void MovePromptSelectionDown_RaisesOnStateChanged()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();
        _service.StartSelectingPrompt();
        var stateChanged = false;
        _service.OnStateChanged += () => stateChanged = true;

        _service.MovePromptSelectionDown();

        Assert.That(stateChanged, Is.True);
    }

    [Test]
    public void AcceptPromptSelection_RaisesOnStateChanged()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();
        _service.StartSelectingPrompt();
        var stateChanged = false;
        _service.OnStateChanged += () => stateChanged = true;

        _service.AcceptPromptSelection();

        Assert.That(stateChanged, Is.True);
    }

    #endregion
}
