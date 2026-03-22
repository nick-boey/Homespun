using Homespun.Features.Workflows.Services;
using Microsoft.AspNetCore.Mvc;

namespace Homespun.Features.Workflows.Controllers;

/// <summary>
/// API endpoints for managing workflows.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class WorkflowsController(IWorkflowService workflowService) : ControllerBase
{
}
