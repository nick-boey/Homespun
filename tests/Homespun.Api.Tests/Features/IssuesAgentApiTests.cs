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
    public async Task CreateSession_WithPromptId_ReturnsCreated()
    {
        // Arrange - Get an IssueAgent prompt
        var promptsResponse = await _client.GetAsync($"/api/agent-prompts/issue-agent/available/{_projectId}");
        promptsResponse.EnsureSuccessStatusCode();
        var prompts = await promptsResponse.Content.ReadFromJsonAsync<List<AgentPrompt>>(JsonOptions);
        Assert.That(prompts, Is.Not.Empty, "Should have at least one IssueAgent prompt");

        var prompt = prompts!.First();
        var request = new CreateIssuesAgentSessionRequest
        {
            ProjectId = _projectId,
            PromptName = prompt.Name,
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
    public async Task CreateSession_WithInvalidPromptId_ReturnsNotFound()
    {
        // Arrange
        var request = new CreateIssuesAgentSessionRequest
        {
            ProjectId = _projectId,
            PromptName = "nonexistent-prompt-id"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/issues-agent/session", request, JsonOptions);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task CreateSession_WithStandardCategoryPromptId_ReturnsBadRequest()
    {
        // Arrange - Get a Standard category prompt
        var promptsResponse = await _client.GetAsync("/api/agent-prompts");
        promptsResponse.EnsureSuccessStatusCode();
        var prompts = await promptsResponse.Content.ReadFromJsonAsync<List<AgentPrompt>>(JsonOptions);
        Assert.That(prompts, Is.Not.Empty);

        var standardPrompt = prompts!.First(p => p.Category == PromptCategory.Standard);
        var request = new CreateIssuesAgentSessionRequest
        {
            ProjectId = _projectId,
            PromptName = standardPrompt.Name
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/issues-agent/session", request, JsonOptions);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
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
    public async Task CreateSession_WithoutPromptId_ReturnsCreated()
    {
        // Arrange - no prompt ID, backward-compatible behavior
        var request = new CreateIssuesAgentSessionRequest
        {
            ProjectId = _projectId,
            UserInstructions = "Test instructions without prompt"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/issues-agent/session", request, JsonOptions);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
    }
}
