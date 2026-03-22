using Homespun.Features.Projects;
using Homespun.Features.Workflows.Controllers;
using Homespun.Features.Workflows.Services;
using Homespun.Shared.Models.Projects;
using Homespun.Shared.Models.Workflows;
using Homespun.Shared.Requests;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Homespun.Tests.Features.Workflows;

/// <summary>
/// Unit tests for WorkflowsController.
/// </summary>
[TestFixture]
public class WorkflowsControllerTests
{
    private Mock<IWorkflowStorageService> _workflowStorageServiceMock = null!;
    private Mock<IWorkflowExecutionService> _workflowExecutionServiceMock = null!;
    private Mock<IWorkflowContextStore> _workflowContextStoreMock = null!;
    private Mock<IProjectService> _projectServiceMock = null!;
    private WorkflowsController _controller = null!;

    private static readonly Project TestProject = new()
    {
        Id = "project-123",
        Name = "Test Project",
        LocalPath = "/path/to/project",
        DefaultBranch = "main"
    };

    private static WorkflowDefinition CreateTestWorkflow(string id, string title) => new()
    {
        Id = id,
        ProjectId = TestProject.Id,
        Title = title,
        Description = "Test workflow description",
        Nodes = [],
        Edges = [],
        Enabled = true,
        Version = 1,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private static WorkflowExecution CreateTestExecution(string id, string workflowId) => new()
    {
        Id = id,
        WorkflowId = workflowId,
        ProjectId = TestProject.Id,
        Status = WorkflowExecutionStatus.Running,
        Trigger = new ExecutionTriggerInfo
        {
            Type = WorkflowTriggerType.Manual,
            Timestamp = DateTime.UtcNow
        },
        CreatedAt = DateTime.UtcNow,
        StartedAt = DateTime.UtcNow
    };

    [SetUp]
    public void SetUp()
    {
        _workflowStorageServiceMock = new Mock<IWorkflowStorageService>();
        _workflowExecutionServiceMock = new Mock<IWorkflowExecutionService>();
        _workflowContextStoreMock = new Mock<IWorkflowContextStore>();
        _projectServiceMock = new Mock<IProjectService>();

        _controller = new WorkflowsController(
            _workflowStorageServiceMock.Object,
            _workflowExecutionServiceMock.Object,
            _workflowContextStoreMock.Object,
            _projectServiceMock.Object,
            NullLogger<WorkflowsController>.Instance);

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
    }

    #region Create Tests

    [Test]
    public async Task Create_ReturnsCreatedResult_WithWorkflow()
    {
        // Arrange
        var workflow = CreateTestWorkflow("wf-123", "Test Workflow");

        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _workflowStorageServiceMock
            .Setup(x => x.CreateWorkflowAsync(
                TestProject.LocalPath,
                It.IsAny<CreateWorkflowParams>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var request = new CreateWorkflowRequest
        {
            ProjectId = TestProject.Id,
            Title = "Test Workflow",
            Description = "Test description"
        };

        // Act
        var result = await _controller.Create(request);

        // Assert
        Assert.That(result.Result, Is.TypeOf<CreatedAtActionResult>());
        var createdResult = (CreatedAtActionResult)result.Result!;
        var returnedWorkflow = (WorkflowDefinition)createdResult.Value!;
        Assert.That(returnedWorkflow.Id, Is.EqualTo(workflow.Id));
        Assert.That(returnedWorkflow.Title, Is.EqualTo(workflow.Title));
    }

    [Test]
    public async Task Create_ProjectNotFound_ReturnsNotFound()
    {
        // Arrange
        _projectServiceMock
            .Setup(x => x.GetByIdAsync(It.IsAny<string>()))
            .ReturnsAsync((Project?)null);

        var request = new CreateWorkflowRequest
        {
            ProjectId = "nonexistent",
            Title = "Test Workflow"
        };

        // Act
        var result = await _controller.Create(request);

        // Assert
        Assert.That(result.Result, Is.TypeOf<NotFoundObjectResult>());
    }

    #endregion

    #region GetByProject Tests

    [Test]
    public async Task GetByProject_ReturnsWorkflows()
    {
        // Arrange
        var workflows = new List<WorkflowDefinition>
        {
            CreateTestWorkflow("wf-1", "Workflow 1"),
            CreateTestWorkflow("wf-2", "Workflow 2")
        };

        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _workflowStorageServiceMock
            .Setup(x => x.ListWorkflowsAsync(TestProject.LocalPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflows);

        // Act
        var result = await _controller.GetByProject(TestProject.Id);

        // Assert
        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result!;
        var response = (WorkflowListResponse)okResult.Value!;
        Assert.That(response.Workflows, Has.Count.EqualTo(2));
        Assert.That(response.TotalCount, Is.EqualTo(2));
    }

    [Test]
    public async Task GetByProject_ProjectNotFound_ReturnsNotFound()
    {
        // Arrange
        _projectServiceMock
            .Setup(x => x.GetByIdAsync(It.IsAny<string>()))
            .ReturnsAsync((Project?)null);

        // Act
        var result = await _controller.GetByProject("nonexistent");

        // Assert
        Assert.That(result.Result, Is.TypeOf<NotFoundObjectResult>());
    }

    #endregion

    #region GetById Tests

    [Test]
    public async Task GetById_ReturnsWorkflow()
    {
        // Arrange
        var workflow = CreateTestWorkflow("wf-123", "Test Workflow");

        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _workflowStorageServiceMock
            .Setup(x => x.GetWorkflowAsync(TestProject.LocalPath, "wf-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        // Act
        var result = await _controller.GetById("wf-123", TestProject.Id);

        // Assert
        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result!;
        var returnedWorkflow = (WorkflowDefinition)okResult.Value!;
        Assert.That(returnedWorkflow.Id, Is.EqualTo(workflow.Id));
    }

    [Test]
    public async Task GetById_WorkflowNotFound_ReturnsNotFound()
    {
        // Arrange
        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _workflowStorageServiceMock
            .Setup(x => x.GetWorkflowAsync(TestProject.LocalPath, "nonexistent", It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowDefinition?)null);

        // Act
        var result = await _controller.GetById("nonexistent", TestProject.Id);

        // Assert
        Assert.That(result.Result, Is.TypeOf<NotFoundObjectResult>());
    }

    [Test]
    public async Task GetById_ProjectNotFound_ReturnsNotFound()
    {
        // Arrange
        _projectServiceMock
            .Setup(x => x.GetByIdAsync(It.IsAny<string>()))
            .ReturnsAsync((Project?)null);

        // Act
        var result = await _controller.GetById("wf-123", "nonexistent");

        // Assert
        Assert.That(result.Result, Is.TypeOf<NotFoundObjectResult>());
    }

    #endregion

    #region Update Tests

    [Test]
    public async Task Update_ReturnsUpdatedWorkflow()
    {
        // Arrange
        var updatedWorkflow = CreateTestWorkflow("wf-123", "Updated Workflow");

        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _workflowStorageServiceMock
            .Setup(x => x.UpdateWorkflowAsync(
                TestProject.LocalPath,
                "wf-123",
                It.IsAny<UpdateWorkflowParams>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(updatedWorkflow);

        var request = new UpdateWorkflowRequest
        {
            ProjectId = TestProject.Id,
            Title = "Updated Workflow"
        };

        // Act
        var result = await _controller.Update("wf-123", request);

        // Assert
        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result!;
        var returnedWorkflow = (WorkflowDefinition)okResult.Value!;
        Assert.That(returnedWorkflow.Title, Is.EqualTo("Updated Workflow"));
    }

    [Test]
    public async Task Update_WorkflowNotFound_ReturnsNotFound()
    {
        // Arrange
        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _workflowStorageServiceMock
            .Setup(x => x.UpdateWorkflowAsync(
                TestProject.LocalPath,
                "nonexistent",
                It.IsAny<UpdateWorkflowParams>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowDefinition?)null);

        var request = new UpdateWorkflowRequest
        {
            ProjectId = TestProject.Id,
            Title = "Updated Workflow"
        };

        // Act
        var result = await _controller.Update("nonexistent", request);

        // Assert
        Assert.That(result.Result, Is.TypeOf<NotFoundObjectResult>());
    }

    [Test]
    public async Task Update_ProjectNotFound_ReturnsNotFound()
    {
        // Arrange
        _projectServiceMock
            .Setup(x => x.GetByIdAsync(It.IsAny<string>()))
            .ReturnsAsync((Project?)null);

        var request = new UpdateWorkflowRequest
        {
            ProjectId = "nonexistent",
            Title = "Updated Workflow"
        };

        // Act
        var result = await _controller.Update("wf-123", request);

        // Assert
        Assert.That(result.Result, Is.TypeOf<NotFoundObjectResult>());
    }

    #endregion

    #region Delete Tests

    [Test]
    public async Task Delete_ReturnsNoContent()
    {
        // Arrange
        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _workflowStorageServiceMock
            .Setup(x => x.DeleteWorkflowAsync(TestProject.LocalPath, "wf-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _controller.Delete("wf-123", TestProject.Id);

        // Assert
        Assert.That(result, Is.TypeOf<NoContentResult>());
    }

    [Test]
    public async Task Delete_WorkflowNotFound_ReturnsNotFound()
    {
        // Arrange
        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _workflowStorageServiceMock
            .Setup(x => x.DeleteWorkflowAsync(TestProject.LocalPath, "nonexistent", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _controller.Delete("nonexistent", TestProject.Id);

        // Assert
        Assert.That(result, Is.TypeOf<NotFoundObjectResult>());
    }

    [Test]
    public async Task Delete_ProjectNotFound_ReturnsNotFound()
    {
        // Arrange
        _projectServiceMock
            .Setup(x => x.GetByIdAsync(It.IsAny<string>()))
            .ReturnsAsync((Project?)null);

        // Act
        var result = await _controller.Delete("wf-123", "nonexistent");

        // Assert
        Assert.That(result, Is.TypeOf<NotFoundObjectResult>());
    }

    #endregion

    #region Execute Tests

    [Test]
    public async Task Execute_ReturnsAccepted_WithExecutionResponse()
    {
        // Arrange
        var workflow = CreateTestWorkflow("wf-123", "Test Workflow");
        var execution = CreateTestExecution("exec-123", "wf-123");

        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _workflowStorageServiceMock
            .Setup(x => x.GetWorkflowAsync(TestProject.LocalPath, "wf-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);
        _workflowExecutionServiceMock
            .Setup(x => x.StartWorkflowAsync(
                TestProject.LocalPath,
                "wf-123",
                It.IsAny<TriggerContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StartWorkflowResult
            {
                Success = true,
                Execution = execution
            });

        var request = new ExecuteWorkflowRequest
        {
            ProjectId = TestProject.Id
        };

        // Act
        var result = await _controller.Execute("wf-123", request);

        // Assert
        Assert.That(result.Result, Is.TypeOf<AcceptedResult>());
        var acceptedResult = (AcceptedResult)result.Result!;
        var response = (WorkflowExecutionResponse)acceptedResult.Value!;
        Assert.That(response.ExecutionId, Is.EqualTo(execution.Id));
        Assert.That(response.WorkflowId, Is.EqualTo(workflow.Id));
        Assert.That(response.Status, Is.EqualTo(WorkflowExecutionStatus.Running));
    }

    [Test]
    public async Task Execute_WorkflowNotFound_ReturnsNotFound()
    {
        // Arrange
        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _workflowStorageServiceMock
            .Setup(x => x.GetWorkflowAsync(TestProject.LocalPath, "nonexistent", It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowDefinition?)null);

        var request = new ExecuteWorkflowRequest
        {
            ProjectId = TestProject.Id
        };

        // Act
        var result = await _controller.Execute("nonexistent", request);

        // Assert
        Assert.That(result.Result, Is.TypeOf<NotFoundObjectResult>());
    }

    [Test]
    public async Task Execute_WorkflowDisabled_ReturnsBadRequest()
    {
        // Arrange
        var workflow = CreateTestWorkflow("wf-123", "Disabled Workflow");
        workflow.Enabled = false;

        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _workflowStorageServiceMock
            .Setup(x => x.GetWorkflowAsync(TestProject.LocalPath, "wf-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var request = new ExecuteWorkflowRequest
        {
            ProjectId = TestProject.Id
        };

        // Act
        var result = await _controller.Execute("wf-123", request);

        // Assert
        Assert.That(result.Result, Is.TypeOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task Execute_StartFails_ReturnsBadRequest()
    {
        // Arrange
        var workflow = CreateTestWorkflow("wf-123", "Test Workflow");

        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _workflowStorageServiceMock
            .Setup(x => x.GetWorkflowAsync(TestProject.LocalPath, "wf-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);
        _workflowExecutionServiceMock
            .Setup(x => x.StartWorkflowAsync(
                TestProject.LocalPath,
                "wf-123",
                It.IsAny<TriggerContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StartWorkflowResult
            {
                Success = false,
                Error = "Failed to start workflow"
            });

        var request = new ExecuteWorkflowRequest
        {
            ProjectId = TestProject.Id
        };

        // Act
        var result = await _controller.Execute("wf-123", request);

        // Assert
        Assert.That(result.Result, Is.TypeOf<BadRequestObjectResult>());
    }

    #endregion

    #region ListExecutions Tests

    [Test]
    public async Task ListExecutions_ReturnsExecutions()
    {
        // Arrange
        var workflow = CreateTestWorkflow("wf-123", "Test Workflow");
        var executions = new List<WorkflowExecution>
        {
            CreateTestExecution("exec-1", "wf-123"),
            CreateTestExecution("exec-2", "wf-123")
        };

        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _workflowStorageServiceMock
            .Setup(x => x.GetWorkflowAsync(TestProject.LocalPath, "wf-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);
        _workflowExecutionServiceMock
            .Setup(x => x.ListExecutionsAsync(TestProject.LocalPath, "wf-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(executions);

        // Act
        var result = await _controller.ListExecutions("wf-123", TestProject.Id);

        // Assert
        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result!;
        var response = (ExecutionListResponse)okResult.Value!;
        Assert.That(response.Executions, Has.Count.EqualTo(2));
        Assert.That(response.TotalCount, Is.EqualTo(2));
    }

    [Test]
    public async Task ListExecutions_WorkflowNotFound_ReturnsNotFound()
    {
        // Arrange
        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _workflowStorageServiceMock
            .Setup(x => x.GetWorkflowAsync(TestProject.LocalPath, "nonexistent", It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowDefinition?)null);

        // Act
        var result = await _controller.ListExecutions("nonexistent", TestProject.Id);

        // Assert
        Assert.That(result.Result, Is.TypeOf<NotFoundObjectResult>());
    }

    #endregion

    #region GetExecution Tests

    [Test]
    public async Task GetExecution_ReturnsExecution()
    {
        // Arrange
        var execution = CreateTestExecution("exec-123", "wf-123");

        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _workflowExecutionServiceMock
            .Setup(x => x.GetExecutionAsync(TestProject.LocalPath, "exec-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(execution);

        // Act
        var result = await _controller.GetExecution("exec-123", TestProject.Id);

        // Assert
        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result!;
        var returnedExecution = (WorkflowExecution)okResult.Value!;
        Assert.That(returnedExecution.Id, Is.EqualTo(execution.Id));
    }

    [Test]
    public async Task GetExecution_ExecutionNotFound_ReturnsNotFound()
    {
        // Arrange
        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _workflowExecutionServiceMock
            .Setup(x => x.GetExecutionAsync(TestProject.LocalPath, "nonexistent", It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowExecution?)null);

        // Act
        var result = await _controller.GetExecution("nonexistent", TestProject.Id);

        // Assert
        Assert.That(result.Result, Is.TypeOf<NotFoundObjectResult>());
    }

    #endregion

    #region CancelExecution Tests

    [Test]
    public async Task CancelExecution_ReturnsOk()
    {
        // Arrange
        var execution = CreateTestExecution("exec-123", "wf-123");
        execution.Status = WorkflowExecutionStatus.Cancelled;

        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _workflowExecutionServiceMock
            .Setup(x => x.CancelExecutionAsync(TestProject.LocalPath, "exec-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _workflowExecutionServiceMock
            .Setup(x => x.GetExecutionAsync(TestProject.LocalPath, "exec-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(execution);

        var request = new CancelWorkflowExecutionRequest
        {
            ProjectId = TestProject.Id,
            Reason = "User cancelled"
        };

        // Act
        var result = await _controller.CancelExecution("exec-123", request);

        // Assert
        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result!;
        var response = (WorkflowExecutionResponse)okResult.Value!;
        Assert.That(response.ExecutionId, Is.EqualTo(execution.Id));
        Assert.That(response.Status, Is.EqualTo(WorkflowExecutionStatus.Cancelled));
    }

    [Test]
    public async Task CancelExecution_ExecutionNotFound_ReturnsNotFound()
    {
        // Arrange
        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _workflowExecutionServiceMock
            .Setup(x => x.CancelExecutionAsync(TestProject.LocalPath, "nonexistent", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var request = new CancelWorkflowExecutionRequest
        {
            ProjectId = TestProject.Id
        };

        // Act
        var result = await _controller.CancelExecution("nonexistent", request);

        // Assert
        Assert.That(result.Result, Is.TypeOf<NotFoundObjectResult>());
    }

    #endregion

    #region GetExecutionContext Tests

    [Test]
    public async Task GetExecutionContext_ReturnsContext()
    {
        // Arrange
        var context = new StoredWorkflowContext
        {
            ExecutionId = "exec-123",
            WorkflowId = "wf-123",
            WorkingDirectory = "/path/to/work",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _workflowContextStoreMock
            .Setup(x => x.GetContextAsync(TestProject.LocalPath, "exec-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(context);

        // Act
        var result = await _controller.GetExecutionContext("exec-123", TestProject.Id);

        // Assert
        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result!;
        var returnedContext = (StoredWorkflowContext)okResult.Value!;
        Assert.That(returnedContext.ExecutionId, Is.EqualTo(context.ExecutionId));
    }

    [Test]
    public async Task GetExecutionContext_ContextNotFound_ReturnsNotFound()
    {
        // Arrange
        _projectServiceMock
            .Setup(x => x.GetByIdAsync(TestProject.Id))
            .ReturnsAsync(TestProject);
        _workflowContextStoreMock
            .Setup(x => x.GetContextAsync(TestProject.LocalPath, "nonexistent", It.IsAny<CancellationToken>()))
            .ReturnsAsync((StoredWorkflowContext?)null);

        // Act
        var result = await _controller.GetExecutionContext("nonexistent", TestProject.Id);

        // Assert
        Assert.That(result.Result, Is.TypeOf<NotFoundObjectResult>());
    }

    #endregion
}
