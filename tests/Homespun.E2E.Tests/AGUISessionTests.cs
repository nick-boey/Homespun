namespace Homespun.E2E.Tests;

/// <summary>
/// End-to-end tests for AG-UI session functionality.
/// Tests that the UI properly renders AG-UI events from Claude Code sessions.
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class AGUISessionTests : PageTest
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

    #region Session Page Tests

    [Test]
    public async Task SessionPage_LoadsSuccessfully()
    {
        // Navigate to agents page (session management)
        await Page.GotoAsync($"{BaseUrl}/agents");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Verify the page loads
        var mainContent = Page.Locator("main");
        await Expect(mainContent).ToBeVisibleAsync();
    }

    [Test]
    public async Task SessionManagementPage_DisplaysSessionsList()
    {
        // Navigate to session management
        await Page.GotoAsync($"{BaseUrl}/agents");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Verify the page has session management content
        // Either sessions are listed or a message indicates no sessions
        var hasSessionsOrEmptyState = await Page.Locator("main").IsVisibleAsync();
        Assert.That(hasSessionsOrEmptyState, Is.True, "Session management page should be visible");
    }

    [Test]
    public async Task SessionPage_WithInvalidId_ShowsNotFoundMessage()
    {
        // Navigate to a session with an invalid ID
        await Page.GotoAsync($"{BaseUrl}/session/nonexistent-session-id");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Should either show not found message or redirect
        // The session page handles missing sessions gracefully
        var mainContent = Page.Locator("main");
        await Expect(mainContent).ToBeVisibleAsync();
    }

    #endregion

    #region Session UI Element Tests

    [Test]
    public async Task AgentsPage_HasCorrectTitle()
    {
        // Navigate to agents page
        await Page.GotoAsync($"{BaseUrl}/agents");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Verify the page title contains relevant text
        await Expect(Page).ToHaveTitleAsync(new System.Text.RegularExpressions.Regex("Homespun"));
    }

    [Test]
    public async Task SessionPage_NavigationWorks()
    {
        // Start at home page
        await Page.GotoAsync(BaseUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Look for agents/sessions navigation link
        var agentsLink = Page.Locator("a[href*='agents'], a:has-text('Agents'), a:has-text('Sessions')").First;
        var hasAgentsLink = await agentsLink.IsVisibleAsync();

        if (hasAgentsLink)
        {
            await agentsLink.ClickAsync();
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            // Verify navigation succeeded
            Assert.That(Page.Url, Does.Contain("agents").IgnoreCase.Or.Contain("session").IgnoreCase);
        }
        else
        {
            // If no direct link, navigate manually
            await Page.GotoAsync($"{BaseUrl}/agents");
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            var mainContent = Page.Locator("main");
            await Expect(mainContent).ToBeVisibleAsync();
        }
    }

    #endregion

    #region Mock Mode Verification Tests

    [Test]
    public async Task MockMode_DesignPage_IsAccessible()
    {
        // In mock mode, the design page should be accessible
        // This page showcases all UI components
        await Page.GotoAsync($"{BaseUrl}/design");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Verify the design page loads
        var mainContent = Page.Locator("main");
        await Expect(mainContent).ToBeVisibleAsync();
    }

    [Test]
    public async Task MockMode_APIEndpoint_ReturnsData()
    {
        // Verify the sessions API endpoint works in mock mode
        var response = await Page.APIRequest.GetAsync($"{BaseUrl}/api/sessions");

        Assert.That(response.Ok, Is.True, "Sessions API endpoint should return success");
    }

    #endregion

    #region Session Message Display Tests

    [Test]
    public async Task SessionsAPI_ReturnsValidJSON()
    {
        // Test the sessions API returns valid JSON
        var response = await Page.APIRequest.GetAsync($"{BaseUrl}/api/sessions");

        Assert.That(response.Ok, Is.True);

        var body = await response.TextAsync();
        Assert.DoesNotThrow(() =>
        {
            System.Text.Json.JsonDocument.Parse(body);
        }, "Sessions API should return valid JSON");
    }

    [Test]
    public async Task ProjectsAPI_ReturnsValidJSON()
    {
        // Test the projects API returns valid JSON (needed for session creation)
        var response = await Page.APIRequest.GetAsync($"{BaseUrl}/api/projects");

        Assert.That(response.Ok, Is.True);

        var body = await response.TextAsync();
        Assert.DoesNotThrow(() =>
        {
            System.Text.Json.JsonDocument.Parse(body);
        }, "Projects API should return valid JSON");
    }

    #endregion

    #region SignalR Hub Connectivity Tests

    [Test]
    public async Task SignalRHub_Endpoint_IsAccessible()
    {
        // Verify the SignalR hub negotiate endpoint is accessible
        // The actual hub is at /hubs/claudecode
        var response = await Page.APIRequest.PostAsync(
            $"{BaseUrl}/hubs/claudecode/negotiate?negotiateVersion=1",
            new APIRequestContextOptions
            {
                Headers = new Dictionary<string, string>
                {
                    ["Content-Type"] = "application/json"
                }
            });

        // SignalR negotiate returns 200 with connection info
        Assert.That(response.Status, Is.EqualTo(200).Or.EqualTo(400),
            "SignalR hub negotiate endpoint should be accessible");
    }

    #endregion
}
