using Homespun.Features.Git;
using Homespun.Features.OpenSpec.Services;
using Homespun.Features.PullRequests.Data;
using Homespun.Shared.Models.OpenSpec;
using Microsoft.AspNetCore.Mvc;

namespace Homespun.Features.OpenSpec.Controllers;

/// <summary>
/// Endpoints for ingesting and reading OpenSpec per-branch snapshots.
/// </summary>
[ApiController]
[Route("api/openspec")]
[Produces("application/json")]
public class ChangeSnapshotController(
    IBranchStateCacheService cache,
    IBranchStateResolverService resolver,
    ISidecarService sidecarService,
    IGitCloneService cloneService,
    IDataStore dataStore,
    TimeProvider timeProvider,
    ILogger<ChangeSnapshotController> logger) : ControllerBase
{
    /// <summary>
    /// Stores a branch snapshot. Called by the worker at session end after scanning
    /// <c>openspec/changes/</c> on the branch clone.
    /// </summary>
    [HttpPost("branch-state")]
    [ProducesResponseType<BranchStateSnapshot>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<BranchStateSnapshot> PostBranchState([FromBody] BranchStateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectId)
            || string.IsNullOrWhiteSpace(request.Branch)
            || string.IsNullOrWhiteSpace(request.FleeceId))
        {
            return BadRequest("projectId, branch, and fleeceId are required");
        }

        var snapshot = new BranchStateSnapshot
        {
            ProjectId = request.ProjectId,
            Branch = request.Branch,
            FleeceId = request.FleeceId,
            Changes = request.Changes,
            Orphans = request.Orphans,
            CapturedAt = timeProvider.GetUtcNow()
        };

        cache.Put(snapshot);
        logger.LogDebug(
            "Stored OpenSpec branch snapshot for {Project}/{Branch} ({Changes} changes, {Orphans} orphans)",
            request.ProjectId, request.Branch, request.Changes.Count, request.Orphans.Count);

        return Ok(snapshot);
    }

    /// <summary>
    /// Reads the cached branch snapshot. Returns 404 when no fresh snapshot exists
    /// (the on-demand scan endpoint handles live scans; phase 4).
    /// </summary>
    [HttpGet("branch-state")]
    [ProducesResponseType<BranchStateSnapshot>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<BranchStateSnapshot> GetBranchState(
        [FromQuery] string projectId,
        [FromQuery] string branch)
    {
        if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(branch))
        {
            return BadRequest("projectId and branch are required");
        }

        var snapshot = cache.TryGet(projectId, branch);
        if (snapshot is null)
        {
            return NotFound();
        }

        return Ok(snapshot);
    }

    /// <summary>
    /// Returns a snapshot for the branch, falling back to a live on-disk scan when the
    /// cache is cold or stale. Returns 404 when the branch fleece-id, project, or clone
    /// cannot be resolved.
    /// </summary>
    [HttpGet("branch-state/resolve")]
    [ProducesResponseType<BranchStateSnapshot>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BranchStateSnapshot>> ResolveBranchState(
        [FromQuery] string projectId,
        [FromQuery] string branch,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(branch))
        {
            return BadRequest("projectId and branch are required");
        }

        var snapshot = await resolver.GetOrScanAsync(projectId, branch, ct);
        return snapshot is null ? NotFound() : Ok(snapshot);
    }

    /// <summary>
    /// Writes a <c>.homespun.yaml</c> sidecar linking an orphan change to a Fleece
    /// issue. When <see cref="LinkOrphanRequest.Branch"/> is null or empty the
    /// project's main clone is used (main-branch orphans). The next branch scan
    /// picks the sidecar up and the graph reclassifies the change as linked.
    /// </summary>
    [HttpPost("changes/link")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> LinkOrphan(
        [FromBody] LinkOrphanRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectId)
            || string.IsNullOrWhiteSpace(request.ChangeName)
            || string.IsNullOrWhiteSpace(request.FleeceId))
        {
            return BadRequest("projectId, changeName, and fleeceId are required");
        }

        var project = dataStore.GetProject(request.ProjectId);
        if (project is null || string.IsNullOrEmpty(project.LocalPath))
        {
            return NotFound("project or local path not found");
        }

        string? clonePath;
        if (string.IsNullOrWhiteSpace(request.Branch))
        {
            clonePath = project.LocalPath;
        }
        else
        {
            clonePath = await cloneService.GetClonePathForBranchAsync(project.LocalPath, request.Branch);
        }

        if (string.IsNullOrEmpty(clonePath) || !Directory.Exists(clonePath))
        {
            return NotFound("clone path not resolved");
        }

        var changeDir = Path.Combine(clonePath, "openspec", "changes", request.ChangeName);
        if (!Directory.Exists(changeDir))
        {
            return NotFound("change directory not found");
        }

        var sidecar = new ChangeSidecar
        {
            FleeceId = request.FleeceId,
            CreatedBy = "server"
        };
        await sidecarService.WriteSidecarAsync(changeDir, sidecar, ct);

        // Invalidate cached snapshot so the next graph read reflects the link.
        if (!string.IsNullOrWhiteSpace(request.Branch))
        {
            cache.Invalidate(request.ProjectId, request.Branch);
        }

        logger.LogInformation(
            "Linked orphan change {Change} on branch {Branch} to fleece issue {Fleece}",
            request.ChangeName, request.Branch ?? "(main)", request.FleeceId);

        return NoContent();
    }
}

/// <summary>
/// Request body for <c>POST /api/openspec/changes/link</c>.
/// </summary>
public class LinkOrphanRequest
{
    public required string ProjectId { get; init; }
    /// <summary>Leave null/empty for main-branch orphans.</summary>
    public string? Branch { get; init; }
    public required string ChangeName { get; init; }
    public required string FleeceId { get; init; }
}
