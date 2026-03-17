using Fleece.Core.Models;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.Fleece.Services;
using Homespun.Features.Git;
using Homespun.Features.Notifications;
using Homespun.Features.Projects;
using Homespun.Features.PullRequests.Data;
using Homespun.Features.AgentOrchestration.Services;
using Homespun.Shared.Models.Fleece;
using Homespun.Shared.Models.Issues;
using Homespun.Shared.Models.PullRequests;
using Homespun.Shared.Models.Sessions;
using Homespun.Shared.Requests;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace Homespun.Features.Fleece.Controllers;

/// <summary>
/// API endpoints for managing Fleece issues.
/// </summary>
[ApiController]
[Route("api")]
[Produces("application/json")]
public class IssuesController(
    IFleeceService fleeceService,
    IProjectService projectService,
    IDataStore dataStore,
    IHubContext<NotificationHub> notificationHub,
    IIssueBranchResolverService branchResolverService,
    IIssueHistoryService historyService,
    IClaudeSessionService sessionService,
    IAgentPromptService agentPromptService,
    IGitCloneService cloneService,
    IBranchIdBackgroundService branchIdBackgroundService,
    IFleeceChangeApplicationService changeApplicationService,
    IFleeceIssuesSyncService fleeceIssuesSyncService,
    ILogger<IssuesController> logger) : ControllerBase
{
    /// <summary>
    /// Get all issues for a project.
    /// </summary>
    [HttpGet("projects/{projectId}/issues")]
    [ProducesResponseType<List<IssueResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<IssueResponse>>> GetByProject(
        string projectId,
        [FromQuery] IssueStatus? status = null,
        [FromQuery] IssueType? type = null,
        [FromQuery] int? priority = null)
    {
        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        var issues = await fleeceService.ListIssuesAsync(project.LocalPath, status, type, priority);
        var response = issues.ToResponseList();
        logger.LogDebug("Returning {Count} issues for project {ProjectId}", response.Count, projectId);
        return Ok(response);
    }

    /// <summary>
    /// Get ready issues for a project (issues with no blocking dependencies).
    /// </summary>
    [HttpGet("projects/{projectId}/issues/ready")]
    [ProducesResponseType<List<IssueResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<IssueResponse>>> GetReadyIssues(string projectId)
    {
        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        var issues = await fleeceService.GetReadyIssuesAsync(project.LocalPath);
        var response = issues.ToResponseList();
        logger.LogDebug("Returning {Count} ready issues for project {ProjectId}", response.Count, projectId);
        return Ok(response);
    }

    /// <summary>
    /// Get unique assignees for a project's issues.
    /// Returns all unique email addresses found in issue assignments, plus the current user if configured.
    /// </summary>
    [HttpGet("projects/{projectId}/issues/assignees")]
    [ProducesResponseType<List<string>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<string>>> GetProjectAssignees(string projectId)
    {
        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        // Get all issues (including those with terminal statuses) to collect all assignees
        var issues = await fleeceService.ListIssuesAsync(project.LocalPath);
        var assignees = issues
            .Where(i => !string.IsNullOrWhiteSpace(i.AssignedTo))
            .Select(i => i.AssignedTo!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(a => a, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Include current user email if configured and not already in list
        if (!string.IsNullOrWhiteSpace(dataStore.UserEmail) &&
            !assignees.Contains(dataStore.UserEmail, StringComparer.OrdinalIgnoreCase))
        {
            assignees.Insert(0, dataStore.UserEmail);
        }

        logger.LogDebug("Returning {Count} assignees for project {ProjectId}", assignees.Count, projectId);
        return Ok(assignees);
    }

    /// <summary>
    /// Get an issue by ID.
    /// </summary>
    [HttpGet("issues/{issueId}")]
    [ProducesResponseType<IssueResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IssueResponse>> GetById(string issueId, [FromQuery] string projectId)
    {
        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        var issue = await fleeceService.GetIssueAsync(project.LocalPath, issueId);
        if (issue == null)
        {
            return NotFound("Issue not found");
        }
        return Ok(issue.ToResponse());
    }

    /// <summary>
    /// Get the resolved branch name for an issue.
    /// Checks linked PRs first, then existing clones with matching issue ID.
    /// Returns null if no existing branch is found.
    /// </summary>
    [HttpGet("issues/{issueId}/resolved-branch")]
    [ProducesResponseType<ResolvedBranchResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ResolvedBranchResponse>> GetResolvedBranch(string issueId, [FromQuery] string projectId)
    {
        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        var branchName = await branchResolverService.ResolveIssueBranchAsync(projectId, issueId);
        return Ok(new ResolvedBranchResponse { BranchName = branchName });
    }

    /// <summary>
    /// Create a new issue.
    /// If a working branch ID is provided, it will be applied to the issue.
    /// Otherwise, the client should trigger branch ID generation on the edit page.
    /// </summary>
    [HttpPost("issues")]
    [ProducesResponseType<IssueResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IssueResponse>> Create([FromBody] CreateIssueRequest request)
    {
        var project = await projectService.GetByIdAsync(request.ProjectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        // Create the issue first, with user email assignment if configured
        var issue = await fleeceService.CreateIssueAsync(
            project.LocalPath,
            request.Title,
            request.Type,
            request.Description,
            request.Priority,
            request.ExecutionMode,
            assignedTo: dataStore.UserEmail);

        // Apply provided working branch ID if any
        if (!string.IsNullOrWhiteSpace(request.WorkingBranchId))
        {
            issue = await fleeceService.UpdateIssueAsync(
                project.LocalPath,
                issue.Id,
                workingBranchId: request.WorkingBranchId.Trim()) ?? issue;
        }

        // If a parent issue ID was provided, add this issue as a child of that parent
        if (!string.IsNullOrWhiteSpace(request.ParentIssueId))
        {
            issue = await fleeceService.AddParentAsync(
                project.LocalPath,
                issue.Id,
                request.ParentIssueId.Trim(),
                sortOrder: request.ParentSortOrder);
        }

        // If a child issue ID was provided, make the new issue the parent of that child
        if (!string.IsNullOrWhiteSpace(request.ChildIssueId))
        {
            await fleeceService.AddParentAsync(
                project.LocalPath,
                request.ChildIssueId.Trim(),
                issue.Id);
        }

        // Broadcast issue creation to connected clients
        await notificationHub.BroadcastIssuesChanged(request.ProjectId, IssueChangeType.Created, issue.Id);

        // Trigger background branch ID generation if title is provided and no working branch ID
        if (!string.IsNullOrWhiteSpace(request.Title) && string.IsNullOrWhiteSpace(request.WorkingBranchId))
        {
            await branchIdBackgroundService.QueueBranchIdGenerationAsync(issue.Id, request.ProjectId, request.Title);
        }

        return CreatedAtAction(
            nameof(GetById),
            new { issueId = issue.Id, projectId = request.ProjectId },
            issue.ToResponse());
    }

    /// <summary>
    /// Update an issue.
    /// </summary>
    [HttpPut("issues/{issueId}")]
    [ProducesResponseType<IssueResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IssueResponse>> Update(string issueId, [FromBody] UpdateIssueRequest request)
    {
        var project = await projectService.GetByIdAsync(request.ProjectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        // Get the current issue to check for title changes
        var currentIssue = await fleeceService.GetIssueAsync(project.LocalPath, issueId);
        if (currentIssue == null)
        {
            return NotFound("Issue not found");
        }

        // Auto-assign current user if issue has no assignee and request doesn't specify one
        var assignedTo = request.AssignedTo;
        if (string.IsNullOrWhiteSpace(currentIssue.AssignedTo) &&
            string.IsNullOrWhiteSpace(request.AssignedTo) &&
            !string.IsNullOrWhiteSpace(dataStore.UserEmail))
        {
            assignedTo = dataStore.UserEmail;
        }

        var issue = await fleeceService.UpdateIssueAsync(
            project.LocalPath,
            issueId,
            request.Title,
            request.Status,
            request.Type,
            request.Description,
            request.Priority,
            request.ExecutionMode,
            request.WorkingBranchId,
            assignedTo);

        if (issue == null)
        {
            return NotFound("Issue not found");
        }

        // Broadcast issue update to connected clients
        await notificationHub.BroadcastIssuesChanged(request.ProjectId, IssueChangeType.Updated, issueId);

        // Check if title changed and working branch ID is empty
        if (!string.IsNullOrWhiteSpace(request.Title) &&
            request.Title != currentIssue.Title &&
            string.IsNullOrWhiteSpace(issue.WorkingBranchId))
        {
            await branchIdBackgroundService.QueueBranchIdGenerationAsync(issue.Id, request.ProjectId, request.Title);
        }

        return Ok(issue.ToResponse());
    }

    /// <summary>
    /// Delete an issue.
    /// </summary>
    [HttpDelete("issues/{issueId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string issueId, [FromQuery] string projectId)
    {
        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        var deleted = await fleeceService.DeleteIssueAsync(project.LocalPath, issueId);
        if (!deleted)
        {
            return NotFound("Issue not found");
        }

        // Broadcast issue deletion to connected clients
        await notificationHub.BroadcastIssuesChanged(projectId, IssueChangeType.Deleted, issueId);

        return NoContent();
    }

    /// <summary>
    /// Set the parent of an issue.
    /// Can either replace all existing parents or add to existing parents.
    /// </summary>
    [HttpPost("issues/{childId}/set-parent")]
    [ProducesResponseType<IssueResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IssueResponse>> SetParent(string childId, [FromBody] SetParentRequest request)
    {
        var project = await projectService.GetByIdAsync(request.ProjectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        // Check if the child issue exists
        var existingIssue = await fleeceService.GetIssueAsync(project.LocalPath, childId);
        if (existingIssue == null)
        {
            return NotFound("Child issue not found");
        }

        try
        {
            var issue = await fleeceService.SetParentAsync(
                project.LocalPath,
                childId,
                request.ParentIssueId,
                request.AddToExisting);

            // Broadcast issue update to connected clients
            await notificationHub.BroadcastIssuesChanged(request.ProjectId, IssueChangeType.Updated, childId);

            return Ok(issue.ToResponse());
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("cycle"))
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Move a sibling issue up or down in the series order.
    /// </summary>
    [HttpPost("issues/{issueId}/move-sibling")]
    [ProducesResponseType<IssueResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IssueResponse>> MoveSeriesSibling(string issueId, [FromBody] MoveSeriesSiblingRequest request)
    {
        var project = await projectService.GetByIdAsync(request.ProjectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        // Check if the issue exists
        var existingIssue = await fleeceService.GetIssueAsync(project.LocalPath, issueId);
        if (existingIssue == null)
        {
            return NotFound("Issue not found");
        }

        try
        {
            var issue = await fleeceService.MoveSeriesSiblingAsync(
                project.LocalPath,
                issueId,
                request.Direction);

            // Broadcast issue update to connected clients
            await notificationHub.BroadcastIssuesChanged(request.ProjectId, IssueChangeType.Updated, issueId);

            return Ok(issue.ToResponse());
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    #region Agent Operations

    /// <summary>
    /// Run an agent on an issue.
    /// This endpoint handles the complete agent startup flow:
    /// 1. Fetches issue data
    /// 2. Resolves or creates the working branch/clone
    /// 3. Fetches the prompt and renders the template with issue context
    /// 4. Creates the session and sends the initial message
    /// </summary>
    [HttpPost("issues/{issueId}/run")]
    [ProducesResponseType<RunAgentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<AgentAlreadyRunningResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RunAgentResponse>> RunAgent(string issueId, [FromBody] RunAgentRequest request)
    {
        // Fetch project
        var project = await projectService.GetByIdAsync(request.ProjectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        // Fetch issue
        var issue = await fleeceService.GetIssueAsync(project.LocalPath, issueId);
        if (issue == null)
        {
            return NotFound("Issue not found");
        }

        // Check for existing active session on this issue
        var existingSession = sessionService.GetSessionByEntityId(issueId);
        if (existingSession != null && existingSession.Status.IsActive())
        {
            return Conflict(new AgentAlreadyRunningResponse
            {
                SessionId = existingSession.Id,
                Status = existingSession.Status,
                Message = "An agent is already running on this issue"
            });
        }

        // Fetch prompt (if provided)
        AgentPrompt? prompt = null;
        string? renderedMessage = null;
        SessionMode mode = SessionMode.Plan; // Default for None

        if (!string.IsNullOrEmpty(request.PromptId))
        {
            prompt = agentPromptService.GetPrompt(request.PromptId);
            if (prompt == null)
            {
                return NotFound("Prompt not found");
            }
            mode = prompt.Mode;
        }

        // Resolve branch name - check for existing branch first, then generate from issue
        var branchName = await branchResolverService.ResolveIssueBranchAsync(request.ProjectId, issueId)
            ?? BranchNameGenerator.GenerateBranchName(issue);

        // Get or create clone for the branch
        string? clonePath = await cloneService.GetClonePathForBranchAsync(project.LocalPath, branchName);

        if (string.IsNullOrEmpty(clonePath))
        {
            // Clone doesn't exist, create it
            var baseBranch = !string.IsNullOrEmpty(request.BaseBranch)
                ? request.BaseBranch
                : project.DefaultBranch;

            // Pull latest changes on main repo before creating clone
            logger.LogInformation("Pulling latest changes from {BaseBranch} before creating clone", baseBranch);
            var pullResult = await fleeceIssuesSyncService.PullFleeceOnlyAsync(
                project.LocalPath,
                baseBranch,
                HttpContext.RequestAborted);

            if (!pullResult.Success)
            {
                logger.LogWarning("Auto-pull failed before clone creation: {Error}, continuing anyway", pullResult.ErrorMessage);
            }
            else if (pullResult.WasBehindRemote)
            {
                logger.LogInformation("Pulled {Commits} commits and merged {Issues} issues before clone creation",
                    pullResult.CommitsPulled, pullResult.IssuesMerged);
            }

            logger.LogInformation("Creating clone for branch {BranchName} from base {BaseBranch}", branchName, baseBranch);

            clonePath = await cloneService.CreateCloneAsync(
                project.LocalPath,
                branchName,
                createBranch: true,
                baseBranch: baseBranch);

            if (string.IsNullOrEmpty(clonePath))
            {
                return BadRequest("Failed to create clone for issue branch");
            }

            logger.LogInformation("Created clone at {ClonePath} for issue {IssueId}", clonePath, issueId);
        }
        else
        {
            logger.LogInformation("Using existing clone at {ClonePath} for branch {BranchName}", clonePath, branchName);
        }

        // Render the prompt template with issue context (if prompt exists)
        if (prompt != null)
        {
            var promptContext = new PromptContext
            {
                Title = issue.Title,
                Id = issue.Id,
                Description = issue.Description,
                Branch = branchName,
                Type = issue.Type.ToString()
            };

            renderedMessage = agentPromptService.RenderTemplate(prompt.InitialMessage, promptContext);
        }

        // Determine model
        var model = request.Model ?? project.DefaultModel ?? "sonnet";

        // Create session
        var session = await sessionService.StartSessionAsync(
            issueId,
            request.ProjectId,
            clonePath,
            mode,
            model,
            systemPrompt: null);

        // Send the rendered initial message to start agent work
        if (!string.IsNullOrWhiteSpace(renderedMessage))
        {
            // Fire and forget - the message will be processed asynchronously
            // and clients will receive updates via SignalR
            _ = Task.Run(async () =>
            {
                try
                {
                    await sessionService.SendMessageAsync(session.Id, renderedMessage, mode);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error sending initial message for session {SessionId}", session.Id);
                }
            });
        }

        return Ok(new RunAgentResponse
        {
            SessionId = session.Id,
            BranchName = branchName,
            ClonePath = clonePath
        });
    }

    /// <summary>
    /// Apply agent changes from a session back to the main branch.
    /// </summary>
    [HttpPost("issues/{issueId}/apply-agent-changes")]
    [ProducesResponseType<ApplyAgentChangesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApplyAgentChangesResponse>> ApplyAgentChanges(
        string issueId,
        [FromBody] ApplyAgentChangesRequest request)
    {
        // Validate project
        var project = await projectService.GetByIdAsync(request.ProjectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        // Validate issue exists
        var issue = await fleeceService.GetIssueAsync(project.LocalPath, issueId);
        if (issue == null)
        {
            return NotFound("Issue not found");
        }

        // Apply changes
        var result = await changeApplicationService.ApplyChangesAsync(
            request.ProjectId,
            request.SessionId,
            request.ConflictStrategy,
            request.DryRun);

        if (!request.DryRun && result.Success)
        {
            // Broadcast issue changes to connected clients
            await notificationHub.BroadcastIssuesChanged(request.ProjectId, IssueChangeType.Updated, null);
        }

        return Ok(result);
    }

    /// <summary>
    /// Resolve specific conflicts from a previous apply attempt.
    /// </summary>
    [HttpPost("issues/{issueId}/resolve-conflicts")]
    [ProducesResponseType<ApplyAgentChangesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApplyAgentChangesResponse>> ResolveConflicts(
        string issueId,
        [FromBody] ResolveConflictsRequest request)
    {
        // Validate project
        var project = await projectService.GetByIdAsync(request.ProjectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        // Apply resolutions
        var result = await changeApplicationService.ResolveConflictsAsync(
            request.ProjectId,
            request.SessionId,
            request.Resolutions);

        if (result.Success)
        {
            // Broadcast issue changes to connected clients
            await notificationHub.BroadcastIssuesChanged(request.ProjectId, IssueChangeType.Updated, null);
        }

        return Ok(result);
    }

    #endregion

    #region History Operations

    /// <summary>
    /// Get the current history state for a project.
    /// </summary>
    [HttpGet("projects/{projectId}/issues/history/state")]
    [ProducesResponseType<IssueHistoryState>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IssueHistoryState>> GetHistoryState(string projectId)
    {
        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        var state = await historyService.GetStateAsync(project.LocalPath);
        return Ok(state);
    }

    /// <summary>
    /// Undo the last change to issues.
    /// </summary>
    [HttpPost("projects/{projectId}/issues/history/undo")]
    [ProducesResponseType<IssueHistoryOperationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IssueHistoryOperationResponse>> Undo(string projectId)
    {
        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        var issues = await historyService.UndoAsync(project.LocalPath);
        if (issues == null)
        {
            return Ok(new IssueHistoryOperationResponse
            {
                Success = false,
                ErrorMessage = "Nothing to undo"
            });
        }

        // Apply the snapshot to the FleeceService cache and disk
        await fleeceService.ApplyHistorySnapshotAsync(project.LocalPath, issues);

        // Broadcast issues changed to connected clients
        await notificationHub.BroadcastIssuesChanged(projectId, IssueChangeType.Updated, null);

        var state = await historyService.GetStateAsync(project.LocalPath);
        return Ok(new IssueHistoryOperationResponse
        {
            Success = true,
            State = state
        });
    }

    /// <summary>
    /// Redo a previously undone change.
    /// </summary>
    [HttpPost("projects/{projectId}/issues/history/redo")]
    [ProducesResponseType<IssueHistoryOperationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IssueHistoryOperationResponse>> Redo(string projectId)
    {
        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        var issues = await historyService.RedoAsync(project.LocalPath);
        if (issues == null)
        {
            return Ok(new IssueHistoryOperationResponse
            {
                Success = false,
                ErrorMessage = "Nothing to redo"
            });
        }

        // Apply the snapshot to the FleeceService cache and disk
        await fleeceService.ApplyHistorySnapshotAsync(project.LocalPath, issues);

        // Broadcast issues changed to connected clients
        await notificationHub.BroadcastIssuesChanged(projectId, IssueChangeType.Updated, null);

        var state = await historyService.GetStateAsync(project.LocalPath);
        return Ok(new IssueHistoryOperationResponse
        {
            Success = true,
            State = state
        });
    }

    #endregion
}
