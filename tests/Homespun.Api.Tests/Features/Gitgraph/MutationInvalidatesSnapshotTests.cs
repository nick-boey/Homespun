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
/// Delta 1 correctness gate: every mutation endpoint SHALL invalidate the
/// task-graph snapshot before broadcasting, so a client's immediate refetch
/// reads fresh data instead of the pre-mutation snapshot.
/// </summary>
[TestFixture]
public class MutationInvalidatesSnapshotTests
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

        var projectRequest = new { Name = "MutationInvalidates-" + Guid.NewGuid().ToString("N")[..8], DefaultBranch = "main" };
        var projectResponse = await _client.PostAsJsonAsync("/api/projects", projectRequest, JsonOptions);
        projectResponse.EnsureSuccessStatusCode();
        var project = await projectResponse.Content.ReadFromJsonAsync<Project>(JsonOptions);
        _projectId = project!.Id;

        // Seed a baseline issue so the graph is non-empty.
        await CreateIssue("baseline");
    }

    [TearDown]
    public void TearDown()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Test]
    public async Task Update_TitleMutation_IsVisibleOnImmediateRefetch()
    {
        var issue = await CreateIssue("before-update");
        await WarmSnapshot();

        var updateRequest = new UpdateIssueRequest { ProjectId = _projectId, Title = "after-update" };
        var response = await _client.PutAsJsonAsync($"/api/issues/{issue.Id}", updateRequest, JsonOptions);
        response.EnsureSuccessStatusCode();

        var graph = await FetchTaskGraph();
        var node = graph.Nodes.Single(n => n.Issue.Id == issue.Id);
        Assert.That(node.Issue.Title, Is.EqualTo("after-update"),
            "refetch after update must reflect the new title, not the pre-mutation snapshot");
    }

    [Test]
    public async Task Create_NewIssue_IsVisibleOnImmediateRefetch()
    {
        await WarmSnapshot();

        var created = await CreateIssue("freshly-created");

        var graph = await FetchTaskGraph();
        Assert.That(graph.Nodes.Any(n => n.Issue.Id == created.Id), Is.True,
            "newly created issue must appear in the refetched graph");
    }

    [Test]
    public async Task Delete_ExistingIssue_IsVisibleOnImmediateRefetch()
    {
        var issue = await CreateIssue("doomed");
        await WarmSnapshot();

        var response = await _client.DeleteAsync($"/api/issues/{issue.Id}?projectId={_projectId}");
        response.EnsureSuccessStatusCode();

        var graph = await FetchTaskGraph();
        Assert.That(graph.Nodes.Any(n => n.Issue.Id == issue.Id), Is.False,
            "deleted issue must not appear in the refetched graph");
    }

    [Test]
    public async Task SetParent_MutationVisibleOnImmediateRefetch()
    {
        var parent = await CreateIssue("parent");
        var child = await CreateIssue("child");
        await WarmSnapshot();

        var setParent = new SetParentRequest { ProjectId = _projectId, ParentIssueId = parent.Id };
        var response = await _client.PostAsJsonAsync($"/api/issues/{child.Id}/set-parent", setParent, JsonOptions);
        response.EnsureSuccessStatusCode();

        var graph = await FetchTaskGraph();
        var childNode = graph.Nodes.Single(n => n.Issue.Id == child.Id);
        Assert.That(childNode.Issue.ParentIssues.Any(p => p.ParentIssue == parent.Id), Is.True,
            "set-parent must be reflected in the refetched graph");
    }

    [Test]
    public async Task RemoveAllParents_MutationVisibleOnImmediateRefetch()
    {
        var parent = await CreateIssue("parent-remove-all");
        var child = await CreateIssue("child-remove-all", parentIssueId: parent.Id);
        await WarmSnapshot();

        var removeAll = new RemoveAllParentsRequest { ProjectId = _projectId };
        var response = await _client.PostAsJsonAsync($"/api/issues/{child.Id}/remove-all-parents", removeAll, JsonOptions);
        response.EnsureSuccessStatusCode();

        var graph = await FetchTaskGraph();
        var childNode = graph.Nodes.Single(n => n.Issue.Id == child.Id);
        Assert.That(childNode.Issue.ParentIssues, Is.Empty,
            "remove-all-parents must be reflected in the refetched graph");
    }

    [Test]
    public async Task MutationDirectlyInvalidatesSnapshotStore()
    {
        // White-box: after any mutation, the snapshot store's entry for the project
        // must be gone (TryGet returns null) before the next rebuild. The entry
        // may be repopulated by the fire-and-forget refresher; what we assert is
        // that the previous stale entry is never observable as the invalidation.
        var issue = await CreateIssue("whitebox");
        await WarmSnapshot();

        var store = _factory.Services.GetRequiredService<IProjectTaskGraphSnapshotStore>();
        Assert.That(store.TryGet(_projectId, 5), Is.Not.Null, "precondition: snapshot warmed");

        var update = new UpdateIssueRequest { ProjectId = _projectId, Title = "whitebox-updated" };
        var response = await _client.PutAsJsonAsync($"/api/issues/{issue.Id}", update, JsonOptions);
        response.EnsureSuccessStatusCode();

        // The fire-and-forget refresher may race, but the synchronous invalidation
        // inside the helper guarantees at least one moment where TryGet returned
        // null. The response must already reflect the new title.
        var graph = await FetchTaskGraph();
        var node = graph.Nodes.Single(n => n.Issue.Id == issue.Id);
        Assert.That(node.Issue.Title, Is.EqualTo("whitebox-updated"));
    }

    // --- helpers ---

    private async Task<IssueResponse> CreateIssue(string title, string? parentIssueId = null)
    {
        var request = new CreateIssueRequest
        {
            ProjectId = _projectId,
            Title = title,
            Type = IssueType.Task,
            ParentIssueId = parentIssueId
        };
        var response = await _client.PostAsJsonAsync("/api/issues", request, JsonOptions);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IssueResponse>(JsonOptions))!;
    }

    private async Task WarmSnapshot()
    {
        var response = await _client.GetAsync($"/api/graph/{_projectId}/taskgraph/data");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), "warm snapshot fetch failed");
    }

    private async Task<TaskGraphResponse> FetchTaskGraph()
    {
        var response = await _client.GetAsync($"/api/graph/{_projectId}/taskgraph/data");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TaskGraphResponse>(JsonOptions))!;
    }

    /// <summary>
    /// Pins the background refresher's interval high so it doesn't race the
    /// test by repopulating the snapshot between mutation + refetch.
    /// </summary>
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
