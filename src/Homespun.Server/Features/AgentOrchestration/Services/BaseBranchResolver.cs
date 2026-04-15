using Homespun.Features.Fleece.Services;
using Homespun.Features.GitHub;

namespace Homespun.Features.AgentOrchestration.Services;

/// <summary>
/// Result of resolving the base branch for an agent start request.
/// </summary>
public record BaseBranchResolutionResult(string? BaseBranch, string? Error);

/// <summary>
/// Interface for resolving the base branch for agent startup.
/// </summary>
public interface IBaseBranchResolver
{
    /// <summary>
    /// Resolves the correct base branch for an agent start request.
    /// </summary>
    /// <param name="request">The agent start request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A result containing either the resolved base branch name or an error message.
    /// </returns>
    Task<BaseBranchResolutionResult> ResolveBaseBranchAsync(AgentStartRequest request, CancellationToken ct = default);
}

/// <summary>
/// Resolves the base branch for agent startup based on stacked PR relationships.
/// </summary>
/// <remarks>
/// The resolution logic follows these steps:
/// 1. If an explicit BaseBranch is specified in the request, use it
/// 2. Check for a prior sibling in the issue hierarchy
/// 3. If the prior sibling has an open PR, use the PR's branch as the base
/// 4. Otherwise (no prior sibling, or prior sibling's PR is merged/missing), use the default branch
/// </remarks>
public class BaseBranchResolver(
    IProjectFleeceService fleeceService,
    IGitHubService gitHubService,
    ILogger<BaseBranchResolver> logger) : IBaseBranchResolver
{
    /// <inheritdoc/>
    public async Task<BaseBranchResolutionResult> ResolveBaseBranchAsync(
        AgentStartRequest request, CancellationToken ct = default)
    {
        var projectPath = request.ProjectLocalPath;
        var issueId = request.IssueId;

        // Step 1: If BaseBranch is already specified in the request, use it
        if (!string.IsNullOrEmpty(request.BaseBranch))
        {
            logger.LogDebug(
                "Using explicit base branch {BaseBranch} for issue {IssueId}",
                request.BaseBranch, issueId);

            return new BaseBranchResolutionResult(request.BaseBranch, null);
        }

        // Step 2: Check for prior sibling in the hierarchy
        var priorSibling = await fleeceService.GetPriorSiblingAsync(projectPath, issueId, ct);
        if (priorSibling == null)
        {
            logger.LogDebug(
                "No prior sibling found for issue {IssueId}, using default branch {DefaultBranch}",
                issueId, request.ProjectDefaultBranch);

            return new BaseBranchResolutionResult(request.ProjectDefaultBranch, null);
        }

        // Step 3: Check if the prior sibling has a linked PR
        var pr = await gitHubService.GetPullRequestForIssueAsync(request.ProjectId, priorSibling.Id);
        if (pr == null)
        {
            // No PR found - either never created or already merged (merged PRs are removed from tracking)
            logger.LogDebug(
                "Prior sibling {PriorSiblingId} has no tracked PR (may have been merged), using default branch {DefaultBranch}",
                priorSibling.Id, request.ProjectDefaultBranch);

            return new BaseBranchResolutionResult(request.ProjectDefaultBranch, null);
        }

        // Step 4: PR found - it's an open PR, use its branch as the base for stacking
        // Note: Only open PRs are tracked locally (OpenPullRequestStatus enum doesn't have Merged)
        logger.LogInformation(
            "Using stacked PR branch {BranchName} from prior sibling {PriorSiblingId} as base for issue {IssueId}",
            pr.BranchName, priorSibling.Id, issueId);

        return new BaseBranchResolutionResult(pr.BranchName, null);
    }
}
