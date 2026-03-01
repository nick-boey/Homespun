using Fleece.Core.Models;
using Homespun.Client.Components;
using Homespun.Client.Features.Issues.Components;
using Homespun.Client.Services;
using Homespun.Shared.Models.Fleece;
using Homespun.Tests.Helpers;
using Moq;

namespace Homespun.Tests.Services;

[TestFixture]
public class KeyboardNavigationServiceMoveTests
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
            CreateIssueLine("ISSUE-001", "First issue", 0),
            CreateIssueLine("ISSUE-002", "Second issue", 0),
            CreateIssueLine("ISSUE-003", "Third issue", 1, parentLane: 0),
            CreateIssueLine("ISSUE-004", "Fourth issue", 0)
        ];

        _service.Initialize(_sampleRenderLines);
        _service.SetProjectId("proj-1");
    }

    private static TaskGraphIssueRenderLine CreateIssueLine(
        string issueId, string title, int lane, int? parentLane = null)
    {
        return new TaskGraphIssueRenderLine(
            IssueId: issueId,
            Title: title,
            Description: null,
            BranchName: null,
            Lane: lane,
            Marker: TaskGraphMarkerType.Open,
            ParentLane: parentLane,
            IsFirstChild: false,
            IsSeriesChild: false,
            DrawTopLine: false,
            DrawBottomLine: false,
            SeriesConnectorFromLane: null,
            IssueType: IssueType.Task,
            Status: IssueStatus.Open,
            HasDescription: false,
            LinkedPr: null,
            AgentStatus: null
        );
    }

    #region StartMakeChildOf Tests

    [Test]
    public void StartMakeChildOf_SetsSelectingMoveTargetMode()
    {
        // Arrange - select an issue
        _service.SelectFirstActionable();
        Assert.That(_service.SelectedIssueId, Is.EqualTo("ISSUE-001"));

        // Act
        _service.StartMakeChildOf();

        // Assert
        Assert.That(_service.EditMode, Is.EqualTo(KeyboardEditMode.SelectingMoveTarget));
        Assert.That(_service.CurrentMoveOperation, Is.EqualTo(MoveOperationType.AsChildOf));
        Assert.That(_service.MoveSourceIssueId, Is.EqualTo("ISSUE-001"));
    }

    [Test]
    public void StartMakeChildOf_WhenNoSelection_DoesNothing()
    {
        // Arrange - no selection
        Assert.That(_service.SelectedIndex, Is.EqualTo(-1));

        // Act
        _service.StartMakeChildOf();

        // Assert
        Assert.That(_service.EditMode, Is.EqualTo(KeyboardEditMode.Viewing));
        Assert.That(_service.CurrentMoveOperation, Is.Null);
        Assert.That(_service.MoveSourceIssueId, Is.Null);
    }

    [Test]
    public void StartMakeChildOf_WhenInOtherMode_DoesNothing()
    {
        // Arrange - start editing
        _service.SelectFirstActionable();
        _service.StartEditingAtStart();
        Assert.That(_service.EditMode, Is.EqualTo(KeyboardEditMode.EditingExisting));

        // Act
        _service.StartMakeChildOf();

        // Assert - still in editing mode
        Assert.That(_service.EditMode, Is.EqualTo(KeyboardEditMode.EditingExisting));
        Assert.That(_service.CurrentMoveOperation, Is.Null);
    }

    #endregion

    #region StartMakeParentOf Tests

    [Test]
    public void StartMakeParentOf_SetsSelectingMoveTargetMode()
    {
        // Arrange
        _service.SelectFirstActionable();

        // Act
        _service.StartMakeParentOf();

        // Assert
        Assert.That(_service.EditMode, Is.EqualTo(KeyboardEditMode.SelectingMoveTarget));
        Assert.That(_service.CurrentMoveOperation, Is.EqualTo(MoveOperationType.AsParentOf));
        Assert.That(_service.MoveSourceIssueId, Is.EqualTo("ISSUE-001"));
    }

    [Test]
    public void StartMakeParentOf_WhenNoSelection_DoesNothing()
    {
        // Act
        _service.StartMakeParentOf();

        // Assert
        Assert.That(_service.EditMode, Is.EqualTo(KeyboardEditMode.Viewing));
        Assert.That(_service.CurrentMoveOperation, Is.Null);
    }

    #endregion

    #region CancelMoveOperation Tests

    [Test]
    public void CancelMoveOperation_ReturnsToViewingMode()
    {
        // Arrange - enter selection mode
        _service.SelectFirstActionable();
        _service.StartMakeChildOf();
        Assert.That(_service.EditMode, Is.EqualTo(KeyboardEditMode.SelectingMoveTarget));

        // Act
        _service.CancelMoveOperation();

        // Assert
        Assert.That(_service.EditMode, Is.EqualTo(KeyboardEditMode.Viewing));
        Assert.That(_service.CurrentMoveOperation, Is.Null);
        Assert.That(_service.MoveSourceIssueId, Is.Null);
        // Selection should remain
        Assert.That(_service.SelectedIssueId, Is.EqualTo("ISSUE-001"));
    }

    [Test]
    public void CancelMoveOperation_WhenNotInMoveMode_DoesNothing()
    {
        // Arrange
        _service.SelectFirstActionable();
        Assert.That(_service.EditMode, Is.EqualTo(KeyboardEditMode.Viewing));

        // Act
        _service.CancelMoveOperation();

        // Assert - no change
        Assert.That(_service.EditMode, Is.EqualTo(KeyboardEditMode.Viewing));
        Assert.That(_service.SelectedIssueId, Is.EqualTo("ISSUE-001"));
    }

    #endregion

    #region CompleteMoveOperation Tests

    [Test]
    public async Task CompleteMoveOperation_MakeChildOf_FiresEventWithCorrectParameters()
    {
        // Arrange
        _service.SelectFirstActionable(); // Select ISSUE-001
        _service.StartMakeChildOf();

        string? capturedSourceId = null;
        string? capturedTargetId = null;
        MoveOperationType? capturedOperation = null;
        bool? capturedAddToExisting = null;

        _service.OnMoveOperationRequested += (source, target, operation, addToExisting) =>
        {
            capturedSourceId = source;
            capturedTargetId = target;
            capturedOperation = operation;
            capturedAddToExisting = addToExisting;
            return Task.CompletedTask;
        };

        // Act - complete with target ISSUE-002, no shift
        await _service.CompleteMoveOperationAsync("ISSUE-002", addToExisting: false);

        // Assert
        Assert.That(capturedSourceId, Is.EqualTo("ISSUE-001"));
        Assert.That(capturedTargetId, Is.EqualTo("ISSUE-002"));
        Assert.That(capturedOperation, Is.EqualTo(MoveOperationType.AsChildOf));
        Assert.That(capturedAddToExisting, Is.False);

        // Should return to viewing mode
        Assert.That(_service.EditMode, Is.EqualTo(KeyboardEditMode.Viewing));
        Assert.That(_service.CurrentMoveOperation, Is.Null);
        Assert.That(_service.MoveSourceIssueId, Is.Null);
    }

    [Test]
    public async Task CompleteMoveOperation_MakeParentOf_FiresEventWithCorrectParameters()
    {
        // Arrange
        _service.SelectFirstActionable();
        _service.StartMakeParentOf();

        string? capturedSourceId = null;
        string? capturedTargetId = null;
        MoveOperationType? capturedOperation = null;
        bool? capturedAddToExisting = null;

        _service.OnMoveOperationRequested += (source, target, operation, addToExisting) =>
        {
            capturedSourceId = source;
            capturedTargetId = target;
            capturedOperation = operation;
            capturedAddToExisting = addToExisting;
            return Task.CompletedTask;
        };

        // Act - complete with target ISSUE-002, shift held
        await _service.CompleteMoveOperationAsync("ISSUE-002", addToExisting: true);

        // Assert
        Assert.That(capturedSourceId, Is.EqualTo("ISSUE-001"));
        Assert.That(capturedTargetId, Is.EqualTo("ISSUE-002"));
        Assert.That(capturedOperation, Is.EqualTo(MoveOperationType.AsParentOf));
        Assert.That(capturedAddToExisting, Is.True);
    }

    [Test]
    public async Task CompleteMoveOperation_ClickingOnSameIssue_CancelsOperation()
    {
        // Arrange
        _service.SelectFirstActionable(); // ISSUE-001
        _service.StartMakeChildOf();

        bool eventFired = false;
        _service.OnMoveOperationRequested += (_, _, _, _) =>
        {
            eventFired = true;
            return Task.CompletedTask;
        };

        // Act - click on the same issue as source
        await _service.CompleteMoveOperationAsync("ISSUE-001", addToExisting: false);

        // Assert - should cancel, not fire event
        Assert.That(eventFired, Is.False);
        Assert.That(_service.EditMode, Is.EqualTo(KeyboardEditMode.Viewing));
    }

    [Test]
    public async Task CompleteMoveOperation_WhenNotInMoveMode_DoesNothing()
    {
        // Arrange
        _service.SelectFirstActionable();
        Assert.That(_service.EditMode, Is.EqualTo(KeyboardEditMode.Viewing));

        bool eventFired = false;
        _service.OnMoveOperationRequested += (_, _, _, _) =>
        {
            eventFired = true;
            return Task.CompletedTask;
        };

        // Act
        await _service.CompleteMoveOperationAsync("ISSUE-002", addToExisting: false);

        // Assert
        Assert.That(eventFired, Is.False);
    }

    #endregion

    #region State Change Events Tests

    [Test]
    public void StartMakeChildOf_RaisesStateChangedEvent()
    {
        // Arrange
        _service.SelectFirstActionable();
        bool stateChanged = false;
        _service.OnStateChanged += () => stateChanged = true;

        // Act
        _service.StartMakeChildOf();

        // Assert
        Assert.That(stateChanged, Is.True);
    }

    [Test]
    public void CancelMoveOperation_RaisesStateChangedEvent()
    {
        // Arrange
        _service.SelectFirstActionable();
        _service.StartMakeChildOf();

        bool stateChanged = false;
        _service.OnStateChanged += () => stateChanged = true;

        // Act
        _service.CancelMoveOperation();

        // Assert
        Assert.That(stateChanged, Is.True);
    }

    #endregion
}
