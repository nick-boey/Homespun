using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.PullRequests.Data;
using Homespun.Shared.Models.Gitgraph;
using Microsoft.AspNetCore.Mvc;

namespace Homespun.Features.AgentOrchestration.Controllers;

/// <summary>
/// Per-issue agent-status decoration map for the task-graph view.
/// Sourced from the in-memory <see cref="IClaudeSessionStore"/>; the most-recent
/// session per <c>EntityId</c> wins.
/// </summary>
[ApiController]
[Route("api")]
[Produces("application/json")]
public class AgentStatusesController(
    IDataStore dataStore,
    IClaudeSessionStore sessionStore) : ControllerBase
{
    [HttpGet("projects/{projectId}/agent-statuses")]
    [ProducesResponseType<Dictionary<string, AgentStatusData>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<Dictionary<string, AgentStatusData>> GetAgentStatuses(string projectId)
    {
        var project = dataStore.GetProject(projectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        var sessions = sessionStore.GetByProjectId(projectId);
        var byEntityId = sessions
            .Where(s => !string.IsNullOrEmpty(s.EntityId))
            .GroupBy(s => s.EntityId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => AgentStatusData.FromSession(g.OrderByDescending(s => s.LastActivityAt).First()),
                StringComparer.OrdinalIgnoreCase);

        return Ok(byEntityId);
    }
}
