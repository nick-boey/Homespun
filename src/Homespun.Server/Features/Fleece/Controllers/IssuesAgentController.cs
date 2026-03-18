using Fleece.Core.Models;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.Fleece.Services;
using Homespun.Features.Git;
using Homespun.Features.Gitgraph.Services;
using Homespun.Features.Notifications;
using Homespun.Features.Projects;
using Homespun.Shared.Models.Fleece;
using Homespun.Shared.Models.Issues;
using Homespun.Shared.Models.Sessions;
using Homespun.Shared.Requests;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace Homespun.Features.Fleece.Controllers;

/// <summary>
/// API endpoints for the Issues Agent workflow.
/// The Issues Agent is a specialized session type for modifying Fleece issues.
/// </summary>
[ApiController]
[Route("api/issues-agent")]
[Produces("application/json")]
public class IssuesAgentController(
    IProjectService projectService,
    IFleeceService fleeceService,
    IFleeceIssuesSyncService fleeceIssuesSyncService,
    IFleeceChangeDetectionService changeDetectionService,
    IFleeceChangeApplicationService changeApplicationService,
    IGitCloneService cloneService,
    IClaudeSessionService sessionService,
    IAgentPromptService agentPromptService,
    IGraphService graphService,
    IHubContext<NotificationHub> notificationHub,
    ILogger<IssuesAgentController> logger) : ControllerBase
{
    /// <summary>
    /// Create a new Issues Agent session.
    /// This creates a session on a new branch specifically for modifying issues.
    /// </summary>
    [HttpPost("session")]
    [ProducesResponseType<CreateIssuesAgentSessionResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CreateIssuesAgentSessionResponse>> CreateSession(
        [FromBody] CreateIssuesAgentSessionRequest request)
    {
        // Validate project exists
        var project = await projectService.GetByIdAsync(request.ProjectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        // Generate branch name: issues-agent-{timestamp}
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var branchName = $"issues-agent-{timestamp}";

        // Pull latest from main branch before creating clone
        var baseBranch = project.DefaultBranch;
        logger.LogInformation("Pulling latest changes from {BaseBranch} before creating Issues Agent clone", baseBranch);

        var pullResult = await fleeceIssuesSyncService.PullFleeceOnlyAsync(
            project.LocalPath,
            baseBranch,
            HttpContext.RequestAborted);

        if (!pullResult.Success)
        {
            logger.LogWarning("Auto-pull failed before Issues Agent clone creation: {Error}, continuing anyway",
                pullResult.ErrorMessage);
        }
        else if (pullResult.WasBehindRemote)
        {
            logger.LogInformation("Pulled {Commits} commits and merged {Issues} issues before Issues Agent clone creation",
                pullResult.CommitsPulled, pullResult.IssuesMerged);
        }

        // Create clone with new branch
        logger.LogInformation("Creating Issues Agent clone for branch {BranchName} from base {BaseBranch}",
            branchName, baseBranch);

        var clonePath = await cloneService.CreateCloneAsync(
            project.LocalPath,
            branchName,
            createBranch: true,
            baseBranch: baseBranch);

        if (string.IsNullOrEmpty(clonePath))
        {
            return BadRequest("Failed to create clone for Issues Agent session");
        }

        logger.LogInformation("Created Issues Agent clone at {ClonePath}", clonePath);

        // Get the IssueModify prompt
        var prompt = agentPromptService.GetPromptBySessionType(SessionType.IssueModify);
        if (prompt == null)
        {
            // Ensure default prompts exist
            await agentPromptService.EnsureDefaultPromptsAsync();
            prompt = agentPromptService.GetPromptBySessionType(SessionType.IssueModify);
        }

        // Determine model
        var model = request.Model ?? project.DefaultModel ?? "sonnet";

        // Create session with IssueModify type
        // Use branch name as entity ID (like CreateBranchSession)
        var session = await sessionService.StartSessionAsync(
            branchName,
            request.ProjectId,
            clonePath,
            SessionMode.Build,  // Build mode needed for Bash/fleece CLI
            model,
            systemPrompt: null);

        // Set session type to IssueModify
        session.SessionType = SessionType.IssueModify;

        return Created(
            string.Empty,
            new CreateIssuesAgentSessionResponse
            {
                SessionId = session.Id,
                BranchName = branchName,
                ClonePath = clonePath
            });
    }

    /// <summary>
    /// Get the issue diff for an Issues Agent session.
    /// Compares the main branch issues with the session's branch issues.
    /// </summary>
    [HttpGet("{sessionId}/diff")]
    [ProducesResponseType<IssueDiffResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IssueDiffResponse>> GetDiff(string sessionId)
    {
        // Get session
        var session = sessionService.GetSession(sessionId);
        if (session == null)
        {
            return NotFound("Session not found");
        }

        // Validate session type
        if (session.SessionType != SessionType.IssueModify)
        {
            return BadRequest("Session is not an Issues Agent session");
        }

        // Get project
        var project = await projectService.GetByIdAsync(session.ProjectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        // Detect changes using the FleeceChangeDetectionService
        var changes = await changeDetectionService.DetectChangesAsync(
            session.ProjectId,
            sessionId,
            HttpContext.RequestAborted);

        // Build task graphs for both branches
        // Main branch graph
        var mainGraph = await graphService.BuildEnhancedTaskGraphAsync(session.ProjectId);
        var mainGraphResponse = mainGraph ?? new TaskGraphResponse();

        // Session branch graph - load from session's working directory
        var sessionTaskGraph = await fleeceService.GetTaskGraphAsync(
            session.WorkingDirectory,
            HttpContext.RequestAborted);

        var sessionGraphResponse = sessionTaskGraph?.ToResponse() ?? new TaskGraphResponse();

        // Calculate summary
        var summary = new IssueDiffSummary
        {
            Created = changes.Count(c => c.ChangeType == ChangeType.Created),
            Updated = changes.Count(c => c.ChangeType == ChangeType.Updated),
            Deleted = changes.Count(c => c.ChangeType == ChangeType.Deleted)
        };

        return Ok(new IssueDiffResponse
        {
            MainBranchGraph = mainGraphResponse,
            SessionBranchGraph = sessionGraphResponse,
            Changes = changes,
            Summary = summary
        });
    }

    /// <summary>
    /// Accept changes from an Issues Agent session.
    /// Applies the changes to the main branch and stops the session.
    /// </summary>
    [HttpPost("{sessionId}/accept")]
    [ProducesResponseType<AcceptIssuesAgentChangesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AcceptIssuesAgentChangesResponse>> AcceptChanges(string sessionId)
    {
        // Get session
        var session = sessionService.GetSession(sessionId);
        if (session == null)
        {
            return NotFound("Session not found");
        }

        // Validate session type
        if (session.SessionType != SessionType.IssueModify)
        {
            return BadRequest("Session is not an Issues Agent session");
        }

        // Get project
        var project = await projectService.GetByIdAsync(session.ProjectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        // Apply changes using AgentWins strategy (accept all agent changes)
        var result = await changeApplicationService.ApplyChangesAsync(
            session.ProjectId,
            sessionId,
            ConflictResolutionStrategy.AgentWins,
            dryRun: false);

        if (!result.Success)
        {
            return BadRequest(new AcceptIssuesAgentChangesResponse
            {
                Success = false,
                Message = result.Message
            });
        }

        // Stop the session
        await sessionService.StopSessionAsync(sessionId, HttpContext.RequestAborted);

        // Broadcast issue changes to connected clients
        await notificationHub.BroadcastIssuesChanged(session.ProjectId, IssueChangeType.Updated, null);

        logger.LogInformation("Accepted {Count} changes from Issues Agent session {SessionId}",
            result.Changes.Count, sessionId);

        return Ok(new AcceptIssuesAgentChangesResponse
        {
            Success = true,
            Message = $"Applied {result.Changes.Count} changes",
            RedirectUrl = $"/projects/{session.ProjectId}/issues"
        });
    }

    /// <summary>
    /// Cancel an Issues Agent session without applying changes.
    /// Stops the session and optionally deletes the clone.
    /// </summary>
    [HttpPost("{sessionId}/cancel")]
    [ProducesResponseType<AcceptIssuesAgentChangesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AcceptIssuesAgentChangesResponse>> CancelSession(string sessionId)
    {
        // Get session
        var session = sessionService.GetSession(sessionId);
        if (session == null)
        {
            return NotFound("Session not found");
        }

        // Stop the session
        await sessionService.StopSessionAsync(sessionId, HttpContext.RequestAborted);

        logger.LogInformation("Cancelled Issues Agent session {SessionId}", sessionId);

        return Ok(new AcceptIssuesAgentChangesResponse
        {
            Success = true,
            Message = "Session cancelled",
            RedirectUrl = $"/projects/{session.ProjectId}/issues"
        });
    }

}
