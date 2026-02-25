using Fleece.Core.Models;
using Homespun.Client.Components;
using Homespun.Shared.Models.Fleece;
using Homespun.Shared.Requests;
using Homespun.Shared.Utilities;

namespace Homespun.Client.Services;

/// <summary>
/// Implementation of keyboard navigation service for Vim-like task graph navigation.
/// </summary>
public class KeyboardNavigationService : IKeyboardNavigationService
{
    private readonly HttpIssueApiService _issueApi;
    private List<TaskGraphIssueRenderLine> _renderLines = [];
    private List<TaskGraphNodeResponse> _taskGraphNodes = [];

    // Type cycling debounce: tracks last type change time per issue
    private readonly Dictionary<string, DateTime> _lastTypeCycleTime = new();
    private static readonly TimeSpan TypeCycleDebounce = TimeSpan.FromSeconds(3);

    // Type cycling order: Task -> Bug -> Feature -> Chore -> Task
    private static readonly IssueType[] TypeCycleOrder =
        [IssueType.Task, IssueType.Bug, IssueType.Feature, IssueType.Chore];

    // Search state
    private string _searchTerm = "";
    private bool _isSearching;
    private bool _isSearchEmbedded;
    private List<int> _matchingIndices = [];
    private int _currentMatchIndex = -1;

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

    public int SelectedPromptIndex { get; private set; }

    public string? ProjectId { get; private set; }

    public event Action? OnStateChanged;

    public event Func<Task>? OnIssueChanged;

    public event Action<string>? OnOpenEditRequested;

    // Search properties
    public string SearchTerm => _searchTerm;
    public bool IsSearching => _isSearching;
    public bool IsSearchEmbedded => _isSearchEmbedded;
    public IReadOnlyList<int> MatchingIndices => _matchingIndices;
    public int CurrentMatchIndex => _currentMatchIndex;

    #region Navigation

    public void MoveUp()
    {
        if (EditMode != KeyboardEditMode.Viewing) return;
        if (_isSearching) return; // Ignore navigation while typing in search bar
        if (_renderLines.Count == 0) return;
        if (SelectedIndex <= 0) return;

        SelectedIndex--;
        NotifyStateChanged();
    }

    public void MoveDown()
    {
        if (EditMode != KeyboardEditMode.Viewing) return;
        if (_isSearching) return; // Ignore navigation while typing in search bar
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

    public void MoveToFirst()
    {
        if (EditMode != KeyboardEditMode.Viewing) return;
        if (_renderLines.Count == 0) return;

        SelectedIndex = 0;
        NotifyStateChanged();
    }

    public void MoveToLast()
    {
        if (EditMode != KeyboardEditMode.Viewing) return;
        if (_renderLines.Count == 0) return;

        SelectedIndex = _renderLines.Count - 1;
        NotifyStateChanged();
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
        SelectedPromptIndex = 0;

        // Also clear search state if searching
        if (_isSearching || _isSearchEmbedded)
        {
            _isSearching = false;
            _isSearchEmbedded = false;
            _searchTerm = "";
            _matchingIndices = [];
            _currentMatchIndex = -1;
        }

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
            var request = new CreateIssueRequest
            {
                ProjectId = ProjectId,
                Title = PendingNewIssue.Title.Trim(),
                Type = IssueType.Task
            };

            // Tab: new issue becomes parent of the adjacent issue
            if (PendingNewIssue.PendingChildId != null)
            {
                request.ChildIssueId = PendingNewIssue.PendingChildId;
            }
            // Shift+Tab: new issue becomes child of the adjacent issue
            else if (PendingNewIssue.PendingParentId != null)
            {
                request.ParentIssueId = PendingNewIssue.PendingParentId;
            }
            // No Tab/Shift+Tab: inherit parent from reference issue (sibling creation)
            else if (PendingNewIssue.InheritedParentIssueId != null)
            {
                request.ParentIssueId = PendingNewIssue.InheritedParentIssueId;
                request.ParentSortOrder = PendingNewIssue.InheritedParentSortOrder;
            }

            await _issueApi.CreateIssueAsync(request);

            EditMode = KeyboardEditMode.Viewing;
            PendingNewIssue = null;
            NotifyStateChanged();
            await NotifyIssueChangedAsync();
        }
    }

    #endregion

    #region Agent Prompt Selection

    public void StartSelectingPrompt()
    {
        if (EditMode != KeyboardEditMode.Viewing) return;
        if (SelectedIndex < 0 || SelectedIndex >= _renderLines.Count) return;

        EditMode = KeyboardEditMode.SelectingAgentPrompt;
        SelectedPromptIndex = 0;
        NotifyStateChanged();
    }

