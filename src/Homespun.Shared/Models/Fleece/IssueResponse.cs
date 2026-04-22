using Fleece.Core.Models;

namespace Homespun.Shared.Models.Fleece;

/// <summary>
/// Marks an <see cref="IssueResponse"/> property as structure-preserving — eligible
/// for in-place snapshot patching via <c>IProjectTaskGraphSnapshotStore.PatchIssueFields</c>.
/// Mutations limited to these fields can skip the ~3s rebuild.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class PatchableFieldAttribute : Attribute;

/// <summary>
/// Marks an <see cref="IssueResponse"/> property as topology-affecting — mutations
/// to this field force a full snapshot invalidation so lanes, grouping, and derived
/// data are recomputed.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class TopologyFieldAttribute : Attribute;

/// <summary>
/// API response model for Fleece issues.
/// Uses regular settable properties to avoid deserialization issues with
/// Fleece.Core.Models.Issue's required init-only properties in trimmed environments (Blazor WASM).
/// </summary>
public class IssueResponse
{
    public string Id { get; set; } = "";
    [PatchableField] public string Title { get; set; } = "";
    [PatchableField] public string? Description { get; set; }
    [TopologyField] public IssueStatus Status { get; set; }
    [TopologyField] public IssueType Type { get; set; }
    [PatchableField] public int? Priority { get; set; }
    [TopologyField] public List<int> LinkedPRs { get; set; } = [];
    [TopologyField] public List<string> LinkedIssues { get; set; } = [];
    [TopologyField] public List<ParentIssueRefResponse> ParentIssues { get; set; } = [];
    [PatchableField] public List<string> Tags { get; set; } = [];
    [TopologyField] public string? WorkingBranchId { get; set; }
    [PatchableField] public ExecutionMode ExecutionMode { get; set; }
    [PatchableField] public string? CreatedBy { get; set; }
    [PatchableField] public string? AssignedTo { get; set; }
    [PatchableField] public DateTimeOffset LastUpdate { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// API response model for parent issue references.
/// </summary>
public class ParentIssueRefResponse
{
    public string ParentIssue { get; set; } = "";
    public string? SortOrder { get; set; }
}
