namespace Homespun.E2E.Tests;

/// <summary>
/// End-to-end tests for the collapsible issue details sidebar.
/// Verifies that the sidebar opens only on click, not on keyboard navigation.
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
    public async Task Sidebar_IsCollapsedByDefault()
    {
        // Navigate to the demo project page
        await Page.GotoAsync($"{BaseUrl}/projects/demo-project");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Wait for task graph to render
        var taskGraphRow = Page.Locator(".task-graph-row").First;
        await Expect(taskGraphRow).ToBeVisibleAsync(new() { Timeout = 10000 });

        // Verify the sidebar exists but is not open
        var sidebar = Page.Locator(".detail-sidebar");
        await Expect(sidebar).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("open"));
    }

    [Test]
    public async Task KeyboardNavigation_DoesNotOpenSidebar()
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

        // Verify the sidebar is still NOT open
        var sidebar = Page.Locator(".detail-sidebar");
        await Expect(sidebar).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("open"));
    }

    [Test]
    public async Task ClickingIssue_OpensSidebar()
    {
        // Navigate to the demo project page
        await Page.GotoAsync($"{BaseUrl}/projects/demo-project");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Wait for task graph to render
        var taskGraphRow = Page.Locator("[data-testid='task-graph-issue-row']").First;
        await Expect(taskGraphRow).ToBeVisibleAsync(new() { Timeout = 10000 });

        // Click on an issue row
        await taskGraphRow.ClickAsync();

        // Verify the sidebar opens
        var sidebar = Page.Locator(".detail-sidebar.open");
        await Expect(sidebar).ToBeVisibleAsync(new() { Timeout = 5000 });

        // Verify IssueDetailPanel is shown in the sidebar
        var issuePanel = sidebar.Locator(".issue-detail-panel");
        await Expect(issuePanel).ToBeVisibleAsync(new() { Timeout = 5000 });
    }

    [Test]
    public async Task ClosingPanel_CollapsesSidebar()
    {
        // Navigate to the demo project page
        await Page.GotoAsync($"{BaseUrl}/projects/demo-project");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Wait for task graph to render and click an issue to open sidebar
        var taskGraphRow = Page.Locator("[data-testid='task-graph-issue-row']").First;
        await Expect(taskGraphRow).ToBeVisibleAsync(new() { Timeout = 10000 });
        await taskGraphRow.ClickAsync();

        // Verify sidebar is open
        var sidebar = Page.Locator(".detail-sidebar.open");
        await Expect(sidebar).ToBeVisibleAsync(new() { Timeout = 5000 });

        // Click the close button on the panel
        var closeButton = sidebar.Locator("button[title='Close panel']");
        await closeButton.ClickAsync();

        // Verify the sidebar collapses
        await Expect(sidebar).Not.ToBeVisibleAsync(new() { Timeout = 5000 });
    }

    [Test]
    public async Task EscapeKey_ClosesSidebar()
    {
        // Navigate to the demo project page
        await Page.GotoAsync($"{BaseUrl}/projects/demo-project");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Wait for task graph to render and click an issue to open sidebar
        var taskGraphRow = Page.Locator("[data-testid='task-graph-issue-row']").First;
        await Expect(taskGraphRow).ToBeVisibleAsync(new() { Timeout = 10000 });
        await taskGraphRow.ClickAsync();

        // Verify sidebar is open
        var sidebar = Page.Locator(".detail-sidebar.open");
        await Expect(sidebar).ToBeVisibleAsync(new() { Timeout = 5000 });

        // Press Escape to close the sidebar
        await Page.Keyboard.PressAsync("Escape");

        // Verify the sidebar collapses
        await Expect(sidebar).Not.ToBeVisibleAsync(new() { Timeout = 5000 });
    }

    [Test]
    public async Task KeyboardNavigation_DoesNotChangeSidebarContent_WhenOpen()
    {
        // Navigate to the demo project page
        await Page.GotoAsync($"{BaseUrl}/projects/demo-project");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Wait for task graph to render
        var issueRows = Page.Locator("[data-testid='task-graph-issue-row']");
        await Expect(issueRows.First).ToBeVisibleAsync(new() { Timeout = 10000 });

        // Click on the first issue to open sidebar
        var firstRow = issueRows.First;
        await firstRow.ClickAsync();

        // Verify sidebar is open
        var sidebar = Page.Locator(".detail-sidebar.open");
        await Expect(sidebar).ToBeVisibleAsync(new() { Timeout = 5000 });

        // Get the issue ID shown in the sidebar
        var issueIdBadge = sidebar.Locator(".badge:text-matches('[A-Za-z0-9]{6}')").First;
        var clickedIssueId = await issueIdBadge.TextContentAsync();

        // Use keyboard to navigate to a different issue
        await Page.Keyboard.PressAsync("j");

        // The sidebar should still show the originally clicked issue
        var currentIssueId = await issueIdBadge.TextContentAsync();
        Assert.That(currentIssueId, Is.EqualTo(clickedIssueId),
            "Sidebar should continue showing the clicked issue, not the keyboard-selected one");
    }
}
