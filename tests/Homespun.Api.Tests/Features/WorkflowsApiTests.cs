using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Homespun.Shared.Models.Projects;
using Homespun.Shared.Models.Workflows;
using Homespun.Shared.Requests;

namespace Homespun.Api.Tests.Features;

[TestFixture]
public class WorkflowsApiTests
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

        // Create a project to use across tests
        var request = new { Name = "WorkflowsApiTest-" + Guid.NewGuid().ToString("N")[..8], DefaultBranch = "main" };
        var response = await _client.PostAsJsonAsync("/api/projects", request, JsonOptions);
        response.EnsureSuccessStatusCode();
        var project = await response.Content.ReadFromJsonAsync<Project>(JsonOptions);
        _projectId = project!.Id;
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private async Task<WorkflowDefinition> CreateWorkflow(string? title = null, bool enabled = true)
    {
        var request = new CreateWorkflowRequest
        {
            ProjectId = _projectId,
            Title = title ?? "Test workflow " + Guid.NewGuid().ToString("N")[..8],
            Description = "A test workflow",
            Enabled = enabled,
            Steps =
            [
                new WorkflowStep
                {
                    Id = "step-1",
                    Name = "Test Step",
                    StepType = WorkflowStepType.Agent,
                    Prompt = "Do something"
                }
            ]
        };
        var response = await _client.PostAsJsonAsync("/api/workflows", request, JsonOptions);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WorkflowDefinition>(JsonOptions))!;
    }

    // --- POST /api/workflows (Create) ---

    [Test]
    public async Task Create_WithValidData_ReturnsCreated()
    {
        // Arrange
        var title = "Create test " + Guid.NewGuid().ToString("N")[..8];
        var request = new CreateWorkflowRequest
        {
            ProjectId = _projectId,
            Title = title,
            Description = "Test description",
            Enabled = true
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/workflows", request, JsonOptions);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var workflow = await response.Content.ReadFromJsonAsync<WorkflowDefinition>(JsonOptions);
        Assert.Multiple(() =>
        {
            Assert.That(workflow, Is.Not.Null);
            Assert.That(workflow!.Title, Is.EqualTo(title));
            Assert.That(workflow.Description, Is.EqualTo("Test description"));
            Assert.That(workflow.Enabled, Is.True);
            Assert.That(workflow.Id, Is.Not.Empty);
            Assert.That(workflow.ProjectId, Is.EqualTo(_projectId));
        });
    }

    [Test]
    public async Task Create_WithNonExistentProject_ReturnsNotFound()
    {
        // Arrange
        var request = new CreateWorkflowRequest
        {
            ProjectId = "non-existent-project",
            Title = "Should fail"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/workflows", request, JsonOptions);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    // --- GET /api/projects/{projectId}/workflows (List) ---

    [Test]
    public async Task GetByProject_ReturnsOk_WithWorkflows()
    {
        // Arrange
        var title = "List test " + Guid.NewGuid().ToString("N")[..8];
        await CreateWorkflow(title);

        // Act
        var response = await _client.GetAsync($"/api/projects/{_projectId}/workflows");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<WorkflowListResponse>(JsonOptions);
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Workflows.Any(w => w.Title == title), Is.True);
            Assert.That(result.TotalCount, Is.GreaterThan(0));
        });
    }

    [Test]
    public async Task GetByProject_EmptyProject_ReturnsEmptyList()
    {
        // Arrange - create a fresh project with no workflows
        var request = new { Name = "EmptyWf-" + Guid.NewGuid().ToString("N")[..8], DefaultBranch = "main" };
        var projectResponse = await _client.PostAsJsonAsync("/api/projects", request, JsonOptions);
        projectResponse.EnsureSuccessStatusCode();
        var project = await projectResponse.Content.ReadFromJsonAsync<Project>(JsonOptions);

        // Act
        var response = await _client.GetAsync($"/api/projects/{project!.Id}/workflows");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<WorkflowListResponse>(JsonOptions);
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Workflows, Is.Empty);
            Assert.That(result.TotalCount, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task GetByProject_NonExistentProject_ReturnsNotFound()
    {
        // Act
        var response = await _client.GetAsync("/api/projects/non-existent-id/workflows");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    // --- GET /api/workflows/{workflowId} (Get by ID) ---

    [Test]
    public async Task GetById_ReturnsWorkflow_WhenExists()
    {
        // Arrange
        var created = await CreateWorkflow();

        // Act
        var response = await _client.GetAsync($"/api/workflows/{created.Id}?projectId={_projectId}");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var workflow = await response.Content.ReadFromJsonAsync<WorkflowDefinition>(JsonOptions);
        Assert.Multiple(() =>
        {
            Assert.That(workflow, Is.Not.Null);
            Assert.That(workflow!.Id, Is.EqualTo(created.Id));
            Assert.That(workflow.Title, Is.EqualTo(created.Title));
        });
    }

    [Test]
    public async Task GetById_NonExistentWorkflow_ReturnsNotFound()
    {
        // Act
        var response = await _client.GetAsync($"/api/workflows/non-existent?projectId={_projectId}");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GetById_NonExistentProject_ReturnsNotFound()
    {
        // Act
        var response = await _client.GetAsync("/api/workflows/some-id?projectId=non-existent");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    // --- PUT /api/workflows/{workflowId} (Update) ---

    [Test]
    public async Task Update_ReturnsUpdatedWorkflow_WhenExists()
    {
        // Arrange
        var created = await CreateWorkflow();
        var newTitle = "Updated title " + Guid.NewGuid().ToString("N")[..8];
        var request = new UpdateWorkflowRequest
        {
            ProjectId = _projectId,
            Title = newTitle,
            Description = "Updated description"
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/workflows/{created.Id}", request, JsonOptions);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var workflow = await response.Content.ReadFromJsonAsync<WorkflowDefinition>(JsonOptions);
        Assert.Multiple(() =>
        {
            Assert.That(workflow, Is.Not.Null);
            Assert.That(workflow!.Title, Is.EqualTo(newTitle));
            Assert.That(workflow.Description, Is.EqualTo("Updated description"));
        });
    }

    [Test]
    public async Task Update_NonExistentWorkflow_ReturnsNotFound()
    {
        // Arrange
        var request = new UpdateWorkflowRequest
        {
            ProjectId = _projectId,
            Title = "Does not matter"
        };

        // Act
        var response = await _client.PutAsJsonAsync("/api/workflows/non-existent", request, JsonOptions);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Update_NonExistentProject_ReturnsNotFound()
    {
        // Arrange
        var request = new UpdateWorkflowRequest
        {
            ProjectId = "non-existent",
            Title = "Does not matter"
        };

        // Act
        var response = await _client.PutAsJsonAsync("/api/workflows/some-id", request, JsonOptions);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    // --- DELETE /api/workflows/{workflowId} ---

    [Test]
    public async Task Delete_ExistingWorkflow_ReturnsNoContent()
    {
        // Arrange
        var created = await CreateWorkflow();

        // Act
        var response = await _client.DeleteAsync($"/api/workflows/{created.Id}?projectId={_projectId}");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task Delete_VerifyRemoval_ReturnsNotFoundAfterDeletion()
    {
        // Arrange
        var created = await CreateWorkflow();

        // Act
        var deleteResponse = await _client.DeleteAsync($"/api/workflows/{created.Id}?projectId={_projectId}");
        Assert.That(deleteResponse.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        // Assert - verify workflow is gone
        var getResponse = await _client.GetAsync($"/api/workflows/{created.Id}?projectId={_projectId}");
        Assert.That(getResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Delete_NonExistentWorkflow_ReturnsNotFound()
    {
        // Act
        var response = await _client.DeleteAsync($"/api/workflows/non-existent?projectId={_projectId}");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Delete_NonExistentProject_ReturnsNotFound()
    {
        // Act
        var response = await _client.DeleteAsync("/api/workflows/some-id?projectId=non-existent");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    // --- POST /api/workflows/{workflowId}/execute ---

    [Test]
    public async Task Execute_ValidWorkflow_ReturnsAccepted()
    {
        // Arrange
        var created = await CreateWorkflow();
        var request = new ExecuteWorkflowRequest { ProjectId = _projectId };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/workflows/{created.Id}/execute", request, JsonOptions);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
        var result = await response.Content.ReadFromJsonAsync<WorkflowExecutionResponse>(JsonOptions);
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.ExecutionId, Is.Not.Empty);
            Assert.That(result.WorkflowId, Is.EqualTo(created.Id));
        });
    }

    [Test]
    public async Task Execute_NonExistentWorkflow_ReturnsNotFound()
    {
        // Arrange
        var request = new ExecuteWorkflowRequest { ProjectId = _projectId };

        // Act
        var response = await _client.PostAsJsonAsync("/api/workflows/non-existent/execute", request, JsonOptions);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Execute_NonExistentProject_ReturnsNotFound()
    {
        // Arrange
        var request = new ExecuteWorkflowRequest { ProjectId = "non-existent" };

        // Act
        var response = await _client.PostAsJsonAsync("/api/workflows/some-id/execute", request, JsonOptions);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Execute_DisabledWorkflow_ReturnsBadRequest()
    {
        // Arrange
        var created = await CreateWorkflow(enabled: false);
        var request = new ExecuteWorkflowRequest { ProjectId = _projectId };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/workflows/{created.Id}/execute", request, JsonOptions);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    // --- GET /api/workflows/{workflowId}/executions ---

    [Test]
    public async Task ListExecutions_ReturnsOk()
    {
        // Arrange
        var created = await CreateWorkflow();
        // Execute the workflow to create an execution
        var execRequest = new ExecuteWorkflowRequest { ProjectId = _projectId };
        await _client.PostAsJsonAsync($"/api/workflows/{created.Id}/execute", execRequest, JsonOptions);

        // Act
        var response = await _client.GetAsync($"/api/workflows/{created.Id}/executions?projectId={_projectId}");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<ExecutionListResponse>(JsonOptions);
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Executions, Is.Not.Empty);
            Assert.That(result.TotalCount, Is.GreaterThan(0));
        });
    }

    [Test]
    public async Task ListExecutions_NonExistentWorkflow_ReturnsNotFound()
    {
        // Act
        var response = await _client.GetAsync($"/api/workflows/non-existent/executions?projectId={_projectId}");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task ListExecutions_NonExistentProject_ReturnsNotFound()
    {
        // Act
        var response = await _client.GetAsync("/api/workflows/some-id/executions?projectId=non-existent");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    // --- GET /api/executions/{executionId} ---

    [Test]
    public async Task GetExecution_ReturnsOk_WhenExists()
    {
        // Arrange
        var created = await CreateWorkflow();
        var execRequest = new ExecuteWorkflowRequest { ProjectId = _projectId };
        var execResponse = await _client.PostAsJsonAsync($"/api/workflows/{created.Id}/execute", execRequest, JsonOptions);
        var execResult = await execResponse.Content.ReadFromJsonAsync<WorkflowExecutionResponse>(JsonOptions);

        // Act
        var response = await _client.GetAsync($"/api/executions/{execResult!.ExecutionId}?projectId={_projectId}");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var execution = await response.Content.ReadFromJsonAsync<WorkflowExecution>(JsonOptions);
        Assert.Multiple(() =>
        {
            Assert.That(execution, Is.Not.Null);
            Assert.That(execution!.Id, Is.EqualTo(execResult.ExecutionId));
            Assert.That(execution.WorkflowId, Is.EqualTo(created.Id));
        });
    }

    [Test]
    public async Task GetExecution_NonExistentExecution_ReturnsNotFound()
    {
        // Act
        var response = await _client.GetAsync($"/api/executions/non-existent?projectId={_projectId}");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GetExecution_NonExistentProject_ReturnsNotFound()
    {
        // Act
        var response = await _client.GetAsync("/api/executions/some-id?projectId=non-existent");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    // --- POST /api/executions/{executionId}/cancel ---

    [Test]
    public async Task CancelExecution_NonExistentExecution_ReturnsNotFound()
    {
        // Arrange
        var request = new CancelWorkflowExecutionRequest
        {
            ProjectId = _projectId,
            Reason = "Test cancellation"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/executions/non-existent/cancel", request, JsonOptions);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task CancelExecution_NonExistentProject_ReturnsNotFound()
    {
        // Arrange
        var request = new CancelWorkflowExecutionRequest
        {
            ProjectId = "non-existent",
            Reason = "Test cancellation"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/executions/some-id/cancel", request, JsonOptions);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    // --- GET /api/executions/{executionId}/context ---

    [Test]
    public async Task GetExecutionContext_NonExistentExecution_ReturnsNotFound()
    {
        // Act
        var response = await _client.GetAsync($"/api/executions/non-existent/context?projectId={_projectId}");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GetExecutionContext_NonExistentProject_ReturnsNotFound()
    {
        // Act
        var response = await _client.GetAsync("/api/executions/some-id/context?projectId=non-existent");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    // --- POST /api/executions/{executionId}/steps/{stepId}/signal ---

    [Test]
    public async Task SignalStep_NonExistentExecution_ReturnsNotFound()
    {
        // Arrange
        var request = new WorkflowStepSignalRequest
        {
            ProjectId = _projectId,
            Status = "success"
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/executions/non-existent/steps/step-1/signal", request, JsonOptions);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task SignalStep_NonExistentProject_ReturnsNotFound()
    {
        // Arrange
        var request = new WorkflowStepSignalRequest
        {
            ProjectId = "non-existent",
            Status = "success"
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/executions/some-id/steps/step-1/signal", request, JsonOptions);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    // --- GET /api/workflow-templates ---

    [Test]
    public async Task ListTemplates_ReturnsOk()
    {
        // Act
        var response = await _client.GetAsync("/api/workflow-templates");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var templates = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.That(templates.ValueKind, Is.EqualTo(JsonValueKind.Array));
    }

    // --- POST /api/workflow-templates/{templateId}/create ---

    [Test]
    public async Task CreateFromTemplate_NonExistentTemplate_ReturnsNotFound()
    {
        // Act
        var response = await _client.PostAsync(
            $"/api/workflow-templates/non-existent/create?projectId={_projectId}", null);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task CreateFromTemplate_NonExistentProject_ReturnsNotFound()
    {
        // Act
        var response = await _client.PostAsync(
            "/api/workflow-templates/some-template/create?projectId=non-existent", null);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
