using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Homespun.Shared.Models.Projects;
using Homespun.Shared.Requests;

namespace Homespun.Api.Tests.Features;

[TestFixture]
public class IssuesRelationshipApiTests
{
    private HomespunWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;
    private string _projectId = null!;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _factory = new HomespunWebApplicationFactory();
        _client = _factory.CreateClient();

        // Create a project to use in tests
        var createProjectRequest = new { Name = "RelationshipTest", DefaultBranch = "main" };
        var projectResponse = await _client.PostAsJsonAsync("/api/projects", createProjectRequest, JsonOptions);
        projectResponse.EnsureSuccessStatusCode();
        var project = await projectResponse.Content.ReadFromJsonAsync<Project>(JsonOptions);
        _projectId = project!.Id;
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Test]
    public async Task RemoveParent_NonExistentChild_ReturnsNotFound()
    {
        var request = new RemoveParentRequest { ProjectId = _projectId, ParentIssueId = "some-parent" };
        var response = await _client.PostAsJsonAsync("/api/issues/non-existent/remove-parent", request, JsonOptions);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task RemoveParent_NonExistentProject_ReturnsNotFound()
    {
        var request = new RemoveParentRequest { ProjectId = "bad-project", ParentIssueId = "some-parent" };
        var response = await _client.PostAsJsonAsync("/api/issues/some-child/remove-parent", request, JsonOptions);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task RemoveAllParents_NonExistentIssue_ReturnsNotFound()
    {
        var request = new RemoveAllParentsRequest { ProjectId = _projectId };
        var response = await _client.PostAsJsonAsync("/api/issues/non-existent/remove-all-parents", request, JsonOptions);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task RemoveAllParents_NonExistentProject_ReturnsNotFound()
    {
        var request = new RemoveAllParentsRequest { ProjectId = "bad-project" };
        var response = await _client.PostAsJsonAsync("/api/issues/some-issue/remove-all-parents", request, JsonOptions);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
