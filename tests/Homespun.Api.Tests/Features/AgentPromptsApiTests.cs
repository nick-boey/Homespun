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

    [Test]
    public async Task RemoveOverride_ReturnsOk_WhenOverrideExists()
    {
        // Arrange - Create an override first
        var globalPromptsResponse = await _client.GetAsync("/api/agent-prompts");
        var globalPrompts = await globalPromptsResponse.Content.ReadFromJsonAsync<List<AgentPrompt>>(JsonOptions);
        var buildPrompt = globalPrompts!.First(p => p.Name == "Build");
        var projectId = "remove-override-test-project";

        var createRequest = new CreateOverrideRequest
        {
            GlobalPromptId = buildPrompt.Id,
            ProjectId = projectId,
            InitialMessage = "Custom message for removal test"
        };
        var createResponse = await _client.PostAsJsonAsync("/api/agent-prompts/create-override", createRequest, JsonOptions);
        createResponse.EnsureSuccessStatusCode();
        var overridePrompt = await createResponse.Content.ReadFromJsonAsync<AgentPrompt>(JsonOptions);

        // Act
        var response = await _client.DeleteAsync($"/api/agent-prompts/{overridePrompt!.Id}/override");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var globalPrompt = await response.Content.ReadFromJsonAsync<AgentPrompt>(JsonOptions);
        Assert.Multiple(() =>
        {
            Assert.That(globalPrompt, Is.Not.Null);
            Assert.That(globalPrompt!.Name, Is.EqualTo("Build"));
            Assert.That(globalPrompt.ProjectId, Is.Null); // Should return the global prompt
        });
    }

    [Test]
    public async Task RemoveOverride_ReturnsNotFound_WhenPromptDoesNotExist()
    {
        // Act
        var response = await _client.DeleteAsync("/api/agent-prompts/non-existent-id/override");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task RemoveOverride_ReturnsBadRequest_WhenPromptIsGlobal()
    {
        // Arrange - Get a global prompt
        var globalPromptsResponse = await _client.GetAsync("/api/agent-prompts");
        var globalPrompts = await globalPromptsResponse.Content.ReadFromJsonAsync<List<AgentPrompt>>(JsonOptions);
        var globalPrompt = globalPrompts!.First();

        // Act
        var response = await _client.DeleteAsync($"/api/agent-prompts/{globalPrompt.Id}/override");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task RemoveOverride_ReturnsBadRequest_WhenNotActuallyAnOverride()
    {
        // Arrange - Create a project-only prompt (not an override of a global prompt)
        var createRequest = new CreateAgentPromptRequest
        {
            Name = "UniqueProjectOnlyPrompt",
            InitialMessage = "Test message",
            Mode = SessionMode.Build,
            ProjectId = "test-project-unique"
        };
        var createResponse = await _client.PostAsJsonAsync("/api/agent-prompts", createRequest, JsonOptions);
        createResponse.EnsureSuccessStatusCode();
        var projectPrompt = await createResponse.Content.ReadFromJsonAsync<AgentPrompt>(JsonOptions);

        // Act
        var response = await _client.DeleteAsync($"/api/agent-prompts/{projectPrompt!.Id}/override");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task RemoveOverride_GlobalPromptAppearsInProjectPrompts_AfterRemoval()
    {
        // Arrange - Create an override
        var globalPromptsResponse = await _client.GetAsync("/api/agent-prompts");
        var globalPrompts = await globalPromptsResponse.Content.ReadFromJsonAsync<List<AgentPrompt>>(JsonOptions);
        var planPrompt = globalPrompts!.First(p => p.Name == "Plan");
        var projectId = "remove-override-restore-test";

        var createRequest = new CreateOverrideRequest
        {
            GlobalPromptId = planPrompt.Id,
            ProjectId = projectId,
            InitialMessage = "Custom Plan message"
        };
        var createResponse = await _client.PostAsJsonAsync("/api/agent-prompts/create-override", createRequest, JsonOptions);
        var overridePrompt = await createResponse.Content.ReadFromJsonAsync<AgentPrompt>(JsonOptions);

        // Verify the override exists
        var projectPromptsBeforeResponse = await _client.GetAsync($"/api/agent-prompts/project/{projectId}");
        var projectPromptsBefore = await projectPromptsBeforeResponse.Content.ReadFromJsonAsync<List<AgentPrompt>>(JsonOptions);
        var planPromptBefore = projectPromptsBefore!.First(p => p.Name == "Plan");
        Assert.That(planPromptBefore.IsOverride, Is.True);

        // Act - Remove the override
        await _client.DeleteAsync($"/api/agent-prompts/{overridePrompt!.Id}/override");

        // Assert - Global prompt should now appear instead
        var projectPromptsAfterResponse = await _client.GetAsync($"/api/agent-prompts/project/{projectId}");
        var projectPromptsAfter = await projectPromptsAfterResponse.Content.ReadFromJsonAsync<List<AgentPrompt>>(JsonOptions);
        var planPromptAfter = projectPromptsAfter!.First(p => p.Name == "Plan");
        Assert.Multiple(() =>
        {
            Assert.That(planPromptAfter.ProjectId, Is.Null);
            Assert.That(planPromptAfter.IsOverride, Is.False);
            Assert.That(planPromptAfter.Id, Is.EqualTo(planPrompt.Id)); // Same as original global prompt
        });
    }
}
