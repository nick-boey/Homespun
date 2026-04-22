using Homespun.Features.ClaudeCode.Services;
using Homespun.Shared.Models.Sessions;
using Microsoft.AspNetCore.Mvc;

namespace Homespun.Features.ClaudeCode.Controllers;

/// <summary>
/// Server-fetched catalog of Claude models. Frontend consumes this to build
/// model-selection UI; controllers call the same catalog via
/// <see cref="IModelCatalogService.ResolveModelIdAsync"/> to resolve stored
/// aliases like <c>"opus"</c> to concrete ids before launching agents.
/// </summary>
[ApiController]
[Route("api/models")]
[Produces("application/json")]
public class ModelsController(IModelCatalogService catalog) : ControllerBase
{
    /// <summary>
    /// Returns the current Claude model catalog. Exactly one entry has
    /// <see cref="ClaudeModelInfo.IsDefault"/> set.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ClaudeModelInfo>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ClaudeModelInfo>>> List(CancellationToken cancellationToken)
    {
        var models = await catalog.ListAsync(cancellationToken);
        return Ok(models);
    }
}
