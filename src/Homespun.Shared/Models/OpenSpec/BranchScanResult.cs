namespace Homespun.Shared.Models.OpenSpec;

/// <summary>
/// The output of scanning a branch clone's <c>openspec/changes/</c> directory for change state.
/// </summary>
public class BranchScanResult
{
    /// <summary>
    /// The branch's fleece-id suffix (parsed from the branch name) used to match sidecars.
    /// </summary>
    public required string BranchFleeceId { get; init; }

    /// <summary>
    /// Changes whose sidecar <c>fleeceId</c> matches <see cref="BranchFleeceId"/>.
    /// Includes both live changes (<c>openspec/changes/&lt;name&gt;/</c>) and archived ones
    /// (<c>openspec/changes/archive/&lt;dated&gt;-&lt;name&gt;/</c>).
    /// </summary>
    public List<LinkedChangeInfo> LinkedChanges { get; init; } = new();

    /// <summary>
    /// Changes that exist on disk but lack a sidecar.
    /// </summary>
    public List<OrphanChangeInfo> OrphanChanges { get; init; } = new();

    /// <summary>
    /// Change names whose sidecar points to a different fleece-id (inherited from main).
    /// Reported for diagnostics; excluded from the branch's linked changes.
    /// </summary>
    public List<string> InheritedChangeNames { get; init; } = new();
}

/// <summary>
/// A change linked to the branch's fleece issue via its sidecar.
/// </summary>
public class LinkedChangeInfo
{
    /// <summary>
    /// The change name (the directory name under <c>openspec/changes/</c>, with any
    /// archive date prefix removed when <see cref="IsArchived"/> is true).
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Absolute path to the change directory.
    /// </summary>
    public required string Directory { get; init; }

    /// <summary>
    /// The sidecar's <c>createdBy</c> value.
    /// </summary>
    public required string CreatedBy { get; init; }

    /// <summary>
    /// True when the change has been archived (lives under <c>openspec/changes/archive/</c>).
    /// </summary>
    public bool IsArchived { get; init; }

    /// <summary>
    /// For archived changes, the dated archive folder name (e.g. <c>2026-04-16-my-change</c>).
    /// Null for live changes.
    /// </summary>
    public string? ArchivedFolderName { get; init; }

    /// <summary>
    /// Artifact state from <c>openspec status --change &lt;name&gt; --json</c>.
    /// Null when the CLI could not be invoked successfully (e.g. archived changes that
    /// <c>openspec</c> no longer recognises by name).
    /// </summary>
    public ChangeArtifactState? ArtifactState { get; init; }

    /// <summary>
    /// Parsed state of <c>tasks.md</c>. <see cref="TaskStateSummary.Empty"/> when no tasks file exists.
    /// </summary>
    public TaskStateSummary TaskState { get; init; } = TaskStateSummary.Empty;
}

/// <summary>
/// A change directory without a sidecar.
/// </summary>
public class OrphanChangeInfo
{
    public required string Name { get; init; }
    public required string Directory { get; init; }

    /// <summary>
    /// True when the change appears to have been added on this branch (via
    /// <c>git log --diff-filter=A</c>). Detection is best-effort; defaults to false when
    /// no base branch is supplied or the git query fails.
    /// </summary>
    public bool CreatedOnBranch { get; init; }
}
