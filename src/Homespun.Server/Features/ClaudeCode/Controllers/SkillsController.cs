using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.Projects;
using Homespun.Shared.Models.Sessions;
using Microsoft.AspNetCore.Mvc;

namespace Homespun.Features.ClaudeCode.Controllers;

/// <summary>
/// API endpoints for discovering agent skills declared under a project's
/// <c>.claude/skills/</c> directory.
/// </summary>
[ApiController]
[Route("api/skills")]
[Produces("application/json")]
public class SkillsController(
    IProjectService projectService,
    ISkillDiscoveryService skillDiscovery,
    ILogger<SkillsController> logger) : ControllerBase
{
    /// <summary>
    /// List the skills available in the given project, grouped into OpenSpec,
    /// Homespun prompt skills, and general skills.
    /// </summary>
    [HttpGet("project/{projectId}")]
    [ProducesResponseType<DiscoveredSkills>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DiscoveredSkills>> GetForProject(
        string projectId,
        CancellationToken cancellationToken)
    {
        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
        {
            logger.LogWarning("Skills requested for unknown project {ProjectId}", projectId);
            return NotFound("Project not found");
        }

        var skills = await skillDiscovery.DiscoverSkillsAsync(project.LocalPath, cancellationToken);

        // Drop skill bodies over the wire — only metadata is needed for UI listing.
        // The server reads the body at dispatch time.
        return Ok(new DiscoveredSkills
        {
            OpenSpec = skills.OpenSpec.Select(StripBody).ToList(),
            Homespun = skills.Homespun.Select(StripBody).ToList(),
            General = skills.General.Select(StripBody).ToList(),
        });
    }

    private static SkillDescriptor StripBody(SkillDescriptor source) => new()
    {
        Name = source.Name,
        Description = source.Description,
        Category = source.Category,
        Mode = source.Mode,
        Args = source.Args,
        SkillBody = null,
    };
}
