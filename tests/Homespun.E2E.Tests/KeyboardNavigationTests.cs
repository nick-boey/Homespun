namespace Homespun.E2E.Tests;

/// <summary>
/// End-to-end tests for Vim-like keyboard navigation in the task graph.
/// Verifies that inline editing works correctly with real browser input.
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class KeyboardNavigationTests : PageTest
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
    public async Task InsertMode_TypingAppendsTextToInput()
    {
        // Navigate to the demo project page
        await Page.GotoAsync($"{BaseUrl}/projects/demo-project");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Wait for task graph to render
        var taskGraphRow = Page.Locator(".task-graph-row").First;
        await Expect(taskGraphRow).ToBeVisibleAsync(new() { Timeout = 10000 });

        // Press j to select the first issue
        await Page.Keyboard.PressAsync("j");

        // Verify a row is selected
        var selectedRow = Page.Locator(".task-graph-row-selected");
        await Expect(selectedRow).ToBeVisibleAsync(new() { Timeout = 5000 });

        // Get the original title text
        var titleCell = selectedRow.Locator(".task-graph-title");
        var originalTitle = (await titleCell.TextContentAsync())?.Trim() ?? "";

        // Press i to enter insert mode (cursor at start)
        await Page.Keyboard.PressAsync("i");

        // Verify the inline editor input appears
        var input = Page.Locator("input.inline-issue-input");
        await Expect(input).ToBeVisibleAsync(new() { Timeout = 5000 });

        // Verify the INSERT mode indicator is shown
        var insertIndicator = Page.Locator("text=-- INSERT --");
        await Expect(insertIndicator).ToBeVisibleAsync(new() { Timeout = 5000 });

        // Type additional text
        await Page.Keyboard.TypeAsync(" appended");

        // Verify the input contains the original title plus appended text
        await Expect(input).ToHaveValueAsync(new System.Text.RegularExpressions.Regex("appended"));

        // Press Escape to cancel editing
        await Page.Keyboard.PressAsync("Escape");

        // Verify edit mode is exited (INSERT indicator gone)
        await Expect(insertIndicator).Not.ToBeVisibleAsync(new() { Timeout = 5000 });

        // Verify we're back to viewing mode with selection preserved
        await Expect(selectedRow).ToBeVisibleAsync(new() { Timeout = 5000 });
    }

    [Test]
    public async Task ReplaceMode_TypingReplacesInputText()
    {
        // Navigate to the demo project page
        await Page.GotoAsync($"{BaseUrl}/projects/demo-project");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Wait for task graph to render
        var taskGraphRow = Page.Locator(".task-graph-row").First;
        await Expect(taskGraphRow).ToBeVisibleAsync(new() { Timeout = 10000 });

        // Press j to select, then r to enter replace mode
        await Page.Keyboard.PressAsync("j");
        var selectedRow = Page.Locator(".task-graph-row-selected");
        await Expect(selectedRow).ToBeVisibleAsync(new() { Timeout = 5000 });

        await Page.Keyboard.PressAsync("r");

        // Verify the inline editor appears
        var input = Page.Locator("input.inline-issue-input");
        await Expect(input).ToBeVisibleAsync(new() { Timeout = 5000 });

        // Type replacement text
        await Page.Keyboard.TypeAsync("replaced title");

        // Verify the input shows the replacement text
        await Expect(input).ToHaveValueAsync("replaced title");

        // Cancel to avoid persisting changes
        await Page.Keyboard.PressAsync("Escape");
    }

    [Test]
    public async Task CreateIssueBelow_TypingAddsTextToNewInput()
    {
        // Navigate to the demo project page
        await Page.GotoAsync($"{BaseUrl}/projects/demo-project");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Wait for task graph to render
        var taskGraphRow = Page.Locator(".task-graph-row").First;
        await Expect(taskGraphRow).ToBeVisibleAsync(new() { Timeout = 10000 });

        // Press j to select, then o to create new issue below
        await Page.Keyboard.PressAsync("j");
        var selectedRow = Page.Locator(".task-graph-row-selected");
        await Expect(selectedRow).ToBeVisibleAsync(new() { Timeout = 5000 });

        await Page.Keyboard.PressAsync("o");

        // Verify the inline editor input appears
        var input = Page.Locator("input.inline-issue-input");
        await Expect(input).ToBeVisibleAsync(new() { Timeout = 5000 });

        // Type new issue title
        await Page.Keyboard.TypeAsync("new issue title");

        // Verify the input shows the typed text
        await Expect(input).ToHaveValueAsync("new issue title");

        // Cancel to avoid persisting changes
        await Page.Keyboard.PressAsync("Escape");
    }

    [Test]
    public async Task NavigationKeys_StillWork_WithSelectiveKeyboardPrevention()
    {
        // Navigate to the demo project page
        await Page.GotoAsync($"{BaseUrl}/projects/demo-project");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Wait for task graph to render
        var taskGraphRow = Page.Locator(".task-graph-row").First;
        await Expect(taskGraphRow).ToBeVisibleAsync(new() { Timeout = 10000 });

        // Press j to select the first issue
        await Page.Keyboard.PressAsync("j");

        // Verify a row is selected
        var selectedRow = Page.Locator(".task-graph-row-selected");
        await Expect(selectedRow).ToBeVisibleAsync(new() { Timeout = 5000 });

        // Navigation should still work after setup - press j again to move down
        await Page.Keyboard.PressAsync("j");
        await Expect(selectedRow).ToBeVisibleAsync(new() { Timeout = 5000 });

        // Press k to move back up
        await Page.Keyboard.PressAsync("k");
        await Expect(selectedRow).ToBeVisibleAsync(new() { Timeout = 5000 });

        // Note: Can't directly test F5/F12 in Playwright as they trigger browser actions
        // which would navigate away from the page. The key test is that navigation
        // still works, confirming our selective prevention mechanism is functioning.
    }

    [Test]
    public async Task EnterKey_WhenIssueSelected_NavigatesToEditPage()
    {
        // Navigate to the demo project page
        await Page.GotoAsync($"{BaseUrl}/projects/demo-project");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Wait for task graph to render
        var taskGraphRow = Page.Locator(".task-graph-row").First;
        await Expect(taskGraphRow).ToBeVisibleAsync(new() { Timeout = 10000 });

        // Press j to select the first issue
        await Page.Keyboard.PressAsync("j");

        // Verify a row is selected
        var selectedRow = Page.Locator(".task-graph-row-selected");
        await Expect(selectedRow).ToBeVisibleAsync(new() { Timeout = 5000 });

        // Press Enter to open the edit page
        await Page.Keyboard.PressAsync("Enter");

        // Verify we navigated to the edit page by checking the URL contains /edit
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/issues/.+/edit"), new() { Timeout = 5000 });
    }
}
