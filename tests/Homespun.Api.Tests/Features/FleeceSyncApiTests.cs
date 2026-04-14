using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Homespun.Shared.Models.Fleece;
using Homespun.Shared.Models.Projects;
using SessionCacheSummary = Homespun.Features.ClaudeCode.Data.SessionCacheSummary;

namespace Homespun.Api.Tests.Features;

[TestFixture]
public class FleeceSyncApiTests
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

        // Create a project to use in fleece sync tests
        var createProjectRequest = new { Name = "fleece-sync-test-" + Guid.NewGuid().ToString("N")[..8] };
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

    // --- GET /api/fleece-sync/{projectId}/branch-status ---

    [Test]
    public async Task GetBranchStatus_ReturnsOk_ForExistingProject()
    {
        // Act
        var response = await _client.GetAsync($"/api/fleece-sync/{_projectId}/branch-status");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<BranchStatusResult>(JsonOptions);
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Success, Is.True);
        });
    }

    [Test]
    public async Task GetBranchStatus_ReturnsNotFound_ForNonExistentProject()
    {
        // Act
        var response = await _client.GetAsync("/api/fleece-sync/non-existent-project/branch-status");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    // --- POST /api/fleece-sync/{projectId}/sync ---

    [Test]
    public async Task Sync_ReturnsOk_ForExistingProject()
    {
        // Act
        var response = await _client.PostAsync($"/api/fleece-sync/{_projectId}/sync", null);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<FleeceIssueSyncResult>(JsonOptions);
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Success, Is.True);
        });
    }

    [Test]
    public async Task Sync_ReturnsNotFound_ForNonExistentProject()
    {
        // Act
        var response = await _client.PostAsync("/api/fleece-sync/non-existent-project/sync", null);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    // --- POST /api/fleece-sync/{projectId}/pull ---

    [Test]
    public async Task Pull_ReturnsOk_ForExistingProject()
    {
        // Act
        var response = await _client.PostAsync($"/api/fleece-sync/{_projectId}/pull", null);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<FleecePullResult>(JsonOptions);
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Success, Is.True);
        });
    }

    [Test]
    public async Task Pull_ReturnsNotFound_ForNonExistentProject()
    {
        // Act
        var response = await _client.PostAsync("/api/fleece-sync/non-existent-project/pull", null);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    // --- POST /api/fleece-sync/{projectId}/discard-non-fleece-and-pull ---

    [Test]
    public async Task DiscardNonFleeceAndPull_ReturnsOk_ForExistingProject()
    {
        // Act
        var response = await _client.PostAsync($"/api/fleece-sync/{_projectId}/discard-non-fleece-and-pull", null);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<FleecePullResult>(JsonOptions);
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Success, Is.True);
        });
    }

    [Test]
    public async Task DiscardNonFleeceAndPull_ReturnsNotFound_ForNonExistentProject()
    {
        // Act
        var response = await _client.PostAsync("/api/fleece-sync/non-existent-project/discard-non-fleece-and-pull", null);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}

[TestFixture]
public class SessionCacheApiTests
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

    // --- GET /api/sessions/{sessionId}/cache/messages ---

    [Test]
    public async Task GetMessages_ReturnsOk_ForNonExistentSession()
    {
        // The messages endpoint always returns 200 OK (empty list for unknown sessions)
        var response = await _client.GetAsync("/api/sessions/non-existent-session/cache/messages");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var content = await response.Content.ReadAsStringAsync();
        Assert.That(content, Is.Not.Null.And.Not.Empty);
    }

    // --- GET /api/sessions/{sessionId}/cache/summary ---

    [Test]
    public async Task GetSummary_ReturnsNotFound_ForNonExistentSession()
    {
        // Act
        var response = await _client.GetAsync("/api/sessions/non-existent-session/cache/summary");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    // --- GET /api/sessions/cache/project/{projectId} ---

    [Test]
    public async Task ListSessions_ReturnsOk_ForNonExistentProject()
    {
        // The list endpoint always returns 200 OK (empty list for unknown projects)
        var response = await _client.GetAsync("/api/sessions/cache/project/non-existent-project");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var sessions = await response.Content.ReadFromJsonAsync<List<SessionCacheSummary>>(JsonOptions);
        Assert.Multiple(() =>
        {
            Assert.That(sessions, Is.Not.Null);
            Assert.That(sessions!, Is.Empty);
        });
    }

    // --- GET /api/sessions/cache/entity/{projectId}/{entityId} ---

    [Test]
    public async Task GetEntitySessionIds_ReturnsOk_ForNonExistentEntity()
    {
        // The entity endpoint always returns 200 OK (empty list for unknown entities)
        var response = await _client.GetAsync("/api/sessions/cache/entity/non-existent-project/non-existent-entity");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var sessionIds = await response.Content.ReadFromJsonAsync<List<string>>(JsonOptions);
        Assert.Multiple(() =>
        {
            Assert.That(sessionIds, Is.Not.Null);
            Assert.That(sessionIds!, Is.Empty);
        });
    }
}
