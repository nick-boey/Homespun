using Homespun.Features.Containers.Services;
using Homespun.Shared;
using Homespun.Shared.Models.Containers;
using Microsoft.AspNetCore.Mvc;

namespace Homespun.Features.Containers.Controllers;

/// <summary>
/// Controller for managing worker containers.
/// </summary>
[ApiController]
[Route(ApiRoutes.Containers)]
public class ContainersController(IContainerQueryService containerService) : ControllerBase
{
    /// <summary>
    /// Gets all worker containers currently running.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<WorkerContainerDto>>> GetAll(CancellationToken cancellationToken)
    {
        var containers = await containerService.GetAllContainersAsync(cancellationToken);
        return Ok(containers.ToList());
    }

    /// <summary>
    /// Stops a container by ID.
    /// </summary>
    /// <param name="containerId">The container ID to stop.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpDelete("{containerId}")]
    public async Task<IActionResult> Stop(string containerId, CancellationToken cancellationToken)
    {
        var result = await containerService.StopContainerAsync(containerId, cancellationToken);
        if (!result)
        {
            return NotFound();
        }
        return NoContent();
    }
}
