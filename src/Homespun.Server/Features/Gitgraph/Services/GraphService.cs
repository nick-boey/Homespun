using Fleece.Core.Models;
using Fleece.Core.Models.Graph;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.Fleece;
using Homespun.Features.Fleece.Services;
using Homespun.Features.Git;
using Homespun.Features.GitHub;
using Homespun.Features.Gitgraph.Telemetry;
using Homespun.Features.OpenSpec.Services;
using Homespun.Features.Projects;
using Homespun.Features.PullRequests.Data;
using Homespun.Shared.Models.Fleece;
using Homespun.Shared.Models.GitHub;

namespace Homespun.Features.Gitgraph.Services;

/// <summary>
/// Service for building graph data from Fleece Issues and PullRequests.
/// Uses IProjectFleeceService for fast issue access.
/// Supports caching of PR data to improve page load times.
/// Cache is stored in JSONL files alongside the project data for fast startup.
/// </summary>
public class GraphService(
    IProjectService projectService,
    IGitHubService gitHubService,
    IProjectFleeceService fleeceService,
    IClaudeSessionStore sessionStore,
    IDataStore dataStore,
    PullRequestWorkflowService workflowService,
    IGraphCacheService cacheService,
    IPRStatusResolver prStatusResolver,
    IIssueGraphOpenSpecEnricher openSpecEnricher,
    IGitCloneService cloneService,
    ILogger<GraphService> logger) : IGraphService
{
    private readonly GraphBuilder _graphBuilder = new();
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
    public async Task<GitgraphJsonData> IncrementalRefreshAsync(string projectId, int? maxPastPRs = 5)
    {
        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
        {
            logger.LogWarning("Project not found: {ProjectId}", projectId);
            return _mapper.ToJson(new Graph([], new Dictionary<string, GraphBranch>()));
        }

        // Ensure cache is loaded from disk
        cacheService.LoadCacheForProject(projectId, project.LocalPath);
        var cachedData = cacheService.GetCachedPRData(projectId);

        if (cachedData == null)
        {
            // No cache - fall back to full fetch (cold start)
            logger.LogInformation("No cache for project {ProjectId}, performing full fetch", projectId);
            return await BuildGraphJsonWithFreshDataAsync(projectId, maxPastPRs);
        }

        // Fetch only open PRs from GitHub
        logger.LogDebug("Incremental refresh for project {ProjectId}: fetching open PRs only", projectId);
        var freshOpenPrs = await GetOpenPullRequestsSafe(projectId);

        // Detect newly closed PRs: cached open PRs not in fresh open list
        var freshOpenPrNumbers = freshOpenPrs.Select(p => p.Number).ToHashSet();
        var cachedOpenPrNumbers = cachedData.OpenPrs.Select(p => p.Number).ToHashSet();
        var newlyClosedPrNumbers = cachedOpenPrNumbers.Except(freshOpenPrNumbers).ToList();

        if (newlyClosedPrNumbers.Count > 0)
        {
            // Build RemovedPrInfo list from tracked PRs
            var trackedPrs = dataStore.GetPullRequestsByProject(projectId);
            var trackedByNumber = trackedPrs
                .Where(pr => pr.GitHubPRNumber.HasValue)
                .ToDictionary(pr => pr.GitHubPRNumber!.Value);

            var removedPrs = newlyClosedPrNumbers.Select(prNumber => new RemovedPrInfo
            {
                PullRequestId = trackedByNumber.TryGetValue(prNumber, out var tp) ? tp.Id : prNumber.ToString(),
                GitHubPrNumber = prNumber,
                FleeceIssueId = trackedByNumber.TryGetValue(prNumber, out var tp2) ? tp2.FleeceIssueId : null
            }).ToList();

            logger.LogInformation(
                "Detected {Count} newly closed PRs for project {ProjectId}: {PrNumbers}",
                newlyClosedPrNumbers.Count, projectId, string.Join(", ", newlyClosedPrNumbers));

            await prStatusResolver.ResolveClosedPRStatusesAsync(projectId, removedPrs);
        }

        // Reload cache after status resolution (it may have been updated)
        cachedData = cacheService.GetCachedPRData(projectId) ?? cachedData;
        var closedPrs = cachedData.ClosedPrs;

        // Build issue-PR statuses from tracked PRs + fresh open PR data (no GitHub call)
        var issuePrStatuses = BuildIssuePrStatusesFromTrackedPrs(projectId, freshOpenPrs);

        // Update cache with fresh open PRs + existing closed PRs + statuses
        await cacheService.CachePRDataWithStatusesAsync(
            projectId, project.LocalPath, freshOpenPrs, closedPrs, issuePrStatuses);

        // Build graph from the refreshed data
        var allPrs = closedPrs.Concat(freshOpenPrs).ToList();
        var issues = await GetIssuesAsync(project.LocalPath);
        var linkedIssueIds = GetLinkedIssueIds(projectId);
        var filteredIssues = issues.Where(i => !linkedIssueIds.Contains(i.Id)).ToList();

        logger.LogDebug(
            "Incremental refresh for project {ProjectId}: {OpenPrCount} open PRs, {ClosedPrCount} closed PRs, {NewlyClosedCount} newly closed, {IssueCount} issues",
            projectId, freshOpenPrs.Count, closedPrs.Count, newlyClosedPrNumbers.Count, filteredIssues.Count);

        var graph = _graphBuilder.Build(allPrs, filteredIssues, maxPastPRs, issuePrStatuses);
        var jsonData = _mapper.ToJson(graph);
        EnrichWithAgentStatuses(jsonData, projectId);
        return jsonData;
    }

    /// <summary>
    /// Builds issue-PR status lookup from tracked PRs and fresh open PR data.
    /// This avoids calling GetOpenPullRequestsWithStatusAsync which hits GitHub for review statuses.
    /// </summary>
    private Dictionary<string, PullRequestStatus> BuildIssuePrStatusesFromTrackedPrs(
        string projectId, List<PullRequestInfo> openPrs)
    {
        var result = new Dictionary<string, PullRequestStatus>(StringComparer.OrdinalIgnoreCase);

        var trackedPrs = dataStore.GetPullRequestsByProject(projectId)
            .Where(pr => !string.IsNullOrEmpty(pr.FleeceIssueId) && pr.GitHubPRNumber.HasValue)
            .ToList();

        if (trackedPrs.Count == 0)
            return result;

        var openPrNumbers = openPrs.Select(p => p.Number).ToHashSet();

        foreach (var trackedPr in trackedPrs)
        {
            if (trackedPr.FleeceIssueId != null && trackedPr.GitHubPRNumber.HasValue)
            {
                if (openPrNumbers.Contains(trackedPr.GitHubPRNumber.Value))
                {
                    // PR is open - use InProgress status to indicate it's active
                    result[trackedPr.FleeceIssueId] = PullRequestStatus.InProgress;
                }
                // If not in open PRs, the status will be resolved by PRStatusResolver
            }
        }

        return result;
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
    public async Task<GraphLayout<Issue>?> BuildTaskGraphAsync(string projectId)
    {
        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
        {
            logger.LogWarning("Project not found: {ProjectId}", projectId);
            return null;
        }

        try
        {
            var layout = await fleeceService.GetTaskGraphAsync(project.LocalPath);
            if (layout == null)
            {
                logger.LogDebug("No task graph available for project {ProjectId}", projectId);
                return null;
            }

            logger.LogDebug(
                "Building task graph for project {ProjectId}: {NodeCount} nodes, {TotalLanes} lanes",
                projectId, layout.Nodes.Count, layout.TotalLanes);

            return layout;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to build task graph for project {ProjectId}", projectId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<string?> BuildTaskGraphTextAsync(string projectId)
    {
        var layout = await BuildTaskGraphAsync(projectId);
        if (layout == null)
            return null;

        return TaskGraphTextRenderer.Render(layout);
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
            Status = session.Status,
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
                .Where(pr => !string.IsNullOrEmpty(pr.FleeceIssueId) && pr.GitHubPRNumber.HasValue)
                .ToList();

            if (trackedPrs.Count == 0)
                return result;

            // Get open PRs with status from GitHub
            var openPrsWithStatus = await workflowService.GetOpenPullRequestsWithStatusAsync(projectId);
            var prStatusByNumber = openPrsWithStatus.ToDictionary(p => p.PullRequest.Number, p => p.Status);

            foreach (var trackedPr in trackedPrs)
            {
                if (trackedPr.FleeceIssueId != null && trackedPr.GitHubPRNumber.HasValue)
                {
                    if (prStatusByNumber.TryGetValue(trackedPr.GitHubPRNumber.Value, out var status))
                    {
                        result[trackedPr.FleeceIssueId] = status;
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
            .Where(pr => !string.IsNullOrEmpty(pr.FleeceIssueId))
            .Select(pr => pr.FleeceIssueId!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets issue IDs that are linked to open PRs.
    /// Tracked PRs in the data store are always open (closed/merged PRs are removed).
    /// These issues should be included in the task graph regardless of their status.
    /// </summary>
    private IEnumerable<string> GetOpenPrLinkedIssueIds(string projectId)
    {
        var prs = dataStore.GetPullRequestsByProject(projectId);
        return prs
            .Where(pr => !string.IsNullOrEmpty(pr.FleeceIssueId))
            .Select(pr => pr.FleeceIssueId!);
    }

    private static bool ComputeIsActionable(Issue issue, int lane, IReadOnlySet<string> openPrLinkedIssueIds)
    {
        return lane == 0 || openPrLinkedIssueIds.Contains(issue.Id);
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

    /// <inheritdoc />
    public async Task<TaskGraphResponse?> BuildEnhancedTaskGraphAsync(string projectId, int maxPastPRs = 5)
    {
        using var buildActivity = GraphgraphActivitySource.Instance.StartActivity("graph.taskgraph.build");
        buildActivity?.SetTag("project.id", projectId);

        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
        {
            logger.LogWarning("Project not found: {ProjectId}", projectId);
            return null;
        }

        try
        {
            // Get issue IDs that are linked to open PRs (tracked PRs are always open)
            // These issues should be included in the task graph regardless of their status
            var openPrLinkedIssueIds = GetOpenPrLinkedIssueIds(projectId).ToHashSet(StringComparer.OrdinalIgnoreCase);

            GraphLayout<Issue>? layout;
            using (var fleeceScan = GraphgraphActivitySource.Instance.StartActivity("graph.taskgraph.fleece.scan"))
            {
                fleeceScan?.SetTag("project.id", projectId);
                layout = await fleeceService.GetTaskGraphWithAdditionalIssuesAsync(
                    project.LocalPath,
                    openPrLinkedIssueIds);
                fleeceScan?.SetTag("layout.nodes", layout?.Nodes.Count ?? 0);
                fleeceScan?.SetTag("layout.edges", layout?.Edges.Count ?? 0);
                fleeceScan?.SetTag("layout.rows", layout?.TotalRows ?? 0);
                fleeceScan?.SetTag("layout.lanes", layout?.TotalLanes ?? 0);
            }

            if (layout == null)
            {
                logger.LogDebug("No task graph available for project {ProjectId}", projectId);
                return null;
            }

            // Build task graph response
            var response = new TaskGraphResponse
            {
                TotalLanes = layout.TotalLanes,
                TotalRows = layout.TotalRows,
                Nodes = layout.Nodes.Select(n => new TaskGraphNodeResponse
                {
                    Issue = IssueDtoMapper.ToResponse(n.Node),
                    Lane = n.Lane,
                    Row = n.Row,
                    IsActionable = ComputeIsActionable(n.Node, n.Lane, openPrLinkedIssueIds)
                }).ToList(),
                Edges = layout.Edges.Select(GitgraphApiMapper.MapEdge).ToList()
            };

            using (var prCacheSpan = GraphgraphActivitySource.Instance.StartActivity("graph.taskgraph.prcache"))
            {
                prCacheSpan?.SetTag("project.id", projectId);
                // Get merged/closed PRs
                cacheService.LoadCacheForProject(projectId, project.LocalPath);
                var cachedData = cacheService.GetCachedPRData(projectId);

                if (cachedData != null)
                {
                    var closedPrs = cachedData.ClosedPrs
                        .Where(pr => pr.Status == PullRequestStatus.Merged || pr.Status == PullRequestStatus.Closed)
                        .OrderBy(pr => pr.MergedAt ?? pr.ClosedAt ?? pr.CreatedAt)  // Oldest at top, newest at bottom
                        .ToList();

                    var totalClosedPrs = closedPrs.Count;
                    // Take the most recent N PRs (from the end of the sorted list)
                    var shownPrs = closedPrs.Count > maxPastPRs
                        ? closedPrs.Skip(closedPrs.Count - maxPastPRs).ToList()
                        : closedPrs;

                    response.MergedPrs = shownPrs.Select(pr => new TaskGraphPrResponse
                    {
                        Number = pr.Number,
                        Title = pr.Title,
                        Url = pr.HtmlUrl,
                        IsMerged = pr.Status == PullRequestStatus.Merged,
                        HasDescription = !string.IsNullOrWhiteSpace(pr.Body)
                    }).ToList();

                    response.HasMorePastPrs = totalClosedPrs > maxPastPRs;
                    response.TotalPastPrsShown = shownPrs.Count;
                }
            }

            using (var sessionsSpan = GraphgraphActivitySource.Instance.StartActivity("graph.taskgraph.sessions"))
            {
                sessionsSpan?.SetTag("project.id", projectId);

                // Get agent statuses
                var sessions = sessionStore.GetByProjectId(projectId);
                logger.LogDebug(
                    "Found {SessionCount} sessions for project {ProjectId}",
                    sessions.Count, projectId);

                // Filter out sessions without EntityId and group by EntityId
                var validSessions = sessions.Where(s => !string.IsNullOrWhiteSpace(s.EntityId)).ToList();
                logger.LogDebug(
                    "Filtered {ValidCount} valid sessions from {TotalCount} total sessions (excluded {ExcludedCount} with null/empty EntityId)",
                    validSessions.Count, sessions.Count, sessions.Count - validSessions.Count);

                var sessionsByEntityId = validSessions
                    .GroupBy(s => s.EntityId, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.LastActivityAt).First(), StringComparer.OrdinalIgnoreCase);

                if (logger.IsEnabled(LogLevel.Debug))
                {
                    logger.LogDebug(
                        "Sessions grouped by entity ID: {EntityIds}",
                        string.Join(", ", sessionsByEntityId.Keys.Select(k => $"'{k}'")));
                }

                foreach (var node in response.Nodes)
                {
                    if (string.IsNullOrWhiteSpace(node.Issue?.Id))
                    {
                        logger.LogWarning(
                            "Skipping node with null or empty issue ID. Title: {Title}",
                            node.Issue?.Title ?? "N/A");
                        continue;
                    }

                    logger.LogDebug(
                        "Checking session for issue '{IssueId}' (Title: {IssueTitle})",
                        node.Issue.Id, node.Issue.Title);

                    if (sessionsByEntityId.TryGetValue(node.Issue.Id, out var session))
                    {
                        var statusData = CreateAgentStatusData(session);
                        response.AgentStatuses[node.Issue.Id] = statusData;
                        logger.LogDebug(
                            "Matched session for issue '{IssueId}': SessionId={SessionId}, Status={Status}, IsActive={IsActive}",
                            node.Issue.Id, session.Id, session.Status, statusData.IsActive);
                    }
                    else if (logger.IsEnabled(LogLevel.Debug))
                    {
                        logger.LogDebug(
                            "No session found for issue '{IssueId}' (available keys: {AvailableKeys})",
                            node.Issue.Id, string.Join(", ", sessionsByEntityId.Keys.Take(5).Select(k => $"'{k}'")));
                    }
                }
            }

            // Get linked PRs
            var trackedPrs = dataStore.GetPullRequestsByProject(projectId)
                .Where(pr => !string.IsNullOrEmpty(pr.FleeceIssueId) && pr.GitHubPRNumber.HasValue)
                .ToList();

            foreach (var trackedPr in trackedPrs)
            {
                if (trackedPr.FleeceIssueId != null && trackedPr.GitHubPRNumber.HasValue)
                {
                    response.LinkedPrs[trackedPr.FleeceIssueId] = new TaskGraphLinkedPr
                    {
                        Number = trackedPr.GitHubPRNumber.Value,
                        Url = $"https://github.com/{project.GitHubOwner}/{project.GitHubRepo}/pull/{trackedPr.GitHubPRNumber.Value}",
                        Status = trackedPr.Status.ToString()
                    };
                }
            }

            // Enrich with OpenSpec per-issue state and main-branch orphans.
            // Build the branch-resolution context once so the enricher does not fan out
            // to `ListClonesAsync` / `GetPullRequestsByProject` per visible node.
            var clones = await cloneService.ListClonesAsync(project.LocalPath);
            var prBranches = dataStore.GetPullRequestsByProject(projectId)
                .Where(p => !string.IsNullOrEmpty(p.FleeceIssueId) && !string.IsNullOrEmpty(p.BranchName))
                .GroupBy(p => p.FleeceIssueId!, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First().BranchName!, StringComparer.Ordinal);
            var branchContext = new BranchResolutionContext(clones, prBranches);

            await openSpecEnricher.EnrichAsync(projectId, response, branchContext);

            logger.LogDebug(
                "Built enhanced task graph for project {ProjectId}: {NodeCount} nodes, {PrCount} merged PRs, {AgentCount} agent statuses, {LinkedPrCount} linked PRs, {OpenSpecStateCount} OpenSpec states",
                projectId, response.Nodes.Count, response.MergedPrs.Count, response.AgentStatuses.Count, response.LinkedPrs.Count, response.OpenSpecStates.Count);

            return response;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to build enhanced task graph for project {ProjectId}", projectId);
            return null;
        }
    }
}
