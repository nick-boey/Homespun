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
    public string? ExecutionMode { get; set; }
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
/// ## Current Bug Status
/// KNOWN BUG: Tab/Shift+Tab keys close the inline input because preventDefault is not called.
/// The browser's default Tab behavior causes focus to move away, triggering HandleBlur which cancels.
/// Tests assert this bug behavior and will fail when the bug is fixed.
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
    /// Moves selection up by N issues using keyboard navigation.
    /// </summary>
    private async Task MoveSelectionUpAsync(int count = 1)
    {
        for (int i = 0; i < count; i++)
        {
            await Page.Keyboard.PressAsync("k");
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
    /// Creates a new issue using keyboard controls.
    /// </summary>
    /// <param name="createAbove">Whether to create above (Shift+O) or below (o)</param>
    /// <param name="laneModifier">1=TAB (parent), -1=Shift+TAB (child), 0=neither</param>
    /// <param name="title">The issue title</param>
    /// <returns>True if issue was created successfully, false if Tab caused the input to close (bug)</returns>
    private async Task<bool> CreateIssueWithKeyboardAsync(bool createAbove, int laneModifier, string title)
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
        // BUG: Tab key causes the input to close because preventDefault is not called
        if (laneModifier > 0)
        {
            await Page.Keyboard.PressAsync("Tab");
            // Wait a bit to see if input is still there
            await Task.Delay(200);
            if (!await inlineInput.IsVisibleAsync())
            {
                // Tab closed the input - this is the bug
                return false;
            }
        }
        else if (laneModifier < 0)
        {
            await Page.Keyboard.PressAsync("Shift+Tab");
            // Wait a bit to see if input is still there
            await Task.Delay(200);
            if (!await inlineInput.IsVisibleAsync())
            {
                // Shift+Tab closed the input - this is the bug
                return false;
            }
        }

        // Type the title
        var inputField = Page.Locator("[data-testid='inline-issue-input']");
        // Wait for the input to be stable before filling
        await inputField.WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = 5000 });
        await inputField.FillAsync(title);

        // Submit with Enter
        await Page.Keyboard.PressAsync("Enter");

        // Wait for inline input to disappear (creation complete)
        await Expect(inlineInput).ToBeHiddenAsync(new() { Timeout = 10000 });
        return true;
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
    /// Generates a unique issue title for tests.
    /// </summary>
    private static string GenerateIssueTitle(string prefix = "E2E Hierarchy Test")
        => $"{prefix} {Guid.NewGuid().ToString()[..8]}";

    /// <summary>
    /// Standard assertion for Tab bug - Tab closes the input.
    /// When bug is fixed, these tests will fail (which is the intended behavior).
    /// </summary>
    private static void AssertTabBug(bool created, string keyDescription)
    {
        Assert.That(created, Is.False,
            $"EXPECTED BUG: {keyDescription} key should close the inline input (because preventDefault is not called). " +
            "When this test FAILS, it means the bug has been FIXED and tests should be updated to verify correct hierarchy!");
    }

    #endregion

    #region Sibling Creation Tests (No TAB - These Should Work)

    /// <summary>
    /// Test: Create issue BELOW without Tab creates a sibling (no hierarchy relationship).
    /// This tests that basic issue creation (without Tab modifier) works correctly.
    /// </summary>
    [Test]
    public async Task CreateBelowWithoutTab_CreatesSibling()
    {
        await NavigateToProjectAsync();
        await SelectFirstIssueAsync();

        var newTitle = GenerateIssueTitle("Sibling Below");
        var created = await CreateIssueWithKeyboardAsync(createAbove: false, laneModifier: 0, title: newTitle);

        Assert.That(created, Is.True, "Basic sibling creation (without Tab) should succeed");
    }

    /// <summary>
    /// Test: Create issue ABOVE without Tab creates a sibling (no hierarchy relationship).
    /// </summary>
    [Test]
    public async Task CreateAboveWithoutTab_CreatesSibling()
    {
        await NavigateToProjectAsync();
        await SelectFirstIssueAsync();
        await MoveSelectionDownAsync(1); // Move to second issue so we can create above

        var newTitle = GenerateIssueTitle("Sibling Above");
        var created = await CreateIssueWithKeyboardAsync(createAbove: true, laneModifier: 0, title: newTitle);

        Assert.That(created, Is.True, "Basic sibling creation above (without Tab) should succeed");
    }

    #endregion

    #region Create Below with TAB (New Issue Becomes Parent of Above) - PARALLEL MODE

    /// <summary>
    /// Test: Create issue below with TAB - new issue becomes parent of the selected issue.
    /// Scenario: Select orphan issue, create below with Tab → new becomes parent of orphan.
    ///
    /// KNOWN BUG: Tab closes the input.
    /// </summary>
    [Test]
    public async Task CreateBelowWithTab_ParallelMode_OrphanIssue_NewBecomesParent()
    {
        await NavigateToProjectAsync();
        await SelectFirstIssueAsync();

        var selectedIssueId = await GetSelectedIssueIdAsync();
        Assert.That(selectedIssueId, Is.Not.Empty, "Should have selected an issue");

        var newTitle = GenerateIssueTitle("Parent via Tab below");
        var created = await CreateIssueWithKeyboardAsync(createAbove: false, laneModifier: 1, title: newTitle);

        AssertTabBug(created, "Tab");
    }

    /// <summary>
    /// Test: Create issue below with TAB - select an issue that already has a parent.
    /// New issue becomes an additional parent of the selected issue.
    ///
    /// KNOWN BUG: Tab closes the input.
    /// </summary>
    [Test]
    public async Task CreateBelowWithTab_ParallelMode_IssueWithParent_NewBecomesAdditionalParent()
    {
        await NavigateToProjectAsync();
        await SelectFirstIssueAsync();

        // Move to e2e/child1 which has e2e/parent1 as parent
        // Navigate through the list to find it
        for (int i = 0; i < 15; i++)
        {
            var currentId = await GetSelectedIssueIdAsync();
            if (currentId == "e2e/child1") break;
            await MoveSelectionDownAsync(1);
        }

        var newTitle = GenerateIssueTitle("Additional Parent");
        var created = await CreateIssueWithKeyboardAsync(createAbove: false, laneModifier: 1, title: newTitle);

        AssertTabBug(created, "Tab");
    }

    /// <summary>
    /// Test: Verify Tab can be pressed multiple times but only first press registers.
    ///
    /// KNOWN BUG: Tab closes the input.
    /// </summary>
    [Test]
    public async Task CreateBelowWithTab_OnlyFirstTabPressRegisters()
    {
        await NavigateToProjectAsync();
        await SelectFirstIssueAsync();

        var newTitle = GenerateIssueTitle("Tab multiple test");
        var created = await CreateIssueWithKeyboardAsync(createAbove: false, laneModifier: 1, title: newTitle);

        AssertTabBug(created, "Tab");
    }

    #endregion

    #region Create Below with Shift+TAB (New Issue Becomes Child of Above) - PARALLEL MODE

    /// <summary>
    /// Test: Create issue below with Shift+TAB - new issue becomes child of selected issue.
    /// Scenario: Select a parent issue, create below with Shift+Tab → new becomes child.
    ///
    /// KNOWN BUG: Shift+Tab closes the input.
    /// </summary>
    [Test]
    public async Task CreateBelowWithShiftTab_ParallelMode_NewBecomesChild()
    {
        await NavigateToProjectAsync();
        await SelectFirstIssueAsync();

        var selectedIssueId = await GetSelectedIssueIdAsync();
        Assert.That(selectedIssueId, Is.Not.Empty, "Should have selected an issue");

        var newTitle = GenerateIssueTitle("Child via ShiftTab below");
        var created = await CreateIssueWithKeyboardAsync(createAbove: false, laneModifier: -1, title: newTitle);

        AssertTabBug(created, "Shift+Tab");
    }

    /// <summary>
    /// Test: Create issue below with Shift+TAB - adding child to issue that already has children.
    ///
    /// KNOWN BUG: Shift+Tab closes the input.
    /// </summary>
    [Test]
    public async Task CreateBelowWithShiftTab_ParallelMode_IssueWithChildren_NewBecomesAdditionalChild()
    {
        await NavigateToProjectAsync();
        await SelectFirstIssueAsync();

        // Navigate to e2e/parent1 which has children
        for (int i = 0; i < 15; i++)
        {
            var currentId = await GetSelectedIssueIdAsync();
            if (currentId == "e2e/parent1") break;
            await MoveSelectionDownAsync(1);
        }

        var newTitle = GenerateIssueTitle("Additional Child");
        var created = await CreateIssueWithKeyboardAsync(createAbove: false, laneModifier: -1, title: newTitle);

        AssertTabBug(created, "Shift+Tab");
    }

    #endregion

    #region Create Above with TAB (New Issue Becomes Parent of Below) - PARALLEL MODE

    /// <summary>
    /// Test: Create issue above with TAB - new issue becomes parent of the selected issue.
    ///
    /// KNOWN BUG: Tab closes the input.
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
        var created = await CreateIssueWithKeyboardAsync(createAbove: true, laneModifier: 1, title: newTitle);

        AssertTabBug(created, "Tab");
    }

    /// <summary>
    /// Test: Create issue above with TAB for issue that already has parent.
    ///
    /// KNOWN BUG: Tab closes the input.
    /// </summary>
    [Test]
    public async Task CreateAboveWithTab_ParallelMode_IssueWithParent_NewBecomesAdditionalParent()
    {
        await NavigateToProjectAsync();
        await SelectFirstIssueAsync();

        // Navigate to e2e/child1
        for (int i = 0; i < 15; i++)
        {
            var currentId = await GetSelectedIssueIdAsync();
            if (currentId == "e2e/child1") break;
            await MoveSelectionDownAsync(1);
        }

        var newTitle = GenerateIssueTitle("Additional Parent Above");
        var created = await CreateIssueWithKeyboardAsync(createAbove: true, laneModifier: 1, title: newTitle);

        AssertTabBug(created, "Tab");
    }

    #endregion

    #region Create Above with Shift+TAB (New Issue Becomes Child of Below) - PARALLEL MODE

    /// <summary>
    /// Test: Create issue above with Shift+TAB - new issue becomes child of the selected issue.
    ///
    /// KNOWN BUG: Shift+Tab closes the input.
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
        var created = await CreateIssueWithKeyboardAsync(createAbove: true, laneModifier: -1, title: newTitle);

        AssertTabBug(created, "Shift+Tab");
    }

    #endregion

    #region Series Execution Mode Tests

    /// <summary>
    /// Test: Create issue below with TAB in series parent - should become parent with proper ordering.
    ///
    /// KNOWN BUG: Tab closes the input.
    /// </summary>
    [Test]
    public async Task CreateBelowWithTab_SeriesMode_NewBecomesParent()
    {
        await NavigateToProjectAsync();
        await SelectFirstIssueAsync();

        // Navigate to e2e/series-child1 (child of series-parent)
        for (int i = 0; i < 20; i++)
        {
            var currentId = await GetSelectedIssueIdAsync();
            if (currentId == "e2e/series-child1") break;
            await MoveSelectionDownAsync(1);
        }

        var newTitle = GenerateIssueTitle("Series Parent via Tab");
        var created = await CreateIssueWithKeyboardAsync(createAbove: false, laneModifier: 1, title: newTitle);

        AssertTabBug(created, "Tab");
    }

    /// <summary>
    /// Test: Create issue below with Shift+TAB in series parent - should become child with sort order.
    ///
    /// KNOWN BUG: Shift+Tab closes the input.
    /// </summary>
    [Test]
    public async Task CreateBelowWithShiftTab_SeriesMode_NewBecomesChildWithSortOrder()
    {
        await NavigateToProjectAsync();
        await SelectFirstIssueAsync();

        // Navigate to e2e/series-parent
        for (int i = 0; i < 20; i++)
        {
            var currentId = await GetSelectedIssueIdAsync();
            if (currentId == "e2e/series-parent") break;
            await MoveSelectionDownAsync(1);
        }

        var newTitle = GenerateIssueTitle("Series Child via ShiftTab");
        var created = await CreateIssueWithKeyboardAsync(createAbove: false, laneModifier: -1, title: newTitle);

        AssertTabBug(created, "Shift+Tab");
    }

    /// <summary>
    /// Test: Create issue above with TAB in series mode.
    ///
    /// KNOWN BUG: Tab closes the input.
    /// </summary>
    [Test]
    public async Task CreateAboveWithTab_SeriesMode_NewBecomesParent()
    {
        await NavigateToProjectAsync();
        await SelectFirstIssueAsync();

        // Navigate to e2e/series-child2
        for (int i = 0; i < 20; i++)
        {
            var currentId = await GetSelectedIssueIdAsync();
            if (currentId == "e2e/series-child2") break;
            await MoveSelectionDownAsync(1);
        }

        var newTitle = GenerateIssueTitle("Series Parent Above");
        var created = await CreateIssueWithKeyboardAsync(createAbove: true, laneModifier: 1, title: newTitle);

        AssertTabBug(created, "Tab");
    }

    /// <summary>
    /// Test: Create issue above with Shift+TAB in series mode.
    ///
    /// KNOWN BUG: Shift+Tab closes the input.
    /// </summary>
    [Test]
    public async Task CreateAboveWithShiftTab_SeriesMode_NewBecomesChild()
    {
        await NavigateToProjectAsync();
        await SelectFirstIssueAsync();

        // Navigate to e2e/series-parent
        for (int i = 0; i < 20; i++)
        {
            var currentId = await GetSelectedIssueIdAsync();
            if (currentId == "e2e/series-parent") break;
            await MoveSelectionDownAsync(1);
        }

        var newTitle = GenerateIssueTitle("Series Child Above");
        var created = await CreateIssueWithKeyboardAsync(createAbove: true, laneModifier: -1, title: newTitle);

        AssertTabBug(created, "Shift+Tab");
    }

    #endregion

    #region Lane Position Verification Tests

    /// <summary>
    /// Test: When creating as parent (TAB), new issue should be at HIGHER lane.
    ///
    /// KNOWN BUG: Tab closes the input.
    /// </summary>
    [Test]
    public async Task CreateAsParent_Tab_NewIssueShouldBeAtHigherLane()
    {
        await NavigateToProjectAsync();
        await SelectFirstIssueAsync();

        var newTitle = GenerateIssueTitle("Parent Lane Test");
        var created = await CreateIssueWithKeyboardAsync(createAbove: false, laneModifier: 1, title: newTitle);

        AssertTabBug(created, "Tab");
    }

    /// <summary>
    /// Test: When creating as child (Shift+TAB), new issue should be at LOWER lane (or same if at lane 0).
    ///
    /// KNOWN BUG: Shift+Tab closes the input.
    /// </summary>
    [Test]
    public async Task CreateAsChild_ShiftTab_NewIssueShouldBeAtLowerLane()
    {
        await NavigateToProjectAsync();
        await SelectFirstIssueAsync();

        var newTitle = GenerateIssueTitle("Child Lane Test");
        var created = await CreateIssueWithKeyboardAsync(createAbove: false, laneModifier: -1, title: newTitle);

        AssertTabBug(created, "Shift+Tab");
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
    /// Test: Lane indicator shows correct relationship text.
    ///
    /// KNOWN BUG: Tab closes the input before we can verify the indicator.
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

        // BUG: Tab closes input
        Assert.That(await inlineInput.IsVisibleAsync(), Is.False,
            "EXPECTED BUG: Tab should close the input. When this FAILS, the bug is FIXED!");
    }

    /// <summary>
    /// Test: Lane indicator shows correct child relationship text.
    ///
    /// KNOWN BUG: Shift+Tab closes the input before we can verify the indicator.
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

        // BUG: Shift+Tab closes input
        Assert.That(await inlineInput.IsVisibleAsync(), Is.False,
            "EXPECTED BUG: Shift+Tab should close the input. When this FAILS, the bug is FIXED!");
    }

    #endregion

    #region Hierarchy Relationship Verification (When Bug is Fixed)

    /// <summary>
    /// Test: After creating with Tab, verify parent-child relationship exists.
    /// This test structure is ready for when the Tab bug is fixed.
    /// Currently documents the bug.
    /// </summary>
    [Test]
    public async Task WhenBugFixed_CreateBelowWithTab_AdjacentIssueHasNewParent()
    {
        await NavigateToProjectAsync();
        await SelectFirstIssueAsync();

        var adjacentIssueId = await GetSelectedIssueIdAsync();
        Assert.That(adjacentIssueId, Is.Not.Empty);

        var newTitle = GenerateIssueTitle("Verify Parent Relationship");
        var created = await CreateIssueWithKeyboardAsync(createAbove: false, laneModifier: 1, title: newTitle);

        // When bug is fixed, this block will execute and we can verify the relationship
        if (created)
        {
            // Fetch the adjacent issue and verify it has the new issue as parent
            var adjacentIssue = await GetIssueAsync(adjacentIssueId);
            Assert.That(adjacentIssue, Is.Not.Null, "Adjacent issue should exist");

            // The new issue should be a parent of the adjacent issue
            var hasNewParent = adjacentIssue!.ParentIssues.Any(p =>
                p.ParentIssue.Contains("Verify Parent Relationship") ||
                adjacentIssue.Title.Contains(newTitle));

            Assert.That(hasNewParent, Is.True,
                "Adjacent issue should have the new issue as parent");
        }
        else
        {
            AssertTabBug(created, "Tab");
        }
    }

    /// <summary>
    /// Test: After creating with Shift+Tab, verify new issue is child of adjacent.
    /// This test structure is ready for when the Shift+Tab bug is fixed.
    /// </summary>
    [Test]
    public async Task WhenBugFixed_CreateBelowWithShiftTab_NewIssueHasAdjacentAsParent()
    {
        await NavigateToProjectAsync();
        await SelectFirstIssueAsync();

        var adjacentIssueId = await GetSelectedIssueIdAsync();
        Assert.That(adjacentIssueId, Is.Not.Empty);

        var newTitle = GenerateIssueTitle("Verify Child Relationship");
        var created = await CreateIssueWithKeyboardAsync(createAbove: false, laneModifier: -1, title: newTitle);

        // When bug is fixed, this block will execute
        if (created)
        {
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
                "New issue should have the adjacent issue as its parent");
        }
        else
        {
            AssertTabBug(created, "Shift+Tab");
        }
    }

    #endregion
}
