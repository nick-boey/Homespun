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
}
