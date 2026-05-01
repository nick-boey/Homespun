using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Homespun.Shared.Models.OpenSpec;
using Homespun.Shared.Models.Projects;

namespace Homespun.Api.Tests.Features.OpenSpec;

[TestFixture]
public class OrphanChangesEndpointTests
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
        var request = new { Name = "Orphans-" + Guid.NewGuid().ToString("N")[..8], DefaultBranch = "main" };
        var response = await _client.PostAsJsonAsync("/api/projects", request, JsonOptions);
        response.EnsureSuccessStatusCode();
        var project = await response.Content.ReadFromJsonAsync<Project>(JsonOptions);
        return project!.Id;
    }

    [Test]
    public async Task EmptyProject_ReturnsEmptyList()
    {
        var projectId = await CreateProject();

        var response = await _client.GetAsync($"/api/projects/{projectId}/orphan-changes");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var orphans = await response.Content.ReadFromJsonAsync<List<SnapshotOrphan>>(JsonOptions);
        Assert.That(orphans, Is.Not.Null);
        Assert.That(orphans, Is.Empty);
    }

    [Test]
    public async Task NonExistentProject_ReturnsNotFound()
    {
        var response = await _client.GetAsync("/api/projects/non-existent/orphan-changes");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task ResponseShape_IsListOfSnapshotOrphan()
    {
        var projectId = await CreateProject();

        var response = await _client.GetAsync($"/api/projects/{projectId}/orphan-changes");
        var orphans = await response.Content.ReadFromJsonAsync<List<SnapshotOrphan>>(JsonOptions);

        Assert.That(orphans, Is.Not.Null);
        // Even when empty, the response must round-trip cleanly into List<SnapshotOrphan>.
    }
}
