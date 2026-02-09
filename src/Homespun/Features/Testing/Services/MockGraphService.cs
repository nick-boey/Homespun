using Fleece.Core.Models;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.Gitgraph.Services;
using Homespun.Features.PullRequests.Data;
using Microsoft.Extensions.Logging;

namespace Homespun.Features.Testing.Services;

/// <summary>
/// Mock implementation of IGraphService that builds graph data from MockDataStore.
/// </summary>
public class MockGraphService : IGraphService
{
    private readonly IDataStore _dataStore;
    private readonly IClaudeSessionStore _sessionStore;
    private readonly ILogger<MockGraphService> _logger;
    private readonly GraphBuilder _graphBuilder = new();
    private readonly GitgraphApiMapper _mapper = new();

    public MockGraphService(
        IDataStore dataStore,
        IClaudeSessionStore sessionStore,
        ILogger<MockGraphService> logger)
    {
        _dataStore = dataStore;
        _sessionStore = sessionStore;
        _logger = logger;
    }

    public Task<Graph> BuildGraphAsync(string projectId, int? maxPastPRs = 5)
    {
        _logger.LogDebug("[Mock] BuildGraph for project {ProjectId}", projectId);

        var project = _dataStore.GetProject(projectId);
        if (project == null)
        {
            _logger.LogWarning("[Mock] Project not found: {ProjectId}", projectId);
            return Task.FromResult(new Graph([], new Dictionary<string, GraphBranch>()));
        }

        // Convert stored PullRequests to PullRequestInfo (these are open PRs)
        var pullRequests = _dataStore.GetPullRequestsByProject(projectId);
        var openPrInfos = pullRequests.Select(ConvertToPullRequestInfo).ToList();

        // Add fake merged PR history to form the main trunk
        var mergedPrHistory = GetMergedPrHistory();
        var allPrInfos = mergedPrHistory.Concat(openPrInfos).ToList();

        // Add fake issues to test full timeline scope
        var fakeIssues = GetFakeIssues();

        _logger.LogDebug("[Mock] Building graph with {PrCount} PRs ({MergedCount} merged, {OpenCount} open) and {IssueCount} issues",
            allPrInfos.Count, mergedPrHistory.Count, openPrInfos.Count, fakeIssues.Count);

        // Use the existing GraphBuilder to construct the graph
        var graph = _graphBuilder.Build(allPrInfos, fakeIssues, maxPastPRs);
        return Task.FromResult(graph);
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
            }
        ];
    }

    /// <summary>
    /// Returns a list of fake issues to populate the timeline.
    /// Includes orphan issues (grouped and ungrouped) and issues with dependencies.
    /// </summary>
    private static List<Issue> GetFakeIssues()
    {
        var now = DateTimeOffset.UtcNow;
        return
        [
            // Orphan issues
            new Issue
            {
                Id = "ISSUE-001",
                Title = "Add dark mode support",
                Description = "Implement a dark mode theme option for better accessibility and user preference",
                Type = IssueType.Feature,
                Status = IssueStatus.Open,
                Priority = 2,
                CreatedAt = now.AddDays(-14),
                LastUpdate = now.AddDays(-2)
            },
            new Issue
            {
                Id = "ISSUE-002",
                Title = "Improve mobile responsiveness",
                Description = "Ensure all pages display correctly on mobile devices and tablets",
                Type = IssueType.Task,
                Status = IssueStatus.Open,
                Priority = 3,
                CreatedAt = now.AddDays(-12),
                LastUpdate = now.AddDays(-1)
            },

            // Orphan issue
            new Issue
            {
                Id = "ISSUE-003",
                Title = "Fix login timeout bug",
                Description = "Users are being logged out unexpectedly after 5 minutes of inactivity",
                Type = IssueType.Bug,
                Status = IssueStatus.Progress,
                Priority = 1,
                CreatedAt = now.AddDays(-7),
                LastUpdate = now.AddHours(-6)
            },

            // Issues with dependencies - forms a chain: ISSUE-004 -> ISSUE-005 -> ISSUE-006
            new Issue
            {
                Id = "ISSUE-004",
                Title = "Design API schema",
                Description = "Define the REST API schema for the new feature endpoints",
                Type = IssueType.Task,
                Status = IssueStatus.Open,
                Priority = 2,
                ParentIssues = [], // Root of the dependency chain
                CreatedAt = now.AddDays(-10),
                LastUpdate = now.AddDays(-3)
            },
            new Issue
            {
                Id = "ISSUE-005",
                Title = "Implement API endpoints",
                Description = "Build the REST API endpoints based on the approved schema",
                Type = IssueType.Task,
                Status = IssueStatus.Open,
                Priority = 2,
                ParentIssues = [new ParentIssueRef { ParentIssue = "ISSUE-004", SortOrder = "0" }], // Depends on ISSUE-004
                CreatedAt = now.AddDays(-9),
                LastUpdate = now.AddDays(-2)
            },
            new Issue
            {
                Id = "ISSUE-006",
                Title = "Write API documentation",
                Description = "Document all new API endpoints with examples and usage guidelines",
                Type = IssueType.Chore,
                Status = IssueStatus.Open,
                Priority = 3,
                ParentIssues = [new ParentIssueRef { ParentIssue = "ISSUE-005", SortOrder = "0" }], // Depends on ISSUE-005
                CreatedAt = now.AddDays(-8),
                LastUpdate = now.AddDays(-1)
            },

            // Extended dependency tree branching from ISSUE-005
            // ISSUE-005 -> ISSUE-007 -> ISSUE-008 -> ISSUE-009 -> ISSUE-010
            //                                     -> ISSUE-011
            //                        -> ISSUE-012
            // ISSUE-005 -> ISSUE-013

            new Issue
            {
                Id = "ISSUE-007",
                Title = "Implement GET endpoints",
                Description = "Build GET endpoints for retrieving resources from the API",
                Type = IssueType.Task,
                Status = IssueStatus.Open,
                Priority = 2,
                ParentIssues = [new ParentIssueRef { ParentIssue = "ISSUE-005", SortOrder = "0" }],
                CreatedAt = now.AddDays(-7),
                LastUpdate = now.AddDays(-1)
            },
            new Issue
            {
                Id = "ISSUE-008",
                Title = "Implement POST endpoints",
                Description = "Build POST endpoints for creating new resources",
                Type = IssueType.Task,
                Status = IssueStatus.Open,
                Priority = 2,
                ParentIssues = [new ParentIssueRef { ParentIssue = "ISSUE-007", SortOrder = "0" }],
                CreatedAt = now.AddDays(-6),
                LastUpdate = now.AddDays(-1)
            },
            new Issue
            {
                Id = "ISSUE-009",
                Title = "Implement PUT/PATCH endpoints",
                Description = "Build PUT/PATCH endpoints for updating existing resources",
                Type = IssueType.Task,
                Status = IssueStatus.Open,
                Priority = 2,
                ParentIssues = [new ParentIssueRef { ParentIssue = "ISSUE-008", SortOrder = "0" }],
                CreatedAt = now.AddDays(-5),
                LastUpdate = now.AddDays(-1)
            },
            new Issue
            {
                Id = "ISSUE-010",
                Title = "Implement DELETE endpoints",
                Description = "Build DELETE endpoints for removing resources",
                Type = IssueType.Task,
                Status = IssueStatus.Open,
                Priority = 2,
                ParentIssues = [new ParentIssueRef { ParentIssue = "ISSUE-009", SortOrder = "0" }],
                CreatedAt = now.AddDays(-4),
                LastUpdate = now.AddDays(-1)
            },
            new Issue
            {
                Id = "ISSUE-011",
                Title = "Add request validation",
                Description = "Implement request validation middleware for all API endpoints",
                Type = IssueType.Task,
                Status = IssueStatus.Open,
                Priority = 3,
                ParentIssues = [new ParentIssueRef { ParentIssue = "ISSUE-008", SortOrder = "0" }],
                CreatedAt = now.AddDays(-5),
                LastUpdate = now.AddDays(-2)
            },
            new Issue
            {
                Id = "ISSUE-012",
                Title = "Add rate limiting",
                Description = "Implement rate limiting to prevent API abuse",
                Type = IssueType.Task,
                Status = IssueStatus.Open,
                Priority = 3,
                ParentIssues = [new ParentIssueRef { ParentIssue = "ISSUE-007", SortOrder = "0" }],
                CreatedAt = now.AddDays(-6),
                LastUpdate = now.AddDays(-3)
            },
            new Issue
            {
                Id = "ISSUE-013",
                Title = "Set up API monitoring",
                Description = "Configure monitoring and alerting for API health and performance",
                Type = IssueType.Chore,
                Status = IssueStatus.Open,
                Priority = 4,
                ParentIssues = [new ParentIssueRef { ParentIssue = "ISSUE-005", SortOrder = "0" }],
                CreatedAt = now.AddDays(-7),
                LastUpdate = now.AddDays(-2)
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

        // Build lookup by entity ID (works for issues directly, and for PRs via their internal ID)
        var sessionsByEntityId = sessions.ToDictionary(s => s.EntityId, StringComparer.OrdinalIgnoreCase);

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
