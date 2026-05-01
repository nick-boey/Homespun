using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Homespun.Features.Fleece.Services;
using Homespun.Features.OpenSpec.Services;
using Fleece.Core.Models;
using Homespun.Shared.Models.Fleece;
using Homespun.Shared.Models.Projects;
using Homespun.Shared.Requests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Homespun.Api.Tests.Features.Gitgraph;

/// <summary>
/// Tier 5 end-to-end — asserts the per-project snapshot store short-circuits the
/// enricher on repeat reads and that <c>POST /api/graph/{projectId}/refresh</c>
/// busts the snapshot so the next read rebuilds.
/// </summary>
[TestFixture]
public class TaskGraphSnapshotEndToEndTests
{
    private SnapshotFactory _factory = null!;
    private HttpClient _client = null!;
    private CountingEnricher _counter = null!;
    private string _projectId = null!;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _counter = new CountingEnricher();
        _factory = new SnapshotFactory(_counter);
        _client = _factory.CreateClient();

        var request = new { Name = "TaskGraphSnapshot-" + Guid.NewGuid().ToString("N")[..8], DefaultBranch = "main" };
        var response = await _client.PostAsJsonAsync("/api/projects", request, JsonOptions);
        response.EnsureSuccessStatusCode();
        var project = await response.Content.ReadFromJsonAsync<Project>(JsonOptions);
        _projectId = project!.Id;

        // Seed at least one issue so BuildEnhancedTaskGraphAsync returns a
        // non-null response (empty Fleece projects would otherwise 404).
        var issueRequest = new CreateIssueRequest
        {
            ProjectId = _projectId,
            Title = "snapshot-test-seed",
            Type = IssueType.Task
        };
        var issueResponse = await _client.PostAsJsonAsync("/api/issues", issueRequest, JsonOptions);
        issueResponse.EnsureSuccessStatusCode();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Test, Order(1)]
    public async Task First_Call_Populates_Snapshot_And_Invokes_Enricher()
    {
        _counter.Reset();

        var response = await _client.GetAsync($"/api/graph/{_projectId}/taskgraph/data");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(_counter.Count, Is.EqualTo(1),
            "Cold-path request should invoke the enricher exactly once");
    }

    [Test, Order(2)]
    public async Task Second_Call_Hits_Snapshot_Without_Invoking_Enricher()
    {
        _counter.Reset();

        var response = await _client.GetAsync($"/api/graph/{_projectId}/taskgraph/data");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(_counter.Count, Is.EqualTo(0),
            "Cached snapshot must short-circuit the enricher");
    }

    [Test, Order(3)]
    public async Task Refresh_Endpoint_Invalidates_Snapshot_And_Forces_Rebuild()
    {
        // Force the snapshot-invalidation side-effect via the refresh endpoint.
        var refresh = await _client.PostAsync($"/api/graph/{_projectId}/refresh", content: null);
        Assert.That(refresh.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        _counter.Reset();

        var response = await _client.GetAsync($"/api/graph/{_projectId}/taskgraph/data");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(_counter.Count, Is.EqualTo(1),
            "Refresh must have invalidated the snapshot forcing a recompute");
    }

    private sealed class SnapshotFactory(CountingEnricher counter) : HomespunWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            // Pin snapshot interval high so the background refresher doesn't race
            // the test and repopulate the cache on its own tick.
            builder.UseSetting("TaskGraphSnapshot:Enabled", "true");
            builder.UseSetting("TaskGraphSnapshot:RefreshIntervalSeconds", "3600");
            builder.UseSetting("TaskGraphSnapshot:IdleEvictionMinutes", "60");

            builder.ConfigureServices(services =>
            {
                // Replace the enricher with a counting wrapper so we can assert
                // invocation counts from the test without reflecting on DI.
                for (var i = services.Count - 1; i >= 0; i--)
                {
                    if (services[i].ServiceType == typeof(IIssueGraphOpenSpecEnricher))
                    {
                        services.RemoveAt(i);
                    }
                }

                services.AddScoped<IIssueGraphOpenSpecEnricher>(sp => new CountingEnricherProxy(counter));
            });
        }
    }

    private sealed class CountingEnricher
    {
        private int _count;
        public int Count => _count;
        public void Reset() => Interlocked.Exchange(ref _count, 0);
        public void Increment() => Interlocked.Increment(ref _count);
    }

    private sealed class CountingEnricherProxy(CountingEnricher counter) : IIssueGraphOpenSpecEnricher
    {
        public Task EnrichAsync(
            string projectId,
            TaskGraphResponse response,
            BranchResolutionContext? branchContext = null,
            CancellationToken ct = default)
        {
            counter.Increment();
            return Task.CompletedTask;
        }

        public Task<Dictionary<string, Homespun.Shared.Models.OpenSpec.IssueOpenSpecState>> GetOpenSpecStatesAsync(
            string projectId,
            IReadOnlyCollection<string> issueIds,
            BranchResolutionContext? branchContext = null,
            CancellationToken ct = default)
            => Task.FromResult(new Dictionary<string, Homespun.Shared.Models.OpenSpec.IssueOpenSpecState>());

        public Task<List<Homespun.Shared.Models.OpenSpec.SnapshotOrphan>> GetMainOrphanChangesAsync(
            string projectId,
            BranchResolutionContext? branchContext = null,
            CancellationToken ct = default)
            => Task.FromResult(new List<Homespun.Shared.Models.OpenSpec.SnapshotOrphan>());
    }
}
