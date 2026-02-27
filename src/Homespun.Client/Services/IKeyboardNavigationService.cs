using Homespun.Client.Components;
using Homespun.Shared.Models.Fleece;

namespace Homespun.Client.Services;

/// <summary>
/// Enum representing the keyboard editing mode states.
/// </summary>
public enum KeyboardEditMode
{
    /// <summary>Normal viewing mode - keyboard commands navigate the task graph.</summary>
    Viewing,
    /// <summary>Editing an existing issue's title.</summary>
    EditingExisting,
    /// <summary>Creating a new issue inline.</summary>
    CreatingNew,
    /// <summary>Selecting an agent prompt from the dropdown.</summary>
    SelectingAgentPrompt
}

/// <summary>
/// Enum representing cursor position when entering edit mode.
/// </summary>
public enum EditCursorPosition
{
    /// <summary>Cursor at the start of the text (i command).</summary>
    Start,
    /// <summary>Cursor at the end of the text (a command).</summary>
    End,
    /// <summary>Text cleared and cursor at start (r command).</summary>
    Replace
}

/// <summary>
/// Represents state for inline editing of an issue title.
/// </summary>
public record InlineEditState
{
    /// <summary>The issue ID being edited.</summary>
    public required string IssueId { get; init; }

    /// <summary>The current title text being edited.</summary>
    public string Title { get; set; } = "";

    /// <summary>The original title before editing (for cancel/revert).</summary>
    public required string OriginalTitle { get; init; }

    /// <summary>Where the cursor should be positioned when entering edit mode.</summary>
    public EditCursorPosition CursorPosition { get; init; }
}

/// <summary>
/// Represents a pending new issue that exists only client-side until saved.
/// </summary>
public record PendingNewIssue
{
    /// <summary>Position in the render list where this issue should be inserted.</summary>
    public int InsertAtIndex { get; init; }

    /// <summary>The title being entered for the new issue.</summary>
    public string Title { get; set; } = "";

    /// <summary>Parent ID set when Shift+Tab is pressed to make this a child of the adjacent issue.</summary>
    public string? PendingParentId { get; set; }

    /// <summary>Child ID set when Tab is pressed to make this a parent of the adjacent issue.</summary>
    public string? PendingChildId { get; set; }

    /// <summary>Sort order for series parent positioning.</summary>
    public string? SortOrder { get; set; }

    /// <summary>True if created with Shift+O (above current), false for o (below current).</summary>
    public bool IsAbove { get; init; }

    /// <summary>Reference issue ID used to determine placement context.</summary>
    public string? ReferenceIssueId { get; init; }

    /// <summary>Inherited parent issue ID from the reference issue's parent (sibling creation).</summary>
    public string? InheritedParentIssueId { get; set; }

    /// <summary>Inherited sort order for the parent relationship.</summary>
    public string? InheritedParentSortOrder { get; set; }
}

/// <summary>
/// Service for managing Vim-like keyboard navigation state in the task graph.
/// Centralizes selection, editing, and issue creation state management.
/// </summary>
public interface IKeyboardNavigationService
{
    /// <summary>Current selected index in the render list.</summary>
    int SelectedIndex { get; }

    /// <summary>ID of the currently selected issue, or null if none selected.</summary>
    string? SelectedIssueId { get; }

    /// <summary>Current keyboard edit mode (viewing, editing existing, creating new).</summary>
    KeyboardEditMode EditMode { get; }

    /// <summary>State for editing an existing issue's title, or null if not editing.</summary>
    InlineEditState? PendingEdit { get; }

    /// <summary>State for a new issue being created, or null if not creating.</summary>
    PendingNewIssue? PendingNewIssue { get; }

    /// <summary>Current selected prompt index in the agent dropdown (0-based).</summary>
    int SelectedPromptIndex { get; }

    /// <summary>The current project ID for API operations.</summary>
    string? ProjectId { get; }

    /// <summary>Raised when any navigation or edit state changes.</summary>
    event Action? OnStateChanged;

    /// <summary>Raised when an issue has been created or updated and the graph should be refreshed.</summary>
    event Func<Task>? OnIssueChanged;

    /// <summary>Raised when the user requests to open the full edit page for an issue (Enter key).</summary>
    event Action<string>? OnOpenEditRequested;

    // Navigation methods

    /// <summary>Move selection up (k or ArrowUp).</summary>
    void MoveUp();

    /// <summary>Move selection down (j or ArrowDown).</summary>
    void MoveDown();

