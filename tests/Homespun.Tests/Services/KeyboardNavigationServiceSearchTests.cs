using Fleece.Core.Models;
using Homespun.Client.Components;
using Homespun.Client.Services;
using Homespun.Shared.Models.Fleece;
using Homespun.Tests.Helpers;
using Moq;

namespace Homespun.Tests.Services;

[TestFixture]
public class KeyboardNavigationServiceSearchTests
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
            CreateIssueLine("ISSUE-001", "First issue about authentication", 0, TaskGraphMarkerType.Actionable),
            CreateIssueLine("ISSUE-002", "Second issue for user login", 0, TaskGraphMarkerType.Open),
            CreateIssueLine("ISSUE-003", "Third issue testing auth flow", 1, TaskGraphMarkerType.Open, parentLane: 2),
            CreateIssueLine("ISSUE-004", "Fourth issue unrelated", 2, TaskGraphMarkerType.Open)
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
            Status: IssueStatus.Open,
            HasDescription: false,
            LinkedPr: null,
            AgentStatus: null
        );
    }

    #region StartSearch Tests

    [Test]
    public void StartSearch_SetsIsSearchingTrue()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();

        _service.StartSearch();

        Assert.That(_service.IsSearching, Is.True);
        Assert.That(_service.SearchTerm, Is.Empty);
    }

    [Test]
    public void StartSearch_DuringEditMode_IsIgnored()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();
        _service.StartEditingAtStart(); // Enter edit mode

        _service.StartSearch();

        Assert.That(_service.IsSearching, Is.False);
        Assert.That(_service.EditMode, Is.EqualTo(KeyboardEditMode.EditingExisting));
    }

    [Test]
    public void StartSearch_ClearsPreviousSearchTerm()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();
        _service.StartSearch();
        _service.UpdateSearchTerm("auth");
        _service.EmbedSearch();

        // Start a new search
        _service.StartSearch();

        Assert.That(_service.IsSearching, Is.True);
        Assert.That(_service.SearchTerm, Is.Empty);
        Assert.That(_service.IsSearchEmbedded, Is.False);
    }

    [Test]
    public void StartSearch_RaisesOnStateChanged()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();
        var stateChanged = false;
        _service.OnStateChanged += () => stateChanged = true;

        _service.StartSearch();

        Assert.That(stateChanged, Is.True);
    }

    #endregion

    #region UpdateSearchTerm Tests

    [Test]
    public void UpdateSearchTerm_ComputesMatchingIndices_CaseInsensitive()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();
        _service.StartSearch();

        _service.UpdateSearchTerm("AUTH");

        Assert.That(_service.MatchingIndices, Is.Not.Empty);
        Assert.That(_service.MatchingIndices, Contains.Item(0)); // "First issue about authentication"
        Assert.That(_service.MatchingIndices, Contains.Item(2)); // "Third issue testing auth flow"
    }

    [Test]
    public void UpdateSearchTerm_EmptyTerm_ClearsMatches()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();
        _service.StartSearch();
        _service.UpdateSearchTerm("auth");

        _service.UpdateSearchTerm("");

        Assert.That(_service.MatchingIndices, Is.Empty);
    }

    [Test]
    public void UpdateSearchTerm_NoMatches_ReturnsEmptyList()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();
        _service.StartSearch();

        _service.UpdateSearchTerm("xyz123nonexistent");

        Assert.That(_service.MatchingIndices, Is.Empty);
        Assert.That(_service.CurrentMatchIndex, Is.EqualTo(-1));
    }

    [Test]
    public void UpdateSearchTerm_FindsPartialMatches()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();
        _service.StartSearch();

        _service.UpdateSearchTerm("issue");

        // All issues contain "issue" in their titles
        Assert.That(_service.MatchingIndices.Count, Is.EqualTo(4));
    }

    [Test]
    public void UpdateSearchTerm_NotInSearchMode_IsIgnored()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();
        // Don't call StartSearch()

        _service.UpdateSearchTerm("auth");

        Assert.That(_service.MatchingIndices, Is.Empty);
        Assert.That(_service.SearchTerm, Is.Empty);
    }

    #endregion

    #region EmbedSearch Tests

    [Test]
    public void EmbedSearch_SetsIsSearchEmbeddedTrue_HidesSearchBar()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();
        _service.StartSearch();
        _service.UpdateSearchTerm("auth");

        _service.EmbedSearch();

        Assert.That(_service.IsSearchEmbedded, Is.True);
        Assert.That(_service.IsSearching, Is.False);
        Assert.That(_service.SearchTerm, Is.EqualTo("auth"));
    }

    [Test]
    public void EmbedSearch_SelectsFirstMatch()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectIssue("ISSUE-004"); // Start at the end
        _service.StartSearch();
        _service.UpdateSearchTerm("auth");

        _service.EmbedSearch();

        // Should select the first matching issue (index 0 - "authentication")
        Assert.That(_service.SelectedIndex, Is.EqualTo(0));
        Assert.That(_service.SelectedIssueId, Is.EqualTo("ISSUE-001"));
        Assert.That(_service.CurrentMatchIndex, Is.EqualTo(0));
    }

    [Test]
    public void EmbedSearch_NoMatches_DoesNotChangeSelection()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectIssue("ISSUE-002");
        var originalIndex = _service.SelectedIndex;
        _service.StartSearch();
        _service.UpdateSearchTerm("xyz123nonexistent");

        _service.EmbedSearch();

        Assert.That(_service.SelectedIndex, Is.EqualTo(originalIndex));
        Assert.That(_service.IsSearchEmbedded, Is.True);
        Assert.That(_service.CurrentMatchIndex, Is.EqualTo(-1));
    }

    [Test]
    public void EmbedSearch_NotInSearchMode_IsIgnored()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();

        _service.EmbedSearch();

        Assert.That(_service.IsSearchEmbedded, Is.False);
    }

    [Test]
    public void EmbedSearch_RaisesOnStateChanged()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();
        _service.StartSearch();
        _service.UpdateSearchTerm("auth");
        var stateChanged = false;
        _service.OnStateChanged += () => stateChanged = true;

        _service.EmbedSearch();

        Assert.That(stateChanged, Is.True);
    }

    #endregion

    #region MoveToNextMatch Tests

    [Test]
    public void MoveToNextMatch_IncrementsCurrent_WrapsAtEnd()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();
        _service.StartSearch();
        _service.UpdateSearchTerm("auth");
        _service.EmbedSearch();

        // First match is already selected (index 0)
        Assert.That(_service.CurrentMatchIndex, Is.EqualTo(0));

        // Move to next match
        _service.MoveToNextMatch();
        Assert.That(_service.CurrentMatchIndex, Is.EqualTo(1));
        Assert.That(_service.SelectedIndex, Is.EqualTo(2)); // "Third issue testing auth flow"

        // Move to next - should wrap to beginning
        _service.MoveToNextMatch();
        Assert.That(_service.CurrentMatchIndex, Is.EqualTo(0));
        Assert.That(_service.SelectedIndex, Is.EqualTo(0)); // Back to first match
    }

    [Test]
    public void MoveToNextMatch_NotEmbedded_IsIgnored()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();
        _service.StartSearch();
        _service.UpdateSearchTerm("auth");
        // Don't call EmbedSearch()

        var originalIndex = _service.SelectedIndex;
        _service.MoveToNextMatch();

        Assert.That(_service.SelectedIndex, Is.EqualTo(originalIndex));
    }

    [Test]
    public void MoveToNextMatch_NoMatches_IsIgnored()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();
        _service.StartSearch();
        _service.UpdateSearchTerm("xyz123nonexistent");
        _service.EmbedSearch();

        var originalIndex = _service.SelectedIndex;
        _service.MoveToNextMatch();

        Assert.That(_service.SelectedIndex, Is.EqualTo(originalIndex));
    }

    [Test]
    public void MoveToNextMatch_RaisesOnStateChanged()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();
        _service.StartSearch();
        _service.UpdateSearchTerm("auth");
        _service.EmbedSearch();
        var stateChanged = false;
        _service.OnStateChanged += () => stateChanged = true;

        _service.MoveToNextMatch();

        Assert.That(stateChanged, Is.True);
    }

    #endregion

    #region MoveToPreviousMatch Tests

    [Test]
    public void MoveToPreviousMatch_DecrementsCurrent_WrapsAtStart()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();
        _service.StartSearch();
        _service.UpdateSearchTerm("auth");
        _service.EmbedSearch();

        // First match is selected (index 0)
        Assert.That(_service.CurrentMatchIndex, Is.EqualTo(0));

        // Move to previous - should wrap to end
        _service.MoveToPreviousMatch();
        Assert.That(_service.CurrentMatchIndex, Is.EqualTo(1)); // Last match
        Assert.That(_service.SelectedIndex, Is.EqualTo(2)); // "Third issue testing auth flow"

        // Move to previous again
        _service.MoveToPreviousMatch();
        Assert.That(_service.CurrentMatchIndex, Is.EqualTo(0));
        Assert.That(_service.SelectedIndex, Is.EqualTo(0)); // Back to first
    }

    [Test]
    public void MoveToPreviousMatch_NotEmbedded_IsIgnored()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();
        _service.StartSearch();
        _service.UpdateSearchTerm("auth");
        // Don't call EmbedSearch()

        var originalIndex = _service.SelectedIndex;
        _service.MoveToPreviousMatch();

        Assert.That(_service.SelectedIndex, Is.EqualTo(originalIndex));
    }

    [Test]
    public void MoveToPreviousMatch_NoMatches_IsIgnored()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();
        _service.StartSearch();
        _service.UpdateSearchTerm("xyz123nonexistent");
        _service.EmbedSearch();

        var originalIndex = _service.SelectedIndex;
        _service.MoveToPreviousMatch();

        Assert.That(_service.SelectedIndex, Is.EqualTo(originalIndex));
    }

    #endregion

    #region ClearSearch Tests

    [Test]
    public void ClearSearch_ResetsAllSearchState()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();
        _service.StartSearch();
        _service.UpdateSearchTerm("auth");
        _service.EmbedSearch();
        _service.MoveToNextMatch();

        _service.ClearSearch();

        Assert.That(_service.IsSearching, Is.False);
        Assert.That(_service.IsSearchEmbedded, Is.False);
        Assert.That(_service.SearchTerm, Is.Empty);
        Assert.That(_service.MatchingIndices, Is.Empty);
        Assert.That(_service.CurrentMatchIndex, Is.EqualTo(-1));
    }

    [Test]
    public void ClearSearch_PreservesSelection()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();
        _service.StartSearch();
        _service.UpdateSearchTerm("auth");
        _service.EmbedSearch();
        _service.MoveToNextMatch();
        var currentIndex = _service.SelectedIndex;

        _service.ClearSearch();

        // Selection should be preserved after clearing search
        Assert.That(_service.SelectedIndex, Is.EqualTo(currentIndex));
    }

    [Test]
    public void ClearSearch_RaisesOnStateChanged()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();
        _service.StartSearch();
        _service.UpdateSearchTerm("auth");
        _service.EmbedSearch();
        var stateChanged = false;
        _service.OnStateChanged += () => stateChanged = true;

        _service.ClearSearch();

        Assert.That(stateChanged, Is.True);
    }

    [Test]
    public void ClearSearch_WhenNotSearching_IsNoOp()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();
        var stateChanged = false;
        _service.OnStateChanged += () => stateChanged = true;

        _service.ClearSearch();

        // Should still raise state changed even if already clear
        Assert.That(_service.IsSearching, Is.False);
        Assert.That(_service.IsSearchEmbedded, Is.False);
    }

    #endregion

    #region Navigation During Search Tests

    [Test]
    public void MoveDown_WhileSearchEmbedded_StillNavigatesAllIssues()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();
        _service.StartSearch();
        _service.UpdateSearchTerm("auth");
        _service.EmbedSearch();

        // Currently at index 0, move down to index 1 (non-matching issue)
        _service.MoveDown();

        Assert.That(_service.SelectedIndex, Is.EqualTo(1));
        Assert.That(_service.SelectedIssueId, Is.EqualTo("ISSUE-002")); // Non-matching issue
    }

    [Test]
    public void MoveUp_WhileSearchEmbedded_StillNavigatesAllIssues()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectIssue("ISSUE-003");
        _service.StartSearch();
        _service.UpdateSearchTerm("auth");
        _service.EmbedSearch();

        // EmbedSearch selects first match (index 0). Move to index 2 first via next match.
        _service.MoveToNextMatch(); // Now at index 2 (second auth match)

        // Move up from index 2 to index 1 (non-matching issue)
        _service.MoveUp();

        Assert.That(_service.SelectedIndex, Is.EqualTo(1));
        Assert.That(_service.SelectedIssueId, Is.EqualTo("ISSUE-002")); // Non-matching issue
    }

    [Test]
    public void Navigation_WhileSearching_IsIgnored()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();
        _service.StartSearch();
        var originalIndex = _service.SelectedIndex;

        // While typing search term, navigation should not change selection
        _service.MoveDown();

        Assert.That(_service.SelectedIndex, Is.EqualTo(originalIndex));
    }

    #endregion

    #region Search State Property Tests

    [Test]
    public void MatchingIndices_ReturnsReadOnlyList()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();
        _service.StartSearch();
        _service.UpdateSearchTerm("auth");

        var matches = _service.MatchingIndices;

        Assert.That(matches, Is.InstanceOf<IReadOnlyList<int>>());
    }

    [Test]
    public void SearchTerm_ReturnsCurrentSearchTerm()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();
        _service.StartSearch();
        _service.UpdateSearchTerm("authentication");

        Assert.That(_service.SearchTerm, Is.EqualTo("authentication"));
    }

    [Test]
    public void CurrentMatchIndex_IsMinusOne_WhenNoMatches()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();

        Assert.That(_service.CurrentMatchIndex, Is.EqualTo(-1));
    }

    #endregion

    #region Edge Cases

    [Test]
    public void StartSearch_EmptyIssueList_DoesNotThrow()
    {
        _service.Initialize([]);

        Assert.DoesNotThrow(() => _service.StartSearch());
        Assert.That(_service.IsSearching, Is.True);
    }

    [Test]
    public void UpdateSearchTerm_EmptyIssueList_DoesNotThrow()
    {
        _service.Initialize([]);
        _service.StartSearch();

        Assert.DoesNotThrow(() => _service.UpdateSearchTerm("test"));
        Assert.That(_service.MatchingIndices, Is.Empty);
    }

    [Test]
    public void Initialize_ResetsSearchState()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();
        _service.StartSearch();
        _service.UpdateSearchTerm("auth");
        _service.EmbedSearch();

        // Re-initialize with new data
        _service.Initialize(_sampleRenderLines);

        Assert.That(_service.IsSearching, Is.False);
        Assert.That(_service.IsSearchEmbedded, Is.False);
        Assert.That(_service.SearchTerm, Is.Empty);
        Assert.That(_service.MatchingIndices, Is.Empty);
    }

    [Test]
    public void StartSearch_WhileSearchEmbedded_RestartsSearch()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();
        _service.StartSearch();
        _service.UpdateSearchTerm("auth");
        _service.EmbedSearch();

        // Start a new search while embedded
        _service.StartSearch();

        Assert.That(_service.IsSearching, Is.True);
        Assert.That(_service.IsSearchEmbedded, Is.False);
        Assert.That(_service.SearchTerm, Is.Empty);
    }

    [Test]
    public void CancelEdit_DuringSearch_ClearsSearch()
    {
        _service.Initialize(_sampleRenderLines);
        _service.SelectFirstActionable();
        _service.StartSearch();
        _service.UpdateSearchTerm("auth");

        _service.CancelEdit();

        Assert.That(_service.IsSearching, Is.False);
        Assert.That(_service.SearchTerm, Is.Empty);
    }

    #endregion
}
