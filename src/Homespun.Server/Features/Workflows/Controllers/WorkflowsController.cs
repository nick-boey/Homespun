using Homespun.Features.Projects;
using Homespun.Features.Workflows.Services;
using Homespun.Shared.Models.Workflows;
using Homespun.Shared.Requests;
using Microsoft.AspNetCore.Mvc;

namespace Homespun.Features.Workflows.Controllers;

/// <summary>
/// API endpoints for managing workflows and workflow executions.
/// </summary>
[ApiController]
[Route("api")]
[Produces("application/json")]
public class WorkflowsController(
    IWorkflowStorageService workflowStorageService,
    IWorkflowExecutionService workflowExecutionService,
    IWorkflowContextStore workflowContextStore,
    IProjectService projectService,
    ILogger<WorkflowsController> logger) : ControllerBase
{
    #region Workflow CRUD

    /// <summary>
    /// Create a new workflow.
    /// </summary>
    [HttpPost("workflows")]
    [ProducesResponseType<WorkflowDefinition>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WorkflowDefinition>> Create([FromBody] CreateWorkflowRequest request)
    {
        var project = await projectService.GetByIdAsync(request.ProjectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        var createParams = new CreateWorkflowParams
        {
            ProjectId = request.ProjectId,
            Title = request.Title,
            Description = request.Description,
            Steps = request.Steps,
            Trigger = request.Trigger,
            Settings = request.Settings,
            Enabled = request.Enabled
        };

        var workflow = await workflowStorageService.CreateWorkflowAsync(project.LocalPath, createParams);

        logger.LogInformation("Created workflow {WorkflowId} in project {ProjectId}", workflow.Id, request.ProjectId);

        return CreatedAtAction(
            nameof(GetById),
            new { workflowId = workflow.Id, projectId = request.ProjectId },
            workflow);
    }

    /// <summary>
    /// List all workflows for a project.
    /// </summary>
    [HttpGet("projects/{projectId}/workflows")]
    [ProducesResponseType<WorkflowListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WorkflowListResponse>> GetByProject(string projectId)
    {
        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        var workflows = await workflowStorageService.ListWorkflowsAsync(project.LocalPath);

        var response = new WorkflowListResponse
        {
            Workflows = workflows.Select(w => new WorkflowSummary
            {
                Id = w.Id,
                Title = w.Title,
                Description = w.Description,
                Enabled = w.Enabled,
                TriggerType = w.Trigger?.Type,
                StepCount = w.Steps.Count,
                Version = w.Version,
                UpdatedAt = w.UpdatedAt
            }).ToList(),
            TotalCount = workflows.Count
        };

        logger.LogDebug("Returning {Count} workflows for project {ProjectId}", response.TotalCount, projectId);

        return Ok(response);
    }

    /// <summary>
    /// Get a workflow definition by ID.
    /// </summary>
    [HttpGet("workflows/{workflowId}")]
    [ProducesResponseType<WorkflowDefinition>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WorkflowDefinition>> GetById(string workflowId, [FromQuery] string projectId)
    {
        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        var workflow = await workflowStorageService.GetWorkflowAsync(project.LocalPath, workflowId);
        if (workflow == null)
        {
            return NotFound("Workflow not found");
        }

        return Ok(workflow);
    }

    /// <summary>
    /// Update a workflow definition.
    /// </summary>
    [HttpPut("workflows/{workflowId}")]
    [ProducesResponseType<WorkflowDefinition>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WorkflowDefinition>> Update(string workflowId, [FromBody] UpdateWorkflowRequest request)
    {
        var project = await projectService.GetByIdAsync(request.ProjectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        var updateParams = new UpdateWorkflowParams
        {
            Title = request.Title,
            Description = request.Description,
            Steps = request.Steps,
            Trigger = request.Trigger,
            Settings = request.Settings,
            Enabled = request.Enabled
        };

        var workflow = await workflowStorageService.UpdateWorkflowAsync(project.LocalPath, workflowId, updateParams);
        if (workflow == null)
        {
            return NotFound("Workflow not found");
        }

        logger.LogInformation("Updated workflow {WorkflowId} in project {ProjectId}", workflowId, request.ProjectId);

        return Ok(workflow);
    }

    /// <summary>
    /// Delete a workflow.
    /// </summary>
    [HttpDelete("workflows/{workflowId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string workflowId, [FromQuery] string projectId)
    {
        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        var deleted = await workflowStorageService.DeleteWorkflowAsync(project.LocalPath, workflowId);
        if (!deleted)
        {
            return NotFound("Workflow not found");
        }

        logger.LogInformation("Deleted workflow {WorkflowId} from project {ProjectId}", workflowId, projectId);

        return NoContent();
    }

    #endregion

    #region Workflow Execution

    /// <summary>
    /// Start a workflow execution.
    /// </summary>
    [HttpPost("workflows/{workflowId}/execute")]
    [ProducesResponseType<WorkflowExecutionResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WorkflowExecutionResponse>> Execute(string workflowId, [FromBody] ExecuteWorkflowRequest request)
    {
        var project = await projectService.GetByIdAsync(request.ProjectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        var workflow = await workflowStorageService.GetWorkflowAsync(project.LocalPath, workflowId);
        if (workflow == null)
        {
            return NotFound("Workflow not found");
        }

        if (!workflow.Enabled)
        {
            return BadRequest("Workflow is disabled");
        }

        var triggerContext = new TriggerContext
        {
            TriggerType = WorkflowTriggerType.Manual,
            Input = request.Input ?? []
        };

        if (request.Environment != null)
        {
            foreach (var kvp in request.Environment)
            {
                triggerContext.Input[$"env.{kvp.Key}"] = kvp.Value;
            }
        }

        var result = await workflowExecutionService.StartWorkflowAsync(project.LocalPath, workflowId, triggerContext);

        if (!result.Success)
        {
            return BadRequest(result.Error);
        }

        logger.LogInformation(
            "Started workflow execution {ExecutionId} for workflow {WorkflowId} in project {ProjectId}",
            result.Execution!.Id, workflowId, request.ProjectId);

        return Accepted(new WorkflowExecutionResponse
        {
            ExecutionId = result.Execution.Id,
            WorkflowId = workflowId,
            Status = result.Execution.Status,
            Message = "Workflow execution started"
        });
    }

    /// <summary>
    /// List executions for a workflow.
    /// </summary>
    [HttpGet("workflows/{workflowId}/executions")]
    [ProducesResponseType<ExecutionListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ExecutionListResponse>> ListExecutions(string workflowId, [FromQuery] string projectId)
    {
        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        var workflow = await workflowStorageService.GetWorkflowAsync(project.LocalPath, workflowId);
        if (workflow == null)
        {
            return NotFound("Workflow not found");
        }

        var executions = await workflowExecutionService.ListExecutionsAsync(project.LocalPath, workflowId);

        var response = new ExecutionListResponse
        {
            Executions = executions.Select(e => new ExecutionSummary
            {
                Id = e.Id,
                WorkflowId = e.WorkflowId,
                WorkflowTitle = workflow.Title,
                Status = e.Status,
                TriggerType = e.Trigger.Type,
                CreatedAt = e.CreatedAt,
                StartedAt = e.StartedAt,
                CompletedAt = e.CompletedAt,
                DurationMs = e.CompletedAt.HasValue && e.StartedAt.HasValue
                    ? (long)(e.CompletedAt.Value - e.StartedAt.Value).TotalMilliseconds
                    : null,
                TriggeredBy = e.TriggeredBy,
                ErrorMessage = e.ErrorMessage
            }).ToList(),
            TotalCount = executions.Count
        };

        logger.LogDebug(
            "Returning {Count} executions for workflow {WorkflowId} in project {ProjectId}",
            response.TotalCount, workflowId, projectId);

        return Ok(response);
    }

    /// <summary>
    /// Get the state of a workflow execution.
    /// </summary>
    [HttpGet("executions/{executionId}")]
    [ProducesResponseType<WorkflowExecution>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WorkflowExecution>> GetExecution(string executionId, [FromQuery] string projectId)
    {
        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        var execution = await workflowExecutionService.GetExecutionAsync(project.LocalPath, executionId);
        if (execution == null)
        {
            return NotFound("Execution not found");
        }

        return Ok(execution);
    }

    /// <summary>
    /// Cancel a workflow execution.
    /// </summary>
    [HttpPost("executions/{executionId}/cancel")]
    [ProducesResponseType<WorkflowExecutionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WorkflowExecutionResponse>> CancelExecution(
        string executionId,
        [FromBody] CancelWorkflowExecutionRequest request)
    {
        var project = await projectService.GetByIdAsync(request.ProjectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        var cancelled = await workflowExecutionService.CancelExecutionAsync(project.LocalPath, executionId);
        if (!cancelled)
        {
            return NotFound("Execution not found or already completed");
        }

        var execution = await workflowExecutionService.GetExecutionAsync(project.LocalPath, executionId);

        logger.LogInformation(
            "Cancelled workflow execution {ExecutionId} in project {ProjectId}. Reason: {Reason}",
            executionId, request.ProjectId, request.Reason);

        return Ok(new WorkflowExecutionResponse
        {
            ExecutionId = executionId,
            WorkflowId = execution?.WorkflowId ?? "",
            Status = execution?.Status ?? WorkflowExecutionStatus.Cancelled,
            Message = "Workflow execution cancelled"
        });
    }

    /// <summary>
    /// Get the execution context for a workflow execution.
    /// </summary>
    [HttpGet("executions/{executionId}/context")]
    [ProducesResponseType<StoredWorkflowContext>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StoredWorkflowContext>> GetExecutionContext(string executionId, [FromQuery] string projectId)
    {
        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        var context = await workflowContextStore.GetContextAsync(project.LocalPath, executionId);
        if (context == null)
        {
            return NotFound("Execution context not found");
        }

        return Ok(context);
    }

    #endregion
}
