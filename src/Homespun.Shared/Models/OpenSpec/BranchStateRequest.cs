namespace Homespun.Shared.Models.OpenSpec;

/// <summary>
/// Worker-to-server payload for <c>POST /api/openspec/branch-state</c>.
/// </summary>
public class BranchStateRequest
{
    public required string ProjectId { get; init; }
    public required string Branch { get; init; }
    public required string FleeceId { get; init; }
    public List<SnapshotChange> Changes { get; init; } = new();
    public List<SnapshotOrphan> Orphans { get; init; } = new();
}
