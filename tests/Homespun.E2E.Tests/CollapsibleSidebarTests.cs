namespace Homespun.E2E.Tests;

/// <summary>
/// End-to-end tests for the inline issue detail expansion.
/// Verifies that issue rows expand only on double-click, not on keyboard navigation.
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class CollapsibleSidebarTests : PageTest
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

    [Test]
    public async Task InlineDetail_IsCollapsedByDefault()
    {
        // Navigate to the demo project page
        await Page.GotoAsync($"{BaseUrl}/projects/demo-project");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Wait for task graph to render
        var taskGraphRow = Page.Locator(".task-graph-row").First;
        await Expect(taskGraphRow).ToBeVisibleAsync(new() { Timeout = 10000 });

        // Verify no rows are expanded by default
        var expandedRow = Page.Locator(".task-graph-row-expanded");
        await Expect(expandedRow).Not.ToBeVisibleAsync();

        // Verify no inline detail panel is visible
        var inlineDetail = Page.Locator("[data-testid='inline-issue-detail']");
        await Expect(inlineDetail).Not.ToBeVisibleAsync();
    }

    [Test]
    public async Task KeyboardNavigation_DoesNotExpandRow()
    {
        // Navigate to the demo project page
        await Page.GotoAsync($"{BaseUrl}/projects/demo-project");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Wait for task graph to render
        var taskGraphRow = Page.Locator(".task-graph-row").First;
        await Expect(taskGraphRow).ToBeVisibleAsync(new() { Timeout = 10000 });

        // Press j to select the first issue using keyboard
        await Page.Keyboard.PressAsync("j");

        // Verify a row is selected (keyboard navigation works)
        var selectedRow = Page.Locator(".task-graph-row-selected");
        await Expect(selectedRow).ToBeVisibleAsync(new() { Timeout = 5000 });

        // Verify no row is expanded
        var expandedRow = Page.Locator(".task-graph-row-expanded");
        await Expect(expandedRow).Not.ToBeVisibleAsync();

        // Verify no inline detail panel is visible
        var inlineDetail = Page.Locator("[data-testid='inline-issue-detail']");
        await Expect(inlineDetail).Not.ToBeVisibleAsync();
    }

    [Test]
    public async Task DoubleClickingIssue_ExpandsRow()
    {
        // Navigate to the demo project page
        await Page.GotoAsync($"{BaseUrl}/projects/demo-project");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Wait for task graph to render
        var taskGraphRow = Page.Locator("[data-testid='task-graph-issue-row']").First;
        await Expect(taskGraphRow).ToBeVisibleAsync(new() { Timeout = 10000 });

        // Double-click on an issue row
        await taskGraphRow.DblClickAsync();

        // Verify the row is expanded
        var expandedRow = Page.Locator(".task-graph-row-expanded");
        await Expect(expandedRow).ToBeVisibleAsync(new() { Timeout = 5000 });

        // Verify inline detail panel is shown
        var inlineDetail = Page.Locator("[data-testid='inline-issue-detail']");
        await Expect(inlineDetail).ToBeVisibleAsync(new() { Timeout = 5000 });
    }

    [Test]
    public async Task DoubleClickingExpandedIssue_CollapsesRow()
    {
        // Navigate to the demo project page
        await Page.GotoAsync($"{BaseUrl}/projects/demo-project");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Wait for task graph to render and double-click an issue to expand
        var taskGraphRow = Page.Locator("[data-testid='task-graph-issue-row']").First;
        await Expect(taskGraphRow).ToBeVisibleAsync(new() { Timeout = 10000 });
        await taskGraphRow.DblClickAsync();

        // Verify row is expanded
        var expandedRow = Page.Locator(".task-graph-row-expanded");
        await Expect(expandedRow).ToBeVisibleAsync(new() { Timeout = 5000 });

        // Double-click again to collapse
        await taskGraphRow.DblClickAsync();

        // Verify the row is collapsed
        await Expect(expandedRow).Not.ToBeVisibleAsync(new() { Timeout = 5000 });

        // Verify inline detail panel is hidden
        var inlineDetail = Page.Locator("[data-testid='inline-issue-detail']");
        await Expect(inlineDetail).Not.ToBeVisibleAsync(new() { Timeout = 5000 });
    }

    [Test]
    public async Task EscapeKey_CollapsesExpandedRow()
    {
        // Navigate to the demo project page
        await Page.GotoAsync($"{BaseUrl}/projects/demo-project");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Wait for task graph to render and double-click an issue to expand
        var taskGraphRow = Page.Locator("[data-testid='task-graph-issue-row']").First;
        await Expect(taskGraphRow).ToBeVisibleAsync(new() { Timeout = 10000 });
        await taskGraphRow.DblClickAsync();

        // Verify row is expanded
        var expandedRow = Page.Locator(".task-graph-row-expanded");
        await Expect(expandedRow).ToBeVisibleAsync(new() { Timeout = 5000 });

        // Press Escape to collapse
        await Page.Keyboard.PressAsync("Escape");

        // Verify the row is collapsed
        await Expect(expandedRow).Not.ToBeVisibleAsync(new() { Timeout = 5000 });

        // Verify inline detail panel is hidden
        var inlineDetail = Page.Locator("[data-testid='inline-issue-detail']");
        await Expect(inlineDetail).Not.ToBeVisibleAsync(new() { Timeout = 5000 });
    }

    [Test]
    public async Task KeyboardNavigation_DoesNotChangeExpandedContent_WhenExpanded()
    {
        // Navigate to the demo project page
        await Page.GotoAsync($"{BaseUrl}/projects/demo-project");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Wait for task graph to render
        var issueRows = Page.Locator("[data-testid='task-graph-issue-row']");
        await Expect(issueRows.First).ToBeVisibleAsync(new() { Timeout = 10000 });

        // Double-click on the first issue to expand
        var firstRow = issueRows.First;
        await firstRow.DblClickAsync();

        // Verify row is expanded and inline detail is shown
        var expandedRow = Page.Locator(".task-graph-row-expanded");
        await Expect(expandedRow).ToBeVisibleAsync(new() { Timeout = 5000 });

        // Get the issue ID shown in the inline detail panel
        var inlineDetail = Page.Locator("[data-testid='inline-issue-detail']");
        var expandedIssueId = await firstRow.GetAttributeAsync("data-issue-id");

        // Use keyboard to navigate to a different issue
        await Page.Keyboard.PressAsync("j");

        // Verify the original row is still expanded (expansion doesn't change on navigation)
        var stillExpandedRow = Page.Locator($"[data-issue-id='{expandedIssueId}'].task-graph-row-expanded");
        await Expect(stillExpandedRow).ToBeVisibleAsync(new() { Timeout = 5000 });

        // Verify inline detail panel is still visible
        await Expect(inlineDetail).ToBeVisibleAsync(new() { Timeout = 5000 });
    }
}
