using System.Text.RegularExpressions;
using Fleece.Core.Models;
using Homespun.Shared.Models.Fleece;

namespace Homespun.Shared.Models.PullRequests;

/// <summary>
/// Utility class for generating branch names from issue data.
/// Branch format: {type}/{branch-id}+{issue-id}
/// </summary>
public static partial class BranchNameGenerator
{
    /// <summary>
    /// Generates a branch name for an issue, recalculating from the current issue properties.
    /// </summary>
    public static string GenerateBranchName(Issue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);
        return GenerateBranchNamePreview(issue.Id, issue.Type, issue.Title, issue.WorkingBranchId);
    }

    /// <summary>
    /// Generates a branch name for an issue response DTO.
    /// </summary>
    public static string GenerateBranchName(IssueResponse issue)
    {
        ArgumentNullException.ThrowIfNull(issue);
        return GenerateBranchNamePreview(issue.Id, issue.Type, issue.Title, issue.WorkingBranchId);
    }

    /// <summary>
    /// Generates a branch name preview from individual components.
    /// </summary>
    public static string GenerateBranchNamePreview(
        string issueId,
        IssueType type,
        string title,
        string? workingBranchId = null)
    {
        var typeStr = type.ToString().ToLowerInvariant();

        var branchId = !string.IsNullOrWhiteSpace(workingBranchId)
            ? workingBranchId.Trim()
            : SanitizeForBranch(title);

        return $"{typeStr}/{branchId}+{issueId}";
    }

    /// <summary>
    /// Sanitizes a string for use in a branch name.
    /// </summary>
    public static string SanitizeForBranch(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return "<title>";

        var sanitized = input.ToLowerInvariant()
            .Replace(" ", "-")
            .Replace("_", "-");

        sanitized = NonAlphanumericRegex().Replace(sanitized, "");
        sanitized = ConsecutiveHyphensRegex().Replace(sanitized, "-");

        return sanitized.Trim('-');
    }

    [GeneratedRegex("[^a-z0-9-]")]
    private static partial Regex NonAlphanumericRegex();

    [GeneratedRegex("-+")]
    private static partial Regex ConsecutiveHyphensRegex();
}
