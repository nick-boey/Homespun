using Fleece.Core.Models;

namespace Homespun.Shared.Models.Fleece;

/// <summary>
/// Represents a diff between two versions of an issue.
/// </summary>
public class IssueDiff
{
    /// <summary>
    /// The issue ID.
    /// </summary>
    public required string IssueId { get; set; }

    /// <summary>
    /// The type of change.
    /// </summary>
    public IssueChangeType ChangeType { get; set; }

    /// <summary>
    /// The original issue state (null if Added).
    /// </summary>
    public Issue? OriginalIssue { get; set; }

    /// <summary>
    /// The modified issue state (null if Deleted).
    /// </summary>
    public Issue? ModifiedIssue { get; set; }

    /// <summary>
    /// List of fields that changed (empty for Added/Deleted).
    /// </summary>
    public List<string> ChangedFields { get; set; } = [];
}