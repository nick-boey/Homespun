using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.Fleece.Services;
using Homespun.Features.Projects;
using Homespun.Shared.Models.Git;
using Homespun.Shared.Models.Sessions;
using Homespun.Shared.Requests;
using Microsoft.AspNetCore.Mvc;

namespace Homespun.Features.Git.Controllers;

/// <summary>
/// API endpoints for managing Git clones.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class ClonesController(
    IGitCloneService cloneService,
    IProjectService projectService,
    IFleeceIssuesSyncService fleeceIssuesSyncService,
    IClaudeSessionService sessionService,
    ILogger<ClonesController> logger) : ControllerBase
{
    /// <summary>
    /// List clones for a project.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<List<CloneInfo>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<CloneInfo>>> List([FromQuery] string projectId)
    {
        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        var clones = await cloneService.ListClonesAsync(project.LocalPath);
        return Ok(clones);
    }

    /// <summary>
    /// Create a new clone.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<CreateCloneResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CreateCloneResponse>> Create([FromBody] CreateCloneRequest request)
    {
        var project = await projectService.GetByIdAsync(request.ProjectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        var clonePath = await cloneService.CreateCloneAsync(
            project.LocalPath,
            request.BranchName,
            request.CreateBranch,
            request.BaseBranch);

        if (clonePath == null)
        {
            return BadRequest("Failed to create clone");
        }

        return Created(
            string.Empty,
            new CreateCloneResponse { Path = clonePath, BranchName = request.BranchName });
    }

    /// <summary>
    /// Delete a clone.
    /// </summary>
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete([FromQuery] string projectId, [FromQuery] string clonePath)
    {
        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        var removed = await cloneService.RemoveCloneAsync(project.LocalPath, clonePath);
        if (!removed)
        {
            return BadRequest("Failed to remove clone");
        }

        return NoContent();
    }

    /// <summary>
    /// Check if a clone exists for a branch.
    /// </summary>
    [HttpGet("exists")]
    [ProducesResponseType<CloneExistsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CloneExistsResponse>> Exists([FromQuery] string projectId, [FromQuery] string branchName)
    {
        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        var exists = await cloneService.CloneExistsAsync(project.LocalPath, branchName);
        return Ok(new CloneExistsResponse { Exists = exists });
    }

    /// <summary>
    /// Prune clones (remove stale entries).
    /// </summary>
    [HttpPost("prune")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Prune([FromQuery] string projectId)
    {
        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        await cloneService.PruneClonesAsync(project.LocalPath);
        return NoContent();
    }

    /// <summary>
    /// List local branches for a project.
    /// </summary>
    [HttpGet("branches")]
    [ProducesResponseType<List<BranchInfo>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<BranchInfo>>> ListBranches([FromQuery] string repoPath)
    {
        var branches = await cloneService.ListLocalBranchesAsync(repoPath);
        return Ok(branches);
    }

    /// <summary>
    /// Get changed files between a clone and a target branch.
    /// </summary>
    [HttpGet("changed-files")]
    [ProducesResponseType<List<FileChangeInfo>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<FileChangeInfo>>> GetChangedFiles(
        [FromQuery] string workingDirectory,
        [FromQuery] string targetBranch)
    {
        var files = await cloneService.GetChangedFilesAsync(workingDirectory, targetBranch);
        return Ok(files);
    }

    /// <summary>
    /// Pull latest changes for a clone.
    /// </summary>
    [HttpPost("pull")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Pull([FromQuery] string clonePath)
    {
        var success = await cloneService.PullLatestAsync(clonePath);
        if (!success)
        {
            return BadRequest("Failed to pull latest");
        }
        return NoContent();
    }

    /// <summary>
    /// Get branch and commit information for a session's working directory.
    /// </summary>
    [HttpGet("session-branch-info")]
    [ProducesResponseType<SessionBranchInfo>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SessionBranchInfo>> GetSessionBranchInfo([FromQuery] string workingDirectory)
    {
        var info = await cloneService.GetSessionBranchInfoAsync(workingDirectory);
        if (info == null)
        {
            return NotFound();
        }
        return Ok(info);
    }

    /// <summary>
    /// Create a new session on a branch.
    /// This endpoint handles the complete flow:
    /// 1. Pulls latest changes on the base branch
    /// 2. Creates or gets existing clone for the branch
    /// 3. Pulls clone to get remote changes (in case branch exists remotely)
    /// 4. Creates a session in Plan mode
    /// </summary>
    [HttpPost("session")]
    [ProducesResponseType<CreateBranchSessionResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CreateBranchSessionResponse>> CreateBranchSession([FromBody] CreateBranchSessionRequest request)
    {
        // Validate project exists
        var project = await projectService.GetByIdAsync(request.ProjectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        // Validate branch name is not empty
        if (string.IsNullOrWhiteSpace(request.BranchName))
        {
            return BadRequest("Branch name is required");
        }

        var branchName = request.BranchName.Trim();
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

        // Get or create clone for the branch
        string? clonePath = await cloneService.GetClonePathForBranchAsync(project.LocalPath, branchName);

        if (string.IsNullOrEmpty(clonePath))
        {
            // Clone doesn't exist, create it
            logger.LogInformation("Creating clone for branch {BranchName} from base {BaseBranch}", branchName, baseBranch);

            clonePath = await cloneService.CreateCloneAsync(
                project.LocalPath,
                branchName,
                createBranch: true,
                baseBranch: baseBranch);

            if (string.IsNullOrEmpty(clonePath))
            {
                return BadRequest("Failed to create clone for branch");
            }

            logger.LogInformation("Created clone at {ClonePath} for branch {BranchName}", clonePath, branchName);
        }
        else
        {
            logger.LogInformation("Using existing clone at {ClonePath} for branch {BranchName}", clonePath, branchName);
        }

        // Pull the clone to get remote changes (in case branch exists remotely)
        var clonePullSuccess = await cloneService.PullLatestAsync(clonePath);
        if (!clonePullSuccess)
        {
            logger.LogWarning("Failed to pull latest changes for clone at {ClonePath}, continuing anyway", clonePath);
        }

        // Determine model
        var model = project.DefaultModel ?? "sonnet";

        // Create session in Plan mode using branch name as entity ID
        var session = await sessionService.StartSessionAsync(
            branchName,
            request.ProjectId,
            clonePath,
            SessionMode.Plan,
            model,
            systemPrompt: null);

        return Created(
            string.Empty,
            new CreateBranchSessionResponse
            {
                SessionId = session.Id,
                BranchName = branchName,
                ClonePath = clonePath
            });
    }
}

