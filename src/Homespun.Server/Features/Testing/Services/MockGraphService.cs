using Fleece.Core.Models;
using Fleece.Core.Services;
using Fleece.Core.Services.Interfaces;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.Fleece;
using Homespun.Features.Fleece.Services;
using Homespun.Features.Gitgraph.Services;
using Homespun.Features.PullRequests.Data;
using Homespun.Shared.Models.Fleece;
using Microsoft.Extensions.Logging;

namespace Homespun.Features.Testing.Services;

/// <summary>
/// Mock implementation of IGraphService that builds graph data from MockDataStore.
/// </summary>
public class MockGraphService : IGraphService
{
    private readonly IDataStore _dataStore;
    private readonly IFleeceService _fleeceService;
    private readonly IClaudeSessionStore _sessionStore;
    private readonly ILogger<MockGraphService> _logger;
    private readonly GraphBuilder _graphBuilder = new();
    private readonly GitgraphApiMapper _mapper = new();

    public MockGraphService(
        IDataStore dataStore,
        IFleeceService fleeceService,
        IClaudeSessionStore sessionStore,
        ILogger<MockGraphService> logger)
    {
        _dataStore = dataStore;
        _fleeceService = fleeceService;
        _sessionStore = sessionStore;
        _logger = logger;
    }

    public async Task<Graph> BuildGraphAsync(string projectId, int? maxPastPRs = 5)
    {
        _logger.LogDebug("[Mock] BuildGraph for project {ProjectId}", projectId);

        var project = _dataStore.GetProject(projectId);
        if (project == null)
        {
            _logger.LogWarning("[Mock] Project not found: {ProjectId}", projectId);
            return new Graph([], new Dictionary<string, GraphBranch>());
        }

        // Convert stored PullRequests to PullRequestInfo (these are open PRs)
        var pullRequests = _dataStore.GetPullRequestsByProject(projectId);
        var openPrInfos = pullRequests.Select(ConvertToPullRequestInfo).ToList();

        // Add fake merged PR history to form the main trunk
        var mergedPrHistory = GetMergedPrHistory();
        var allPrInfos = mergedPrHistory.Concat(openPrInfos).ToList();

        // Get issues from the mock fleece service (includes seeded + created issues)
        var issues = await GetIssuesForProjectAsync(project.LocalPath);

        _logger.LogDebug("[Mock] Building graph with {PrCount} PRs ({MergedCount} merged, {OpenCount} open) and {IssueCount} issues",
            allPrInfos.Count, mergedPrHistory.Count, openPrInfos.Count, issues.Count);

        // Use the existing GraphBuilder to construct the graph
        var graph = _graphBuilder.Build(allPrInfos, issues, maxPastPRs);
        return graph;
    }

    public async Task<GitgraphJsonData> BuildGraphJsonAsync(string projectId, int? maxPastPRs = 5, bool useCache = true)
    {
        _logger.LogDebug("[Mock] BuildGraphJson for project {ProjectId} (useCache: {UseCache})", projectId, useCache);

        var graph = await BuildGraphAsync(projectId, maxPastPRs);
        var jsonData = _mapper.ToJson(graph);

        // Enrich nodes with agent status data
        EnrichWithAgentStatuses(jsonData, projectId);

        return jsonData;
    }

    public async Task<GitgraphJsonData> BuildGraphJsonWithFreshDataAsync(string projectId, int? maxPastPRs = 5)
    {
        _logger.LogDebug("[Mock] BuildGraphJsonWithFreshData for project {ProjectId}", projectId);

        // In mock mode, just build the graph normally
        var graph = await BuildGraphAsync(projectId, maxPastPRs);
        var jsonData = _mapper.ToJson(graph);

        // Enrich nodes with agent status data
        EnrichWithAgentStatuses(jsonData, projectId);

        return jsonData;
    }

    public async Task<Graph?> BuildGraphFromCacheOnlyAsync(string projectId, int? maxPastPRs = 5)
    {
        _logger.LogDebug("[Mock] BuildGraphFromCacheOnly for project {ProjectId}", projectId);

        // In mock mode, return the same data (simulates cache hit)
        return await BuildGraphAsync(projectId, maxPastPRs);
    }