    /// <summary>Navigate to parent issue (l or ArrowRight).</summary>
    void MoveToParent();

    /// <summary>Navigate to child issue (h or ArrowLeft).</summary>
    void MoveToChild();

    /// <summary>Move selection to the first issue (g command).</summary>
    void MoveToFirst();

    /// <summary>Move selection to the last issue (G command).</summary>
    void MoveToLast();

    // Editing methods

    /// <summary>Start editing at the start of the title (i command).</summary>
    void StartEditingAtStart();

    /// <summary>Start editing at the end of the title (a command).</summary>
    void StartEditingAtEnd();

    /// <summary>Clear title and start editing (r command).</summary>
    void StartReplacingTitle();

    /// <summary>Create a new issue below the current selection (o command).</summary>
    void CreateIssueBelow();

    /// <summary>Create a new issue above the current selection (Shift+O command).</summary>
    void CreateIssueAbove();

    /// <summary>Make the editing issue a child of the issue above (Tab in edit mode).</summary>
    void IndentAsChild();

    /// <summary>Remove parent relationship from the editing issue (Shift+Tab in edit mode).</summary>
    void UnindentAsSibling();

    /// <summary>Cancel the current edit or creation, discarding changes.</summary>
    void CancelEdit();

    /// <summary>Accept the current edit, persisting changes via API.</summary>
    Task AcceptEditAsync();

    /// <summary>Accept the current edit and raise event to open description editor.</summary>
    Task AcceptEditAndOpenDescriptionAsync();

    /// <summary>Raised when an issue is created/edited and should be opened for description editing.</summary>
    event Func<string, Task>? OnIssueCreatedForEdit;

    // Agent prompt selection methods

    /// <summary>Start selecting an agent prompt (e command). Transitions to SelectingAgentPrompt mode.</summary>
    void StartSelectingPrompt();

    /// <summary>Move prompt selection down in the dropdown.</summary>
    void MovePromptSelectionDown();

    /// <summary>Move prompt selection up in the dropdown.</summary>
    void MovePromptSelectionUp();

    /// <summary>Accept the current prompt selection and return to Viewing mode.</summary>
    void AcceptPromptSelection();

    /// <summary>Update the title text while editing.</summary>
    void UpdateEditTitle(string title);

    // Initialization methods

    /// <summary>Initialize the service with the current render lines from the task graph.</summary>
    void Initialize(List<TaskGraphIssueRenderLine> renderLines);

    /// <summary>Set the project ID for API operations.</summary>
    void SetProjectId(string projectId);

    /// <summary>Set the task graph nodes for parent inheritance computation.</summary>
    void SetTaskGraphNodes(List<TaskGraphNodeResponse> nodes);

    /// <summary>Select the first actionable issue in the list.</summary>
    void SelectFirstActionable();

    /// <summary>Select an issue by its ID.</summary>
    void SelectIssue(string issueId);

    /// <summary>Open the selected issue in the full edit page (Enter key in viewing mode).</summary>
    void OpenSelectedIssueForEdit();

    // Type cycling

    /// <summary>
    /// Cycle the selected issue's type to the next type in sequence (Tab in viewing mode).
    /// Has a 3-second debounce to prevent rapid changes.
    /// Order: Task -> Bug -> Feature -> Chore -> Task
    /// </summary>
    Task CycleIssueTypeAsync();

    // Search properties

    /// <summary>The current search term being searched for.</summary>
    string SearchTerm { get; }

    /// <summary>True when the search bar is open and user is typing.</summary>
    bool IsSearching { get; }

    /// <summary>True when search is committed and user is navigating results with n/N.</summary>
    bool IsSearchEmbedded { get; }

    /// <summary>Indices in the render list that match the search term.</summary>
    IReadOnlyList<int> MatchingIndices { get; }

    /// <summary>Current position in MatchingIndices for n/N navigation.</summary>
    int CurrentMatchIndex { get; }

    // Search methods

    /// <summary>Open the search bar (/ key).</summary>
    void StartSearch();

    /// <summary>Update the search term as user types.</summary>
    void UpdateSearchTerm(string term);

    /// <summary>Commit the search and hide the search bar (Enter key).</summary>
    void EmbedSearch();

    /// <summary>Navigate to the next matching issue (n key).</summary>
    void MoveToNextMatch();

    /// <summary>Navigate to the previous matching issue (N key).</summary>
    void MoveToPreviousMatch();

    /// <summary>Clear all search state and return to normal viewing (Escape key).</summary>
    void ClearSearch();
}
