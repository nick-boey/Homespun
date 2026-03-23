using Homespun.Features.Projects;
using Homespun.Features.Workflows.Services;
using Homespun.Shared.Models.Workflows;
using Microsoft.AspNetCore.Mvc;

namespace Homespun.Features.Workflows.Controllers;

/// <summary>
/// API endpoints for workflow templates.
/// </summary>
[ApiController]
[Route("api/workflow-templates")]
[Produces("application/json")]
public class WorkflowTemplateController(
    IWorkflowTemplateService templateService,
    IWorkflowStorageService storageService,
    IProjectService projectService,
    ILogger<WorkflowTemplateController> logger) : ControllerBase
{
    /// <summary>
    /// List all available workflow templates.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<WorkflowTemplateSummary>>(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<WorkflowTemplateSummary>> ListTemplates()
    {
        var templates = templateService.GetTemplates();
        return Ok(templates);
    }

    /// <summary>
    /// Create a workflow from a template.
    /// </summary>
    [HttpPost("{templateId}/create")]
    [ProducesResponseType<WorkflowDefinition>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WorkflowDefinition>> CreateFromTemplate(
        string templateId,
        [FromQuery] string projectId)
    {
        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        var workflow = templateService.CreateWorkflowFromTemplate(templateId, projectId);
        if (workflow == null)
        {
            return NotFound("Template not found");
        }

        var createParams = new CreateWorkflowParams
        {
            ProjectId = projectId,
            Title = workflow.Title,
            Description = workflow.Description,
            Steps = workflow.Steps,
            Settings = workflow.Settings,
            Enabled = workflow.Enabled,
            CreatedBy = "template"
        };

        var created = await storageService.CreateWorkflowAsync(project.LocalPath, createParams);

        logger.LogInformation(
            "Created workflow {WorkflowId} from template {TemplateId} in project {ProjectId}",
            created.Id, templateId, projectId);

        return CreatedAtAction(
            actionName: nameof(WorkflowsController.GetById),
            controllerName: "Workflows",
            routeValues: new { workflowId = created.Id, projectId },
            value: created);
    }
}
