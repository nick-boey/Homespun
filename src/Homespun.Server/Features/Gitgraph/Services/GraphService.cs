using Fleece.Core.Models;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.Fleece.Services;
using Homespun.Features.Projects;
using Homespun.Features.PullRequests.Data;

namespace Homespun.Features.Gitgraph.Services;

/// <summary>
/// Service for building graph data from Fleece Issues and PullRequests.
/// Uses IFleeceService for fast issue access.
/// Supports caching of PR data to improve page load times.
/// Cache is stored in JSONL files alongside the project data for fast startup.
/// </summary>
public class GraphService(
    IProjectService projectService,
    IGitHubService gitHubService,
    IFleeceService fleeceService,
    IClaudeSessionStore sessionStore,
    IDataStore dataStore,
    PullRequestWorkflowService workflowService,
    IGraphCacheService cacheService,
    ILogger<GraphService> logger) : IGraphService
{
    private readonly GraphBuilder _graphBuilder = new();
    private readonly TaskGraphBuilder _taskGraphBuilder = new();
    private readonly GitgraphApiMapper _mapper = new();

    /// <inheritdoc />
    public async Task<Graph> BuildGraphAsync(string projectId, int? maxPastPRs = 5)
    {
        return await BuildGraphInternalAsync(projectId, maxPastPRs, useCache: false);
    }

    /// <inheritdoc />
    public async Task<GitgraphJsonData> BuildGraphJsonAsync(string projectId, int? maxPastPRs = 5, bool useCache = true)
    {
        var graph = await BuildGraphInternalAsync(projectId, maxPastPRs, useCache);
        var jsonData = _mapper.ToJson(graph);

        // Enrich nodes with agent status data
        EnrichWithAgentStatuses(jsonData, projectId);

        return jsonData;
    }

    /// <inheritdoc />
    public async Task<GitgraphJsonData> BuildGraphJsonWithFreshDataAsync(string projectId, int? maxPastPRs = 5)
    {
        var graph = await BuildGraphInternalWithStatusCachingAsync(projectId, maxPastPRs);
        var jsonData = _mapper.ToJson(graph);

        // Enrich nodes with agent status data
        EnrichWithAgentStatuses(jsonData, projectId);

        return jsonData;
    }

    /// <inheritdoc />
    public async Task<Graph?> BuildGraphFromCacheOnlyAsync(string projectId, int? maxPastPRs = 5)
    {
        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
        {
            logger.LogWarning("Project not found: {ProjectId}", projectId);
            return null;
        }

        // Ensure cache is loaded from disk for this project
        cacheService.LoadCacheForProject(projectId, project.LocalPath);

        var cachedData = cacheService.GetCachedPRData(projectId);
        if (cachedData == null)
        {
            logger.LogDebug("No cached data available for project {ProjectId}", projectId);
            return null;
        }

        logger.LogDebug("Building graph from cache only for project {ProjectId}, cached at {CachedAt}",
            projectId, cachedData.CachedAt);

        var allPrs = cachedData.ClosedPrs.Concat(cachedData.OpenPrs).ToList();

        // Fetch issues from Fleece (fast local operation)
        var issues = await GetIssuesAsync(project.LocalPath);

        // Filter out issues that are linked to PRs
        var linkedIssueIds = GetLinkedIssueIds(projectId);
        var filteredIssues = issues.Where(i => !linkedIssueIds.Contains(i.Id)).ToList();

        // Use cached PR statuses (no GitHub API calls)
        var issuePrStatuses = cachedData.IssuePrStatuses;

        logger.LogDebug(
            "Building graph from cache for project {ProjectId}: {OpenPrCount} open PRs, {ClosedPrCount} closed PRs, {IssueCount} issues, {StatusCount} cached statuses",
            projectId, cachedData.OpenPrs.Count, cachedData.ClosedPrs.Count, filteredIssues.Count, issuePrStatuses.Count);

        return _graphBuilder.Build(allPrs, filteredIssues, maxPastPRs, issuePrStatuses);
    }

    /// <inheritdoc />
    public async Task<GitgraphJsonData?> BuildGraphJsonFromCacheOnlyAsync(string projectId, int? maxPastPRs = 5)
    {
        var graph = await BuildGraphFromCacheOnlyAsync(projectId, maxPastPRs);
        if (graph == null)
            return null;

        var jsonData = _mapper.ToJson(graph);

        // Enrich nodes with agent status data
        EnrichWithAgentStatuses(jsonData, projectId);

        return jsonData;
    }

    /// <inheritdoc />
    public DateTime? GetCacheTimestamp(string projectId)
    {
        return cacheService.GetCacheTimestamp(projectId);
    }

    /// <inheritdoc />
    public bool HasCachedData(string projectId)
    {
        return cacheService.GetCachedPRData(projectId) != null;
    }

    /// <inheritdoc />
    public async Task<Graph?> BuildTaskGraphAsync(string projectId)
    {
        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
        {
            logger.LogWarning("Project not found: {ProjectId}", projectId);
            return null;
        }

        try
        {
            // Get task graph from Fleece.Core
            var taskGraph = await fleeceService.GetTaskGraphAsync(project.LocalPath);
            if (taskGraph == null)
            {
                logger.LogDebug("No task graph available for project {ProjectId}", projectId);
                return null;
            }

            // Get issues for detail lookup
            var issues = await GetIssuesAsync(project.LocalPath);

            // Filter out issues that are linked to PRs
            var linkedIssueIds = GetLinkedIssueIds(projectId);
            var filteredIssues = issues.Where(i => !linkedIssueIds.Contains(i.Id)).ToList();

            // Build lookup of issue ID to PR status
            var issuePrStatuses = await GetIssuePrStatusesAsync(projectId, filteredIssues);

            logger.LogDebug(
                "Building task graph for project {ProjectId}: {NodeCount} nodes, {TotalLanes} lanes",
                projectId, taskGraph.Nodes.Count, taskGraph.TotalLanes);

            // Note: TaskGraphBuilder.Build was updated in Fleece.Core v1.2.0 - issues are now embedded in TaskGraphNode
            return _taskGraphBuilder.Build(taskGraph, issuePrStatuses);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to build task graph for project {ProjectId}", projectId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<GitgraphJsonData?> BuildTaskGraphJsonAsync(string projectId)
    {
        var graph = await BuildTaskGraphAsync(projectId);
        if (graph == null)
            return null;

        var jsonData = _mapper.ToJson(graph);

        // Enrich nodes with agent status data
        EnrichWithAgentStatuses(jsonData, projectId);

        return jsonData;
    }

    /// <summary>
    /// Internal method to build graph with optional caching support.
    /// </summary>
    private async Task<Graph> BuildGraphInternalAsync(string projectId, int? maxPastPRs, bool useCache)
    {
        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
        {
            logger.LogWarning("Project not found: {ProjectId}", projectId);
            return new Graph([], new Dictionary<string, GraphBranch>());
        }

        List<PullRequestInfo> openPrs;
        List<PullRequestInfo> closedPrs;

        // Ensure cache is loaded from disk for this project
        if (useCache)
        {
            cacheService.LoadCacheForProject(projectId, project.LocalPath);
        }

        // Try to use cached PR data if requested
        var cachedData = useCache ? cacheService.GetCachedPRData(projectId) : null;

        if (cachedData != null)
        {
            logger.LogDebug("Using cached PR data for project {ProjectId}, cached at {CachedAt}",
                projectId, cachedData.CachedAt);
            openPrs = cachedData.OpenPrs;
            closedPrs = cachedData.ClosedPrs;
        }
        else
        {
            // Fetch PRs from GitHub (expensive operation)
            logger.LogDebug("Fetching fresh PR data from GitHub for project {ProjectId}", projectId);
            openPrs = await GetOpenPullRequestsSafe(projectId);
            closedPrs = await GetClosedPullRequestsSafe(projectId);

            // Cache the fresh data for future use
            await cacheService.CachePRDataAsync(projectId, project.LocalPath, openPrs, closedPrs);
        }

        var allPrs = closedPrs.Concat(openPrs).ToList();

        // Fetch issues from Fleece (fast operation, always fresh)
        var issues = await GetIssuesAsync(project.LocalPath);

        // Filter out issues that are linked to PRs (they will be shown with the PR instead)
        var linkedIssueIds = GetLinkedIssueIds(projectId);
        var filteredIssues = issues.Where(i => !linkedIssueIds.Contains(i.Id)).ToList();

        // Build lookup of issue ID to PR status for remaining issues with linked PRs
        var issuePrStatuses = await GetIssuePrStatusesAsync(projectId, filteredIssues);

        logger.LogDebug(
            "Building graph for project {ProjectId}: {OpenPrCount} open PRs, {ClosedPrCount} closed PRs, {IssueCount} issues ({FilteredCount} after filtering linked), {LinkedPrCount} with PR status, maxPastPRs: {MaxPastPRs}, fromCache: {FromCache}",
            projectId, openPrs.Count, closedPrs.Count, issues.Count, filteredIssues.Count, issuePrStatuses.Count, maxPastPRs, cachedData != null);

        return _graphBuilder.Build(allPrs, filteredIssues, maxPastPRs, issuePrStatuses);
    }

    /// <summary>
    /// Internal method to build graph with fresh data and cache including PR statuses.
    /// Used by BuildGraphJsonWithFreshDataAsync to ensure statuses are cached for future cache-only loads.
    /// </summary>
    private async Task<Graph> BuildGraphInternalWithStatusCachingAsync(string projectId, int? maxPastPRs)
    {
        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
        {
            logger.LogWarning("Project not found: {ProjectId}", projectId);
            return new Graph([], new Dictionary<string, GraphBranch>());
        }

        // Fetch PRs from GitHub (expensive operation)
        logger.LogDebug("Fetching fresh PR data from GitHub for project {ProjectId}", projectId);
        var openPrs = await GetOpenPullRequestsSafe(projectId);
        var closedPrs = await GetClosedPullRequestsSafe(projectId);

        var allPrs = closedPrs.Concat(openPrs).ToList();

        // Fetch issues from Fleece (fast operation, always fresh)
        var issues = await GetIssuesAsync(project.LocalPath);

        // Filter out issues that are linked to PRs (they will be shown with the PR instead)
        var linkedIssueIds = GetLinkedIssueIds(projectId);
        var filteredIssues = issues.Where(i => !linkedIssueIds.Contains(i.Id)).ToList();

        // Build lookup of issue ID to PR status for remaining issues with linked PRs
        var issuePrStatuses = await GetIssuePrStatusesAsync(projectId, filteredIssues);

        // Cache the fresh data INCLUDING statuses for future cache-only loads
        await cacheService.CachePRDataWithStatusesAsync(projectId, project.LocalPath, openPrs, closedPrs, issuePrStatuses);

        logger.LogDebug(
            "Building graph with fresh data for project {ProjectId}: {OpenPrCount} open PRs, {ClosedPrCount} closed PRs, {IssueCount} issues, {StatusCount} PR statuses cached",
            projectId, openPrs.Count, closedPrs.Count, filteredIssues.Count, issuePrStatuses.Count);

        return _graphBuilder.Build(allPrs, filteredIssues, maxPastPRs, issuePrStatuses);
    }

    /// <summary>
    /// Enriches graph commit data with agent session statuses.
    /// </summary>
    private void EnrichWithAgentStatuses(GitgraphJsonData jsonData, string projectId)
    {
        // Get all sessions for this project
        var sessions = sessionStore.GetByProjectId(projectId);
        if (sessions.Count == 0) return;

        // Build lookup by entity ID, taking the most recently active session per entity
        // (multiple sessions can exist for the same entity when agents are stopped and restarted)
        var sessionsByEntityId = sessions
            .GroupBy(s => s.EntityId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.LastActivityAt).First(), StringComparer.OrdinalIgnoreCase);

        // Build lookup from GitHub PR number to internal PR ID for matching PR sessions
        var trackedPrs = dataStore.GetPullRequestsByProject(projectId);
        var prIdByGitHubNumber = trackedPrs
            .Where(pr => pr.GitHubPRNumber.HasValue)
            .ToDictionary(pr => pr.GitHubPRNumber!.Value, pr => pr.Id);

        // Enrich commits with agent status
        foreach (var commit in jsonData.Commits)
        {
            // Check if there's an active session for this issue
            if (commit.IssueId != null && sessionsByEntityId.TryGetValue(commit.IssueId, out var session))
            {
                commit.AgentStatus = CreateAgentStatusData(session);
            }
            // Check if there's an active session for this PR
            else if (commit.PullRequestNumber.HasValue &&
                     prIdByGitHubNumber.TryGetValue(commit.PullRequestNumber.Value, out var prEntityId) &&
                     sessionsByEntityId.TryGetValue(prEntityId, out var prSession))
            {
                commit.AgentStatus = CreateAgentStatusData(prSession);
            }
        }
    }

    /// <summary>
    /// Creates AgentStatusData from a ClaudeSession.
    /// </summary>
    private static AgentStatusData CreateAgentStatusData(ClaudeSession session)
    {
        return new AgentStatusData
        {
            IsActive = session.Status.IsActive(),
            Status = session.Status.ToString(),
            SessionId = session.Id
        };
    }

    /// <summary>
    /// Gets PR status for issues that have linked PRs.
    /// </summary>
    private async Task<Dictionary<string, PullRequestStatus>> GetIssuePrStatusesAsync(string projectId, List<Issue> issues)
    {
        var result = new Dictionary<string, PullRequestStatus>(StringComparer.OrdinalIgnoreCase);

        try
        {
            // Get tracked PRs linked to issues
            var trackedPrs = dataStore.GetPullRequestsByProject(projectId)
                .Where(pr => !string.IsNullOrEmpty(pr.BeadsIssueId) && pr.GitHubPRNumber.HasValue)
                .ToList();

            if (trackedPrs.Count == 0)
                return result;

            // Get open PRs with status from GitHub
            var openPrsWithStatus = await workflowService.GetOpenPullRequestsWithStatusAsync(projectId);
            var prStatusByNumber = openPrsWithStatus.ToDictionary(p => p.PullRequest.Number, p => p.Status);

            foreach (var trackedPr in trackedPrs)
            {
                if (trackedPr.BeadsIssueId != null && trackedPr.GitHubPRNumber.HasValue)
                {
                    if (prStatusByNumber.TryGetValue(trackedPr.GitHubPRNumber.Value, out var status))
                    {
                        result[trackedPr.BeadsIssueId] = status;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get PR statuses for issues in project {ProjectId}", projectId);
        }

        return result;
    }

    private async Task<List<PullRequestInfo>> GetOpenPullRequestsSafe(string projectId)
    {
        try
        {
            return await gitHubService.GetOpenPullRequestsAsync(projectId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch open PRs for project {ProjectId}", projectId);
            return [];
        }
    }

    private async Task<List<PullRequestInfo>> GetClosedPullRequestsSafe(string projectId)
    {
        try
        {
            return await gitHubService.GetClosedPullRequestsAsync(projectId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch closed PRs for project {ProjectId}", projectId);
            return [];
        }
    }

    /// <summary>
    /// Gets the set of issue IDs that are linked to PRs.
    /// Issues linked to PRs should not be shown in the graph (their info is shown with the PR instead).
    /// </summary>
    private HashSet<string> GetLinkedIssueIds(string projectId)
    {
        var prs = dataStore.GetPullRequestsByProject(projectId);
        return prs
            .Where(pr => !string.IsNullOrEmpty(pr.BeadsIssueId))
            .Select(pr => pr.BeadsIssueId!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets issues from Fleece.
    /// Returns open issues only (no Complete, Closed, Archived, or Deleted).
    /// </summary>
    private async Task<List<Issue>> GetIssuesAsync(string workingDirectory)
    {
        try
        {
            // Check if .fleece directory exists
            var fleeceDir = Path.Combine(workingDirectory, ".fleece");
            if (!Directory.Exists(fleeceDir))
            {
                logger.LogDebug("Fleece not initialized for {WorkingDirectory}", workingDirectory);
                return [];
            }

            // Get open issues only (all non-completed statuses)
            var issues = await fleeceService.ListIssuesAsync(workingDirectory);
            return issues
                .Where(i => i.Status is IssueStatus.Open or IssueStatus.Progress or IssueStatus.Review)
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get issues for {WorkingDirectory}", workingDirectory);
            return [];
        }
    }
}
