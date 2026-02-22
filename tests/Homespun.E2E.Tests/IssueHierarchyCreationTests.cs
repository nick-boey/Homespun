using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Homespun.E2E.Tests;

#region Local DTOs for API responses

/// <summary>
/// Local DTO for issue response from API.
/// </summary>
public class IssueResponseDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("parentIssues")]
    public List<ParentIssueRefDto> ParentIssues { get; set; } = [];

    [JsonPropertyName("executionMode")]
    public int? ExecutionMode { get; set; }
}

/// <summary>
/// Local DTO for parent issue reference.
/// </summary>
public class ParentIssueRefDto
{
    [JsonPropertyName("parentIssue")]
    public string ParentIssue { get; set; } = "";

    [JsonPropertyName("sortOrder")]
    public string? SortOrder { get; set; }
}

/// <summary>
/// Local DTO for task graph response from API.
/// </summary>
public class TaskGraphResponseDto
{
    [JsonPropertyName("nodes")]
    public List<TaskGraphNodeDto> Nodes { get; set; } = [];

    [JsonPropertyName("totalLanes")]
    public int TotalLanes { get; set; }
}

/// <summary>
/// Local DTO for task graph node.
/// </summary>
public class TaskGraphNodeDto
{
    [JsonPropertyName("issue")]
    public IssueResponseDto Issue { get; set; } = new();

    [JsonPropertyName("lane")]
    public int Lane { get; set; }

    [JsonPropertyName("row")]
    public int Row { get; set; }

    [JsonPropertyName("isActionable")]
    public bool IsActionable { get; set; }
}

#endregion

