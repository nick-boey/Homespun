using Fleece.Core.Models;
using Homespun.Features.AgentOrchestration.Services;
using Homespun.Features.Fleece.Services;
using Homespun.Features.Projects;
using Microsoft.AspNetCore.Mvc;

namespace Homespun.Features.Fleece.Controllers;

/// <summary>
/// API endpoints for managing Fleece issues.
/// </summary>
[ApiController]
[Route("api")]
[Produces("application/json")]
public class IssuesController(
    IFleeceService fleeceService,
    IProjectService projectService,
    IBranchIdGeneratorService branchIdGeneratorService,
    ILogger<IssuesController> logger) : ControllerBase
{
    /// <summary>
    /// Get all issues for a project.
    /// </summary>
    [HttpGet("projects/{projectId}/issues")]
    [ProducesResponseType<List<Issue>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<Issue>>> GetByProject(
        string projectId,
        [FromQuery] IssueStatus? status = null,
        [FromQuery] IssueType? type = null,
        [FromQuery] int? priority = null)
    {
        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        var issues = await fleeceService.ListIssuesAsync(project.LocalPath, status, type, priority);
        return Ok(issues.ToList());
    }

    /// <summary>
    /// Get ready issues for a project (issues with no blocking dependencies).
    /// </summary>
    [HttpGet("projects/{projectId}/issues/ready")]
    [ProducesResponseType<List<Issue>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<Issue>>> GetReadyIssues(string projectId)
    {
        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        var issues = await fleeceService.GetReadyIssuesAsync(project.LocalPath);
        return Ok(issues.ToList());
    }

    /// <summary>
    /// Get an issue by ID.
    /// </summary>
    [HttpGet("issues/{issueId}")]
    [ProducesResponseType<Issue>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Issue>> GetById(string issueId, [FromQuery] string projectId)
    {
        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        var issue = await fleeceService.GetIssueAsync(project.LocalPath, issueId);
        if (issue == null)
        {
            return NotFound("Issue not found");
        }
        return Ok(issue);
    }

    /// <summary>
    /// Create a new issue.
    /// If no working branch ID is provided, one will be auto-generated using AI.
    /// </summary>
    [HttpPost("issues")]
    [ProducesResponseType<Issue>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Issue>> Create([FromBody] CreateIssueRequest request)
    {
        var project = await projectService.GetByIdAsync(request.ProjectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        // Create the issue first
        var issue = await fleeceService.CreateIssueAsync(
            project.LocalPath,
            request.Title,
            request.Type,
            request.Description,
            request.Priority,
            request.Group);

        // Auto-generate working branch ID if not provided
        if (string.IsNullOrWhiteSpace(request.WorkingBranchId))
        {
            try
            {
                var branchIdResult = await branchIdGeneratorService.GenerateAsync(request.Title);
                if (branchIdResult.Success && !string.IsNullOrWhiteSpace(branchIdResult.BranchId))
                {
                    // Update the issue with the generated branch ID
                    issue = await fleeceService.UpdateIssueAsync(
                        project.LocalPath,
                        issue.Id,
                        workingBranchId: branchIdResult.BranchId) ?? issue;

                    logger.LogInformation(
                        "Auto-generated working branch ID '{BranchId}' for issue '{IssueId}' (AI: {WasAiGenerated})",
                        branchIdResult.BranchId, issue.Id, branchIdResult.WasAiGenerated);
                }
            }
            catch (Exception ex)
            {
                // Log but don't fail the issue creation
                logger.LogWarning(ex, "Failed to auto-generate working branch ID for issue '{IssueId}'", issue.Id);
            }
        }
        else
        {
            // Use the provided working branch ID
            issue = await fleeceService.UpdateIssueAsync(
                project.LocalPath,
                issue.Id,
                workingBranchId: request.WorkingBranchId.Trim()) ?? issue;
        }

        return CreatedAtAction(
            nameof(GetById),
            new { issueId = issue.Id, projectId = request.ProjectId },
            issue);
    }

    /// <summary>
    /// Update an issue.
    /// </summary>
    [HttpPut("issues/{issueId}")]
    [ProducesResponseType<Issue>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Issue>> Update(string issueId, [FromBody] UpdateIssueRequest request)
    {
        var project = await projectService.GetByIdAsync(request.ProjectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        var issue = await fleeceService.UpdateIssueAsync(
            project.LocalPath,
            issueId,
            request.Title,
            request.Status,
            request.Type,
            request.Description,
            request.Priority);

        if (issue == null)
        {
            return NotFound("Issue not found");
        }

        return Ok(issue);
    }

    /// <summary>
    /// Delete an issue.
    /// </summary>
    [HttpDelete("issues/{issueId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string issueId, [FromQuery] string projectId)
    {
        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        var deleted = await fleeceService.DeleteIssueAsync(project.LocalPath, issueId);
        if (!deleted)
        {
            return NotFound("Issue not found");
        }

        return NoContent();
    }
}

/// <summary>
/// Request model for creating an issue.
/// </summary>
public class CreateIssueRequest
{
    /// <summary>
    /// The project ID.
    /// </summary>
    public required string ProjectId { get; set; }

    /// <summary>
    /// Issue title.
    /// </summary>
    public required string Title { get; set; }

    /// <summary>
    /// Issue type.
    /// </summary>
    public IssueType Type { get; set; } = IssueType.Task;

    /// <summary>
    /// Issue description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Issue priority (1-5).
    /// </summary>
    public int? Priority { get; set; }

    /// <summary>
    /// Issue group for categorization.
    /// </summary>
    public string? Group { get; set; }

    /// <summary>
    /// Optional working branch ID. If not provided, one will be auto-generated using AI.
    /// </summary>
    public string? WorkingBranchId { get; set; }
}

/// <summary>
/// Request model for updating an issue.
/// </summary>
public class UpdateIssueRequest
{
    /// <summary>
    /// The project ID.
    /// </summary>
    public required string ProjectId { get; set; }

    /// <summary>
    /// Issue title.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Issue status.
    /// </summary>
    public IssueStatus? Status { get; set; }

    /// <summary>
    /// Issue type.
    /// </summary>
    public IssueType? Type { get; set; }

    /// <summary>
    /// Issue description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Issue priority (1-5).
    /// </summary>
    public int? Priority { get; set; }
}