    public async Task<GitgraphJsonData?> BuildGraphJsonFromCacheOnlyAsync(string projectId, int? maxPastPRs = 5)
    {
        _logger.LogDebug("[Mock] BuildGraphJsonFromCacheOnly for project {ProjectId}", projectId);

        // In mock mode, return the same data (simulates cache hit)
        var graph = await BuildGraphAsync(projectId, maxPastPRs);
        var jsonData = _mapper.ToJson(graph);

        // Enrich nodes with agent status data
        EnrichWithAgentStatuses(jsonData, projectId);

        return jsonData;
    }

    public async Task<GitgraphJsonData> IncrementalRefreshAsync(string projectId, int? maxPastPRs = 5)
    {
        _logger.LogDebug("[Mock] IncrementalRefresh for project {ProjectId}", projectId);

        // In mock mode, just build the graph normally (no real GitHub API to refresh from)
        var graph = await BuildGraphAsync(projectId, maxPastPRs);
        var jsonData = _mapper.ToJson(graph);
        EnrichWithAgentStatuses(jsonData, projectId);
        return jsonData;
    }

    public DateTime? GetCacheTimestamp(string projectId)
    {
        // Mock mode doesn't have real caching, return current time
        return DateTime.UtcNow;
    }

    public bool HasCachedData(string projectId)
    {
        // Mock mode always has "cached" data available
        return true;
    }

    public async Task<TaskGraph?> BuildTaskGraphAsync(string projectId)
    {
        _logger.LogDebug("[Mock] BuildTaskGraph for project {ProjectId}", projectId);

        var project = _dataStore.GetProject(projectId);
        if (project == null) return null;

        var allIssues = await GetIssuesForProjectAsync(project.LocalPath);

        // Filter to open issues (matching real FleeceService.GetTaskGraphAsync behavior)
        var openIssues = allIssues
            .Where(i => i.Status is IssueStatus.Open or IssueStatus.Progress or IssueStatus.Review)
            .ToList();

        if (openIssues.Count == 0) return null;

        // Use Fleece.Core's IssueGraphService for correct ordering (v1.4.0 consolidation)
        var mockIssueService = new MockIssueServiceAdapter(openIssues);
        return await mockIssueService.BuildTaskGraphLayoutAsync();
    }

    public async Task<string?> BuildTaskGraphTextAsync(string projectId)
    {
        _logger.LogDebug("[Mock] BuildTaskGraphText for project {ProjectId}", projectId);

        var taskGraph = await BuildTaskGraphAsync(projectId);
        if (taskGraph == null)
            return null;

        return TaskGraphTextRenderer.Render(taskGraph);
    }

    public async Task<TaskGraphResponse?> BuildEnhancedTaskGraphAsync(string projectId, int maxPastPRs = 5)
    {
        _logger.LogDebug("[Mock] BuildEnhancedTaskGraph for project {ProjectId}", projectId);

        var taskGraph = await BuildTaskGraphAsync(projectId);
        if (taskGraph == null)
            return null;

        // Build task graph response
        var response = new TaskGraphResponse
        {
            TotalLanes = taskGraph.TotalLanes,
            Nodes = taskGraph.Nodes.Select(n => new TaskGraphNodeResponse
            {
                Issue = IssueDtoMapper.ToResponse(n.Issue),
                Lane = n.Lane,
                Row = n.Row,
                IsActionable = n.IsActionable
            }).ToList()
        };

        // Add merged PRs
        var mergedPrs = GetMergedPrHistory();
        var totalMergedPrs = mergedPrs.Count;
        var shownPrs = mergedPrs.Take(maxPastPRs).ToList();

        response.MergedPrs = shownPrs.Select(pr => new TaskGraphPrResponse
        {
            Number = pr.Number,
            Title = pr.Title,
            Url = pr.HtmlUrl,
            IsMerged = pr.Status == PullRequestStatus.Merged,
            HasDescription = !string.IsNullOrWhiteSpace(pr.Body)
        }).ToList();

        response.HasMorePastPrs = totalMergedPrs > maxPastPRs;
        response.TotalPastPrsShown = shownPrs.Count;

        // Get agent statuses
        var sessions = _sessionStore.GetByProjectId(projectId);
        var sessionsByEntityId = sessions
            .GroupBy(s => s.EntityId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.LastActivityAt).First(), StringComparer.OrdinalIgnoreCase);

