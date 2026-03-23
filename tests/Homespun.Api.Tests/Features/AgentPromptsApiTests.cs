using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Homespun.Features.ClaudeCode.Controllers;
using Homespun.Shared.Models.Sessions;

namespace Homespun.Api.Tests.Features;

[TestFixture]
public class AgentPromptsApiTests
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

    [SetUp]
    public async Task SetUp()
    {
        // Ensure default prompts exist before each test
        await _client.PostAsync("/api/agent-prompts/ensure-defaults", null);
    }

    [Test]
    public async Task CreateOverride_ReturnsCreated_WhenGlobalPromptExists()
    {
        // Arrange - Get a global prompt first
        var globalPromptsResponse = await _client.GetAsync("/api/agent-prompts");
        globalPromptsResponse.EnsureSuccessStatusCode();
        var globalPrompts = await globalPromptsResponse.Content.ReadFromJsonAsync<List<AgentPrompt>>(JsonOptions);
        Assert.That(globalPrompts, Is.Not.Empty);

        var globalPrompt = globalPrompts!.First();
        var request = new CreateOverrideRequest
        {
            GlobalPromptId = globalPrompt.Id,
            ProjectId = "test-project-123",
            InitialMessage = "Custom override message"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/agent-prompts/create-override", request, JsonOptions);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var overridePrompt = await response.Content.ReadFromJsonAsync<AgentPrompt>(JsonOptions);
        Assert.Multiple(() =>
        {
            Assert.That(overridePrompt, Is.Not.Null);
            Assert.That(overridePrompt!.Name, Is.EqualTo(globalPrompt.Name));
            Assert.That(overridePrompt.ProjectId, Is.EqualTo("test-project-123"));
            Assert.That(overridePrompt.InitialMessage, Is.EqualTo("Custom override message"));
            Assert.That(overridePrompt.Mode, Is.EqualTo(globalPrompt.Mode));
            Assert.That(overridePrompt.Id, Is.Not.EqualTo(globalPrompt.Id));
        });
    }

    [Test]
    public async Task CreateOverride_CopiesInitialMessage_WhenNotProvided()
    {
        // Arrange - Get a global prompt first
        var globalPromptsResponse = await _client.GetAsync("/api/agent-prompts");
        globalPromptsResponse.EnsureSuccessStatusCode();
        var globalPrompts = await globalPromptsResponse.Content.ReadFromJsonAsync<List<AgentPrompt>>(JsonOptions);
        Assert.That(globalPrompts, Is.Not.Empty);

        var globalPrompt = globalPrompts!.First();
        var request = new CreateOverrideRequest
        {
            GlobalPromptId = globalPrompt.Id,
            ProjectId = "test-project-456",
            InitialMessage = null
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/agent-prompts/create-override", request, JsonOptions);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var overridePrompt = await response.Content.ReadFromJsonAsync<AgentPrompt>(JsonOptions);
        Assert.That(overridePrompt!.InitialMessage, Is.EqualTo(globalPrompt.InitialMessage));
    }

    [Test]
    public async Task CreateOverride_ReturnsNotFound_WhenGlobalPromptDoesNotExist()
    {
        // Arrange
        var request = new CreateOverrideRequest
        {
            GlobalPromptId = "non-existent-id",
            ProjectId = "test-project",
            InitialMessage = null
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/agent-prompts/create-override", request, JsonOptions);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task CreateOverride_ReturnsBadRequest_WhenPromptIsNotGlobal()
    {
        // Arrange - Create a project-scoped prompt first
        var createRequest = new CreateAgentPromptRequest
        {
            Name = "ProjectPrompt",
            InitialMessage = "Test message",
            Mode = SessionMode.Build,
            ProjectId = "project-for-test"
        };
        var createResponse = await _client.PostAsJsonAsync("/api/agent-prompts", createRequest, JsonOptions);
        createResponse.EnsureSuccessStatusCode();
        var projectPrompt = await createResponse.Content.ReadFromJsonAsync<AgentPrompt>(JsonOptions);

        // Try to create override from the project prompt
        var overrideRequest = new CreateOverrideRequest
        {
            GlobalPromptId = projectPrompt!.Id,
            ProjectId = "another-project",
            InitialMessage = null
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/agent-prompts/create-override", overrideRequest);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task CreateOverride_OverrideAppearsInProjectPrompts()
    {
        // Arrange
        var globalPromptsResponse = await _client.GetAsync("/api/agent-prompts");
        var globalPrompts = await globalPromptsResponse.Content.ReadFromJsonAsync<List<AgentPrompt>>(JsonOptions);
        var buildPrompt = globalPrompts!.First(p => p.Name == "Build");
        var projectId = "override-test-project";

        var request = new CreateOverrideRequest
        {
            GlobalPromptId = buildPrompt.Id,
            ProjectId = projectId,
            InitialMessage = "Custom build message"
        };

        // Act
        await _client.PostAsJsonAsync("/api/agent-prompts/create-override", request, JsonOptions);
        var projectPromptsResponse = await _client.GetAsync($"/api/agent-prompts/project/{projectId}");
        var projectPrompts = await projectPromptsResponse.Content.ReadFromJsonAsync<List<AgentPrompt>>(JsonOptions);

        // Assert
        var overriddenBuild = projectPrompts!.FirstOrDefault(p => p.Name == "Build");
        Assert.Multiple(() =>
        {
            Assert.That(overriddenBuild, Is.Not.Null);
            Assert.That(overriddenBuild!.ProjectId, Is.EqualTo(projectId));
            Assert.That(overriddenBuild.IsOverride, Is.True);
            Assert.That(overriddenBuild.InitialMessage, Is.EqualTo("Custom build message"));
        });
    }
}
