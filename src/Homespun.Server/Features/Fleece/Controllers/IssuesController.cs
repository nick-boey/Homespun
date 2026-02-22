using Fleece.Core.Models;
using Homespun.Features.Fleece.Services;
using Homespun.Features.Notifications;
using Homespun.Features.Projects;
using Homespun.Shared.Models.Fleece;
using Homespun.Shared.Requests;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

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
    IHubContext<NotificationHub> notificationHub,
    IIssueBranchResolverService branchResolverService,
    ILogger<IssuesController> logger) : ControllerBase
{
    /// <summary>
    /// Get all issues for a project.
    /// </summary>
    [HttpGet("projects/{projectId}/issues")]
    [ProducesResponseType<List<IssueResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<IssueResponse>>> GetByProject(
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
        var response = issues.ToResponseList();
        logger.LogDebug("Returning {Count} issues for project {ProjectId}", response.Count, projectId);
        return Ok(response);
    }

    /// <summary>
    /// Get ready issues for a project (issues with no blocking dependencies).
    /// </summary>
    [HttpGet("projects/{projectId}/issues/ready")]
    [ProducesResponseType<List<IssueResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<IssueResponse>>> GetReadyIssues(string projectId)
    {
        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        var issues = await fleeceService.GetReadyIssuesAsync(project.LocalPath);
        var response = issues.ToResponseList();
        logger.LogDebug("Returning {Count} ready issues for project {ProjectId}", response.Count, projectId);
        return Ok(response);
    }

    /// <summary>
    /// Get an issue by ID.
    /// </summary>
    [HttpGet("issues/{issueId}")]
    [ProducesResponseType<IssueResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IssueResponse>> GetById(string issueId, [FromQuery] string projectId)
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
        return Ok(issue.ToResponse());
    }

    /// <summary>
    /// Get the resolved branch name for an issue.
    /// Checks linked PRs first, then existing clones with matching issue ID.
    /// Returns null if no existing branch is found.
    /// </summary>
    [HttpGet("issues/{issueId}/resolved-branch")]
    [ProducesResponseType<ResolvedBranchResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ResolvedBranchResponse>> GetResolvedBranch(string issueId, [FromQuery] string projectId)
    {
        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        var branchName = await branchResolverService.ResolveIssueBranchAsync(projectId, issueId);
        return Ok(new ResolvedBranchResponse { BranchName = branchName });
    }

    /// <summary>
    /// Create a new issue.
    /// If a working branch ID is provided, it will be applied to the issue.
    /// Otherwise, the client should trigger branch ID generation on the edit page.
    /// </summary>
    [HttpPost("issues")]
    [ProducesResponseType<IssueResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IssueResponse>> Create([FromBody] CreateIssueRequest request)
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
            request.ExecutionMode);

        // Apply provided working branch ID if any
        if (!string.IsNullOrWhiteSpace(request.WorkingBranchId))
        {
            issue = await fleeceService.UpdateIssueAsync(
                project.LocalPath,
                issue.Id,
                workingBranchId: request.WorkingBranchId.Trim()) ?? issue;
        }

        // If a parent issue ID was provided, add this issue as a child of that parent
        if (!string.IsNullOrWhiteSpace(request.ParentIssueId))
        {
            issue = await fleeceService.AddParentAsync(
                project.LocalPath,
                issue.Id,
                request.ParentIssueId.Trim(),
                sortOrder: request.ParentSortOrder);
        }

        // If a child issue ID was provided, make the new issue the parent of that child
        if (!string.IsNullOrWhiteSpace(request.ChildIssueId))
        {
            await fleeceService.AddParentAsync(
                project.LocalPath,
                request.ChildIssueId.Trim(),
                issue.Id);
        }

        // Broadcast issue creation to connected clients
        await notificationHub.BroadcastIssuesChanged(request.ProjectId, IssueChangeType.Created, issue.Id);

        return CreatedAtAction(
            nameof(GetById),
            new { issueId = issue.Id, projectId = request.ProjectId },
            issue.ToResponse());
    }

    /// <summary>
    /// Update an issue.
    /// </summary>
    [HttpPut("issues/{issueId}")]
    [ProducesResponseType<IssueResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IssueResponse>> Update(string issueId, [FromBody] UpdateIssueRequest request)
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
            request.Priority,
            workingBranchId: request.WorkingBranchId);

        if (issue == null)
        {
            return NotFound("Issue not found");
        }

        // Broadcast issue update to connected clients
        await notificationHub.BroadcastIssuesChanged(request.ProjectId, IssueChangeType.Updated, issueId);

        return Ok(issue.ToResponse());
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

        // Broadcast issue deletion to connected clients
        await notificationHub.BroadcastIssuesChanged(projectId, IssueChangeType.Deleted, issueId);

        return NoContent();
    }
}
