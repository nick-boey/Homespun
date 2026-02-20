using Homespun.Features.Fleece.Services;
using Homespun.Features.Projects;
using Homespun.Shared.Models.Fleece;
using Microsoft.AspNetCore.Mvc;

namespace Homespun.Features.Fleece.Controllers;

[ApiController]
[Route("api/fleece-sync")]
[Produces("application/json")]
public class FleeceIssueSyncController(
    IFleeceIssuesSyncService fleeceIssuesSyncService,
    IFleeceService fleeceService,
    IProjectService projectService) : ControllerBase
{
    [HttpGet("{projectId}/branch-status")]
    [ProducesResponseType<BranchStatusResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BranchStatusResult>> GetBranchStatus(string projectId, CancellationToken ct)
    {
        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        var status = await fleeceIssuesSyncService.CheckBranchStatusAsync(
            project.LocalPath,
            project.DefaultBranch,
            ct);

        return Ok(status);
    }

    [HttpPost("{projectId}/sync")]
    [ProducesResponseType<FleeceIssueSyncResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FleeceIssueSyncResult>> Sync(string projectId, CancellationToken ct)
    {
        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        var result = await fleeceIssuesSyncService.SyncAsync(
            project.LocalPath,
            project.DefaultBranch,
            ct);

        // Reload cache from disk after sync to ensure frontend gets fresh data
        if (result.Success)
        {
            await fleeceService.ReloadFromDiskAsync(project.LocalPath, ct);
        }

        return Ok(result);
    }

    [HttpPost("{projectId}/pull")]
    [ProducesResponseType<FleecePullResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FleecePullResult>> Pull(string projectId, CancellationToken ct)
    {
        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        var result = await fleeceIssuesSyncService.PullFleeceOnlyAsync(
            project.LocalPath,
            project.DefaultBranch,
            ct);

        // Reload cache from disk after pull to ensure frontend gets fresh data
        if (result.Success)
        {
            await fleeceService.ReloadFromDiskAsync(project.LocalPath, ct);
        }

        return Ok(result);
    }
}
