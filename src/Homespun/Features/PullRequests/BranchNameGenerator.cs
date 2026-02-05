using System.Text.RegularExpressions;
using Fleece.Core.Models;

namespace Homespun.Features.PullRequests;

/// <summary>
/// Utility class for generating branch names from issue data.
/// Branch format: {type}/{branch-id}+{issue-id}
///
/// Example: "feature/improve-tool-output+aLP3LH"
/// Example: "bug/fix-authentication+xyz123"
/// </summary>
public static partial class BranchNameGenerator
{
    /// <summary>
    /// Generates a branch name for an issue, recalculating from the current issue properties.
    /// This should always be called just before creating a branch/worktree to ensure
    /// the branch name reflects the current issue state.
    /// </summary>
    /// <param name="issue">The issue to generate a branch name for.</param>
    /// <returns>The generated branch name.</returns>
    public static string GenerateBranchName(Issue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);

        var type = issue.Type.ToString().ToLowerInvariant();

        // Use working branch ID if set, otherwise generate from title
        var branchId = !string.IsNullOrWhiteSpace(issue.WorkingBranchId)
            ? issue.WorkingBranchId.Trim()
            : SanitizeForBranch(issue.Title);

        return $"{type}/{branchId}+{issue.Id}";
    }

    /// <summary>
    /// Generates a branch name preview from individual components.
    /// Used by the IssueEdit form where issue data is being edited but not yet saved.
    /// </summary>
    /// <param name="issueId">The issue ID.</param>
    /// <param name="type">The issue type.</param>
    /// <param name="title">The issue title (used if workingBranchId is empty).</param>
    /// <param name="workingBranchId">Optional custom branch ID.</param>
    /// <returns>The generated branch name preview.</returns>
    public static string GenerateBranchNamePreview(
        string issueId,
        IssueType type,
        string title,
        string? workingBranchId = null)
    {
        var typeStr = type.ToString().ToLowerInvariant();

        // Use working branch ID if set, otherwise generate from title
        var branchId = !string.IsNullOrWhiteSpace(workingBranchId)
            ? workingBranchId.Trim()
            : SanitizeForBranch(title);

        return $"{typeStr}/{branchId}+{issueId}";
    }

    /// <summary>
    /// Sanitizes a string for use in a branch name.
    /// Converts to lowercase, replaces spaces and underscores with hyphens,
    /// removes special characters, and normalizes consecutive hyphens.
    /// </summary>
    /// <param name="input">The string to sanitize.</param>
    /// <returns>A sanitized string safe for use in branch names.</returns>
    public static string SanitizeForBranch(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return "<title>";

        // Convert to lowercase, replace spaces and underscores with hyphens
        var sanitized = input.ToLowerInvariant()
            .Replace(" ", "-")
            .Replace("_", "-");

        // Remove any character that isn't alphanumeric or hyphen
        sanitized = NonAlphanumericRegex().Replace(sanitized, "");

        // Remove consecutive hyphens
        sanitized = ConsecutiveHyphensRegex().Replace(sanitized, "-");

        // Trim hyphens from start and end
        return sanitized.Trim('-');
    }

    [GeneratedRegex("[^a-z0-9-]")]
    private static partial Regex NonAlphanumericRegex();

    [GeneratedRegex("-+")]
    private static partial Regex ConsecutiveHyphensRegex();
}
