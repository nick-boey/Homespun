using Homespun.Features.Gitgraph.Services;
using Homespun.Shared.Models.Fleece;
using Microsoft.AspNetCore.Mvc;

namespace Homespun.Features.Gitgraph.Controllers;

/// <summary>
/// Legacy task-graph endpoint kept for the static diff view + the
/// agent OpenSpec tab. Computes <see cref="TaskGraphResponse"/> on demand
/// (no snapshot store — client-side layout makes server-side caching
/// redundant for the live view; this endpoint is read-only and only
/// hit by the diff path).
/// </summary>
[ApiController]
[Route("api/graph")]
[Produces("application/json")]
public class GraphController(IGraphService graphService) : ControllerBase
{
    [HttpGet("{projectId}/taskgraph/data")]
    [ProducesResponseType<TaskGraphResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TaskGraphResponse>> GetTaskGraphData(
        string projectId,
        [FromQuery] int maxPastPRs = 5)
    {
        var taskGraph = await graphService.BuildEnhancedTaskGraphAsync(projectId, maxPastPRs);
        if (taskGraph == null)
            return NotFound("No task graph available for this project.");
        return Ok(taskGraph);
    }
}
