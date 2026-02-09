using Homespun.Features.Fleece.Services;
using Homespun.Features.Projects;
using Microsoft.AspNetCore.Mvc;

namespace Homespun.Features.Fleece.Controllers;

[ApiController]
[Route("api/fleece-sync")]
[Produces("application/json")]
public class FleeceIssueSyncController(
    IFleeceIssuesSyncService fleeceIssuesSyncService,
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

        return Ok(result);
    }
}
