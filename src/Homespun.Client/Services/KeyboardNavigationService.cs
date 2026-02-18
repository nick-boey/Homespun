using Fleece.Core.Models;
using Homespun.Client.Components;
using Homespun.Shared.Requests;

namespace Homespun.Client.Services;

/// <summary>
/// Implementation of keyboard navigation service for Vim-like task graph navigation.
/// </summary>
public class KeyboardNavigationService : IKeyboardNavigationService
{
    private readonly HttpIssueApiService _issueApi;
    private List<TaskGraphIssueRenderLine> _renderLines = [];

    public KeyboardNavigationService(HttpIssueApiService issueApi)
    {
        _issueApi = issueApi;
    }

    public int SelectedIndex { get; private set; } = -1;

    public string? SelectedIssueId =>
        SelectedIndex >= 0 && SelectedIndex < _renderLines.Count
            ? _renderLines[SelectedIndex].IssueId
            : null;

    public KeyboardEditMode EditMode { get; private set; } = KeyboardEditMode.Viewing;

    public InlineEditState? PendingEdit { get; private set; }

    public PendingNewIssue? PendingNewIssue { get; private set; }

    public string? ProjectId { get; private set; }

    public event Action? OnStateChanged;

    public event Func<Task>? OnIssueChanged;

    #region Navigation

    public void MoveUp()
    {
        if (EditMode != KeyboardEditMode.Viewing) return;
        if (_renderLines.Count == 0) return;
        if (SelectedIndex <= 0) return;

        SelectedIndex--;
        NotifyStateChanged();
    }

    public void MoveDown()
    {
        if (EditMode != KeyboardEditMode.Viewing) return;
        if (_renderLines.Count == 0) return;
        if (SelectedIndex >= _renderLines.Count - 1) return;

        SelectedIndex++;
        NotifyStateChanged();
    }

    public void MoveToParent()
    {
        if (EditMode != KeyboardEditMode.Viewing) return;
        if (SelectedIndex < 0 || SelectedIndex >= _renderLines.Count) return;

        var currentLine = _renderLines[SelectedIndex];
        if (!currentLine.ParentLane.HasValue) return;

        // Find the issue at the parent lane
        var parentIndex = FindIssueAtLane(currentLine.ParentLane.Value);
        if (parentIndex >= 0)
        {
            SelectedIndex = parentIndex;
            NotifyStateChanged();
        }
    }

    public void MoveToChild()
    {
        if (EditMode != KeyboardEditMode.Viewing) return;
        if (SelectedIndex < 0 || SelectedIndex >= _renderLines.Count) return;

        var currentLine = _renderLines[SelectedIndex];
        var currentLane = currentLine.Lane;

        // Find an issue whose ParentLane points to the current issue's lane
        var childIndex = FindChildOfLane(currentLane);
        if (childIndex >= 0)
        {
            SelectedIndex = childIndex;
            NotifyStateChanged();
        }
    }

    private int FindIssueAtLane(int lane)
    {
        for (var i = 0; i < _renderLines.Count; i++)
        {
            if (_renderLines[i].Lane == lane)
                return i;
        }
        return -1;
    }

    private int FindChildOfLane(int parentLane)
    {
        for (var i = 0; i < _renderLines.Count; i++)
        {
            if (_renderLines[i].ParentLane == parentLane)
                return i;
        }
        return -1;
    }

    #endregion

    #region Editing

    public void StartEditingAtStart()
    {
        if (EditMode != KeyboardEditMode.Viewing) return;
        if (SelectedIndex < 0 || SelectedIndex >= _renderLines.Count) return;

        var currentLine = _renderLines[SelectedIndex];
        PendingEdit = new InlineEditState
        {
            IssueId = currentLine.IssueId,
            Title = currentLine.Title,
            OriginalTitle = currentLine.Title,
            CursorPosition = EditCursorPosition.Start
        };
        EditMode = KeyboardEditMode.EditingExisting;
        NotifyStateChanged();
    }

    public void StartEditingAtEnd()
    {
        if (EditMode != KeyboardEditMode.Viewing) return;
        if (SelectedIndex < 0 || SelectedIndex >= _renderLines.Count) return;

        var currentLine = _renderLines[SelectedIndex];
        PendingEdit = new InlineEditState
        {
            IssueId = currentLine.IssueId,
            Title = currentLine.Title,
            OriginalTitle = currentLine.Title,
            CursorPosition = EditCursorPosition.End
        };
        EditMode = KeyboardEditMode.EditingExisting;
        NotifyStateChanged();
    }

    public void StartReplacingTitle()
    {
        if (EditMode != KeyboardEditMode.Viewing) return;
        if (SelectedIndex < 0 || SelectedIndex >= _renderLines.Count) return;

        var currentLine = _renderLines[SelectedIndex];
        PendingEdit = new InlineEditState
        {
            IssueId = currentLine.IssueId,
            Title = "", // Clear the title
            OriginalTitle = currentLine.Title,
            CursorPosition = EditCursorPosition.Replace
        };
        EditMode = KeyboardEditMode.EditingExisting;
        NotifyStateChanged();
    }

    public void UpdateEditTitle(string title)
    {
        if (EditMode == KeyboardEditMode.EditingExisting && PendingEdit != null)
        {
            PendingEdit.Title = title;
        }
        else if (EditMode == KeyboardEditMode.CreatingNew && PendingNewIssue != null)
        {
            PendingNewIssue.Title = title;
        }
    }

