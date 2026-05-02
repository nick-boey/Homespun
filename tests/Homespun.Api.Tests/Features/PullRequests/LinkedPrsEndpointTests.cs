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

namespace Homespun.Api.Tests.Features.PullRequests;

[TestFixture]
public class LinkedPrsEndpointTests
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
        var request = new { Name = "LinkedPrs-" + Guid.NewGuid().ToString("N")[..8], DefaultBranch = "main" };
        var response = await _client.PostAsJsonAsync("/api/projects", request, JsonOptions);
        response.EnsureSuccessStatusCode();
        var project = await response.Content.ReadFromJsonAsync<Project>(JsonOptions);
        return project!.Id;
    }

    private async Task<IssueResponse> CreateIssue(string projectId, string title)
    {
        var request = new CreateIssueRequest { ProjectId = projectId, Title = title, Type = IssueType.Task };
        var response = await _client.PostAsJsonAsync("/api/issues", request, JsonOptions);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IssueResponse>(JsonOptions))!;
    }

    private async Task<PullRequest> CreateAndLinkPr(string projectId, string? issueId, int? githubNumber)
    {
        var prRequest = new CreatePullRequestRequest
        {
            ProjectId = projectId,
            Title = "test pr " + Guid.NewGuid().ToString("N")[..6],
            BranchName = "feature/" + Guid.NewGuid().ToString("N")[..8],
            Status = OpenPullRequestStatus.InDevelopment
        };
        var prResp = await _client.PostAsJsonAsync("/api/pull-requests", prRequest, JsonOptions);
        prResp.EnsureSuccessStatusCode();
        var pr = (await prResp.Content.ReadFromJsonAsync<PullRequest>(JsonOptions))!;

        var dataStore = _factory.Services.GetRequiredService<IDataStore>();
        pr.FleeceIssueId = issueId;
        pr.GitHubPRNumber = githubNumber;
        await dataStore.UpdatePullRequestAsync(pr);
        return pr;
    }

    [Test]
    public async Task EmptyProject_ReturnsEmptyMap()
    {
        var projectId = await CreateProject();

        var response = await _client.GetAsync($"/api/projects/{projectId}/linked-prs");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var map = await response.Content.ReadFromJsonAsync<Dictionary<string, LinkedPr>>(JsonOptions);
        Assert.That(map, Is.Not.Null);
        Assert.That(map, Is.Empty);
    }

    [Test]
    public async Task SingleLinkedPr_ReturnsSingleEntry()
    {
        var projectId = await CreateProject();
        var issue = await CreateIssue(projectId, "linked");
        await CreateAndLinkPr(projectId, issue.Id, githubNumber: 42);

        var response = await _client.GetAsync($"/api/projects/{projectId}/linked-prs");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var map = await response.Content.ReadFromJsonAsync<Dictionary<string, LinkedPr>>(JsonOptions);
        Assert.That(map, Is.Not.Null);
        Assert.That(map!.ContainsKey(issue.Id), Is.True);
        Assert.That(map[issue.Id].Number, Is.EqualTo(42));
    }

    [Test]
    public async Task MultipleLinkedPrs_AllReturned()
    {
        var projectId = await CreateProject();
        var i1 = await CreateIssue(projectId, "issue-1");
        var i2 = await CreateIssue(projectId, "issue-2");
        await CreateAndLinkPr(projectId, i1.Id, githubNumber: 100);
        await CreateAndLinkPr(projectId, i2.Id, githubNumber: 101);

        var response = await _client.GetAsync($"/api/projects/{projectId}/linked-prs");
        var map = (await response.Content.ReadFromJsonAsync<Dictionary<string, LinkedPr>>(JsonOptions))!;

        Assert.Multiple(() =>
        {
            Assert.That(map[i1.Id].Number, Is.EqualTo(100));
            Assert.That(map[i2.Id].Number, Is.EqualTo(101));
        });
    }

    [Test]
    public async Task PrWithoutFleeceIssueId_Excluded()
    {
        var projectId = await CreateProject();
        await CreateAndLinkPr(projectId, issueId: null, githubNumber: 200);

        var response = await _client.GetAsync($"/api/projects/{projectId}/linked-prs");
        var map = (await response.Content.ReadFromJsonAsync<Dictionary<string, LinkedPr>>(JsonOptions))!;

        Assert.That(map, Is.Empty);
    }

    [Test]
    public async Task PrWithoutGitHubPRNumber_Excluded()
    {
        var projectId = await CreateProject();
        var issue = await CreateIssue(projectId, "no-pr-num");
        await CreateAndLinkPr(projectId, issue.Id, githubNumber: null);

        var response = await _client.GetAsync($"/api/projects/{projectId}/linked-prs");
        var map = (await response.Content.ReadFromJsonAsync<Dictionary<string, LinkedPr>>(JsonOptions))!;

        Assert.That(map.ContainsKey(issue.Id), Is.False);
    }

    [Test]
    public async Task NonExistentProject_ReturnsNotFound()
    {
        var response = await _client.GetAsync("/api/projects/non-existent/linked-prs");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
