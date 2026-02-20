using Homespun.Features.Fleece;
using Homespun.Features.Fleece.Services;
using Homespun.Features.PullRequests.Data;
using Homespun.Shared.Models.PullRequests;
using Microsoft.AspNetCore.Mvc;

namespace Homespun.Features.PullRequests.Controllers;

/// <summary>
/// API endpoints for managing pull requests.
/// </summary>
[ApiController]
[Route("api")]
[Produces("application/json")]
public class PullRequestsController(
    IDataStore dataStore,
    IGitHubService gitHubService,
    IFleeceService fleeceService,
    PullRequestWorkflowService workflowService) : ControllerBase
{
    /// <summary>
    /// Get all pull requests for a project.
    /// </summary>
    [HttpGet("projects/{projectId}/pull-requests")]
    [ProducesResponseType<List<PullRequest>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<List<PullRequest>> GetByProject(string projectId)
    {
        var project = dataStore.GetProject(projectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        var pullRequests = dataStore.GetPullRequestsByProject(projectId).ToList();
        return Ok(pullRequests);
    }

    /// <summary>
    /// Get a pull request by ID.
    /// </summary>
    [HttpGet("pull-requests/{id}")]
    [ProducesResponseType<PullRequest>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<PullRequest> GetById(string id)
    {
        var pullRequest = dataStore.GetPullRequest(id);
        if (pullRequest == null)
        {
            return NotFound();
        }
        return Ok(pullRequest);
    }

    /// <summary>
    /// Create a pull request.
    /// </summary>
    [HttpPost("pull-requests")]
    [ProducesResponseType<PullRequest>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PullRequest>> Create([FromBody] CreatePullRequestRequest request)
    {
        var project = dataStore.GetProject(request.ProjectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        var pullRequest = new PullRequest
        {
            ProjectId = request.ProjectId,
            ParentId = request.ParentId,
            Title = request.Title,
            Description = request.Description,
            BranchName = request.BranchName,
            Status = request.Status ?? OpenPullRequestStatus.InDevelopment
        };

        await dataStore.AddPullRequestAsync(pullRequest);

        return CreatedAtAction(nameof(GetById), new { id = pullRequest.Id }, pullRequest);
    }

    /// <summary>
    /// Update a pull request.
    /// </summary>
    [HttpPut("pull-requests/{id}")]
    [ProducesResponseType<PullRequest>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PullRequest>> Update(string id, [FromBody] UpdatePullRequestRequest request)
    {
        var pullRequest = dataStore.GetPullRequest(id);
        if (pullRequest == null)
        {
            return NotFound();
        }

        if (request.Title != null)
            pullRequest.Title = request.Title;
        if (request.Description != null)
            pullRequest.Description = request.Description;
        if (request.BranchName != null)
            pullRequest.BranchName = request.BranchName;
        if (request.Status.HasValue)
            pullRequest.Status = request.Status.Value;
        if (request.ParentId != null)
            pullRequest.ParentId = request.ParentId;

        pullRequest.UpdatedAt = DateTime.UtcNow;

        await dataStore.UpdatePullRequestAsync(pullRequest);

        return Ok(pullRequest);
    }

    /// <summary>
    /// Delete a pull request.
    /// </summary>
    [HttpDelete("pull-requests/{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string id)
    {
        var pullRequest = dataStore.GetPullRequest(id);
        if (pullRequest == null)
        {
            return NotFound();
        }

        await dataStore.RemovePullRequestAsync(id);

        return NoContent();
    }

    /// <summary>
    /// Sync pull requests from GitHub for a project.
    /// </summary>
    [HttpPost("projects/{projectId}/sync")]
    [ProducesResponseType<SyncResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SyncResult>> Sync(string projectId)
    {
        var project = dataStore.GetProject(projectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        var result = await gitHubService.SyncPullRequestsAsync(projectId);
        return Ok(result);
    }

    /// <summary>
    /// Get open PRs with status for a project.
    /// </summary>
    [HttpGet("projects/{projectId}/pull-requests/open")]
    [ProducesResponseType<List<PullRequestWithStatus>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<PullRequestWithStatus>>> GetOpenWithStatus(string projectId)
    {
        var project = dataStore.GetProject(projectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        var result = await workflowService.GetOpenPullRequestsWithStatusAsync(projectId);
        return Ok(result);
    }

    /// <summary>
    /// Get merged PRs with time for a project.
    /// </summary>
    [HttpGet("projects/{projectId}/pull-requests/merged")]
    [ProducesResponseType<List<PullRequestWithTime>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<PullRequestWithTime>>> GetMergedWithTime(string projectId)
    {
        var project = dataStore.GetProject(projectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        var result = await workflowService.GetMergedPullRequestsWithTimeAsync(projectId);
        return Ok(result);
    }

    /// <summary>
    /// Get details for a specific merged PR including linked issue information.
    /// </summary>
    [HttpGet("projects/{projectId}/pull-requests/merged/{prNumber:int}")]
    [ProducesResponseType<MergedPullRequestDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MergedPullRequestDetails>> GetMergedPullRequestDetails(string projectId, int prNumber)
    {
        var project = dataStore.GetProject(projectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        var result = await workflowService.GetMergedPullRequestDetailsAsync(projectId, prNumber);
        if (result == null)
        {
            return NotFound("Pull request not found");
        }

        // Load linked issue details if an issue ID was found
        if (!string.IsNullOrEmpty(result.LinkedIssueId) && !string.IsNullOrEmpty(project.LocalPath))
        {
            var issue = await fleeceService.GetIssueAsync(project.LocalPath, result.LinkedIssueId);
            if (issue != null)
            {
                result.LinkedIssue = issue.ToResponse();
            }
        }

        return Ok(result);
    }
}

/// <summary>
/// Request model for creating a pull request.
/// </summary>
public class CreatePullRequestRequest
{
    /// <summary>
    /// The project ID.
    /// </summary>
    public required string ProjectId { get; set; }

    /// <summary>
    /// Pull request title.
    /// </summary>
    public required string Title { get; set; }

    /// <summary>
    /// Pull request description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Git branch name.
    /// </summary>
    public string? BranchName { get; set; }

    /// <summary>
    /// Parent pull request ID for stacking.
    /// </summary>
    public string? ParentId { get; set; }

    /// <summary>
    /// Initial status.
    /// </summary>
    public OpenPullRequestStatus? Status { get; set; }
}

/// <summary>
/// Request model for updating a pull request.
/// </summary>
public class UpdatePullRequestRequest
{
    /// <summary>
    /// Pull request title.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Pull request description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Git branch name.
    /// </summary>
    public string? BranchName { get; set; }

    /// <summary>
    /// Parent pull request ID for stacking.
    /// </summary>
    public string? ParentId { get; set; }

    /// <summary>
    /// Status.
    /// </summary>
    public OpenPullRequestStatus? Status { get; set; }
}