        foreach (var node in response.Nodes)
        {
            if (sessionsByEntityId.TryGetValue(node.Issue.Id, out var session))
            {
                response.AgentStatuses[node.Issue.Id] = CreateAgentStatusData(session);
            }
        }

        _logger.LogDebug(
            "[Mock] Built enhanced task graph: {NodeCount} nodes, {PrCount} merged PRs, {AgentCount} agent statuses",
            response.Nodes.Count, response.MergedPrs.Count, response.AgentStatuses.Count);

        return response;
    }

    /// <summary>
    /// Gets all non-terminal issues for a project from the mock fleece service.
    /// Includes all statuses needed for graph rendering (Open, Progress, Review).
    /// </summary>
    private async Task<List<Issue>> GetIssuesForProjectAsync(string projectPath)
    {
        var issues = await _fleeceService.ListIssuesAsync(projectPath);
        return issues.ToList();
    }

    private static PullRequestInfo ConvertToPullRequestInfo(PullRequest pr)
    {
        return new PullRequestInfo
        {
            Number = pr.GitHubPRNumber ?? 0,
            Title = pr.Title ?? "Untitled",
            Body = pr.Description,
            Status = ConvertStatus(pr.Status),
            BranchName = pr.BranchName,
            HtmlUrl = pr.GitHubPRNumber != null
                ? $"https://github.com/mock-org/mock-repo/pull/{pr.GitHubPRNumber}"
                : null,
            CreatedAt = pr.CreatedAt,
            UpdatedAt = pr.UpdatedAt,
            MergedAt = null, // Open PRs are not merged
            ChecksPassing = true,
            IsApproved = pr.Status == OpenPullRequestStatus.Approved
        };
    }

    private static PullRequestStatus ConvertStatus(OpenPullRequestStatus status)
    {
        return status switch
        {
            OpenPullRequestStatus.InDevelopment => PullRequestStatus.InProgress,
            OpenPullRequestStatus.ReadyForReview => PullRequestStatus.ReadyForReview,
            OpenPullRequestStatus.HasReviewComments => PullRequestStatus.InProgress,
            OpenPullRequestStatus.Approved => PullRequestStatus.ReadyForMerging,
            _ => PullRequestStatus.InProgress
        };
    }

    /// <summary>
    /// Returns a list of fake merged PRs to form the main trunk of the timeline.
    /// Based on recent commits in the repository history.
    /// </summary>
    private static List<PullRequestInfo> GetMergedPrHistory()
    {
        var now = DateTime.UtcNow;
        return
        [
            new PullRequestInfo
            {
                Number = 97,
                Title = "Remove models page navigation entry",
                Body = "Cleanup: remove unused models page link from navigation",
                Status = PullRequestStatus.Merged,
                BranchName = "chore/remove-models-nav",
                HtmlUrl = "https://github.com/mock-org/mock-repo/pull/97",
                CreatedAt = now.AddDays(-32),
                UpdatedAt = now.AddDays(-30),
                MergedAt = now.AddDays(-30),
                ChecksPassing = true,
                IsApproved = true
            },
            new PullRequestInfo
            {
                Number = 98,
                Title = "Fix Test Agent button and SignalR debug panel",
                Body = "Bug fix: Test Agent button now works correctly, SignalR debug panel styling improved",
                Status = PullRequestStatus.Merged,
                BranchName = "fix/test-agent-button",
                HtmlUrl = "https://github.com/mock-org/mock-repo/pull/98",
                CreatedAt = now.AddDays(-27),
                UpdatedAt = now.AddDays(-25),
                MergedAt = now.AddDays(-25),
                ChecksPassing = true,
                IsApproved = true
            },
            new PullRequestInfo
            {
                Number = 90,
                Title = "Allow agent sessions to be resumed",
                Body = "Feature: Agent sessions can now be resumed after being stopped",
                Status = PullRequestStatus.Merged,
                BranchName = "feature/session-resume",
                HtmlUrl = "https://github.com/mock-org/mock-repo/pull/90",
                CreatedAt = now.AddDays(-22),
                UpdatedAt = now.AddDays(-20),
                MergedAt = now.AddDays(-20),
                ChecksPassing = true,
                IsApproved = true
            },
            new PullRequestInfo
            {
                Number = 100,
                Title = "Add mock services for testing without production data",
                Body = "Feature: Mock services allow testing the UI without connecting to production APIs",
                Status = PullRequestStatus.Merged,
                BranchName = "feature/mock-services",
                HtmlUrl = "https://github.com/mock-org/mock-repo/pull/100",
                CreatedAt = now.AddDays(-17),
                UpdatedAt = now.AddDays(-15),
                MergedAt = now.AddDays(-15),
                ChecksPassing = true,
                IsApproved = true
            },
            new PullRequestInfo
            {
                Number = 101,
                Title = "Fix Node.js Docker build stage for Tailwind CSS compilation",
                Body = "Bug fix: Docker build now correctly includes Node.js for Tailwind CSS processing",
                Status = PullRequestStatus.Merged,
                BranchName = "fix/docker-nodejs",
                HtmlUrl = "https://github.com/mock-org/mock-repo/pull/101",
                CreatedAt = now.AddDays(-12),
                UpdatedAt = now.AddDays(-10),
                MergedAt = now.AddDays(-10),
                ChecksPassing = true,
                IsApproved = true
            },
            // Closed (not merged) PR - demonstrates branching off main line
            new PullRequestInfo
            {
                Number = 99,
                Title = "Experimental graph animation (abandoned)",
                Body = "Attempted graph animation feature but decided against it",
                Status = PullRequestStatus.Closed,
                BranchName = "feature/graph-animation",
                HtmlUrl = "https://github.com/mock-org/mock-repo/pull/99",
                CreatedAt = now.AddDays(-11),
                UpdatedAt = now.AddDays(-9),
                ClosedAt = now.AddDays(-9),
                ChecksPassing = false,
                IsApproved = false
            },
            // Another merged PR after the closed one
            new PullRequestInfo
            {
                Number = 102,
                Title = "Add theme persistence to localStorage",
                Body = "Feature: Theme selection now persists across browser sessions",
                Status = PullRequestStatus.Merged,
                BranchName = "feature/theme-persistence",
                HtmlUrl = "https://github.com/mock-org/mock-repo/pull/102",
                CreatedAt = now.AddDays(-8),
                UpdatedAt = now.AddDays(-6),
                MergedAt = now.AddDays(-6),
                ChecksPassing = true,
                IsApproved = true
            }
        ];
    }

    /// <summary>
    /// Enriches graph commit data with agent session statuses.
    /// </summary>
    private void EnrichWithAgentStatuses(GitgraphJsonData jsonData, string projectId)
    {
        // Get all sessions for this project
        var sessions = _sessionStore.GetByProjectId(projectId);
        if (sessions.Count == 0) return;

        // Build lookup by entity ID, taking the most recently active session per entity
        // (multiple sessions can exist for the same entity when agents are stopped and restarted)
        var sessionsByEntityId = sessions
            .GroupBy(s => s.EntityId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.LastActivityAt).First(), StringComparer.OrdinalIgnoreCase);

        // Build lookup from GitHub PR number to internal PR ID for matching PR sessions
        var trackedPrs = _dataStore.GetPullRequestsByProject(projectId);
        var prIdByGitHubNumber = trackedPrs
            .Where(pr => pr.GitHubPRNumber.HasValue)
            .ToDictionary(pr => pr.GitHubPRNumber!.Value, pr => pr.Id);

        _logger.LogDebug("[Mock] EnrichWithAgentStatuses: {SessionCount} sessions, {PrCount} tracked PRs for project {ProjectId}",
            sessions.Count, trackedPrs.Count(), projectId);

        // Enrich commits with agent status
        foreach (var commit in jsonData.Commits)
        {
            // Check if there's an active session for this issue
            if (commit.IssueId != null && sessionsByEntityId.TryGetValue(commit.IssueId, out var session))
            {
                commit.AgentStatus = CreateAgentStatusData(session);
                _logger.LogDebug("[Mock] Added agent status to issue {IssueId}: {Status}", commit.IssueId, session.Status);
            }
            // Check if there's an active session for this PR
            else if (commit.PullRequestNumber.HasValue &&
                     prIdByGitHubNumber.TryGetValue(commit.PullRequestNumber.Value, out var prEntityId) &&
                     sessionsByEntityId.TryGetValue(prEntityId, out var prSession))
            {
                commit.AgentStatus = CreateAgentStatusData(prSession);
                _logger.LogDebug("[Mock] Added agent status to PR #{PrNumber} (entity {EntityId}): {Status}",
                    commit.PullRequestNumber, prEntityId, prSession.Status);
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
}
