namespace Homespun.E2E.Tests;

/// <summary>
/// End-to-end tests for CTRL+Enter save behavior on the IssueEdit page.
/// These tests verify that description changes are saved correctly when
/// CTRL+Enter is pressed without losing focus from the description field.
/// </summary>
/// <remarks>
/// Regression tests for issue wc4DW1: CTRL+Enter behaviour still doesn't save
/// descriptions while editing.
///
/// Root cause: The HandleKeyDown method used a fire-and-forget pattern that
/// caused a race condition with Blazor's binding cycle.
/// </remarks>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class IssueEditCtrlEnterTests : PageTest
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
    /// Verifies that typing in the description field and pressing CTRL+Enter
    /// saves the description correctly (without needing to click elsewhere first).
    /// </summary>
    [Test]
    public async Task IssueEdit_CtrlEnterSavesDescription_WhenFocusInDescriptionField()
    {
        // Arrange - Navigate to projects page and find an issue to edit
        await Page.GotoAsync($"{BaseUrl}/projects");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Find the first project card and click on it
        var projectCard = Page.Locator("[data-testid='project-card'], .card").First;
        var projectExists = await projectCard.IsVisibleAsync();

        if (!projectExists)
        {
            Assert.Inconclusive("No projects available in test environment. This test requires mock mode with seeded data.");
            return;
        }

        await projectCard.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Find an issue to edit - look for issue links or edit buttons
        var issueLink = Page.Locator("[data-testid='issue-edit'], a[href*='/edit'], button:has-text('Edit')").First;
        var issueExists = await issueLink.IsVisibleAsync();

        if (!issueExists)
        {
            Assert.Inconclusive("No issues available in test project. This test requires mock mode with seeded data.");
            return;
        }

        await issueLink.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Verify we're on the edit page
        await Expect(Page.Locator("h1:has-text('Edit Issue'), .page-title")).ToBeVisibleAsync();

        // Arrange - Generate a unique description to verify save
        var uniqueDescription = $"E2E Test Description - CTRL+Enter - {Guid.NewGuid():N}";

        // Act - Type in the description field (without clicking elsewhere)
        var descriptionField = Page.Locator("#description, textarea[id='description']");
        await Expect(descriptionField).ToBeVisibleAsync();

        // Clear any existing content and type new description
        await descriptionField.ClearAsync();
        await descriptionField.FillAsync(uniqueDescription);

        // Ensure focus is still on the description field
        await descriptionField.FocusAsync();

        // Press CTRL+Enter to save (without clicking elsewhere)
        await Page.Keyboard.PressAsync("Control+Enter");

        // Wait for navigation (save should redirect to project page)
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Assert - Verify redirect occurred (away from edit page)
        var currentUrl = Page.Url;
        Assert.That(currentUrl, Does.Not.Contain("/edit"),
            "After CTRL+Enter save, should navigate away from edit page");
    }

    /// <summary>
    /// Verifies that Meta+Enter (Cmd+Enter on Mac) also saves the description.
    /// </summary>
    [Test]
    public async Task IssueEdit_MetaEnterSavesDescription_ForMacSupport()
    {
        // Arrange - Navigate to projects page
        await Page.GotoAsync($"{BaseUrl}/projects");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Find the first project card
        var projectCard = Page.Locator("[data-testid='project-card'], .card").First;
        var projectExists = await projectCard.IsVisibleAsync();

        if (!projectExists)
        {
            Assert.Inconclusive("No projects available in test environment.");
            return;
        }

        await projectCard.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Find an issue to edit
        var issueLink = Page.Locator("[data-testid='issue-edit'], a[href*='/edit'], button:has-text('Edit')").First;
        var issueExists = await issueLink.IsVisibleAsync();

        if (!issueExists)
        {
            Assert.Inconclusive("No issues available in test project.");
            return;
        }

        await issueLink.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Type in the description field
        var descriptionField = Page.Locator("#description, textarea[id='description']");
        await Expect(descriptionField).ToBeVisibleAsync();

        var uniqueDescription = $"E2E Test - Meta+Enter - {Guid.NewGuid():N}";
        await descriptionField.ClearAsync();
        await descriptionField.FillAsync(uniqueDescription);
        await descriptionField.FocusAsync();

        // Act - Press Meta+Enter (Cmd+Enter on Mac)
        await Page.Keyboard.PressAsync("Meta+Enter");

        // Wait for navigation
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Assert
        var currentUrl = Page.Url;
        Assert.That(currentUrl, Does.Not.Contain("/edit"),
            "After Meta+Enter save, should navigate away from edit page");
    }

    /// <summary>
    /// Verifies that regular Enter key does NOT trigger save (allows newlines in textarea).
    /// </summary>
    [Test]
    public async Task IssueEdit_EnterAlone_DoesNotSave_AllowsNewlines()
    {
        // Arrange - Navigate to issue edit page
        await Page.GotoAsync($"{BaseUrl}/projects");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var projectCard = Page.Locator("[data-testid='project-card'], .card").First;
        if (!await projectCard.IsVisibleAsync())
        {
            Assert.Inconclusive("No projects available in test environment.");
            return;
        }

        await projectCard.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var issueLink = Page.Locator("[data-testid='issue-edit'], a[href*='/edit'], button:has-text('Edit')").First;
        if (!await issueLink.IsVisibleAsync())
        {
            Assert.Inconclusive("No issues available in test project.");
            return;
        }

        await issueLink.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var descriptionField = Page.Locator("#description, textarea[id='description']");
        await Expect(descriptionField).ToBeVisibleAsync();

        // Clear and type first line
        await descriptionField.ClearAsync();
        await descriptionField.FillAsync("Line 1");
        await descriptionField.FocusAsync();

        // Store current URL
        var urlBeforeEnter = Page.Url;

        // Act - Press Enter alone (should add newline, not save)
        await Page.Keyboard.PressAsync("Enter");

        // Type second line
        await descriptionField.PressSequentiallyAsync("Line 2");

        // Short delay to ensure no navigation happened
        await Task.Delay(500);

        // Assert - Should still be on edit page (Enter alone doesn't save)
        var currentUrl = Page.Url;
        Assert.That(currentUrl, Is.EqualTo(urlBeforeEnter),
            "Enter alone should not trigger save/navigation");

        // Verify the textarea contains both lines (newline was inserted)
        var textareaValue = await descriptionField.InputValueAsync();
        Assert.That(textareaValue, Does.Contain("Line 1"),
            "First line should be present");
        Assert.That(textareaValue, Does.Contain("Line 2"),
            "Second line should be present after Enter key");
    }
}
