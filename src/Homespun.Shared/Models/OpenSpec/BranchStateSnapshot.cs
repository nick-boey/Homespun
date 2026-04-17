namespace Homespun.Shared.Models.OpenSpec;

/// <summary>
/// A per-branch snapshot of OpenSpec change state, posted by the worker at session end
/// and served to the UI via the graph endpoint.
/// </summary>
public class BranchStateSnapshot
{
    /// <summary>
    /// The project this snapshot belongs to.
    /// </summary>
    public required string ProjectId { get; init; }

    /// <summary>
    /// The branch name (including any <c>feat/</c>-style prefix).
    /// </summary>
    public required string Branch { get; init; }

    /// <summary>
    /// The fleece-id suffix parsed from the branch name.
    /// </summary>
    public required string FleeceId { get; init; }

    /// <summary>
    /// Changes linked to this branch's fleece issue (live and archived).
    /// </summary>
    public List<SnapshotChange> Changes { get; init; } = new();

    /// <summary>
    /// Changes on the branch without a sidecar.
    /// </summary>
    public List<SnapshotOrphan> Orphans { get; init; } = new();

    /// <summary>
    /// Server-side timestamp (UTC) when the snapshot was stored.
    /// </summary>
    public DateTimeOffset CapturedAt { get; init; }
}

/// <summary>
/// Per-change entry in a branch snapshot.
/// </summary>
public class SnapshotChange
{
    public required string Name { get; init; }
    public required string CreatedBy { get; init; }
    public bool IsArchived { get; init; }
    public string? ArchivedFolderName { get; init; }
    public ChangeArtifactState? ArtifactState { get; init; }
    public int TasksDone { get; init; }
    public int TasksTotal { get; init; }
    public string? NextIncomplete { get; init; }
    public List<PhaseState> Phases { get; init; } = new();
}

/// <summary>
/// Per-orphan entry in a branch snapshot.
/// </summary>
public class SnapshotOrphan
{
    public required string Name { get; init; }
    public bool CreatedOnBranch { get; init; }
}