    public void MovePromptSelectionDown()
    {
        if (EditMode != KeyboardEditMode.SelectingAgentPrompt) return;

        SelectedPromptIndex++;
        NotifyStateChanged();
    }

    public void MovePromptSelectionUp()
    {
        if (EditMode != KeyboardEditMode.SelectingAgentPrompt) return;
        if (SelectedPromptIndex <= 0) return;

        SelectedPromptIndex--;
        NotifyStateChanged();
    }

    public void AcceptPromptSelection()
    {
        if (EditMode != KeyboardEditMode.SelectingAgentPrompt) return;

        EditMode = KeyboardEditMode.Viewing;
        SelectedPromptIndex = 0;
        NotifyStateChanged();
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

        var inherited = referenceIssueId != null
            ? GetInheritedParentInfo(referenceIssueId, insertAbove: false)
            : ((string?)null, (string?)null);

        PendingNewIssue = new PendingNewIssue
        {
            InsertAtIndex = SelectedIndex + 1,
            Title = "",
            IsAbove = false,
            ReferenceIssueId = referenceIssueId,
            InheritedParentIssueId = inherited.Item1,
            InheritedParentSortOrder = inherited.Item2
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

        var inherited = referenceIssueId != null
            ? GetInheritedParentInfo(referenceIssueId, insertAbove: true)
            : ((string?)null, (string?)null);

        PendingNewIssue = new PendingNewIssue
        {
            InsertAtIndex = SelectedIndex,
            Title = "",
            IsAbove = true,
            ReferenceIssueId = referenceIssueId,
            InheritedParentIssueId = inherited.Item1,
            InheritedParentSortOrder = inherited.Item2
        };
        EditMode = KeyboardEditMode.CreatingNew;
        NotifyStateChanged();
    }

    public void IndentAsChild()
    {
        if (EditMode == KeyboardEditMode.CreatingNew && PendingNewIssue != null)
        {
            // Tab: new issue becomes PARENT of the adjacent (reference) issue
            if (PendingNewIssue.ReferenceIssueId != null)
            {
                PendingNewIssue.PendingChildId = PendingNewIssue.ReferenceIssueId;
                PendingNewIssue.PendingParentId = null;
                PendingNewIssue.InheritedParentIssueId = null;
                PendingNewIssue.InheritedParentSortOrder = null;
                NotifyStateChanged();
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
            // Shift+Tab: new issue becomes CHILD of the adjacent (reference) issue
            if (PendingNewIssue.ReferenceIssueId != null)
            {
                PendingNewIssue.PendingParentId = PendingNewIssue.ReferenceIssueId;
                PendingNewIssue.PendingChildId = null;
                PendingNewIssue.InheritedParentIssueId = null;
                PendingNewIssue.InheritedParentSortOrder = null;
                NotifyStateChanged();
            }
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

        // Reset search state
        _isSearching = false;
        _isSearchEmbedded = false;
        _searchTerm = "";
        _matchingIndices = [];
        _currentMatchIndex = -1;
    }

    public void SetProjectId(string projectId)
    {
        ProjectId = projectId;
    }

    public void SetTaskGraphNodes(List<TaskGraphNodeResponse> nodes)
    {
        _taskGraphNodes = nodes;
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

    public void OpenSelectedIssueForEdit()
    {
        if (EditMode != KeyboardEditMode.Viewing) return;
        if (SelectedIssueId == null) return;

        OnOpenEditRequested?.Invoke(SelectedIssueId);
    }

    #endregion

    #region Type Cycling

    /// <inheritdoc />
    public async Task CycleIssueTypeAsync()
    {
        if (EditMode != KeyboardEditMode.Viewing) return;
        if (string.IsNullOrEmpty(ProjectId)) return;
        if (SelectedIndex < 0 || SelectedIndex >= _renderLines.Count) return;

        var issueId = _renderLines[SelectedIndex].IssueId;

        // Check debounce - ignore if within 3 seconds of last change for this issue
        var now = DateTime.UtcNow;
        if (_lastTypeCycleTime.TryGetValue(issueId, out var lastTime))
        {
            if (now - lastTime < TypeCycleDebounce) return;
        }

        // Get current type and compute next type in cycle
        var currentType = _renderLines[SelectedIndex].IssueType;
        var nextType = GetNextType(currentType);

        // Update via API
        await _issueApi.UpdateIssueAsync(issueId, new UpdateIssueRequest
        {
            ProjectId = ProjectId,
            Type = nextType
        });

        // Update debounce timestamp
        _lastTypeCycleTime[issueId] = now;

        // Refresh the graph
        await NotifyIssueChangedAsync();
    }

    /// <summary>
    /// Gets the next issue type in the cycling order.
    /// Order: Task -> Bug -> Feature -> Chore -> Task
    /// </summary>
    private static IssueType GetNextType(IssueType current)
    {
        var index = Array.IndexOf(TypeCycleOrder, current);
        if (index < 0) index = 0; // Default to start if unknown type
        return TypeCycleOrder[(index + 1) % TypeCycleOrder.Length];
    }

    #endregion

    #region Search

    public void StartSearch()
    {
        // Only allow starting search in viewing mode
        if (EditMode != KeyboardEditMode.Viewing) return;

        _isSearching = true;
        _isSearchEmbedded = false;
        _searchTerm = "";
        _matchingIndices = [];
        _currentMatchIndex = -1;
        NotifyStateChanged();
    }

    public void UpdateSearchTerm(string term)
    {
        if (!_isSearching) return;

        _searchTerm = term;
        ComputeMatchingIndices();
        NotifyStateChanged();
    }

    public void EmbedSearch()
    {
        if (!_isSearching) return;

        _isSearching = false;
        _isSearchEmbedded = true;

        if (_matchingIndices.Count > 0)
        {
            _currentMatchIndex = 0;
            SelectedIndex = _matchingIndices[0];
        }
        // No matches: keep current selection, CurrentMatchIndex stays at -1

        NotifyStateChanged();
    }

    public void MoveToNextMatch()
    {
        if (!_isSearchEmbedded || _matchingIndices.Count == 0) return;

        _currentMatchIndex = (_currentMatchIndex + 1) % _matchingIndices.Count;
        SelectedIndex = _matchingIndices[_currentMatchIndex];
        NotifyStateChanged();
    }

    public void MoveToPreviousMatch()
    {
        if (!_isSearchEmbedded || _matchingIndices.Count == 0) return;

        _currentMatchIndex = (_currentMatchIndex - 1 + _matchingIndices.Count) % _matchingIndices.Count;
        SelectedIndex = _matchingIndices[_currentMatchIndex];
        NotifyStateChanged();
    }

    public void ClearSearch()
    {
        _isSearching = false;
        _isSearchEmbedded = false;
        _searchTerm = "";
        _matchingIndices = [];
        _currentMatchIndex = -1;
        NotifyStateChanged();
    }

    private void ComputeMatchingIndices()
    {
        _matchingIndices = [];
        _currentMatchIndex = -1;

        if (string.IsNullOrEmpty(_searchTerm))
        {
            return;
        }

        for (var i = 0; i < _renderLines.Count; i++)
        {
            if (_renderLines[i].Title.Contains(_searchTerm, StringComparison.OrdinalIgnoreCase))
            {
                _matchingIndices.Add(i);
            }
        }
    }

    #endregion

    /// <summary>
    /// Gets inherited parent info from the reference issue for sibling creation.
    /// When the reference issue has a parent, the new sibling should inherit that parent.
    /// For series-mode parents, computes an appropriate sort order between adjacent siblings.
    /// </summary>
    private (string? ParentId, string? SortOrder) GetInheritedParentInfo(string issueId, bool insertAbove)
    {
        if (_taskGraphNodes.Count == 0) return (null, null);

        var node = _taskGraphNodes.FirstOrDefault(n => n.Issue.Id == issueId);
        if (node == null || node.Issue.ParentIssues.Count == 0)
        {
            return (null, null);
        }

        var parentRef = node.Issue.ParentIssues[0];
        var parentId = parentRef.ParentIssue;

        // Find all siblings under the same parent, sorted by sort order
        var siblings = _taskGraphNodes
            .Where(n => n.Issue.ParentIssues.Any(p => p.ParentIssue == parentId))
            .Select(n => new
            {
                n.Issue.Id,
                SortOrder = n.Issue.ParentIssues.First(p => p.ParentIssue == parentId).SortOrder ?? "0"
            })
            .OrderBy(s => s.SortOrder, StringComparer.Ordinal)
            .ToList();

        var refIndex = siblings.FindIndex(s => s.Id == issueId);
        if (refIndex < 0) return (parentId, parentRef.SortOrder);

        string? sortOrder;
        if (insertAbove)
        {
            var prevSortOrder = refIndex > 0 ? siblings[refIndex - 1].SortOrder : null;
            sortOrder = LexOrderUtils.ComputeMidpoint(prevSortOrder, siblings[refIndex].SortOrder);
        }
        else
        {
            var nextSortOrder = refIndex < siblings.Count - 1 ? siblings[refIndex + 1].SortOrder : null;
            sortOrder = LexOrderUtils.ComputeMidpoint(siblings[refIndex].SortOrder, nextSortOrder);
        }

        return (parentId, sortOrder);
    }

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
