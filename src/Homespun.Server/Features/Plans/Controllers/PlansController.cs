using System.Security;
using Homespun.Shared.Models.Plans;
using Microsoft.AspNetCore.Mvc;

namespace Homespun.Features.Plans.Controllers;

/// <summary>
/// API endpoints for accessing Claude Code plan files.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class PlansController(IPlansService plansService) : ControllerBase
{
    /// <summary>
    /// Get all plan files for a working directory.
    /// </summary>
    /// <param name="workingDirectory">The working directory containing the .claude/plans folder.</param>
    /// <returns>A list of plan file information.</returns>
    [HttpGet]
    [ProducesResponseType<List<PlanFileInfo>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<List<PlanFileInfo>>> GetPlanFiles([FromQuery] string workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            return BadRequest("Working directory is required");
        }

        var plans = await plansService.ListPlanFilesAsync(workingDirectory);
        return Ok(plans);
    }

    /// <summary>
    /// Get the content of a specific plan file.
    /// </summary>
    /// <param name="workingDirectory">The working directory containing the .claude/plans folder.</param>
    /// <param name="fileName">The name of the plan file.</param>
    /// <returns>The content of the plan file.</returns>
    [HttpGet("content")]
    [ProducesResponseType<string>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<string>> GetPlanContent(
        [FromQuery] string workingDirectory,
        [FromQuery] string fileName)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            return BadRequest("Working directory is required");
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            return BadRequest("File name is required");
        }

        try
        {
            var content = await plansService.GetPlanContentAsync(workingDirectory, fileName);

            if (content == null)
            {
                return NotFound($"Plan file '{fileName}' not found");
            }

            return Ok(content);
        }
        catch (SecurityException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
