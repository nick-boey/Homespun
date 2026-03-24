using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Homespun.Features.AgentOrchestration.Services;
using Homespun.Shared.Requests;
using Microsoft.Extensions.DependencyInjection;

namespace Homespun.Api.Tests.Features.AgentOrchestration;

/// <summary>
/// Integration tests for queue status and control API endpoints.
/// </summary>
[TestFixture]
public class QueueApiTests
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

    [Test]
    public void QueueCoordinator_IsRegisteredInDI()
    {
        var service = _factory.Services.GetService<IQueueCoordinator>();

        Assert.That(service, Is.Not.Null);
        Assert.That(service, Is.InstanceOf<QueueCoordinator>());
    }

    [Test]
    public async Task Start_ReturnsNotFound_WhenProjectDoesNotExist()
    {
        var request = new StartQueueRequest { IssueId = "issue1" };

        var response = await _client.PostAsJsonAsync(
            "/api/projects/nonexistent-project/queue/start", request, JsonOptions);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GetStatus_ReturnsNotFound_WhenProjectDoesNotExist()
    {
        var response = await _client.GetAsync("/api/projects/nonexistent-project/queue/status");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Cancel_ReturnsNotFound_WhenProjectDoesNotExist()
    {
        var response = await _client.PostAsync("/api/projects/nonexistent-project/queue/cancel", null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GetStatus_ReturnsNotFound_WhenNoActiveExecution()
    {
        // Create a project first
        var projectId = await CreateTestProject("queue-status-test");
        if (projectId == null)
        {
            Assert.Inconclusive("Could not create test project in mock mode");
            return;
        }

        var response = await _client.GetAsync($"/api/projects/{projectId}/queue/status");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Cancel_ReturnsNotFound_WhenNoActiveExecution()
    {
        var projectId = await CreateTestProject("queue-cancel-test");
        if (projectId == null)
        {
            Assert.Inconclusive("Could not create test project in mock mode");
            return;
        }

        var response = await _client.PostAsync($"/api/projects/{projectId}/queue/cancel", null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Start_ReturnsBadRequest_WhenIssueIdIsEmpty()
    {
        var projectId = await CreateTestProject("queue-start-empty-issue");
        if (projectId == null)
        {
            Assert.Inconclusive("Could not create test project in mock mode");
            return;
        }

        var request = new StartQueueRequest { IssueId = "" };
        var response = await _client.PostAsJsonAsync(
            $"/api/projects/{projectId}/queue/start", request, JsonOptions);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Start_ReturnsBadRequest_WhenWorkflowIdInvalid()
    {
        var projectId = await CreateTestProject("queue-start-bad-wf");
        if (projectId == null)
        {
            Assert.Inconclusive("Could not create test project in mock mode");
            return;
        }

        var request = new StartQueueRequest
        {
            IssueId = "issue1",
            WorkflowMappings = new Dictionary<string, string>
            {
                { "task", "nonexistent-workflow" }
            }
        };

        var response = await _client.PostAsJsonAsync(
            $"/api/projects/{projectId}/queue/start", request, JsonOptions);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Start_Endpoints_DoNotReturn500()
    {
        var request = new StartQueueRequest { IssueId = "issue1" };

        var startResponse = await _client.PostAsJsonAsync(
            "/api/projects/any-project/queue/start", request, JsonOptions);
        var statusResponse = await _client.GetAsync("/api/projects/any-project/queue/status");
        var cancelResponse = await _client.PostAsync("/api/projects/any-project/queue/cancel", null);

        Assert.Multiple(() =>
        {
            Assert.That((int)startResponse.StatusCode, Is.LessThan(500),
                "Start endpoint should not return 500");
            Assert.That((int)statusResponse.StatusCode, Is.LessThan(500),
                "Status endpoint should not return 500");
            Assert.That((int)cancelResponse.StatusCode, Is.LessThan(500),
                "Cancel endpoint should not return 500");
        });
    }

    private async Task<string?> CreateTestProject(string name)
    {
        var createRequest = new { Name = name, Path = $"/tmp/{name}", DefaultBranch = "main" };
        var response = await _client.PostAsJsonAsync("/api/projects", createRequest, JsonOptions);
        if (!response.IsSuccessStatusCode)
            return null;

        var content = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return content.TryGetProperty("id", out var id) ? id.GetString() : null;
    }
}
