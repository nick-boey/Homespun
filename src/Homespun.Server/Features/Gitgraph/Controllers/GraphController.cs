using Homespun.Features.Gitgraph.Services;
using Microsoft.AspNetCore.Mvc;

namespace Homespun.Features.Gitgraph.Controllers;

[ApiController]
[Route("api/graph")]
[Produces("application/json")]
public class GraphController(IGraphService graphService) : ControllerBase
{
    [HttpGet("{projectId}")]
    [ProducesResponseType<GitgraphJsonData>(StatusCodes.Status200OK)]
    public async Task<ActionResult<GitgraphJsonData>> GetGraph(
        string projectId,
        [FromQuery] int? maxPastPRs,
        [FromQuery] bool useCache = true)
    {
        var data = await graphService.BuildGraphJsonAsync(projectId, maxPastPRs, useCache);
        return Ok(data);
    }

    /// <summary>
    /// Gets the task graph for a project.
    /// The task graph displays issues with actionable items on the left (lane 0)
    /// and parent/blocking issues on the right (higher lanes).
    /// </summary>
    [HttpGet("{projectId}/taskgraph")]
    [ProducesResponseType<GitgraphJsonData>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GitgraphJsonData>> GetTaskGraph(string projectId)
    {
        var data = await graphService.BuildTaskGraphJsonAsync(projectId);
        if (data == null)
        {
            return NotFound("No task graph available for this project.");
        }
        return Ok(data);
    }
}