    public void CancelEdit()
    {
        EditMode = KeyboardEditMode.Viewing;
        PendingEdit = null;
        PendingNewIssue = null;
        NotifyStateChanged();
    }

    public async Task AcceptEditAsync()
    {
        if (string.IsNullOrEmpty(ProjectId))
        {
            // No project ID set, can't make API calls
            return;
        }

        if (EditMode == KeyboardEditMode.EditingExisting && PendingEdit != null)
        {
            if (string.IsNullOrWhiteSpace(PendingEdit.Title))
            {
                // Don't save empty titles
                return;
            }

            // Call API to update issue title
            await _issueApi.UpdateIssueAsync(PendingEdit.IssueId, new UpdateIssueRequest
            {
                ProjectId = ProjectId,
                Title = PendingEdit.Title.Trim()
            });

            EditMode = KeyboardEditMode.Viewing;
            PendingEdit = null;
            NotifyStateChanged();
            await NotifyIssueChangedAsync();
        }
        else if (EditMode == KeyboardEditMode.CreatingNew && PendingNewIssue != null)
        {
            if (string.IsNullOrWhiteSpace(PendingNewIssue.Title))
            {
                // Don't save empty titles
                return;
            }

            // Create the new issue via API
            // Default to Task type and Open status per requirements
            var newIssue = await _issueApi.CreateIssueAsync(new CreateIssueRequest
            {
                ProjectId = ProjectId,
                Title = PendingNewIssue.Title.Trim(),
                Type = IssueType.Task
            });

            EditMode = KeyboardEditMode.Viewing;
            PendingNewIssue = null;
            NotifyStateChanged();
            await NotifyIssueChangedAsync();
        }
    }

    #endregion

    #region Issue Creation

    public void CreateIssueBelow()
    {
        if (EditMode != KeyboardEditMode.Viewing) return;
        if (SelectedIndex < 0) return;

        var referenceIssueId = SelectedIndex < _renderLines.Count
            ? _renderLines[SelectedIndex].IssueId
            : null;

        PendingNewIssue = new PendingNewIssue
        {
            InsertAtIndex = SelectedIndex + 1,
            Title = "",
            IsAbove = false,
            ReferenceIssueId = referenceIssueId
        };
        EditMode = KeyboardEditMode.CreatingNew;
        NotifyStateChanged();
    }

    public void CreateIssueAbove()
    {
        if (EditMode != KeyboardEditMode.Viewing) return;
        if (SelectedIndex < 0) return;

        var referenceIssueId = SelectedIndex < _renderLines.Count
            ? _renderLines[SelectedIndex].IssueId
            : null;

        PendingNewIssue = new PendingNewIssue
        {
            InsertAtIndex = SelectedIndex,
            Title = "",
            IsAbove = true,
            ReferenceIssueId = referenceIssueId
        };
        EditMode = KeyboardEditMode.CreatingNew;
        NotifyStateChanged();
    }

    public void IndentAsChild()
    {
        if (EditMode == KeyboardEditMode.CreatingNew && PendingNewIssue != null)
        {
            // Find the issue above the insertion point to make it the parent
            var aboveIndex = PendingNewIssue.IsAbove
                ? PendingNewIssue.InsertAtIndex - 1
                : PendingNewIssue.InsertAtIndex - 1;

            if (aboveIndex >= 0 && aboveIndex < _renderLines.Count)
            {
                PendingNewIssue.PendingParentId = _renderLines[aboveIndex].IssueId;
            }
        }
        else if (EditMode == KeyboardEditMode.EditingExisting && PendingEdit != null)
        {
            // TODO: Handle indent for existing issues
        }
    }

    public void UnindentAsSibling()
    {
        if (EditMode == KeyboardEditMode.CreatingNew && PendingNewIssue != null)
        {
            PendingNewIssue.PendingParentId = null;
        }
        else if (EditMode == KeyboardEditMode.EditingExisting && PendingEdit != null)
        {
            // TODO: Handle unindent for existing issues
        }
    }

    #endregion

    #region Initialization

    public void Initialize(List<TaskGraphIssueRenderLine> renderLines)
    {
        _renderLines = renderLines;
        SelectedIndex = -1;
        EditMode = KeyboardEditMode.Viewing;
        PendingEdit = null;
        PendingNewIssue = null;
    }

    public void SetProjectId(string projectId)
    {
        ProjectId = projectId;
    }

    public void SelectFirstActionable()
    {
        if (_renderLines.Count == 0) return;

        // Try to find an actionable issue first
        for (var i = 0; i < _renderLines.Count; i++)
        {
            if (_renderLines[i].Marker == TaskGraphMarkerType.Actionable)
            {
                SelectedIndex = i;
                NotifyStateChanged();
                return;
            }
        }

        // Fall back to first issue if none are actionable
        SelectedIndex = 0;
        NotifyStateChanged();
    }

    public void SelectIssue(string issueId)
    {
        for (var i = 0; i < _renderLines.Count; i++)
        {
            if (string.Equals(_renderLines[i].IssueId, issueId, StringComparison.OrdinalIgnoreCase))
            {
                SelectedIndex = i;
                NotifyStateChanged();
                return;
            }
        }
    }

    #endregion

    private void NotifyStateChanged()
    {
        OnStateChanged?.Invoke();
    }

    private async Task NotifyIssueChangedAsync()
    {
        if (OnIssueChanged != null)
        {
            await OnIssueChanged.Invoke();
        }
    }
}
