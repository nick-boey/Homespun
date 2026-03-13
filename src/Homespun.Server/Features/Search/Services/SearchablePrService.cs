using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Homespun.Features.PullRequests.Data;
using Microsoft.Extensions.Logging;

namespace Homespun.Features.Search;

/// <summary>
/// Service for retrieving searchable PR summaries from a project.
/// Combines open and merged PRs from GitHub for search functionality.
/// </summary>
public class SearchablePrService(
    IDataStore dataStore,
    IPrDataProvider prDataProvider,
    ILogger<SearchablePrService> logger) : ISearchablePrService
{
    /// <inheritdoc />
    public async Task<PrListResult> GetPrsAsync(string projectId)
    {
        var project = dataStore.GetProject(projectId);
        if (project == null)
        {
            logger.LogWarning("Project {ProjectId} not found", projectId);
            throw new KeyNotFoundException($"Project '{projectId}' not found");
        }

        // Fetch open and merged PRs in parallel
        var openPrsTask = prDataProvider.GetOpenPullRequestsWithStatusAsync(projectId);
        var mergedPrsTask = prDataProvider.GetMergedPullRequestsWithTimeAsync(projectId);

        await Task.WhenAll(openPrsTask, mergedPrsTask);

        var openPrs = await openPrsTask;
        var mergedPrs = await mergedPrsTask;

        // Combine and deduplicate by PR number, then sort
        var allPrs = openPrs
            .Select(p => new SearchablePr(p.PullRequest.Number, p.PullRequest.Title, p.PullRequest.BranchName))
            .Concat(mergedPrs.Select(p => new SearchablePr(p.PullRequest.Number, p.PullRequest.Title, p.PullRequest.BranchName)))
            .DistinctBy(p => p.Number)
            .OrderBy(p => p.Number)
            .ToList();

        var hash = ComputeHash(allPrs);

        logger.LogDebug("Listed {PrCount} PRs for project {ProjectId}, hash: {Hash}",
            allPrs.Count, projectId, hash);

        return new PrListResult(allPrs.AsReadOnly(), hash);
    }

    private static string ComputeHash(IEnumerable<SearchablePr> prs)
    {
        var content = JsonSerializer.Serialize(prs);
        var bytes = Encoding.UTF8.GetBytes(content);
        var hashBytes = SHA256.HashData(bytes);
        return Convert.ToBase64String(hashBytes);
    }
}
