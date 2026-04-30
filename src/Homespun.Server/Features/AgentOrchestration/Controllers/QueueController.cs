using Homespun.Features.AgentOrchestration.Services;
using Homespun.Features.Projects;
using Homespun.Shared.Requests;
using Microsoft.AspNetCore.Mvc;

namespace Homespun.Features.AgentOrchestration.Controllers;

/// <summary>
/// API endpoints for queue status and control.
/// </summary>
[ApiController]
[Route("api/projects/{projectId}/queue")]
[Produces("application/json")]
public class QueueController(
    IQueueCoordinator queueCoordinator,
    IProjectService projectService,
    ILogger<QueueController> logger) : ControllerBase
{
    private const int DefaultLimit = 50;
    private const int MaxLimit = 200;

    /// <summary>
    /// Start queue execution on a root issue.
    /// </summary>
    [HttpPost("start")]
    [ProducesResponseType<QueueStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<QueueStatusResponse>> Start(
        string projectId,
        [FromBody] StartQueueRequest request,
        CancellationToken cancellationToken)
    {
        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
            return NotFound("Project not found");

        if (string.IsNullOrWhiteSpace(request.IssueId))
            return BadRequest("IssueId is required");

        try
        {
            await queueCoordinator.StartExecution(
                projectId,
                request.IssueId,
                project.LocalPath,
                project.DefaultBranch,
                cancellationToken);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }

        logger.LogInformation("Started queue execution for project {ProjectId} from issue {IssueId}", projectId, request.IssueId);

        var status = BuildStatusResponse(projectId);
        return Ok(status);
    }

    /// <summary>
    /// Get current queue coordinator state for a project. Queues are paginated
    /// via the optional <paramref name="limit"/> (default 50, max 200) and
    /// <paramref name="offset"/> (default 0) query parameters.
    /// </summary>
    [HttpGet("status")]
    [ProducesResponseType<QueueStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<QueueStatusResponse>> GetStatus(
        string projectId,
        [FromQuery] int? limit = null,
        [FromQuery] int? offset = null)
    {
        // Bounds-check pagination first so callers see 400 even when the project or
        // execution does not exist — keeps client retry logic simple.
        if (limit is < 1 or > MaxLimit)
            return BadRequest($"limit must be between 1 and {MaxLimit}");
        if (offset is < 0)
            return BadRequest("offset must be >= 0");

        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
            return NotFound("Project not found");

        var state = queueCoordinator.GetStatus(projectId);
        if (state == null)
            return NotFound("No active execution for this project");

        var response = BuildStatusResponse(projectId, limit ?? DefaultLimit, offset ?? 0);
        return Ok(response);
    }

    /// <summary>
    /// Cancel all active queues for a project.
    /// </summary>
    [HttpPost("cancel")]
    [ProducesResponseType<QueueStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<QueueStatusResponse>> Cancel(string projectId)
    {
        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
            return NotFound("Project not found");

        var state = queueCoordinator.GetStatus(projectId);
        if (state == null)
            return NotFound("No active execution for this project");

        queueCoordinator.CancelAll(projectId);

        logger.LogInformation("Cancelled queue execution for project {ProjectId}", projectId);

        var response = BuildStatusResponse(projectId);
        return Ok(response);
    }

    private QueueStatusResponse? BuildStatusResponse(
        string projectId,
        int limit = DefaultLimit,
        int offset = 0)
    {
        var state = queueCoordinator.GetStatus(projectId);
        if (state == null)
            return null;

        var pageQueues = state.ActiveQueues
            .Skip(offset)
            .Take(limit)
            .Select(q => new QueueDetail
            {
                Id = q.Id,
                State = q.State.ToString(),
                CurrentIssueId = q.CurrentRequest?.IssueId,
                PendingCount = q.PendingRequests.Count,
                History = q.History.Select(h => new QueueHistoryEntry
                {
                    IssueId = h.IssueId,
                    Success = h.Success,
                    Error = h.Error,
                    StartedAt = h.StartedAt,
                    CompletedAt = h.CompletedAt
                }).ToList()
            })
            .ToList();

        // Progress is ALWAYS computed across the full queue set, not the page.
        var allHistory = state.ActiveQueues.SelectMany(q => q.History).ToList();
        var currentCount = state.ActiveQueues.Count(q => q.CurrentRequest != null);
        var pendingCount = state.ActiveQueues.Sum(q => q.PendingRequests.Count);

        return new QueueStatusResponse
        {
            ProjectId = state.ProjectId,
            Status = state.Status.ToString(),
            RootIssueId = state.RootIssueId,
            MaxConcurrency = state.MaxConcurrency,
            RunningQueueCount = state.RunningQueueCount,
            Queues = pageQueues,
            TotalQueueCount = state.ActiveQueues.Count,
            Limit = limit,
            Offset = offset,
            Progress = new QueueProgress
            {
                TotalIssues = allHistory.Count + currentCount + pendingCount,
                Completed = allHistory.Count(h => h.Success),
                Failed = allHistory.Count(h => !h.Success),
                Remaining = currentCount + pendingCount
            }
        };
    }
}
