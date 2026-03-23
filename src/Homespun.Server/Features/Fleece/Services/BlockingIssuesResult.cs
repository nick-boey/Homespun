using Fleece.Core.Models;

namespace Homespun.Features.Fleece.Services;

/// <summary>
/// Result containing details about issues blocking execution of a target issue.
/// </summary>
public record BlockingIssuesResult(
    IReadOnlyList<Issue> OpenChildren,
    IReadOnlyList<Issue> OpenPriorSiblings)
{
    /// <summary>
    /// Returns true if there are any blocking issues (open children or open prior siblings).
    /// </summary>
    public bool IsBlocked => OpenChildren.Count > 0 || OpenPriorSiblings.Count > 0;
}
