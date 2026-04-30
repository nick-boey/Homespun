using Homespun.Features.Notifications;
using Homespun.Features.Projects;
using Homespun.Shared.Models.Fleece;
using Homespun.Shared.Models.Git;
using Homespun.Shared.Requests;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace Homespun.Features.Git.Controllers;

/// <summary>
/// API endpoints for managing Git clones scoped to a project.
/// </summary>
[ApiController]
[Route("api/projects/{projectId}/clones")]
[Produces("application/json")]
public class ProjectClonesController(
    IGitCloneService cloneService,
    IProjectService projectService,
    ICloneEnrichmentService cloneEnrichmentService,
    IHubContext<NotificationHub> notificationHub) : ControllerBase
{
    /// <summary>
    /// List clones for a project.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<List<CloneInfo>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<CloneInfo>>> List(string projectId)
    {
        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        var clones = await cloneService.ListClonesAsync(project.LocalPath);
        return Ok(clones);
    }

    /// <summary>
    /// List enriched clones for a project with linked issue and PR data.
    /// </summary>
    [HttpGet("enriched")]
    [ProducesResponseType<List<EnrichedCloneInfo>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<EnrichedCloneInfo>>> ListEnriched(string projectId)
    {
        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        var enrichedClones = await cloneEnrichmentService.EnrichClonesAsync(projectId, project.LocalPath);
        return Ok(enrichedClones);
    }

    /// <summary>
    /// Create a new clone.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<CreateCloneResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CreateCloneResponse>> Create(
        string projectId,
        [FromBody] CreateCloneRequest request)
    {
        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        var clonePath = await cloneService.CreateCloneAsync(
            project.LocalPath,
            request.BranchName,
            request.CreateBranch,
            request.BaseBranch);

        if (clonePath == null)
        {
            return BadRequest("Failed to create clone");
        }

        await InvalidateGraphSnapshotAsync(projectId);

        return Created(
            string.Empty,
            new CreateCloneResponse { Path = clonePath, BranchName = request.BranchName });
    }

    /// <summary>
    /// Delete a clone.
    /// </summary>
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string projectId, [FromQuery] string clonePath)
    {
        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        var removed = await cloneService.RemoveCloneAsync(project.LocalPath, clonePath);
        if (!removed)
        {
            return BadRequest("Failed to remove clone");
        }

        await InvalidateGraphSnapshotAsync(projectId);

        return NoContent();
    }

    /// <summary>
    /// Bulk delete multiple clones.
    /// </summary>
    [HttpDelete("bulk")]
    [ProducesResponseType<BulkDeleteClonesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BulkDeleteClonesResponse>> BulkDelete(
        string projectId,
        [FromBody] BulkDeleteClonesRequest request)
    {
        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        var results = new List<BulkDeleteResult>();
        foreach (var path in request.ClonePaths)
        {
            var success = await cloneService.RemoveCloneAsync(project.LocalPath, path);
            results.Add(new BulkDeleteResult
            {
                ClonePath = path,
                Success = success,
                Error = success ? null : "Failed to remove clone"
            });
        }

        if (results.Any(r => r.Success))
        {
            await InvalidateGraphSnapshotAsync(projectId);
        }

        return Ok(new BulkDeleteClonesResponse { Results = results });
    }

    /// <summary>
    /// Check if a clone exists for a branch.
    /// </summary>
    [HttpGet("exists")]
    [ProducesResponseType<CloneExistsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CloneExistsResponse>> Exists(string projectId, [FromQuery] string branchName)
    {
        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        var exists = await cloneService.CloneExistsAsync(project.LocalPath, branchName);
        return Ok(new CloneExistsResponse { Exists = exists });
    }

    /// <summary>
    /// Prune clones (remove stale entries).
    /// </summary>
    [HttpPost("prune")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Prune(string projectId)
    {
        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        await cloneService.PruneClonesAsync(project.LocalPath);
        await InvalidateGraphSnapshotAsync(projectId);
        return NoContent();
    }

    /// <summary>
    /// Clone create/remove/prune all change <see cref="Shared.Models.Fleece.TaskGraphResponse.OpenSpecStates"/>
    /// for any issue whose branch is touched. The manual API doesn't carry an issue
    /// id, so we invalidate the whole project snapshot — clients refetch and the
    /// next /taskgraph/data response reflects the new clone topology within ~1s.
    /// </summary>
    private Task InvalidateGraphSnapshotAsync(string projectId) =>
        notificationHub.BroadcastIssueTopologyChanged(
            HttpContext.RequestServices,
            projectId,
            IssueChangeType.Updated,
            issueId: null);
}
