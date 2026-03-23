using Fleece.Core.Models;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.Fleece.Services;
using Homespun.Features.Git;
using Homespun.Features.Gitgraph.Services;
using Homespun.Features.Notifications;
using Homespun.Features.Projects;
using Homespun.Features.PullRequests.Data;
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
    IFleecePostMergeService postMergeService,
    IDataStore dataStore,
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

        // Ensure default prompts exist
        await agentPromptService.EnsureDefaultPromptsAsync();

        // Resolve the prompt to use for initial message and session mode
        AgentPrompt? selectedPrompt;
        SessionMode sessionMode;

        if (!string.IsNullOrWhiteSpace(request.PromptId))
        {
            // Explicit prompt selection: validate it exists and has the correct category
            selectedPrompt = agentPromptService.GetPrompt(request.PromptId);
            if (selectedPrompt == null)
            {
                return NotFound("Prompt not found");
            }

            if (selectedPrompt.Category != PromptCategory.IssueAgent)
            {
                return BadRequest("Prompt must have Category = IssueAgent");
            }

            sessionMode = selectedPrompt.Mode;
        }
        else
        {
            // No prompt ID: fall back to first available IssueAgent prompt for the project
            var availablePrompts = agentPromptService.GetIssueAgentPromptsForProject(request.ProjectId);
            selectedPrompt = availablePrompts.FirstOrDefault();

            // If no user-selectable prompt found, fall back to the IssueAgentModification session type prompt
            selectedPrompt ??= agentPromptService.GetPromptBySessionType(SessionType.IssueAgentModification);

            sessionMode = SessionMode.Build;
        }

        // Get the IssueAgentSystem prompt for system prompt
        var systemPromptTemplate = agentPromptService.GetPromptBySessionType(SessionType.IssueAgentSystem);

        // Determine model
        var model = request.Model ?? project.DefaultModel ?? "sonnet";

        // Create session with resolved mode and system prompt
        var session = await sessionService.StartSessionAsync(
            branchName,
            request.ProjectId,
            clonePath,
            sessionMode,
            model,
            systemPrompt: systemPromptTemplate?.InitialMessage);

        // Set session type to IssueAgentModification
        session.SessionType = SessionType.IssueAgentModification;

        // If user instructions are provided, render the selected prompt and send as initial message
        if (!string.IsNullOrWhiteSpace(request.UserInstructions))
        {
            var promptContext = new PromptContext
            {
                SelectedIssueId = request.SelectedIssueId,
                UserPrompt = request.UserInstructions
            };

            var renderedPrompt = agentPromptService.RenderTemplate(selectedPrompt?.InitialMessage, promptContext);

            if (!string.IsNullOrWhiteSpace(renderedPrompt))
            {
                logger.LogInformation("Sending initial message to Issues Agent session {SessionId}", session.Id);

                // Send the initial message asynchronously (fire and forget)
                var messageMode = sessionMode;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await sessionService.SendMessageAsync(session.Id, renderedPrompt, messageMode);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to send initial message to Issues Agent session {SessionId}", session.Id);
                    }
                });
            }
        }

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
        if (session.SessionType != SessionType.IssueAgentModification)
        {
            return BadRequest("Session is not an Issues Agent session");
        }

        // Get project
        var project = await projectService.GetByIdAsync(session.ProjectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        logger.LogInformation(
            "Getting diff for session {SessionId}: project={ProjectId}, mainPath={MainPath}, clonePath={ClonePath}",
            sessionId, session.ProjectId, project.LocalPath, session.WorkingDirectory);

        // Detect changes using the FleeceChangeDetectionService
        var changes = await changeDetectionService.DetectChangesAsync(
            session.ProjectId,
            sessionId,
            HttpContext.RequestAborted);

        logger.LogInformation(
            "Detected {ChangeCount} changes for session {SessionId}: created={Created}, updated={Updated}, deleted={Deleted}",
            changes.Count, sessionId,
            changes.Count(c => c.ChangeType == ChangeType.Created),
            changes.Count(c => c.ChangeType == ChangeType.Updated),
            changes.Count(c => c.ChangeType == ChangeType.Deleted));

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
        if (session.SessionType != SessionType.IssueAgentModification)
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

        // Post-merge processing: assign unassigned issues and trigger branch ID generation
        await postMergeService.PostMergeProcessAsync(
            project.LocalPath, session.ProjectId, result.Changes, dataStore.UserEmail,
            HttpContext.RequestAborted);

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

    /// <summary>
    /// Refresh the issue diff by clearing the cache and re-reading from disk.
    /// This ensures the latest changes are reflected in the diff.
    /// </summary>
    [HttpPost("{sessionId}/refresh-diff")]
    [ProducesResponseType<IssueDiffResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IssueDiffResponse>> RefreshDiff(string sessionId)
    {
        // Get session
        var session = sessionService.GetSession(sessionId);
        if (session == null)
        {
            return NotFound("Session not found");
        }

        // Validate session type
        if (session.SessionType != SessionType.IssueAgentModification)
        {
            return BadRequest("Session is not an Issues Agent session");
        }

        // Get project
        var project = await projectService.GetByIdAsync(session.ProjectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        logger.LogInformation(
            "Refreshing diff for session {SessionId}: clearing cache for main path {MainPath}",
            sessionId, project.LocalPath);

        // Clear the main branch cache to force re-reading from disk
        await fleeceService.ReloadFromDiskAsync(project.LocalPath, HttpContext.RequestAborted);

        // Now get the updated diff (same logic as GetDiff)
        var changes = await changeDetectionService.DetectChangesAsync(
            session.ProjectId,
            sessionId,
            HttpContext.RequestAborted);

        logger.LogInformation(
            "After refresh, detected {ChangeCount} changes for session {SessionId}",
            changes.Count, sessionId);

        // Build task graphs for both branches
        var mainGraph = await graphService.BuildEnhancedTaskGraphAsync(session.ProjectId);
        var mainGraphResponse = mainGraph ?? new TaskGraphResponse();

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

}
