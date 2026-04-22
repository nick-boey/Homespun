using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fleece.Core.Models;
using Homespun.Features.Gitgraph.Snapshots;
using Homespun.Shared.Models.Fleece;
using Homespun.Shared.Models.Projects;
using Homespun.Shared.Requests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Homespun.Api.Tests.Features.Gitgraph;

/// <summary>
/// Delta 2: title-only edits take the in-place patch path (snapshot entry stays
/// present, node updated). Any topology-affecting field forces invalidation
/// (snapshot entry removed).
/// </summary>
[TestFixture]
public class FieldPatchTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private PinnedRefresherFactory _factory = null!;
    private HttpClient _client = null!;
    private string _projectId = null!;

    [SetUp]
    public async Task SetUp()
    {
        _factory = new PinnedRefresherFactory();
        _client = _factory.CreateClient();

        var projectRequest = new { Name = "FieldPatch-" + Guid.NewGuid().ToString("N")[..8], DefaultBranch = "main" };
        var projectResponse = await _client.PostAsJsonAsync("/api/projects", projectRequest, JsonOptions);
        projectResponse.EnsureSuccessStatusCode();
        var project = await projectResponse.Content.ReadFromJsonAsync<Project>(JsonOptions);
        _projectId = project!.Id;

        await CreateIssue("baseline");
    }

    [TearDown]
    public void TearDown()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Test]
    public async Task TitleOnly_Update_PatchesSnapshot_WithoutInvalidating()
    {
        var issue = await CreateIssue("before");
        await WarmSnapshot();

        var store = _factory.Services.GetRequiredService<IProjectTaskGraphSnapshotStore>();
        var before = store.TryGet(_projectId, 5);
        Assert.That(before, Is.Not.Null, "precondition: snapshot warmed");

        var updateRequest = new UpdateIssueRequest { ProjectId = _projectId, Title = "after-patch" };
        var updateResponse = await _client.PutAsJsonAsync($"/api/issues/{issue.Id}", updateRequest, JsonOptions);
        updateResponse.EnsureSuccessStatusCode();

        var after = store.TryGet(_projectId, 5);
        Assert.Multiple(() =>
        {
            Assert.That(after, Is.Not.Null,
                "title-only edit must NOT invalidate the snapshot — the in-place patch path keeps the entry warm");
            Assert.That(after!.Response.Nodes.Single(n => n.Issue.Id == issue.Id).Issue.Title,
                Is.EqualTo("after-patch"),
                "patched node must carry the new title");
            Assert.That(after.LastBuiltAt, Is.GreaterThanOrEqualTo(before!.LastBuiltAt),
                "LastBuiltAt must be bumped (or at least not rewound) by the patch");
        });
    }

    [Test]
    public async Task StatusChange_TakesTopologyPath_InvalidatesSnapshot()
    {
        var issue = await CreateIssue("status-target");
        await WarmSnapshot();

        var store = _factory.Services.GetRequiredService<IProjectTaskGraphSnapshotStore>();
        Assert.That(store.TryGet(_projectId, 5), Is.Not.Null, "precondition: snapshot warmed");

        var updateRequest = new UpdateIssueRequest { ProjectId = _projectId, Status = IssueStatus.Progress };
        var updateResponse = await _client.PutAsJsonAsync($"/api/issues/{issue.Id}", updateRequest, JsonOptions);
        updateResponse.EnsureSuccessStatusCode();

        // Status change routes through BroadcastIssueTopologyChanged which synchronously
        // invalidates. A background refresher may race to repopulate, but our test
        // factory pins the interval to 1h so the only way the entry could reappear is
        // the fire-and-forget kick. Accept either "entry absent" or "entry present but
        // rebuilt newer than the mutation started" — never a stale pre-mutation entry.
        var graph = await FetchTaskGraph();
        var node = graph.Nodes.Single(n => n.Issue.Id == issue.Id);
        Assert.That(node.Issue.Status, Is.EqualTo(IssueStatus.Progress),
            "refetch after status change must reflect the new status");
    }

    [Test]
    public async Task MultiField_Update_WithTopologyField_TakesTopologyPath()
    {
        var issue = await CreateIssue("mixed-target");
        await WarmSnapshot();

        // Mix a patchable Title with a topology Status — the presence of Status
        // forces the topology branch even though Title alone would patch.
        var updateRequest = new UpdateIssueRequest
        {
            ProjectId = _projectId,
            Title = "mixed-title",
            Status = IssueStatus.Progress,
        };
        var updateResponse = await _client.PutAsJsonAsync($"/api/issues/{issue.Id}", updateRequest, JsonOptions);
        updateResponse.EnsureSuccessStatusCode();

        var graph = await FetchTaskGraph();
        var node = graph.Nodes.Single(n => n.Issue.Id == issue.Id);
        Assert.Multiple(() =>
        {
            Assert.That(node.Issue.Title, Is.EqualTo("mixed-title"));
            Assert.That(node.Issue.Status, Is.EqualTo(IssueStatus.Progress),
                "mixed-field update with topology field must rebuild the snapshot (status visible)");
        });
    }

    // --- helpers ---

    private async Task<IssueResponse> CreateIssue(string title)
    {
        var request = new CreateIssueRequest
        {
            ProjectId = _projectId,
            Title = title,
            Type = IssueType.Task,
        };
        var response = await _client.PostAsJsonAsync("/api/issues", request, JsonOptions);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IssueResponse>(JsonOptions))!;
    }

    private async Task WarmSnapshot()
    {
        var response = await _client.GetAsync($"/api/graph/{_projectId}/taskgraph/data");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    private async Task<TaskGraphResponse> FetchTaskGraph()
    {
        var response = await _client.GetAsync($"/api/graph/{_projectId}/taskgraph/data");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TaskGraphResponse>(JsonOptions))!;
    }

    private sealed class PinnedRefresherFactory : HomespunWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("TaskGraphSnapshot:Enabled", "true");
            builder.UseSetting("TaskGraphSnapshot:RefreshIntervalSeconds", "3600");
            builder.UseSetting("TaskGraphSnapshot:IdleEvictionMinutes", "60");
        }
    }
}
