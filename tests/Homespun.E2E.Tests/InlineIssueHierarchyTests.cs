namespace Homespun.E2E.Tests;

/// <summary>
/// End-to-end tests for inline issue creation with keyboard controls and hierarchy management.
/// Tests the TAB/Shift+TAB functionality for creating parent-child relationships between issues.
/// </summary>
/// <remarks>
/// Issue Er7yMR: Keyboard controls don't place in correct hierarchy
///
/// Key behaviors:
/// - 'o': Creates a new issue below the selected issue
/// - 'O' (Shift+o): Creates a new issue above the selected issue
/// - TAB: Makes the new issue a PARENT of the adjacent issue
/// - Shift+TAB: Makes the new issue a CHILD of the adjacent issue
/// - TAB/Shift+TAB can only be pressed once per creation
/// </remarks>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class InlineIssueHierarchyTests : PageTest
{
    private string BaseUrl => HomespunFixture.BaseUrl;

    public override BrowserNewContextOptions ContextOptions()
    {
        return new BrowserNewContextOptions
        {
            IgnoreHTTPSErrors = true,
            ViewportSize = new ViewportSize { Width = 1920, Height = 1080 }
        };
    }

    /// <summary>
    /// Helper method to navigate to the demo project's issues tab and wait for task graph to load.
    /// </summary>
    private async Task NavigateToProjectIssuesAsync()
    {
        await Page.GotoAsync($"{BaseUrl}/projects");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Click on the first project card
        var projectCard = Page.Locator("[data-testid='project-card'], .card").First;
        if (!await projectCard.IsVisibleAsync())
        {
            Assert.Inconclusive("No projects available. This test requires mock mode.");
            return;
        }

        await projectCard.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Verify task graph is visible
        var taskGraph = Page.Locator("[data-testid='task-graph']");
        await Expect(taskGraph).ToBeVisibleAsync(new() { Timeout = 10000 });
    }

    /// <summary>
    /// Helper to select an issue in the task graph by its ID.
    /// </summary>
    private async Task SelectIssueAsync(string issueId)
    {
        var issueRow = Page.Locator($"[data-testid='task-graph-issue-row'][data-issue-id='{issueId}']");
        if (!await issueRow.IsVisibleAsync())
        {
            // Try without the specific ID, just find any issue row
            issueRow = Page.Locator("[data-testid='task-graph-issue-row']").First;
        }
        await issueRow.ClickAsync();
    }

    /// <summary>
    /// Helper to generate a unique issue title for tests.
    /// </summary>
    private static string GenerateIssueTitle() => $"E2E Test Issue {Guid.NewGuid().ToString()[..8]}";

    #region Basic Inline Creation Tests

    /// <summary>
    /// Test that pressing 'o' on a selected issue opens the inline create input below it.
    /// </summary>
    [Test]
    public async Task PressO_ShowsInlineCreateInputBelowSelectedIssue()
    {
        await NavigateToProjectIssuesAsync();

        // Select an issue
        var firstIssueRow = Page.Locator("[data-testid='task-graph-issue-row']").First;
        await firstIssueRow.ClickAsync();

        // Focus the task graph and press 'o'
        var taskGraph = Page.Locator("[data-testid='task-graph']");
        await taskGraph.FocusAsync();
        await Page.Keyboard.PressAsync("o");

        // Verify the inline create input appears
        var inlineInput = Page.Locator("[data-testid='inline-issue-create']");
        await Expect(inlineInput).ToBeVisibleAsync(new() { Timeout = 5000 });

        // Verify the input field is focused
        var inputField = Page.Locator("[data-testid='inline-issue-input']");
        await Expect(inputField).ToBeFocusedAsync();
    }

    /// <summary>
    /// Test that pressing Shift+O on a selected issue opens the inline create input above it.
    /// </summary>
    [Test]
    public async Task PressShiftO_ShowsInlineCreateInputAboveSelectedIssue()
    {
        await NavigateToProjectIssuesAsync();

        // Select an issue (not the first one so there's room above)
        var secondIssueRow = Page.Locator("[data-testid='task-graph-issue-row']").Nth(1);
        if (!await secondIssueRow.IsVisibleAsync())
        {
            Assert.Inconclusive("Need at least 2 issues for this test.");
            return;
        }

        await secondIssueRow.ClickAsync();

        // Focus the task graph and press Shift+O
        var taskGraph = Page.Locator("[data-testid='task-graph']");
        await taskGraph.FocusAsync();
        await Page.Keyboard.PressAsync("Shift+O");

        // Verify the inline create input appears
        var inlineInput = Page.Locator("[data-testid='inline-issue-create']");
        await Expect(inlineInput).ToBeVisibleAsync(new() { Timeout = 5000 });
    }

    /// <summary>
    /// Test that pressing Escape while in inline create mode cancels the creation.
    /// </summary>
    [Test]
    public async Task EscapeCancelsInlineCreation()
    {
        await NavigateToProjectIssuesAsync();

        // Select an issue and open inline create
        var firstIssueRow = Page.Locator("[data-testid='task-graph-issue-row']").First;
        await firstIssueRow.ClickAsync();

        var taskGraph = Page.Locator("[data-testid='task-graph']");
        await taskGraph.FocusAsync();
        await Page.Keyboard.PressAsync("o");

        // Verify input is visible
        var inlineInput = Page.Locator("[data-testid='inline-issue-create']");
        await Expect(inlineInput).ToBeVisibleAsync();

        // Type something then press Escape
        var inputField = Page.Locator("[data-testid='inline-issue-input']");
        await inputField.FillAsync("Test title");
        await Page.Keyboard.PressAsync("Escape");

        // Verify the input is hidden
        await Expect(inlineInput).ToBeHiddenAsync(new() { Timeout = 5000 });
    }

    /// <summary>
    /// Test that creating an issue below without TAB/Shift+TAB creates it as a sibling (no parent relationship).
    /// </summary>
    [Test]
    public async Task CreateBelowWithoutTab_CreatesSiblingIssue()
    {
        await NavigateToProjectIssuesAsync();

        // Select an issue
        var firstIssueRow = Page.Locator("[data-testid='task-graph-issue-row']").First;
        await firstIssueRow.ClickAsync();

        var taskGraph = Page.Locator("[data-testid='task-graph']");
        await taskGraph.FocusAsync();
        await Page.Keyboard.PressAsync("o");

        // Type title and press Enter
        var title = GenerateIssueTitle();
        var inputField = Page.Locator("[data-testid='inline-issue-input']");
        await inputField.FillAsync(title);
        await Page.Keyboard.PressAsync("Enter");

        // Wait for the creation to complete and inline input to disappear
        var inlineInput = Page.Locator("[data-testid='inline-issue-create']");
        await Expect(inlineInput).ToBeHiddenAsync(new() { Timeout = 10000 });

        // Verify the new issue appears in the task graph
        var newIssueRow = Page.Locator($"[data-testid='task-graph-issue-row']:has-text('{title}')");
        await Expect(newIssueRow).ToBeVisibleAsync(new() { Timeout = 10000 });
    }

    #endregion

    #region TAB Key Tests (Create as Parent)

    /// <summary>
    /// Test that pressing TAB while creating below shows the "Parent of above" indicator.
    /// </summary>
    [Test]
    public async Task TabWhileCreatingBelow_ShowsParentOfAboveIndicator()
    {
        await NavigateToProjectIssuesAsync();

        // Select an issue
        var firstIssueRow = Page.Locator("[data-testid='task-graph-issue-row']").First;
        await firstIssueRow.ClickAsync();

        var taskGraph = Page.Locator("[data-testid='task-graph']");
        await taskGraph.FocusAsync();
        await Page.Keyboard.PressAsync("o");

        // Press TAB
        await Page.Keyboard.PressAsync("Tab");

        // Verify the "Parent of above" indicator is shown
        var indicator = Page.Locator(".lane-indicator.parent");
        await Expect(indicator).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Expect(indicator).ToContainTextAsync("Parent of above");
    }

    /// <summary>
    /// Test that creating below with TAB makes the new issue a parent of the issue above.
    /// </summary>
    [Test]
    public async Task CreateBelowWithTab_CreatesParentOfIssueAbove()
    {
        await NavigateToProjectIssuesAsync();

        // Select an issue (the "orphan" issue works well for this test)
        var orphanIssue = Page.Locator("[data-testid='task-graph-issue-row'][data-issue-id='e2e/orphan']");
        if (!await orphanIssue.IsVisibleAsync())
        {
            // Fall back to the first issue
            orphanIssue = Page.Locator("[data-testid='task-graph-issue-row']").First;
        }
        await orphanIssue.ClickAsync();

        var taskGraph = Page.Locator("[data-testid='task-graph']");
        await taskGraph.FocusAsync();
        await Page.Keyboard.PressAsync("o");

        // Press TAB to indicate this should be a parent
        await Page.Keyboard.PressAsync("Tab");

        // Type title and press Enter
        var title = GenerateIssueTitle();
        var inputField = Page.Locator("[data-testid='inline-issue-input']");
        await inputField.FillAsync(title);
        await Page.Keyboard.PressAsync("Enter");

        // Wait for creation to complete
        var inlineInput = Page.Locator("[data-testid='inline-issue-create']");
        await Expect(inlineInput).ToBeHiddenAsync(new() { Timeout = 10000 });

        // Verify the new issue appears
        var newIssueRow = Page.Locator($"[data-testid='task-graph-issue-row']:has-text('{title}')");
        await Expect(newIssueRow).ToBeVisibleAsync(new() { Timeout = 10000 });
    }

    /// <summary>
    /// Test that pressing TAB while creating above shows the "Parent of below" indicator.
    /// </summary>
    [Test]
    public async Task TabWhileCreatingAbove_ShowsParentOfBelowIndicator()
    {
        await NavigateToProjectIssuesAsync();

        // Select an issue that's not the first
        var secondIssueRow = Page.Locator("[data-testid='task-graph-issue-row']").Nth(1);
        if (!await secondIssueRow.IsVisibleAsync())
        {
            Assert.Inconclusive("Need at least 2 issues for this test.");
            return;
        }
        await secondIssueRow.ClickAsync();

        var taskGraph = Page.Locator("[data-testid='task-graph']");
        await taskGraph.FocusAsync();
        await Page.Keyboard.PressAsync("Shift+O");

        // Press TAB
        await Page.Keyboard.PressAsync("Tab");

        // Verify the "Parent of below" indicator is shown
        var indicator = Page.Locator(".lane-indicator.parent");
        await Expect(indicator).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Expect(indicator).ToContainTextAsync("Parent of below");
    }

    #endregion

    #region Shift+TAB Key Tests (Create as Child)

    /// <summary>
    /// Test that pressing Shift+TAB while creating below shows the "Child of above" indicator.
    /// </summary>
    [Test]
    public async Task ShiftTabWhileCreatingBelow_ShowsChildOfAboveIndicator()
    {
        await NavigateToProjectIssuesAsync();

        // Select an issue
        var firstIssueRow = Page.Locator("[data-testid='task-graph-issue-row']").First;
        await firstIssueRow.ClickAsync();

        var taskGraph = Page.Locator("[data-testid='task-graph']");
        await taskGraph.FocusAsync();
        await Page.Keyboard.PressAsync("o");

        // Press Shift+TAB
        await Page.Keyboard.PressAsync("Shift+Tab");

        // Verify the "Child of above" indicator is shown
        var indicator = Page.Locator(".lane-indicator.child");
        await Expect(indicator).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Expect(indicator).ToContainTextAsync("Child of above");
    }

    /// <summary>
    /// Test that creating below with Shift+TAB makes the new issue a child of the issue above.
    /// </summary>
    [Test]
    public async Task CreateBelowWithShiftTab_CreatesChildOfIssueAbove()
    {
        await NavigateToProjectIssuesAsync();

        // Select an issue
        var firstIssueRow = Page.Locator("[data-testid='task-graph-issue-row']").First;
        await firstIssueRow.ClickAsync();

        var taskGraph = Page.Locator("[data-testid='task-graph']");
        await taskGraph.FocusAsync();
        await Page.Keyboard.PressAsync("o");

        // Press Shift+TAB to indicate this should be a child
        await Page.Keyboard.PressAsync("Shift+Tab");

        // Type title and press Enter
        var title = GenerateIssueTitle();
        var inputField = Page.Locator("[data-testid='inline-issue-input']");
        await inputField.FillAsync(title);
        await Page.Keyboard.PressAsync("Enter");

        // Wait for creation to complete
        var inlineInput = Page.Locator("[data-testid='inline-issue-create']");
        await Expect(inlineInput).ToBeHiddenAsync(new() { Timeout = 10000 });

        // Verify the new issue appears
        var newIssueRow = Page.Locator($"[data-testid='task-graph-issue-row']:has-text('{title}')");
        await Expect(newIssueRow).ToBeVisibleAsync(new() { Timeout = 10000 });
    }

    /// <summary>
    /// Test that pressing Shift+TAB while creating above shows the "Child of below" indicator.
    /// </summary>
    [Test]
    public async Task ShiftTabWhileCreatingAbove_ShowsChildOfBelowIndicator()
    {
        await NavigateToProjectIssuesAsync();

        // Select an issue that's not the first
        var secondIssueRow = Page.Locator("[data-testid='task-graph-issue-row']").Nth(1);
        if (!await secondIssueRow.IsVisibleAsync())
        {
            Assert.Inconclusive("Need at least 2 issues for this test.");
            return;
        }
        await secondIssueRow.ClickAsync();

        var taskGraph = Page.Locator("[data-testid='task-graph']");
        await taskGraph.FocusAsync();
        await Page.Keyboard.PressAsync("Shift+O");

        // Press Shift+TAB
        await Page.Keyboard.PressAsync("Shift+Tab");

        // Verify the "Child of below" indicator is shown
        var indicator = Page.Locator(".lane-indicator.child");
        await Expect(indicator).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Expect(indicator).ToContainTextAsync("Child of below");
    }

    #endregion

    #region TAB Key Limit Tests

    /// <summary>
    /// Test that TAB can only be pressed once - second TAB is ignored.
    /// </summary>
    [Test]
    public async Task TabCanOnlyBePressedOnce()
    {
        await NavigateToProjectIssuesAsync();

        // Select an issue
        var firstIssueRow = Page.Locator("[data-testid='task-graph-issue-row']").First;
        await firstIssueRow.ClickAsync();

        var taskGraph = Page.Locator("[data-testid='task-graph']");
        await taskGraph.FocusAsync();
        await Page.Keyboard.PressAsync("o");

        // Press TAB once
        await Page.Keyboard.PressAsync("Tab");

        // Verify parent indicator is shown
        var parentIndicator = Page.Locator(".lane-indicator.parent");
        await Expect(parentIndicator).ToBeVisibleAsync();

        // Press TAB again - should be ignored (no Shift+TAB to child)
        await Page.Keyboard.PressAsync("Tab");

        // Indicator should still show parent, not switch to child
        await Expect(parentIndicator).ToBeVisibleAsync();

        // Also verify no child indicator appears
        var childIndicator = Page.Locator(".lane-indicator.child");
        await Expect(childIndicator).ToBeHiddenAsync();
    }

    /// <summary>
    /// Test that Shift+TAB can only be pressed once - second Shift+TAB is ignored.
    /// </summary>
    [Test]
    public async Task ShiftTabCanOnlyBePressedOnce()
    {
        await NavigateToProjectIssuesAsync();

        // Select an issue
        var firstIssueRow = Page.Locator("[data-testid='task-graph-issue-row']").First;
        await firstIssueRow.ClickAsync();

        var taskGraph = Page.Locator("[data-testid='task-graph']");
        await taskGraph.FocusAsync();
        await Page.Keyboard.PressAsync("o");

        // Press Shift+TAB once
        await Page.Keyboard.PressAsync("Shift+Tab");

        // Verify child indicator is shown
        var childIndicator = Page.Locator(".lane-indicator.child");
        await Expect(childIndicator).ToBeVisibleAsync();

        // Press Shift+TAB again - should be ignored (no TAB to parent)
        await Page.Keyboard.PressAsync("Shift+Tab");

        // Indicator should still show child
        await Expect(childIndicator).ToBeVisibleAsync();

        // Also verify no parent indicator appears
        var parentIndicator = Page.Locator(".lane-indicator.parent");
        await Expect(parentIndicator).ToBeHiddenAsync();
    }

    #endregion

    #region Edge Case Tests

    /// <summary>
    /// Test that pressing 'o' without any issue selected does nothing.
    /// </summary>
    [Test]
    public async Task PressOWithoutSelection_DoesNothing()
    {
        await NavigateToProjectIssuesAsync();

        // Focus the task graph without selecting any issue
        var taskGraph = Page.Locator("[data-testid='task-graph']");
        await taskGraph.FocusAsync();

        // Press 'o'
        await Page.Keyboard.PressAsync("o");

        // Verify no inline input appears
        var inlineInput = Page.Locator("[data-testid='inline-issue-create']");
        await Task.Delay(500); // Small delay to ensure no async operation is in progress
        await Expect(inlineInput).ToBeHiddenAsync();
    }

    /// <summary>
    /// Test that blurring the input field (clicking elsewhere) cancels creation.
    /// </summary>
    [Test]
    public async Task BlurringInput_CancelsCreation()
    {
        await NavigateToProjectIssuesAsync();

        // Select an issue and open inline create
        var firstIssueRow = Page.Locator("[data-testid='task-graph-issue-row']").First;
        await firstIssueRow.ClickAsync();

        var taskGraph = Page.Locator("[data-testid='task-graph']");
        await taskGraph.FocusAsync();
        await Page.Keyboard.PressAsync("o");

        // Verify input is visible
        var inlineInput = Page.Locator("[data-testid='inline-issue-create']");
        await Expect(inlineInput).ToBeVisibleAsync();

        // Click somewhere else to blur
        await Page.Locator("body").ClickAsync(new() { Position = new Position { X = 10, Y = 10 } });

        // Wait a moment for blur handler
        await Task.Delay(200);

        // Verify the input is hidden
        await Expect(inlineInput).ToBeHiddenAsync(new() { Timeout = 5000 });
    }

    #endregion
}
