using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fleece.Core.Models;
using Homespun.Shared.Models.Fleece;
using Homespun.Shared.Models.OpenSpec;
using Homespun.Shared.Models.Projects;
using Homespun.Shared.Requests;

namespace Homespun.Api.Tests.Features.OpenSpec;

[TestFixture]
public class OpenSpecStatesEndpointTests
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
        var request = new { Name = "OSStates-" + Guid.NewGuid().ToString("N")[..8], DefaultBranch = "main" };
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

    [Test]
    public async Task EmptyProject_ReturnsEmptyMap()
    {
        var projectId = await CreateProject();

        var response = await _client.GetAsync($"/api/projects/{projectId}/openspec-states");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var map = await response.Content.ReadFromJsonAsync<Dictionary<string, IssueOpenSpecState>>(JsonOptions);
        Assert.That(map, Is.Not.Null);
        Assert.That(map, Is.Empty);
    }

    [Test]
    public async Task IssueWithoutOpenSpecChange_ReturnsValidStateOrNoEntry()
    {
        var projectId = await CreateProject();
        var issue = await CreateIssue(projectId, "no-clone");

        var response = await _client.GetAsync($"/api/projects/{projectId}/openspec-states?issues={issue.Id}");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var map = (await response.Content.ReadFromJsonAsync<Dictionary<string, IssueOpenSpecState>>(JsonOptions))!;
        // Either the issue has no entry (no clone scanned) or it resolves to a valid state.
        if (map.TryGetValue(issue.Id, out var state))
        {
            Assert.That(state.BranchState, Is.AnyOf(BranchPresence.None, BranchPresence.Exists, BranchPresence.WithChange));
        }
    }

    [Test]
    public async Task IssuesQueryParam_ScopesToSubset()
    {
        var projectId = await CreateProject();
        var a = await CreateIssue(projectId, "a");
        var b = await CreateIssue(projectId, "b");

        // Pass only a's id; expect b not in the result map (since the query param scopes the scan).
        var response = await _client.GetAsync($"/api/projects/{projectId}/openspec-states?issues={a.Id}");
        var map = (await response.Content.ReadFromJsonAsync<Dictionary<string, IssueOpenSpecState>>(JsonOptions))!;

        Assert.That(map.ContainsKey(b.Id), Is.False);
    }

    [Test]
    public async Task NonExistentProject_ReturnsNotFound()
    {
        var response = await _client.GetAsync("/api/projects/non-existent/openspec-states");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
