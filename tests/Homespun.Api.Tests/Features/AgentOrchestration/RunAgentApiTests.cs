using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Homespun.Shared.Models.Sessions;
using Homespun.Shared.Requests;
using Microsoft.AspNetCore.Hosting;

namespace Homespun.Api.Tests.Features.AgentOrchestration;

/// <summary>
/// Integration tests for <c>POST /api/issues/{issueId}/run</c>.
///
/// These cover the FI-1 gap: the previous suite never asserted that the
/// endpoint returns 202 Accepted with the expected envelope, nor that a
/// double-dispatch is rejected with 409 by the atomic <c>IAgentStartupTracker</c>.
///
/// Mock-mode seeded data is required because the run-agent pipeline does
/// project + issue lookups before queueing background work. We use a dedicated
/// factory that re-enables <c>MockMode:SeedData</c>.
/// </summary>
[TestFixture]
public class RunAgentApiTests
{
    private SeededFactory _factory = null!;
    private HttpClient _client = null!;

    private const string ProjectId = "demo-project";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _factory = new SeededFactory();
        _client = _factory.CreateClient();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Test]
    public async Task RunAgent_ReturnsAccepted_ForValidIssue()
    {
        // ISSUE-013 is an unparented, Open issue in the seed dataset that no other
        // test in this fixture targets — picking it isolates this test's
        // IAgentStartupTracker state from siblings.
        const string issueId = "ISSUE-013";

        var request = new RunAgentRequest
        {
            ProjectId = ProjectId,
            Mode = SessionMode.Plan,
        };

        using var response = await _client.PostAsJsonAsync(
            $"/api/issues/{issueId}/run",
            request,
            JsonOptions);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted),
            "Expected 202 Accepted from the dispatch endpoint — the background pipeline is fire-and-forget.");

        var body = await response.Content.ReadFromJsonAsync<RunAgentAcceptedResponse>(JsonOptions);

        Assert.That(body, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(body!.IssueId, Is.EqualTo(issueId));
            Assert.That(body.BranchName, Is.Not.Empty);
            Assert.That(body.Message, Is.Not.Empty);
        });
    }

    [Test]
    public async Task RunAgent_ReturnsConflict_OnDoubleDispatch()
    {
        // ISSUE-011 — distinct from the happy-path test above so the two cases
        // don't interfere through the in-memory startup tracker.
        const string issueId = "ISSUE-011";

        var request = new RunAgentRequest
        {
            ProjectId = ProjectId,
            Mode = SessionMode.Plan,
        };

        using var first = await _client.PostAsJsonAsync(
            $"/api/issues/{issueId}/run", request, JsonOptions);
        Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));

        // Second dispatch within the startup window must be rejected by the
        // atomic IAgentStartupTracker.TryMarkAsStarting guard.
        using var second = await _client.PostAsJsonAsync(
            $"/api/issues/{issueId}/run", request, JsonOptions);

        Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));

        var body = await second.Content.ReadFromJsonAsync<AgentAlreadyRunningResponse>(JsonOptions);
        Assert.That(body, Is.Not.Null);
        Assert.That(body!.Message, Is.Not.Empty);
    }

    [Test]
    public async Task RunAgent_ReturnsNotFound_WhenProjectMissing()
    {
        var request = new RunAgentRequest
        {
            ProjectId = "no-such-project",
            Mode = SessionMode.Plan,
        };

        using var response = await _client.PostAsJsonAsync(
            "/api/issues/ISSUE-013/run", request, JsonOptions);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task RunAgent_ReturnsNotFound_WhenIssueMissing()
    {
        var request = new RunAgentRequest
        {
            ProjectId = ProjectId,
            Mode = SessionMode.Plan,
        };

        using var response = await _client.PostAsJsonAsync(
            "/api/issues/no-such-issue/run", request, JsonOptions);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    private sealed class SeededFactory : HomespunWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("MockMode:SeedData", "true");
        }
    }
}
