using Homespun.Features.Fleece;
using Homespun.Features.Gitgraph.Services;
using Homespun.Shared.Models.Fleece;
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
    /// Gets the task graph for a project as plain text.
    /// The task graph displays issues with actionable items on the left (lane 0)
    /// and parent/blocking issues on the right (higher lanes).
    /// </summary>
    [HttpGet("{projectId}/taskgraph")]
    [Produces("text/plain")]
    [ProducesResponseType<string>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTaskGraph(string projectId)
    {
        var text = await graphService.BuildTaskGraphTextAsync(projectId);
        if (text == null)
        {
            return NotFound("No task graph available for this project.");
        }
        return Content(text, "text/plain");
    }

    [HttpGet("{projectId}/taskgraph/data")]
    [ProducesResponseType<TaskGraphResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TaskGraphResponse>> GetTaskGraphData(string projectId)
    {
        var taskGraph = await graphService.BuildTaskGraphAsync(projectId);
        if (taskGraph == null)
            return NotFound("No task graph available for this project.");
        return Ok(taskGraph.ToResponse());
    }
}
