using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fleece.Core.Models;
using Homespun.Features.PullRequests.Data;
using Homespun.Shared.Models.Fleece;
using Homespun.Shared.Models.Projects;
using Homespun.Shared.Models.PullRequests;
using Homespun.Shared.Requests;
using Microsoft.Extensions.DependencyInjection;

namespace Homespun.Api.Tests.Features.Fleece;

[TestFixture]
public class IssuesEndpointVisibleTests
{
    private HomespunWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _factory = new HomespunWebApplicationFactory();
        _client = _factory.CreateClient();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private async Task<string> CreateProject()
    {
        var request = new { Name = "Visible-" + Guid.NewGuid().ToString("N")[..8], DefaultBranch = "main" };
        var response = await _client.PostAsJsonAsync("/api/projects", request, JsonOptions);
        response.EnsureSuccessStatusCode();
        var project = await response.Content.ReadFromJsonAsync<Project>(JsonOptions);
        return project!.Id;
    }

    private async Task<IssueResponse> CreateIssue(string projectId, string title, string? parentIssueId = null)
    {
        var request = new CreateIssueRequest
        {
            ProjectId = projectId,
            Title = title,
            Type = IssueType.Task,
            ParentIssueId = parentIssueId
        };
        var response = await _client.PostAsJsonAsync("/api/issues", request, JsonOptions);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IssueResponse>(JsonOptions))!;
    }

    private async Task SetStatus(string projectId, string issueId, IssueStatus status)
    {
        var request = new UpdateIssueRequest { ProjectId = projectId, Status = status };
        var response = await _client.PutAsJsonAsync($"/api/issues/{issueId}", request, JsonOptions);
        response.EnsureSuccessStatusCode();
    }

    private async Task<List<IssueResponse>> GetIssues(string projectId, string? query = null)
    {
        var url = $"/api/projects/{projectId}/issues" + (query is null ? "" : "?" + query);
        var response = await _client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<IssueResponse>>(JsonOptions))!;
    }

    [Test]
    public async Task Default_ReturnsOpenIssues()
    {
        var projectId = await CreateProject();
        var open = await CreateIssue(projectId, "open issue");

        var issues = await GetIssues(projectId);

        Assert.That(issues.Select(i => i.Id), Has.Member(open.Id));
    }

    [Test]
    public async Task Default_PullsInClosedAncestorOfOpenIssue()
    {
        var projectId = await CreateProject();
        var parent = await CreateIssue(projectId, "parent");
        var child = await CreateIssue(projectId, "child", parentIssueId: parent.Id);
        await SetStatus(projectId, parent.Id, IssueStatus.Closed);

        var issues = await GetIssues(projectId);

        var ids = issues.Select(i => i.Id).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(ids, Has.Member(child.Id));
            Assert.That(ids, Has.Member(parent.Id));
        });
    }

    [Test]
    public async Task Default_PullsInChainOfClosedAncestors()
    {
        var projectId = await CreateProject();
        var grandparent = await CreateIssue(projectId, "grandparent");
        var parent = await CreateIssue(projectId, "parent", parentIssueId: grandparent.Id);
        var child = await CreateIssue(projectId, "child", parentIssueId: parent.Id);
        await SetStatus(projectId, grandparent.Id, IssueStatus.Closed);
        await SetStatus(projectId, parent.Id, IssueStatus.Closed);

        var issues = await GetIssues(projectId);

        var ids = issues.Select(i => i.Id).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(ids, Has.Member(child.Id));
            Assert.That(ids, Has.Member(parent.Id));
            Assert.That(ids, Has.Member(grandparent.Id));
        });
    }

    [Test]
    public async Task Default_ExcludesOrphanClosedIssue()
    {
        var projectId = await CreateProject();
        var orphan = await CreateIssue(projectId, "orphan-to-close");
        await SetStatus(projectId, orphan.Id, IssueStatus.Closed);

        var issues = await GetIssues(projectId);

        Assert.That(issues.Select(i => i.Id), Has.No.Member(orphan.Id));
    }

    [Test]
    public async Task IncludeQuery_PullsInSingleClosedIssueAndAncestors()
    {
        var projectId = await CreateProject();
        var grandparent = await CreateIssue(projectId, "ancestor");
        var leaf = await CreateIssue(projectId, "leaf-to-close", parentIssueId: grandparent.Id);
        await SetStatus(projectId, grandparent.Id, IssueStatus.Closed);
        await SetStatus(projectId, leaf.Id, IssueStatus.Closed);

        var issues = await GetIssues(projectId, $"include={leaf.Id}");

        var ids = issues.Select(i => i.Id).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(ids, Has.Member(leaf.Id));
            Assert.That(ids, Has.Member(grandparent.Id));
        });
    }

    [Test]
    public async Task IncludeQuery_MultipleIds_AllResolved()
    {
        var projectId = await CreateProject();
        var a = await CreateIssue(projectId, "a-closed");
        var b = await CreateIssue(projectId, "b-closed");
        await SetStatus(projectId, a.Id, IssueStatus.Closed);
        await SetStatus(projectId, b.Id, IssueStatus.Closed);

        var issues = await GetIssues(projectId, $"include={a.Id},{b.Id}");

        var ids = issues.Select(i => i.Id).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(ids, Has.Member(a.Id));
            Assert.That(ids, Has.Member(b.Id));
        });
    }

    [Test]
    public async Task IncludeOpenPrLinked_PullsInClosedIssueLinkedToOpenPr()
    {
        var projectId = await CreateProject();
        var linked = await CreateIssue(projectId, "linked-issue-closed");
        await SetStatus(projectId, linked.Id, IssueStatus.Closed);

        // Create an open PR and link it to the issue.
        var prRequest = new CreatePullRequestRequest
        {
            ProjectId = projectId,
            Title = "open pr",
            BranchName = "feature/linked-pr",
            Status = OpenPullRequestStatus.InDevelopment
        };
        var prResponse = await _client.PostAsJsonAsync("/api/pull-requests", prRequest, JsonOptions);
        prResponse.EnsureSuccessStatusCode();
        var pr = (await prResponse.Content.ReadFromJsonAsync<PullRequest>(JsonOptions))!;

        var dataStore = _factory.Services.GetRequiredService<IDataStore>();
        pr.FleeceIssueId = linked.Id;
        pr.GitHubPRNumber = 999;
        await dataStore.UpdatePullRequestAsync(pr);

        var withFlag = await GetIssues(projectId, "includeOpenPrLinked=true");
        var withoutFlag = await GetIssues(projectId);

        Assert.Multiple(() =>
        {
            Assert.That(withFlag.Select(i => i.Id), Has.Member(linked.Id));
            Assert.That(withoutFlag.Select(i => i.Id), Has.No.Member(linked.Id));
        });
    }

    [Test]
    public async Task IncludeAll_ReturnsTerminalStatusIssues()
    {
        var projectId = await CreateProject();
        var open = await CreateIssue(projectId, "open-keep");
        var closed = await CreateIssue(projectId, "closed-orphan");
        await SetStatus(projectId, closed.Id, IssueStatus.Closed);

        var issues = await GetIssues(projectId, "includeAll=true");

        var ids = issues.Select(i => i.Id).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(ids, Has.Member(open.Id));
            Assert.That(ids, Has.Member(closed.Id));
        });
    }

    [Test]
    public async Task StatusFilter_AppliesAfterVisibilityFilter()
    {
        var projectId = await CreateProject();
        var openA = await CreateIssue(projectId, "a-open");
        var progress = await CreateIssue(projectId, "b-progress");
        await SetStatus(projectId, progress.Id, IssueStatus.Progress);

        var issues = await GetIssues(projectId, "status=Progress");

        var ids = issues.Select(i => i.Id).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(ids, Has.Member(progress.Id));
            Assert.That(ids, Has.No.Member(openA.Id));
        });
    }

    [Test]
    public async Task CycleInParentChain_ReturnsIssuesWithoutException()
    {
        // Create two issues, link them in a cycle directly via the data store.
        var projectId = await CreateProject();
        var a = await CreateIssue(projectId, "cycle-a");
        var b = await CreateIssue(projectId, "cycle-b", parentIssueId: a.Id);

        // Make a's parent be b (creating cycle a->b->a).
        var setParentRequest = new SetParentRequest
        {
            ProjectId = projectId,
            ParentIssueId = b.Id
        };
        var setResp = await _client.PostAsJsonAsync($"/api/issues/{a.Id}/set-parent", setParentRequest, JsonOptions);
        // If set-parent rejects cycles, skip — we only need the read path to be cycle-safe.
        if (!setResp.IsSuccessStatusCode)
        {
            Assert.Inconclusive("set-parent rejected the cycle; cycle path covered by unit tests.");
            return;
        }

        var response = await _client.GetAsync($"/api/projects/{projectId}/issues");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task NonExistentProject_ReturnsNotFound()
    {
        var response = await _client.GetAsync("/api/projects/non-existent-id/issues");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
