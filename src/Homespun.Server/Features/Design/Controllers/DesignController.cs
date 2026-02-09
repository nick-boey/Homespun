using Homespun.Features.Design;
using Microsoft.AspNetCore.Mvc;

namespace Homespun.Server.Features.Design.Controllers;

[ApiController]
[Route("api/design")]
public class DesignController(IComponentRegistryService componentRegistry) : ControllerBase
{
    [HttpGet("components")]
    public ActionResult<IReadOnlyList<ComponentMetadata>> GetAllComponents()
    {
        return Ok(componentRegistry.GetAllComponents());
    }

    [HttpGet("components/{id}")]
    public ActionResult<ComponentMetadata> GetComponent(string id)
    {
        var component = componentRegistry.GetComponent(id);
        if (component == null)
            return NotFound();
        return Ok(component);
    }

    [HttpGet("categories")]
    public ActionResult<IReadOnlyList<string>> GetCategories()
    {
        return Ok(componentRegistry.GetCategories());
    }

    [HttpGet("categories/{category}/components")]
    public ActionResult<IReadOnlyList<ComponentMetadata>> GetComponentsByCategory(string category)
    {
        return Ok(componentRegistry.GetComponentsByCategory(category));
    }
}
