using Homespun.Features.Projects;
using Homespun.Shared.Models.Sessions;
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
    IProjectService projectService) : ControllerBase
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
}

/// <summary>
/// Request model for creating a clone.
/// </summary>
public class CreateCloneRequest
{
    /// <summary>
    /// The project ID.
    /// </summary>
    public required string ProjectId { get; set; }

    /// <summary>
    /// The branch name.
    /// </summary>
    public required string BranchName { get; set; }

    /// <summary>
    /// Whether to create a new branch.
    /// </summary>
    public bool CreateBranch { get; set; }

    /// <summary>
    /// Base branch for the new branch (if creating).
    /// </summary>
    public string? BaseBranch { get; set; }
}

/// <summary>
/// Response model for creating a clone.
/// </summary>
public class CreateCloneResponse
{
    /// <summary>
    /// The path to the created clone.
    /// </summary>
    public required string Path { get; set; }

    /// <summary>
    /// The branch name.
    /// </summary>
    public required string BranchName { get; set; }
}

/// <summary>
/// Response model for checking clone existence.
/// </summary>
public class CloneExistsResponse
{
    /// <summary>
    /// Whether the clone exists.
    /// </summary>
    public bool Exists { get; set; }
}
