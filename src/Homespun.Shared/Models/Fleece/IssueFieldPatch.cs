using Fleece.Core.Models;

namespace Homespun.Shared.Models.Fleece;

/// <summary>
/// Partial update payload for the structure-preserving subset of <see cref="IssueResponse"/>
/// fields. Null means "unchanged"; non-null values overlay the existing values.
/// Consumed by <c>BroadcastIssueFieldsPatched</c> in Delta 2+.
/// </summary>
public class IssueFieldPatch
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public int? Priority { get; set; }
    public List<string>? Tags { get; set; }
    public string? AssignedTo { get; set; }
    public string? CreatedBy { get; set; }
    public ExecutionMode? ExecutionMode { get; set; }
    public DateTimeOffset? LastUpdate { get; set; }
}
