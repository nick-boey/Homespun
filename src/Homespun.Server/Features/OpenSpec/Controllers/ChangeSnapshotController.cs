using Homespun.Features.Git;
using Homespun.Features.OpenSpec.Services;
using Homespun.Features.PullRequests.Data;
using Homespun.Shared.Models.OpenSpec;
using Homespun.Shared.Models.Projects;
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
    /// issue. Two modes:
    /// <list type="bullet">
    /// <item><description><b>Branchless</b> (<see cref="LinkOrphanRequest.Branch"/> null/empty):
    /// scans every tracked clone (main + <c>.clones/*</c>) for one carrying
    /// <c>openspec/changes/&lt;changeName&gt;/</c> and writes the sidecar into every
    /// match within one request. Returns 404 only when no clone carries the change.</description></item>
    /// <item><description><b>Branch-scoped</b> (<see cref="LinkOrphanRequest.Branch"/> set):
    /// resolves the named clone via <see cref="IGitCloneService.GetClonePathForBranchAsync"/>
    /// and writes the sidecar to that single clone. Returns 404 if the clone or change
    /// directory is missing.</description></item>
    /// </list>
    /// The next branch scan picks the sidecar(s) up and the graph reclassifies the change
    /// as linked.
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

        var sidecar = new ChangeSidecar
        {
            FleeceId = request.FleeceId,
            CreatedBy = "server"
        };

        if (string.IsNullOrWhiteSpace(request.Branch))
        {
            return await LinkAcrossClonesAsync(project, request.ChangeName, sidecar, ct);
        }

        return await LinkOnBranchAsync(project, request.Branch, request.ChangeName, sidecar, ct);
    }

    private async Task<IActionResult> LinkOnBranchAsync(
        Project project,
        string branch,
        string changeName,
        ChangeSidecar sidecar,
        CancellationToken ct)
    {
        var clonePath = await cloneService.GetClonePathForBranchAsync(project.LocalPath!, branch);
        if (string.IsNullOrEmpty(clonePath) || !Directory.Exists(clonePath))
        {
            return NotFound("clone path not resolved");
        }

        var changeDir = Path.Combine(clonePath, "openspec", "changes", changeName);
        if (!Directory.Exists(changeDir))
        {
            return NotFound("change directory not found");
        }

        await sidecarService.WriteSidecarAsync(changeDir, sidecar, ct);

        cache.Invalidate(project.Id, branch);

        logger.LogInformation(
            "Linked orphan change {Change} on branch {Branch} to fleece issue {Fleece}",
            changeName, branch, sidecar.FleeceId);

        return NoContent();
    }

    private async Task<IActionResult> LinkAcrossClonesAsync(
        Project project,
        string changeName,
        ChangeSidecar sidecar,
        CancellationToken ct)
    {
        var matches = new List<(string ClonePath, string ChangeDir, string? BranchKey)>();

        if (Directory.Exists(project.LocalPath))
        {
            var mainChangeDir = Path.Combine(project.LocalPath!, "openspec", "changes", changeName);
            if (Directory.Exists(mainChangeDir))
            {
                matches.Add((project.LocalPath!, mainChangeDir, null));
            }
        }

        var clones = await cloneService.ListClonesAsync(project.LocalPath!);
        foreach (var clone in clones)
        {
            if (clone.IsBare) continue;

            var workdir = !string.IsNullOrEmpty(clone.WorkdirPath) ? clone.WorkdirPath : clone.Path;
            if (string.IsNullOrEmpty(workdir) || !Directory.Exists(workdir)) continue;

            var changeDir = Path.Combine(workdir, "openspec", "changes", changeName);
            if (!Directory.Exists(changeDir)) continue;

            var branchKey = !string.IsNullOrEmpty(clone.ExpectedBranch)
                ? clone.ExpectedBranch
                : clone.Branch?.Replace("refs/heads/", "");

            matches.Add((workdir, changeDir, branchKey));
        }

        if (matches.Count == 0)
        {
            return NotFound("change directory not found in any tracked clone");
        }

        await Task.WhenAll(matches.Select(m =>
            sidecarService.WriteSidecarAsync(m.ChangeDir, sidecar, ct)));

        foreach (var match in matches)
        {
            if (!string.IsNullOrEmpty(match.BranchKey))
            {
                cache.Invalidate(project.Id, match.BranchKey);
            }
        }

        logger.LogInformation(
            "Linked orphan change {Change} to fleece issue {Fleece} across {Count} clone(s)",
            changeName, sidecar.FleeceId, matches.Count);

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
