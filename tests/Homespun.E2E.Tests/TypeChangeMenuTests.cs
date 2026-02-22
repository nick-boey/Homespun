namespace Homespun.E2E.Tests;

/// <summary>
/// End-to-end tests for the type change menu functionality in TaskGraphView.
/// Verifies that clicking the type badge opens a dropdown menu to change the issue type.
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class TypeChangeMenuTests : PageTest
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
    public async Task TypeBadge_ClickOpensMenu()
    {
        // Navigate to the demo project page
        await Page.GotoAsync($"{BaseUrl}/projects/demo-project");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Wait for task graph to render
        var taskGraphRow = Page.Locator(".task-graph-row").First;
        await Expect(taskGraphRow).ToBeVisibleAsync(new() { Timeout = 10000 });

        // Verify type badge is visible
        var typeBadge = Page.Locator(".task-graph-issue-type").First;
        await Expect(typeBadge).ToBeVisibleAsync(new() { Timeout = 5000 });

        // Verify menu is not visible initially
        var typeMenu = Page.Locator(".task-graph-type-menu");
        await Expect(typeMenu).Not.ToBeVisibleAsync();

        // Click the type badge
        await typeBadge.ClickAsync();

        // Verify the menu appears with 4 type options
        await Expect(typeMenu).ToBeVisibleAsync(new() { Timeout = 5000 });

        var menuButtons = typeMenu.Locator("button");
        await Expect(menuButtons).ToHaveCountAsync(4);

        // Verify button labels
        await Expect(menuButtons.Nth(0)).ToHaveTextAsync("Bug");
        await Expect(menuButtons.Nth(1)).ToHaveTextAsync("Task");
        await Expect(menuButtons.Nth(2)).ToHaveTextAsync("Feature");
        await Expect(menuButtons.Nth(3)).ToHaveTextAsync("Chore");
    }

    [Test]
    public async Task TypeBadge_ClickingAgainClosesMenu()
    {
        // Navigate to the demo project page
        await Page.GotoAsync($"{BaseUrl}/projects/demo-project");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Wait for task graph to render
        var taskGraphRow = Page.Locator(".task-graph-row").First;
        await Expect(taskGraphRow).ToBeVisibleAsync(new() { Timeout = 10000 });

        // Get the type badge
        var typeBadge = Page.Locator(".task-graph-issue-type").First;
        var typeMenu = Page.Locator(".task-graph-type-menu");

        // Open the menu
        await typeBadge.ClickAsync();
        await Expect(typeMenu).ToBeVisibleAsync(new() { Timeout = 5000 });

        // Click badge again to close
        await typeBadge.ClickAsync();
        await Expect(typeMenu).Not.ToBeVisibleAsync(new() { Timeout = 5000 });
    }

    [Test]
    public async Task TypeMenu_SelectingTypeClosesMenu()
    {
        // Navigate to the demo project page
        await Page.GotoAsync($"{BaseUrl}/projects/demo-project");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Wait for task graph to render
        var taskGraphRow = Page.Locator(".task-graph-row").First;
        await Expect(taskGraphRow).ToBeVisibleAsync(new() { Timeout = 10000 });

        // Get the type badge
        var typeBadge = Page.Locator(".task-graph-issue-type").First;
        var typeMenu = Page.Locator(".task-graph-type-menu");

        // Open the menu
        await typeBadge.ClickAsync();
        await Expect(typeMenu).ToBeVisibleAsync(new() { Timeout = 5000 });

        // Click a type button (Bug)
        var bugButton = typeMenu.Locator("button.bug");
        await bugButton.ClickAsync();

        // Menu should close after selection
        await Expect(typeMenu).Not.ToBeVisibleAsync(new() { Timeout = 5000 });
    }

    [Test]
    public async Task TypeBadge_ClickDoesNotTriggerRowClick()
    {
        // Navigate to the demo project page
        await Page.GotoAsync($"{BaseUrl}/projects/demo-project");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Wait for task graph to render
        var taskGraphRow = Page.Locator(".task-graph-row").First;
        await Expect(taskGraphRow).ToBeVisibleAsync(new() { Timeout = 10000 });

        // Get the type badge on the first row
        var typeBadge = Page.Locator(".task-graph-issue-type").First;
        var typeMenu = Page.Locator(".task-graph-type-menu");

        // Note the current URL before clicking
        var urlBeforeClick = Page.Url;

        // Click the type badge - should open menu but NOT navigate or select the row
        await typeBadge.ClickAsync();

        // Verify menu opened
        await Expect(typeMenu).ToBeVisibleAsync(new() { Timeout = 5000 });

        // Verify the URL hasn't changed (no navigation happened)
        Assert.That(Page.Url, Is.EqualTo(urlBeforeClick));
    }
}
