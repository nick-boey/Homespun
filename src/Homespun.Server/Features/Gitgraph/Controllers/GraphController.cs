using System.Diagnostics;
using Homespun.Features.Fleece;
using Homespun.Features.Gitgraph.Services;
using Homespun.Features.Gitgraph.Snapshots;
using Homespun.Shared.Models.Fleece;
using Microsoft.AspNetCore.Mvc;

namespace Homespun.Features.Gitgraph.Controllers;

[ApiController]
[Route("api/graph")]
[Produces("application/json")]
public class GraphController(
    IGraphService graphService,
    IProjectTaskGraphSnapshotStore? snapshotStore = null) : ControllerBase
{
    [HttpGet("{projectId}")]
    [ProducesResponseType<GitgraphJsonData>(StatusCodes.Status200OK)]
    public async Task<ActionResult<GitgraphJsonData>> GetGraph(
        string projectId,
        [FromQuery] int? maxPastPRs)
    {
        var data = await graphService.BuildGraphJsonAsync(projectId, maxPastPRs);
        return Ok(data);
    }

    /// <summary>
    /// Performs an incremental refresh: fetches only open PRs from GitHub,
    /// compares with cache to detect newly closed PRs, and updates the cache.
    /// Falls back to full fetch if no cache exists. Also invalidates any
    /// warm task-graph snapshots so the next /taskgraph/data call rebuilds.
    /// </summary>
    [HttpPost("{projectId}/refresh")]
    [ProducesResponseType<GitgraphJsonData>(StatusCodes.Status200OK)]
    public async Task<ActionResult<GitgraphJsonData>> RefreshGraph(
        string projectId,
        [FromQuery] int? maxPastPRs)
    {
        var data = await graphService.IncrementalRefreshAsync(projectId, maxPastPRs);
        snapshotStore?.InvalidateProject(projectId);
        return Ok(data);
    }

    /// <summary>
    /// Gets graph data using ONLY cached data. No GitHub API calls are made.
    /// Returns 404 if no cache exists.
    /// </summary>
    [HttpGet("{projectId}/cached")]
    [ProducesResponseType<GitgraphJsonData>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCachedGraph(
        string projectId,
        [FromQuery] int? maxPastPRs)
    {
        var data = await graphService.BuildGraphJsonFromCacheOnlyAsync(projectId, maxPastPRs);
        if (data == null)
            return NotFound();
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
    public async Task<ActionResult<TaskGraphResponse>> GetTaskGraphData(
        string projectId,
        [FromQuery] int maxPastPRs = 5)
    {
        if (snapshotStore is not null)
        {
            var cached = snapshotStore.TryGet(projectId, maxPastPRs);
            if (cached is not null)
            {
                Activity.Current?.SetTag("snapshot.hit", true);
                return Ok(cached.Response);
            }

            Activity.Current?.SetTag("snapshot.hit", false);
            var computed = await graphService.BuildEnhancedTaskGraphAsync(projectId, maxPastPRs);
            if (computed is null)
                return NotFound("No task graph available for this project.");
            snapshotStore.Store(projectId, maxPastPRs, computed, DateTimeOffset.UtcNow);
            return Ok(computed);
        }

        var taskGraph = await graphService.BuildEnhancedTaskGraphAsync(projectId, maxPastPRs);
        if (taskGraph == null)
            return NotFound("No task graph available for this project.");
        return Ok(taskGraph);
    }
}