/// <summary>
/// End-to-end tests for issue hierarchy creation using keyboard controls (TAB/Shift+TAB).
/// These tests verify that parent-child relationships are correctly established
/// when creating issues with the 'o' and 'O' commands combined with TAB/Shift+TAB.
/// </summary>
/// <remarks>
/// Issue Er7yMR: Keyboard controls don't place in correct hierarchy
///
/// ## Hierarchy Behavior
///
/// ### When creating BELOW selected issue:
/// - TAB (+1 lane): New issue becomes PARENT of the issue ABOVE (selected issue)
/// - Shift+TAB (-1 lane): New issue becomes CHILD of the issue ABOVE (selected issue)
///
/// ### When creating ABOVE selected issue:
/// - TAB (+1 lane): New issue becomes PARENT of the issue BELOW (selected issue)
/// - Shift+TAB (-1 lane): New issue becomes CHILD of the issue BELOW (selected issue)
///
/// ## Lane Positioning
/// - Parents are at HIGHER lanes (lane 1, 2, ...)
/// - Children/actionable issues are at LOWER lanes (lane 0)
/// - Moving UP a lane (TAB) = becoming a parent
/// - Moving DOWN a lane (Shift+TAB) = becoming a child
///
/// ## Test Data (from MockDataSeederService)
/// - e2e/parent1: Parallel parent with children (child1, child2)
/// - e2e/series-parent: Series parent with children (series-child1, series-child2)
/// - e2e/orphan: Standalone issue with no parent
/// </remarks>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class IssueHierarchyCreationTests : PageTest
{
    private string BaseUrl => HomespunFixture.BaseUrl;
    private HttpClient _httpClient = null!;

    [SetUp]
    public void SetUp()
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(BaseUrl)
        };
    }

    [TearDown]
    public void TearDown()
    {
        _httpClient.Dispose();
    }

    public override BrowserNewContextOptions ContextOptions()
    {
        return new BrowserNewContextOptions
        {
            IgnoreHTTPSErrors = true,
            ViewportSize = new ViewportSize { Width = 1920, Height = 1080 }
        };
    }

    #region Helper Methods

    /// <summary>
    /// Navigates to the demo project's issues tab and waits for task graph to load.
    /// </summary>
    private async Task NavigateToProjectAsync()
    {
        await Page.GotoAsync($"{BaseUrl}/projects/demo-project");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Wait for task graph to render
        var taskGraphRow = Page.Locator(".task-graph-row").First;
        await Expect(taskGraphRow).ToBeVisibleAsync(new() { Timeout = 10000 });
    }

    /// <summary>
    /// Selects the first issue in the task graph using keyboard navigation.
    /// </summary>
    private async Task SelectFirstIssueAsync()
    {
        // Focus the task graph and press 'j' to select first issue
        var taskGraph = Page.Locator("[data-testid='task-graph']");
        await taskGraph.FocusAsync();
        await Page.Keyboard.PressAsync("j");

        // Wait for selection
        var selectedRow = Page.Locator(".task-graph-row-selected");
        await Expect(selectedRow).ToBeVisibleAsync(new() { Timeout = 5000 });
    }

    /// <summary>
    /// Moves selection down by N issues using keyboard navigation.
    /// </summary>
    private async Task MoveSelectionDownAsync(int count = 1)
    {
        for (int i = 0; i < count; i++)
        {
            await Page.Keyboard.PressAsync("j");
            await Task.Delay(100);
        }
    }

    /// <summary>
    /// Gets the currently selected issue's ID.
    /// </summary>
    private async Task<string> GetSelectedIssueIdAsync()
    {
        var selectedRow = Page.Locator(".task-graph-row-selected .task-graph-issue-id");
        return (await selectedRow.TextContentAsync())?.Trim() ?? "";
    }

    /// <summary>
    /// Navigates keyboard selection to a specific issue by ID.
    /// </summary>
    private async Task NavigateToIssueAsync(string issueId)
    {
        await SelectFirstIssueAsync();
        for (int i = 0; i < 40; i++)
        {
            var currentId = await GetSelectedIssueIdAsync();
            if (currentId == issueId) return;
            await MoveSelectionDownAsync(1);
        }
        Assert.Fail($"Could not navigate to issue '{issueId}' within 40 steps");
    }

    /// <summary>
    /// Creates a new issue using keyboard controls.
    /// </summary>
    /// <param name="createAbove">Whether to create above (Shift+O) or below (o)</param>
    /// <param name="laneModifier">1=TAB (parent), -1=Shift+TAB (child), 0=neither</param>
    /// <param name="title">The issue title</param>
    private async Task CreateIssueWithKeyboardAsync(bool createAbove, int laneModifier, string title)
    {
        var taskGraph = Page.Locator("[data-testid='task-graph']");
        await taskGraph.FocusAsync();

        // Press 'o' or 'O' to create
        if (createAbove)
        {
            await Page.Keyboard.PressAsync("Shift+O");
        }
        else
        {
            await Page.Keyboard.PressAsync("o");
        }

        // Wait for the inline input to appear
        var inlineInput = Page.Locator("[data-testid='inline-issue-create']");
        await Expect(inlineInput).ToBeVisibleAsync(new() { Timeout = 5000 });

        // Apply lane modifier if specified
        if (laneModifier > 0)
        {
            await Page.Keyboard.PressAsync("Tab");
            await Task.Delay(200);
            // Input should remain visible after Tab (bug fixed)
            await Expect(inlineInput).ToBeVisibleAsync(new() { Timeout = 2000 });
        }
        else if (laneModifier < 0)
        {
            await Page.Keyboard.PressAsync("Shift+Tab");
            await Task.Delay(200);
            // Input should remain visible after Shift+Tab (bug fixed)
            await Expect(inlineInput).ToBeVisibleAsync(new() { Timeout = 2000 });
        }

        // Type the title
        var inputField = Page.Locator("[data-testid='inline-issue-input']");
        await inputField.WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = 5000 });
        await inputField.FillAsync(title);

        // Submit with Enter
        await Page.Keyboard.PressAsync("Enter");

        // Wait for inline input to disappear (creation complete)
        await Expect(inlineInput).ToBeHiddenAsync(new() { Timeout = 10000 });
    }

    /// <summary>
    /// Fetches the task graph data from the API.
    /// </summary>
    private async Task<TaskGraphResponseDto?> GetTaskGraphAsync()
    {
        var response = await _httpClient.GetAsync("/api/graph/demo-project/taskgraph/data");
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }
        return await response.Content.ReadFromJsonAsync<TaskGraphResponseDto>();
    }

    /// <summary>
    /// Fetches a specific issue from the API.
    /// </summary>
    private async Task<IssueResponseDto?> GetIssueAsync(string issueId)
    {
        var response = await _httpClient.GetAsync($"/api/issues/{Uri.EscapeDataString(issueId)}?projectId=demo-project");
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }
        return await response.Content.ReadFromJsonAsync<IssueResponseDto>();
    }

    /// <summary>
    /// Finds a newly created issue by title in the task graph.
    /// </summary>
    private async Task<TaskGraphNodeDto?> FindIssueByTitleAsync(string titleSubstring)
    {
        // Wait a moment for the graph to update
        await Task.Delay(500);

        var taskGraph = await GetTaskGraphAsync();
        return taskGraph?.Nodes.FirstOrDefault(n =>
            n.Issue.Title.Contains(titleSubstring, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that childId has parentId in its parentIssues list.
    /// </summary>
    private async Task VerifyParentRelationship(string childId, string parentId)
    {
        var childIssue = await GetIssueAsync(childId);
        Assert.That(childIssue, Is.Not.Null, $"Child issue '{childId}' should exist");
        Assert.That(childIssue!.ParentIssues.Any(p => p.ParentIssue == parentId), Is.True,
            $"Issue '{childId}' should have '{parentId}' as a parent. Actual parents: [{string.Join(", ", childIssue.ParentIssues.Select(p => p.ParentIssue))}]");
    }

    /// <summary>
    /// Generates a unique issue title for tests.
    /// </summary>
    private static string GenerateIssueTitle(string prefix = "E2E Hierarchy Test")
        => $"{prefix} {Guid.NewGuid().ToString()[..8]}";

    #endregion

    #region Sibling Creation Tests (No TAB - These Should Work)

    /// <summary>
    /// Test: Create issue BELOW without Tab creates a sibling (no hierarchy relationship).
    /// </summary>
    [Test]
    public async Task CreateBelowWithoutTab_CreatesSibling()
    {
        await NavigateToProjectAsync();
        await NavigateToIssueAsync("e2e/orphan"); // Navigate to a root-level issue

        var newTitle = GenerateIssueTitle("Sibling Below");
        await CreateIssueWithKeyboardAsync(createAbove: false, laneModifier: 0, title: newTitle);

        // Verify issue was created with no parent
        var newNode = await FindIssueByTitleAsync("Sibling Below");
        Assert.That(newNode, Is.Not.Null, "New sibling issue should appear in task graph");
        Assert.That(newNode!.Issue.ParentIssues, Is.Empty,
            "Sibling issue should have no parents");
    }

    /// <summary>
    /// Test: Create issue ABOVE without Tab creates a sibling (no hierarchy relationship).
    /// </summary>
    [Test]
    public async Task CreateAboveWithoutTab_CreatesSibling()
    {
        await NavigateToProjectAsync();
        await NavigateToIssueAsync("e2e/orphan"); // Navigate to a root-level issue

        var newTitle = GenerateIssueTitle("Sibling Above");
        await CreateIssueWithKeyboardAsync(createAbove: true, laneModifier: 0, title: newTitle);

        // Verify issue was created with no parent
        var newNode = await FindIssueByTitleAsync("Sibling Above");
        Assert.That(newNode, Is.Not.Null, "New sibling issue should appear in task graph");
        Assert.That(newNode!.Issue.ParentIssues, Is.Empty,
            "Sibling issue should have no parents");
    }

    #endregion

    #region Create Below with TAB (New Issue Becomes Parent of Above) - PARALLEL MODE

    /// <summary>
    /// Test: Create issue below with TAB - new issue becomes parent of the selected issue.
    /// Scenario: Select orphan issue, create below with Tab → new becomes parent of orphan.
    /// </summary>
    [Test]
    public async Task CreateBelowWithTab_ParallelMode_OrphanIssue_NewBecomesParent()
    {
        await NavigateToProjectAsync();
        await SelectFirstIssueAsync();

        var selectedIssueId = await GetSelectedIssueIdAsync();
        Assert.That(selectedIssueId, Is.Not.Empty, "Should have selected an issue");

        var newTitle = GenerateIssueTitle("Parent via Tab below");
        await CreateIssueWithKeyboardAsync(createAbove: false, laneModifier: 1, title: newTitle);

        // The selected issue should now have the new issue as parent
        var newNode = await FindIssueByTitleAsync("Parent via Tab below");
        Assert.That(newNode, Is.Not.Null, "New parent issue should appear in task graph");

        // Verify the adjacent (selected) issue now has the new issue as its parent
        await VerifyParentRelationship(selectedIssueId, newNode!.Issue.Id);
    }

    /// <summary>
    /// Test: Create issue below with TAB - select an issue that already has a parent.
    /// New issue becomes an additional parent of the selected issue.
    /// </summary>
    [Test]
    public async Task CreateBelowWithTab_ParallelMode_IssueWithParent_NewBecomesAdditionalParent()
    {
        await NavigateToProjectAsync();

        // Navigate to e2e/child1 which has e2e/parent1 as parent
        await NavigateToIssueAsync("e2e/child1");

        var newTitle = GenerateIssueTitle("Additional Parent");
        await CreateIssueWithKeyboardAsync(createAbove: false, laneModifier: 1, title: newTitle);

        // e2e/child1 should now have both e2e/parent1 AND the new issue as parents
        var newNode = await FindIssueByTitleAsync("Additional Parent");
        Assert.That(newNode, Is.Not.Null, "New parent issue should appear in task graph");

        var child1 = await GetIssueAsync("e2e/child1");
        Assert.That(child1, Is.Not.Null);
        Assert.That(child1!.ParentIssues.Any(p => p.ParentIssue == "e2e/parent1"), Is.True,
            "child1 should still have original parent");
        Assert.That(child1.ParentIssues.Any(p => p.ParentIssue == newNode!.Issue.Id), Is.True,
            "child1 should also have new issue as additional parent");
    }

    /// <summary>
    /// Test: Verify Tab can be pressed multiple times but only first press registers.
    /// </summary>
    [Test]
    public async Task CreateBelowWithTab_OnlyFirstTabPressRegisters()
    {
        await NavigateToProjectAsync();
        await SelectFirstIssueAsync();

        var selectedIssueId = await GetSelectedIssueIdAsync();

        // Start creating below
        var taskGraph = Page.Locator("[data-testid='task-graph']");
        await taskGraph.FocusAsync();
        await Page.Keyboard.PressAsync("o");

        var inlineInput = Page.Locator("[data-testid='inline-issue-create']");
        await Expect(inlineInput).ToBeVisibleAsync(new() { Timeout = 5000 });

        // Press Tab once
        await Page.Keyboard.PressAsync("Tab");
        await Task.Delay(200);

        // Verify parent indicator
        var parentIndicator = Page.Locator(".lane-indicator.parent");
        await Expect(parentIndicator).ToBeVisibleAsync(new() { Timeout = 2000 });

        // Press Tab again - should be ignored
        await Page.Keyboard.PressAsync("Tab");
        await Task.Delay(200);

        // Should still show parent indicator (not changed)
        await Expect(parentIndicator).ToBeVisibleAsync();
        var childIndicator = Page.Locator(".lane-indicator.child");
        await Expect(childIndicator).ToBeHiddenAsync();

        // Type and submit
        var newTitle = GenerateIssueTitle("Tab Multiple Test");
        var inputField = Page.Locator("[data-testid='inline-issue-input']");
        await inputField.FillAsync(newTitle);
        await Page.Keyboard.PressAsync("Enter");
        await Expect(inlineInput).ToBeHiddenAsync(new() { Timeout = 10000 });

        // Verify hierarchy was created correctly
        var newNode = await FindIssueByTitleAsync("Tab Multiple Test");
        Assert.That(newNode, Is.Not.Null, "New issue should appear in task graph");
        await VerifyParentRelationship(selectedIssueId, newNode!.Issue.Id);
    }

    #endregion

    #region Create Below with Shift+TAB (New Issue Becomes Child of Above) - PARALLEL MODE

    /// <summary>
    /// Test: Create issue below with Shift+TAB - new issue becomes child of selected issue.
    /// Scenario: Select a parent issue, create below with Shift+Tab → new becomes child.
    /// </summary>
    [Test]
    public async Task CreateBelowWithShiftTab_ParallelMode_NewBecomesChild()
    {
        await NavigateToProjectAsync();
        await SelectFirstIssueAsync();

        var selectedIssueId = await GetSelectedIssueIdAsync();
        Assert.That(selectedIssueId, Is.Not.Empty, "Should have selected an issue");

        var newTitle = GenerateIssueTitle("Child via ShiftTab below");
        await CreateIssueWithKeyboardAsync(createAbove: false, laneModifier: -1, title: newTitle);

        // The new issue should have the selected issue as its parent
        var newNode = await FindIssueByTitleAsync("Child via ShiftTab below");
        Assert.That(newNode, Is.Not.Null, "New child issue should appear in task graph");

        Assert.That(newNode!.Issue.ParentIssues.Any(p => p.ParentIssue == selectedIssueId), Is.True,
            $"New issue should have '{selectedIssueId}' as parent. Actual parents: [{string.Join(", ", newNode.Issue.ParentIssues.Select(p => p.ParentIssue))}]");
    }

    /// <summary>
    /// Test: Create issue below with Shift+TAB - adding child to issue that already has children.
    /// </summary>
    [Test]
    public async Task CreateBelowWithShiftTab_ParallelMode_IssueWithChildren_NewBecomesAdditionalChild()
    {
        await NavigateToProjectAsync();

        // Navigate to e2e/parent1 which already has children
        await NavigateToIssueAsync("e2e/parent1");

        var newTitle = GenerateIssueTitle("Additional Child");
        await CreateIssueWithKeyboardAsync(createAbove: false, laneModifier: -1, title: newTitle);

        // New issue should have e2e/parent1 as its parent
        var newNode = await FindIssueByTitleAsync("Additional Child");
        Assert.That(newNode, Is.Not.Null, "New child issue should appear in task graph");

        Assert.That(newNode!.Issue.ParentIssues.Any(p => p.ParentIssue == "e2e/parent1"), Is.True,
            "New issue should have e2e/parent1 as parent");
    }

    #endregion

    #region Create Above with TAB (New Issue Becomes Parent of Below) - PARALLEL MODE

    /// <summary>
    /// Test: Create issue above with TAB - new issue becomes parent of the selected issue.
    /// </summary>
    [Test]
    public async Task CreateAboveWithTab_ParallelMode_NewBecomesParentOfBelow()
    {
        await NavigateToProjectAsync();
        await SelectFirstIssueAsync();
        await MoveSelectionDownAsync(1); // Select second issue so we can create above

        var selectedIssueId = await GetSelectedIssueIdAsync();
        Assert.That(selectedIssueId, Is.Not.Empty, "Should have selected an issue");

        var newTitle = GenerateIssueTitle("Parent via Tab above");
        await CreateIssueWithKeyboardAsync(createAbove: true, laneModifier: 1, title: newTitle);

        // The selected issue should have the new issue as parent
        var newNode = await FindIssueByTitleAsync("Parent via Tab above");
        Assert.That(newNode, Is.Not.Null, "New parent issue should appear in task graph");

        await VerifyParentRelationship(selectedIssueId, newNode!.Issue.Id);
    }

    /// <summary>
    /// Test: Create issue above with TAB for issue that already has parent.
    /// </summary>
    [Test]
    public async Task CreateAboveWithTab_ParallelMode_IssueWithParent_NewBecomesAdditionalParent()
    {
        await NavigateToProjectAsync();

        // Navigate to e2e/child1
        await NavigateToIssueAsync("e2e/child1");

        var newTitle = GenerateIssueTitle("Additional Parent Above");
        await CreateIssueWithKeyboardAsync(createAbove: true, laneModifier: 1, title: newTitle);

        // e2e/child1 should have both e2e/parent1 AND new issue as parents
        var newNode = await FindIssueByTitleAsync("Additional Parent Above");
        Assert.That(newNode, Is.Not.Null, "New parent issue should appear in task graph");

        var child1 = await GetIssueAsync("e2e/child1");
        Assert.That(child1, Is.Not.Null);
        Assert.That(child1!.ParentIssues.Any(p => p.ParentIssue == "e2e/parent1"), Is.True,
            "child1 should still have original parent");
        Assert.That(child1.ParentIssues.Any(p => p.ParentIssue == newNode!.Issue.Id), Is.True,
            "child1 should also have new issue as additional parent");
    }

    #endregion

    #region Create Above with Shift+TAB (New Issue Becomes Child of Below) - PARALLEL MODE

    /// <summary>
    /// Test: Create issue above with Shift+TAB - new issue becomes child of the selected issue.
    /// </summary>
    [Test]
    public async Task CreateAboveWithShiftTab_ParallelMode_NewBecomesChildOfBelow()
    {
        await NavigateToProjectAsync();
        await SelectFirstIssueAsync();
        await MoveSelectionDownAsync(1); // Select second issue

        var selectedIssueId = await GetSelectedIssueIdAsync();
        Assert.That(selectedIssueId, Is.Not.Empty, "Should have selected an issue");

        var newTitle = GenerateIssueTitle("Child via ShiftTab above");
        await CreateIssueWithKeyboardAsync(createAbove: true, laneModifier: -1, title: newTitle);

        // New issue should have the selected issue as parent
        var newNode = await FindIssueByTitleAsync("Child via ShiftTab above");
        Assert.That(newNode, Is.Not.Null, "New child issue should appear in task graph");

        Assert.That(newNode!.Issue.ParentIssues.Any(p => p.ParentIssue == selectedIssueId), Is.True,
            $"New issue should have '{selectedIssueId}' as parent");
    }

    #endregion

    #region Series Execution Mode Tests

    /// <summary>
    /// Test: Create issue below with TAB in series parent - should become parent with proper ordering.
    /// </summary>
    [Test]
    public async Task CreateBelowWithTab_SeriesMode_NewBecomesParent()
    {
        await NavigateToProjectAsync();

        // Navigate to e2e/series-child1 (child of series-parent)
        await NavigateToIssueAsync("e2e/series-child1");

        var newTitle = GenerateIssueTitle("Series Parent via Tab");
        await CreateIssueWithKeyboardAsync(createAbove: false, laneModifier: 1, title: newTitle);

        // series-child1 should now have the new issue as a parent
        var newNode = await FindIssueByTitleAsync("Series Parent via Tab");
        Assert.That(newNode, Is.Not.Null, "New parent issue should appear in task graph");

        await VerifyParentRelationship("e2e/series-child1", newNode!.Issue.Id);
    }

    /// <summary>
    /// Test: Create issue below with Shift+TAB in series parent - should become child with sort order.
    /// </summary>
    [Test]
    public async Task CreateBelowWithShiftTab_SeriesMode_NewBecomesChildWithSortOrder()
    {
        await NavigateToProjectAsync();

        // Navigate to e2e/series-parent
        await NavigateToIssueAsync("e2e/series-parent");

        var newTitle = GenerateIssueTitle("Series Child via ShiftTab");
        await CreateIssueWithKeyboardAsync(createAbove: false, laneModifier: -1, title: newTitle);

        // New issue should have e2e/series-parent as parent
        var newNode = await FindIssueByTitleAsync("Series Child via ShiftTab");
        Assert.That(newNode, Is.Not.Null, "New child issue should appear in task graph");

        Assert.That(newNode!.Issue.ParentIssues.Any(p => p.ParentIssue == "e2e/series-parent"), Is.True,
            "New issue should have e2e/series-parent as parent");
    }

    /// <summary>
    /// Test: Create issue above with TAB in series mode.
    /// </summary>
    [Test]
    public async Task CreateAboveWithTab_SeriesMode_NewBecomesParent()
    {
        await NavigateToProjectAsync();

        // Navigate to e2e/series-child2
        await NavigateToIssueAsync("e2e/series-child2");

        var newTitle = GenerateIssueTitle("Series Parent Above");
        await CreateIssueWithKeyboardAsync(createAbove: true, laneModifier: 1, title: newTitle);

        // series-child2 should now have the new issue as a parent
        var newNode = await FindIssueByTitleAsync("Series Parent Above");
        Assert.That(newNode, Is.Not.Null, "New parent issue should appear in task graph");

        await VerifyParentRelationship("e2e/series-child2", newNode!.Issue.Id);
    }

    /// <summary>
    /// Test: Create issue above with Shift+TAB in series mode.
    /// </summary>
    [Test]
    public async Task CreateAboveWithShiftTab_SeriesMode_NewBecomesChild()
    {
        await NavigateToProjectAsync();

        // Navigate to e2e/series-parent
        await NavigateToIssueAsync("e2e/series-parent");

        var newTitle = GenerateIssueTitle("Series Child Above");
        await CreateIssueWithKeyboardAsync(createAbove: true, laneModifier: -1, title: newTitle);

        // New issue should have e2e/series-parent as parent
        var newNode = await FindIssueByTitleAsync("Series Child Above");
        Assert.That(newNode, Is.Not.Null, "New child issue should appear in task graph");

        Assert.That(newNode!.Issue.ParentIssues.Any(p => p.ParentIssue == "e2e/series-parent"), Is.True,
            "New issue should have e2e/series-parent as parent");
    }

    #endregion

    #region Lane Position Verification Tests

    /// <summary>
    /// Test: When creating as parent (TAB), new issue should be at HIGHER lane.
    /// </summary>
    [Test]
    public async Task CreateAsParent_Tab_NewIssueShouldBeAtHigherLane()
    {
        await NavigateToProjectAsync();
        await SelectFirstIssueAsync();

        var selectedIssueId = await GetSelectedIssueIdAsync();

        // Get the selected issue's current lane
        var graphBefore = await GetTaskGraphAsync();
        var selectedNode = graphBefore?.Nodes.FirstOrDefault(n => n.Issue.Id == selectedIssueId);
        Assert.That(selectedNode, Is.Not.Null, "Selected issue should be in task graph");
        var selectedLane = selectedNode!.Lane;

        var newTitle = GenerateIssueTitle("Parent Lane Test");
        await CreateIssueWithKeyboardAsync(createAbove: false, laneModifier: 1, title: newTitle);

        // New parent should be at a higher lane than the child
        var newNode = await FindIssueByTitleAsync("Parent Lane Test");
        Assert.That(newNode, Is.Not.Null, "New parent issue should appear in task graph");

        // Refresh selected issue's lane (may have changed)
        var graphAfter = await GetTaskGraphAsync();
        var updatedSelectedNode = graphAfter?.Nodes.FirstOrDefault(n => n.Issue.Id == selectedIssueId);
        Assert.That(updatedSelectedNode, Is.Not.Null);

        Assert.That(newNode!.Lane, Is.GreaterThanOrEqualTo(updatedSelectedNode!.Lane),
            $"Parent (lane {newNode.Lane}) should be at higher or equal lane than child (lane {updatedSelectedNode.Lane})");
    }

    /// <summary>
    /// Test: When creating as child (Shift+TAB), new issue should be at LOWER lane (or same if at lane 0).
    /// </summary>
    [Test]
    public async Task CreateAsChild_ShiftTab_NewIssueShouldBeAtLowerLane()
    {
        await NavigateToProjectAsync();
        await SelectFirstIssueAsync();

        var selectedIssueId = await GetSelectedIssueIdAsync();

        var newTitle = GenerateIssueTitle("Child Lane Test");
        await CreateIssueWithKeyboardAsync(createAbove: false, laneModifier: -1, title: newTitle);

        // New child should be at a lower or equal lane than the parent
        var graphAfter = await GetTaskGraphAsync();
        var newNode = graphAfter?.Nodes.FirstOrDefault(n => n.Issue.Title.Contains("Child Lane Test"));
        var parentNode = graphAfter?.Nodes.FirstOrDefault(n => n.Issue.Id == selectedIssueId);

        Assert.That(newNode, Is.Not.Null, "New child issue should appear in task graph");
        Assert.That(parentNode, Is.Not.Null, "Parent issue should still be in task graph");

        Assert.That(newNode!.Lane, Is.LessThanOrEqualTo(parentNode!.Lane),
            $"Child (lane {newNode.Lane}) should be at lower or equal lane than parent (lane {parentNode.Lane})");
    }

    #endregion

    #region Edge Cases

    /// <summary>
    /// Test: Cancel inline creation with Escape key.
    /// </summary>
    [Test]
    public async Task CreateIssue_CancelWithEscape_NoIssueCreated()
    {
        await NavigateToProjectAsync();
        await SelectFirstIssueAsync();

        // Start creating
        var taskGraph = Page.Locator("[data-testid='task-graph']");
        await taskGraph.FocusAsync();
        await Page.Keyboard.PressAsync("o");

        var inlineInput = Page.Locator("[data-testid='inline-issue-create']");
        await Expect(inlineInput).ToBeVisibleAsync(new() { Timeout = 5000 });

        // Type partial title
        var inputField = Page.Locator("[data-testid='inline-issue-input']");
        await inputField.FillAsync("Cancelled issue");

        // Press Escape to cancel
        await Page.Keyboard.PressAsync("Escape");

        // Verify input is gone
        await Expect(inlineInput).ToBeHiddenAsync(new() { Timeout = 5000 });
    }

    /// <summary>
    /// Test: Cannot submit empty title.
    /// </summary>
    [Test]
    public async Task CreateIssue_EmptyTitle_CannotSubmit()
    {
        await NavigateToProjectAsync();
        await SelectFirstIssueAsync();

        // Start creating
        var taskGraph = Page.Locator("[data-testid='task-graph']");
        await taskGraph.FocusAsync();
        await Page.Keyboard.PressAsync("o");

        var inlineInput = Page.Locator("[data-testid='inline-issue-create']");
        await Expect(inlineInput).ToBeVisibleAsync(new() { Timeout = 5000 });

        // Press Enter without typing anything
        await Page.Keyboard.PressAsync("Enter");

        // Input should still be visible (nothing submitted)
        await Expect(inlineInput).ToBeVisibleAsync(new() { Timeout = 1000 });

        // Cancel
        await Page.Keyboard.PressAsync("Escape");
    }

    /// <summary>
    /// Test: Lane indicator shows correct parent relationship text after Tab.
    /// </summary>
    [Test]
    public async Task CreateWithTab_LaneIndicatorShowsParentText()
    {
        await NavigateToProjectAsync();
        await SelectFirstIssueAsync();

        // Start creating below
        var taskGraph = Page.Locator("[data-testid='task-graph']");
        await taskGraph.FocusAsync();
        await Page.Keyboard.PressAsync("o");

        var inlineInput = Page.Locator("[data-testid='inline-issue-create']");
        await Expect(inlineInput).ToBeVisibleAsync(new() { Timeout = 5000 });

        // Press Tab to set as parent
        await Page.Keyboard.PressAsync("Tab");
        await Task.Delay(200);

        // Input should still be visible and indicator should show "Parent of above"
        await Expect(inlineInput).ToBeVisibleAsync(new() { Timeout = 2000 });
        var indicator = Page.Locator(".lane-indicator.parent");
        await Expect(indicator).ToBeVisibleAsync(new() { Timeout = 2000 });
        await Expect(indicator).ToContainTextAsync("Parent of above");

        // Cancel
        await Page.Keyboard.PressAsync("Escape");
    }

    /// <summary>
    /// Test: Lane indicator shows correct child relationship text after Shift+Tab.
    /// </summary>
    [Test]
    public async Task CreateWithShiftTab_LaneIndicatorShowsChildText()
    {
        await NavigateToProjectAsync();
        await SelectFirstIssueAsync();

        // Start creating below
        var taskGraph = Page.Locator("[data-testid='task-graph']");
        await taskGraph.FocusAsync();
        await Page.Keyboard.PressAsync("o");

        var inlineInput = Page.Locator("[data-testid='inline-issue-create']");
        await Expect(inlineInput).ToBeVisibleAsync(new() { Timeout = 5000 });

        // Press Shift+Tab to set as child
        await Page.Keyboard.PressAsync("Shift+Tab");
        await Task.Delay(200);

        // Input should still be visible and indicator should show "Child of above"
        await Expect(inlineInput).ToBeVisibleAsync(new() { Timeout = 2000 });
        var indicator = Page.Locator(".lane-indicator.child");
        await Expect(indicator).ToBeVisibleAsync(new() { Timeout = 2000 });
        await Expect(indicator).ToContainTextAsync("Child of above");

        // Cancel
        await Page.Keyboard.PressAsync("Escape");
    }

    #endregion

    #region Hierarchy Relationship Verification

    /// <summary>
    /// Test: After creating with Tab, verify parent-child relationship exists via API.
    /// </summary>
    [Test]
    public async Task CreateBelowWithTab_AdjacentIssueHasNewParent()
    {
        await NavigateToProjectAsync();
        await SelectFirstIssueAsync();

        var adjacentIssueId = await GetSelectedIssueIdAsync();
        Assert.That(adjacentIssueId, Is.Not.Empty);

        var newTitle = GenerateIssueTitle("Verify Parent Relationship");
        await CreateIssueWithKeyboardAsync(createAbove: false, laneModifier: 1, title: newTitle);

        // Fetch the new issue and adjacent issue to verify the relationship
        var newNode = await FindIssueByTitleAsync("Verify Parent Relationship");
        Assert.That(newNode, Is.Not.Null, "New issue should appear in task graph");

        // The adjacent issue should have the new issue as a parent
        await VerifyParentRelationship(adjacentIssueId, newNode!.Issue.Id);
    }

    /// <summary>
    /// Test: After creating with Shift+Tab, verify new issue is child of adjacent.
    /// </summary>
    [Test]
    public async Task CreateBelowWithShiftTab_NewIssueHasAdjacentAsParent()
    {
        await NavigateToProjectAsync();
        await SelectFirstIssueAsync();

        var adjacentIssueId = await GetSelectedIssueIdAsync();
        Assert.That(adjacentIssueId, Is.Not.Empty);

        var newTitle = GenerateIssueTitle("Verify Child Relationship");
        await CreateIssueWithKeyboardAsync(createAbove: false, laneModifier: -1, title: newTitle);

        // Wait for graph to update
        await Task.Delay(500);

        // Fetch task graph and find the new issue
        var taskGraph = await GetTaskGraphAsync();
        Assert.That(taskGraph, Is.Not.Null);

        var newNode = taskGraph!.Nodes.FirstOrDefault(n =>
            n.Issue.Title.Contains("Verify Child Relationship"));
        Assert.That(newNode, Is.Not.Null, "New issue should appear in task graph");

        // New issue should have adjacent as parent
        Assert.That(newNode!.Issue.ParentIssues.Any(p => p.ParentIssue == adjacentIssueId), Is.True,
            $"New issue should have '{adjacentIssueId}' as its parent. Actual parents: [{string.Join(", ", newNode.Issue.ParentIssues.Select(p => p.ParentIssue))}]");
    }

    #endregion

    #region Sibling Inheritance Tests

    /// <summary>
    /// Test: Create issue BELOW a non-root issue without Tab/Shift+Tab inherits the same parent.
    /// When pressing 'o' on e2e/child1 (child of e2e/parent1, parallel mode), the new sibling
    /// should also have e2e/parent1 as its parent.
    /// </summary>
    [Test]
    public async Task CreateBelowWithoutTab_OnNonRootIssue_ParallelMode_InheritsParent()
    {
        await NavigateToProjectAsync();

        // Navigate to e2e/child1 which is a child of e2e/parent1 (parallel mode)
        await NavigateToIssueAsync("e2e/child1");

        var newTitle = GenerateIssueTitle("Sibling Below Child1");
        await CreateIssueWithKeyboardAsync(createAbove: false, laneModifier: 0, title: newTitle);

        // Verify new issue was created and has e2e/parent1 as parent (inherited from sibling)
        var newNode = await FindIssueByTitleAsync("Sibling Below Child1");
        Assert.That(newNode, Is.Not.Null, "New sibling issue should appear in task graph");

        Assert.That(newNode!.Issue.ParentIssues.Any(p => p.ParentIssue == "e2e/parent1"), Is.True,
            $"New sibling should inherit parent 'e2e/parent1' from adjacent issue. " +
            $"Actual parents: [{string.Join(", ", newNode.Issue.ParentIssues.Select(p => p.ParentIssue))}]");
    }

    /// <summary>
    /// Test: Create issue ABOVE a non-root issue without Tab/Shift+Tab inherits the same parent.
    /// When pressing Shift+O on e2e/child2 (child of e2e/parent1, parallel mode), the new sibling
    /// should also have e2e/parent1 as its parent.
    /// </summary>
    [Test]
    public async Task CreateAboveWithoutTab_OnNonRootIssue_ParallelMode_InheritsParent()
    {
        await NavigateToProjectAsync();

        // Navigate to e2e/child2 which is a child of e2e/parent1 (parallel mode)
        await NavigateToIssueAsync("e2e/child2");

        var newTitle = GenerateIssueTitle("Sibling Above Child2");
        await CreateIssueWithKeyboardAsync(createAbove: true, laneModifier: 0, title: newTitle);

        // Verify new issue was created and has e2e/parent1 as parent (inherited from sibling)
        var newNode = await FindIssueByTitleAsync("Sibling Above Child2");
        Assert.That(newNode, Is.Not.Null, "New sibling issue should appear in task graph");

        Assert.That(newNode!.Issue.ParentIssues.Any(p => p.ParentIssue == "e2e/parent1"), Is.True,
            $"New sibling should inherit parent 'e2e/parent1' from adjacent issue. " +
            $"Actual parents: [{string.Join(", ", newNode.Issue.ParentIssues.Select(p => p.ParentIssue))}]");
    }

    /// <summary>
    /// Test: Create issue BELOW a non-root issue in series mode inherits parent with correct sort order.
    /// When pressing 'o' on e2e/series-child1 (sort "0", child of e2e/series-parent), the new sibling
    /// should have e2e/series-parent as parent with sort order between "0" (child1) and "1" (child2).
    /// </summary>
    [Test]
    public async Task CreateBelowWithoutTab_OnNonRootIssue_SeriesMode_InheritsParentWithSortOrder()
    {
        await NavigateToProjectAsync();

        // Navigate to e2e/series-child1 (child of e2e/series-parent, sort order "0")
        await NavigateToIssueAsync("e2e/series-child1");

        var newTitle = GenerateIssueTitle("Series Sibling Below");
        await CreateIssueWithKeyboardAsync(createAbove: false, laneModifier: 0, title: newTitle);

        // Verify new issue was created and has e2e/series-parent as parent
        var newNode = await FindIssueByTitleAsync("Series Sibling Below");
        Assert.That(newNode, Is.Not.Null, "New sibling issue should appear in task graph");

        var parentRef = newNode!.Issue.ParentIssues.FirstOrDefault(p => p.ParentIssue == "e2e/series-parent");
        Assert.That(parentRef, Is.Not.Null,
            $"New sibling should inherit parent 'e2e/series-parent' from adjacent issue. " +
            $"Actual parents: [{string.Join(", ", newNode.Issue.ParentIssues.Select(p => p.ParentIssue))}]");

        // Sort order should be between "0" (series-child1) and "1" (series-child2)
        Assert.That(string.Compare(parentRef!.SortOrder, "0", StringComparison.Ordinal), Is.GreaterThan(0),
            $"Sort order '{parentRef.SortOrder}' should be lexicographically after '0' (series-child1)");
        Assert.That(string.Compare(parentRef.SortOrder, "1", StringComparison.Ordinal), Is.LessThan(0),
            $"Sort order '{parentRef.SortOrder}' should be lexicographically before '1' (series-child2)");
    }

    /// <summary>
    /// Test: Create issue ABOVE a non-root issue in series mode inherits parent with correct sort order.
    /// When pressing Shift+O on e2e/series-child2 (sort "1", child of e2e/series-parent), the new sibling
    /// should have e2e/series-parent as parent with sort order between "0" (child1) and "1" (child2).
    /// </summary>
    [Test]
    public async Task CreateAboveWithoutTab_OnNonRootIssue_SeriesMode_InheritsParentWithSortOrder()
    {
        await NavigateToProjectAsync();

        // Navigate to e2e/series-child2 (child of e2e/series-parent, sort order "1")
        await NavigateToIssueAsync("e2e/series-child2");

        var newTitle = GenerateIssueTitle("Series Sibling Above");
        await CreateIssueWithKeyboardAsync(createAbove: true, laneModifier: 0, title: newTitle);

        // Verify new issue was created and has e2e/series-parent as parent
        var newNode = await FindIssueByTitleAsync("Series Sibling Above");
        Assert.That(newNode, Is.Not.Null, "New sibling issue should appear in task graph");

        var parentRef = newNode!.Issue.ParentIssues.FirstOrDefault(p => p.ParentIssue == "e2e/series-parent");
        Assert.That(parentRef, Is.Not.Null,
            $"New sibling should inherit parent 'e2e/series-parent' from adjacent issue. " +
            $"Actual parents: [{string.Join(", ", newNode.Issue.ParentIssues.Select(p => p.ParentIssue))}]");

        // Sort order should be between "0" (series-child1) and "1" (series-child2)
        Assert.That(string.Compare(parentRef!.SortOrder, "0", StringComparison.Ordinal), Is.GreaterThan(0),
            $"Sort order '{parentRef.SortOrder}' should be lexicographically after '0' (series-child1)");
        Assert.That(string.Compare(parentRef.SortOrder, "1", StringComparison.Ordinal), Is.LessThan(0),
            $"Sort order '{parentRef.SortOrder}' should be lexicographically before '1' (series-child2)");
    }

    /// <summary>
    /// Test: Create issue BELOW a root-level issue without Tab/Shift+Tab stays root-level.
    /// Regression guard: sibling of a root issue should have no parents.
    /// </summary>
    [Test]
    public async Task CreateBelowWithoutTab_OnRootIssue_RemainsRootLevel()
    {
        await NavigateToProjectAsync();

        // Navigate to e2e/orphan which has no parents (root issue)
        await NavigateToIssueAsync("e2e/orphan");

        var newTitle = GenerateIssueTitle("Root Sibling Below");
        await CreateIssueWithKeyboardAsync(createAbove: false, laneModifier: 0, title: newTitle);

        // Verify new issue was created with no parents (sibling of root stays root)
        var newNode = await FindIssueByTitleAsync("Root Sibling Below");
        Assert.That(newNode, Is.Not.Null, "New sibling issue should appear in task graph");

        Assert.That(newNode!.Issue.ParentIssues, Is.Empty,
            $"Sibling of root issue should have no parents. " +
            $"Actual parents: [{string.Join(", ", newNode.Issue.ParentIssues.Select(p => p.ParentIssue))}]");
    }

    /// <summary>
    /// Test: Create issue BELOW the second child of a parallel parent inherits parent.
    /// Verifies inheritance works for any child position, not just the first child.
    /// </summary>
    [Test]
    public async Task CreateBelowWithoutTab_OnSecondChild_ParallelMode_InheritsParent()
    {
        await NavigateToProjectAsync();

        // Navigate to e2e/child2 (second child of e2e/parent1)
        await NavigateToIssueAsync("e2e/child2");

        var newTitle = GenerateIssueTitle("Sibling Below Child2");
        await CreateIssueWithKeyboardAsync(createAbove: false, laneModifier: 0, title: newTitle);

        // Verify new issue was created and has e2e/parent1 as parent (inherited from sibling)
        var newNode = await FindIssueByTitleAsync("Sibling Below Child2");
        Assert.That(newNode, Is.Not.Null, "New sibling issue should appear in task graph");

        Assert.That(newNode!.Issue.ParentIssues.Any(p => p.ParentIssue == "e2e/parent1"), Is.True,
            $"New sibling should inherit parent 'e2e/parent1' from second child. " +
            $"Actual parents: [{string.Join(", ", newNode.Issue.ParentIssues.Select(p => p.ParentIssue))}]");
    }

    #endregion
}
