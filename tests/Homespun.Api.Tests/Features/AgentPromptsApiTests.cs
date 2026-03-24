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
    public async Task RestoreDefaults_Returns204AndRemovesCustomPrompts()
    {
        // Arrange - Restore first to establish baseline
        await _client.PostAsync("/api/agent-prompts/restore-defaults", null);
        var baselineResponse = await _client.GetAsync("/api/agent-prompts");
        var baselinePrompts = await baselineResponse.Content.ReadFromJsonAsync<List<AgentPrompt>>(JsonOptions);
        var baselineCount = baselinePrompts!.Count;

        // Add a custom global prompt
        var createRequest = new CreateAgentPromptRequest
        {
            Name = "CustomGlobalPrompt",
            InitialMessage = "Custom message",
            Mode = SessionMode.Build
        };
        await _client.PostAsJsonAsync("/api/agent-prompts", createRequest, JsonOptions);

        // Verify custom prompt exists
        var beforeResponse = await _client.GetAsync("/api/agent-prompts");
        var beforePrompts = await beforeResponse.Content.ReadFromJsonAsync<List<AgentPrompt>>(JsonOptions);
        Assert.That(beforePrompts!.Count, Is.EqualTo(baselineCount + 1));

        // Act
        var response = await _client.PostAsync("/api/agent-prompts/restore-defaults", null);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var afterResponse = await _client.GetAsync("/api/agent-prompts");
        var afterPrompts = await afterResponse.Content.ReadFromJsonAsync<List<AgentPrompt>>(JsonOptions);
        Assert.Multiple(() =>
        {
            Assert.That(afterPrompts!.Any(p => p.Name == "CustomGlobalPrompt"), Is.False);
            Assert.That(afterPrompts!.Count, Is.EqualTo(baselineCount));
        });
    }

    [Test]
    public async Task RestoreDefaults_PreservesProjectPrompts()
    {
        // Arrange - Create a project prompt
        var projectId = "restore-defaults-project-test";
        var createRequest = new CreateAgentPromptRequest
        {
            Name = "ProjectPromptSurvives",
            InitialMessage = "Should survive restore",
            Mode = SessionMode.Build,
            ProjectId = projectId
        };
        await _client.PostAsJsonAsync("/api/agent-prompts", createRequest, JsonOptions);

        // Act
        var response = await _client.PostAsync("/api/agent-prompts/restore-defaults", null);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var projectResponse = await _client.GetAsync($"/api/agent-prompts/project/{projectId}");
        var projectPrompts = await projectResponse.Content.ReadFromJsonAsync<List<AgentPrompt>>(JsonOptions);
        Assert.That(projectPrompts!.Any(p => p.Name == "ProjectPromptSurvives"), Is.True);
    }

    [Test]
    public async Task DeleteAllProjectPrompts_Returns204AndRemovesProjectPrompts()
    {
        // Arrange - Create project prompts
        var projectId = "delete-all-project-test";
        var createRequest1 = new CreateAgentPromptRequest
        {
            Name = "ProjectPrompt1",
            InitialMessage = "Message 1",
            Mode = SessionMode.Build,
            ProjectId = projectId
        };
        var createRequest2 = new CreateAgentPromptRequest
        {
            Name = "ProjectPrompt2",
            InitialMessage = "Message 2",
            Mode = SessionMode.Plan,
            ProjectId = projectId
        };
        await _client.PostAsJsonAsync("/api/agent-prompts", createRequest1, JsonOptions);
        await _client.PostAsJsonAsync("/api/agent-prompts", createRequest2, JsonOptions);

        // Verify they exist
        var beforeResponse = await _client.GetAsync($"/api/agent-prompts/project/{projectId}");
        var beforePrompts = await beforeResponse.Content.ReadFromJsonAsync<List<AgentPrompt>>(JsonOptions);
        Assert.That(beforePrompts!.Count(p => p.ProjectId == projectId), Is.GreaterThanOrEqualTo(2));

        // Act
        var response = await _client.DeleteAsync($"/api/agent-prompts/project/{projectId}/all");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var afterResponse = await _client.GetAsync($"/api/agent-prompts/project/{projectId}");
        var afterPrompts = await afterResponse.Content.ReadFromJsonAsync<List<AgentPrompt>>(JsonOptions);
        Assert.That(afterPrompts!.All(p => p.ProjectId != projectId), Is.True);
    }

    [Test]
    public async Task DeleteAllProjectPrompts_PreservesGlobalPrompts()
    {
        // Arrange
        var projectId = "delete-all-preserves-globals-test";
        var createRequest = new CreateAgentPromptRequest
        {
            Name = "ProjectOnlyPrompt",
            InitialMessage = "Will be deleted",
            Mode = SessionMode.Build,
            ProjectId = projectId
        };
        await _client.PostAsJsonAsync("/api/agent-prompts", createRequest, JsonOptions);

        // Act
        var response = await _client.DeleteAsync($"/api/agent-prompts/project/{projectId}/all");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var globalResponse = await _client.GetAsync("/api/agent-prompts");
        var globalPrompts = await globalResponse.Content.ReadFromJsonAsync<List<AgentPrompt>>(JsonOptions);
        Assert.That(globalPrompts!.Any(p => p.Name == "Plan"), Is.True);
    }

    [Test]
    public async Task GetIssueAgentPromptsForProject_ReturnsIssueAgentPrompts()
    {
        // Arrange
        var projectId = "issue-agent-test-project";

        // Act
        var response = await _client.GetAsync($"/api/agent-prompts/issue-agent/available/{projectId}");

        // Assert
        response.EnsureSuccessStatusCode();
        var prompts = await response.Content.ReadFromJsonAsync<List<AgentPrompt>>(JsonOptions);
        Assert.Multiple(() =>
        {
            Assert.That(prompts, Is.Not.Null);
            Assert.That(prompts!, Has.Count.GreaterThanOrEqualTo(1));
            Assert.That(prompts!.Any(p => p.Name == "IssueModify"), Is.True);
            Assert.That(prompts!.All(p => p.Category == PromptCategory.IssueAgent), Is.True);
        });
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
