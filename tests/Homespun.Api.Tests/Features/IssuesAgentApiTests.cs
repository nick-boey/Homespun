using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Homespun.Shared.Models.Projects;
using Homespun.Shared.Models.Sessions;
using Homespun.Shared.Requests;

namespace Homespun.Api.Tests.Features;

[TestFixture]
public class IssuesAgentApiTests
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

        // Ensure default prompts exist
        await _client.PostAsync("/api/agent-prompts/ensure-defaults", null);

        // Create a project to use in tests
        var createProjectRequest = new { Name = "IssuesAgentTest", DefaultBranch = "main" };
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
    public async Task CreateSession_WithBuildMode_ReturnsCreated()
    {
        // Arrange
        var request = new CreateIssuesAgentSessionRequest
        {
            ProjectId = _projectId,
            Mode = SessionMode.Build,
            UserInstructions = "Test instructions"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/issues-agent/session", request, JsonOptions);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var result = await response.Content.ReadFromJsonAsync<CreateIssuesAgentSessionResponse>(JsonOptions);
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.SessionId, Is.Not.Empty);
            Assert.That(result.BranchName, Does.StartWith("issues-agent-"));
        });
    }

    [Test]
    public async Task CreateSession_WithNoInstructions_ReturnsCreated()
    {
        // Arrange - no user instructions, should use fallback message
        var request = new CreateIssuesAgentSessionRequest
        {
            ProjectId = _projectId,
            Mode = SessionMode.Build
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/issues-agent/session", request, JsonOptions);

        // Assert - should succeed with fallback message
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var result = await response.Content.ReadFromJsonAsync<CreateIssuesAgentSessionResponse>(JsonOptions);
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.SessionId, Is.Not.Empty);
            Assert.That(result.BranchName, Does.StartWith("issues-agent-"));
        });
    }

    [Test]
    public async Task CreateSession_WithSelectedIssueId_IncludesIssueIdInBranchName()
    {
        // Arrange
        var request = new CreateIssuesAgentSessionRequest
        {
            ProjectId = _projectId,
            UserInstructions = "Fix the bug",
            SelectedIssueId = "abc123"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/issues-agent/session", request, JsonOptions);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var result = await response.Content.ReadFromJsonAsync<CreateIssuesAgentSessionResponse>(JsonOptions);
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.BranchName, Does.Contain("abc123"));
            Assert.That(result.BranchName, Does.StartWith("issues-agent-abc123-"));
        });
    }

    [Test]
    public async Task CreateSession_WithoutMode_DefaultsToBuild()
    {
        // Arrange - no mode specified, should default to Build
        var request = new CreateIssuesAgentSessionRequest
        {
            ProjectId = _projectId,
            UserInstructions = "Test instructions without mode"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/issues-agent/session", request, JsonOptions);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
    }

    [Test]
    public async Task CreateSession_WithInvalidProjectId_ReturnsNotFound()
    {
        // Arrange
        var request = new CreateIssuesAgentSessionRequest
        {
            ProjectId = "nonexistent-project-id"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/issues-agent/session", request, JsonOptions);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
