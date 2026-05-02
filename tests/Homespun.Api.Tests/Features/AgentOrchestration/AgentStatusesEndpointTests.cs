using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Shared.Models.Gitgraph;
using Homespun.Shared.Models.Projects;
using Homespun.Shared.Models.Sessions;
using Microsoft.Extensions.DependencyInjection;

namespace Homespun.Api.Tests.Features.AgentOrchestration;

[TestFixture]
public class AgentStatusesEndpointTests
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
        var request = new { Name = "AgentStatuses-" + Guid.NewGuid().ToString("N")[..8], DefaultBranch = "main" };
        var response = await _client.PostAsJsonAsync("/api/projects", request, JsonOptions);
        response.EnsureSuccessStatusCode();
        var project = await response.Content.ReadFromJsonAsync<Project>(JsonOptions);
        return project!.Id;
    }

    private void SeedSession(string projectId, string entityId, string sessionId, ClaudeSessionStatus status, DateTime lastActivity)
    {
        var store = _factory.Services.GetRequiredService<IClaudeSessionStore>();
        store.Add(new ClaudeSession
        {
            Id = sessionId,
            ProjectId = projectId,
            EntityId = entityId,
            Status = status,
            Mode = SessionMode.Build,
            Model = "sonnet",
            WorkingDirectory = "/tmp",
            CreatedAt = lastActivity,
            LastActivityAt = lastActivity
        });
    }

    [Test]
    public async Task EmptyProject_ReturnsEmptyMap()
    {
        var projectId = await CreateProject();

        var response = await _client.GetAsync($"/api/projects/{projectId}/agent-statuses");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var map = await response.Content.ReadFromJsonAsync<Dictionary<string, AgentStatusData>>(JsonOptions);
        Assert.That(map, Is.Not.Null);
        Assert.That(map, Is.Empty);
    }

    [Test]
    public async Task SingleSession_ReturnsEntry()
    {
        var projectId = await CreateProject();
        SeedSession(projectId, entityId: "issue-A", sessionId: "sess-1", ClaudeSessionStatus.Running, DateTime.UtcNow);

        var response = await _client.GetAsync($"/api/projects/{projectId}/agent-statuses");
        var map = (await response.Content.ReadFromJsonAsync<Dictionary<string, AgentStatusData>>(JsonOptions))!;

        Assert.That(map.ContainsKey("issue-A"), Is.True);
        Assert.That(map["issue-A"].SessionId, Is.EqualTo("sess-1"));
    }

    [Test]
    public async Task MultipleSessionsSameEntity_MostRecentWins()
    {
        var projectId = await CreateProject();
        var older = DateTime.UtcNow.AddMinutes(-5);
        var newer = DateTime.UtcNow;
        SeedSession(projectId, "issue-B", "sess-old", ClaudeSessionStatus.Stopped, older);
        SeedSession(projectId, "issue-B", "sess-new", ClaudeSessionStatus.Running, newer);

        var response = await _client.GetAsync($"/api/projects/{projectId}/agent-statuses");
        var map = (await response.Content.ReadFromJsonAsync<Dictionary<string, AgentStatusData>>(JsonOptions))!;

        Assert.That(map["issue-B"].SessionId, Is.EqualTo("sess-new"));
    }

    [Test]
    public async Task NonExistentProject_ReturnsNotFound()
    {
        var response = await _client.GetAsync("/api/projects/non-existent/agent-statuses");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
