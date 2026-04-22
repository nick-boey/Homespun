using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Homespun.Shared.Models.Fleece;
using Homespun.Shared.Models.OpenSpec;
using Microsoft.AspNetCore.Hosting;

namespace Homespun.Api.Tests.Features.OpenSpec;

/// <summary>
/// End-to-end coverage of the mock-mode openspec seeding pipeline:
/// the seeder writes per-branch fixtures, MockGitCloneService materialises clone dirs,
/// and the BranchStateResolverService → IssueGraphOpenSpecEnricher chain surfaces them
/// on the <c>/api/graph/{projectId}/taskgraph/data</c> response.
/// </summary>
[TestFixture]
public class MockSeededOpenSpecTaskGraphTests
{
    private SeededFactory _factory = null!;
    private HttpClient _client = null!;

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
    public async Task TaskGraph_Issue006_HasLinkedChangeWithPhases()
    {
        var response = await _client.GetAsync("/api/graph/demo-project/taskgraph/data");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var graph = await response.Content.ReadFromJsonAsync<TaskGraphResponse>(JsonOptions);
        Assert.That(graph, Is.Not.Null);
        Assert.That(graph!.OpenSpecStates, Contains.Key("ISSUE-006"),
            "ISSUE-006 should appear in the OpenSpecStates dictionary");

        var state = graph.OpenSpecStates["ISSUE-006"];
        Assert.That(state.BranchState, Is.EqualTo(BranchPresence.WithChange));
        Assert.That(state.Phases, Is.Not.Empty,
            "Linked in-progress change should expose parsed phases from tasks.md");
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
