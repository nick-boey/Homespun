using Fleece.Core.Models;
using Homespun.Features.ClaudeCode.Data;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.Fleece.Services;
using Homespun.Features.Gitgraph.Services;
using Homespun.Features.GitHub;
using Homespun.Features.Projects;
using Homespun.Features.PullRequests;
using Homespun.Features.PullRequests.Data.Entities;
using Homespun.Features.Testing;
using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.Gitgraph;

/// <summary>
/// Tests for the GraphService's agent status enrichment functionality.
/// Verifies that agent sessions are correctly mapped to both issues and PRs in the graph.
/// </summary>
[TestFixture]
public class GraphServiceAgentStatusTests
{
    private MockDataStore _dataStore = null!;
    private Mock<IProjectService> _mockProjectService = null!;
    private Mock<IGitHubService> _mockGitHubService = null!;
    private Mock<IFleeceService> _mockFleeceService = null!;
    private Mock<IClaudeSessionStore> _mockSessionStore = null!;
    private Mock<PullRequestWorkflowService> _mockWorkflowService = null!;
    private Mock<IGraphCacheService> _mockCacheService = null!;
    private Mock<ILogger<GraphService>> _mockLogger = null!;
    private GraphService _service = null!;
    private Project _testProject = null!;

    [SetUp]
    public async Task SetUp()
    {
        _dataStore = new MockDataStore();

        // Create test project - use a temp path that exists with .fleece directory
        var testPath = Path.Combine(Path.GetTempPath(), $"graphservice-agent-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(testPath);
        Directory.CreateDirectory(Path.Combine(testPath, ".fleece"));

        _testProject = new Project
        {
            Name = "test-repo",
            LocalPath = testPath,
            GitHubOwner = "test-owner",
            GitHubRepo = "test-repo",
            DefaultBranch = "main"
        };
        await _dataStore.AddProjectAsync(_testProject);

        // Set up mocks
        _mockProjectService = new Mock<IProjectService>();
        _mockProjectService.Setup(s => s.GetByIdAsync(_testProject.Id))
            .ReturnsAsync(_testProject);

        _mockGitHubService = new Mock<IGitHubService>();
        _mockGitHubService.Setup(s => s.GetOpenPullRequestsAsync(_testProject.Id))
            .ReturnsAsync(new List<PullRequestInfo>());
        _mockGitHubService.Setup(s => s.GetClosedPullRequestsAsync(_testProject.Id))
            .ReturnsAsync(new List<PullRequestInfo>());

        _mockFleeceService = new Mock<IFleeceService>();
        _mockFleeceService.Setup(s => s.ListIssuesAsync(_testProject.LocalPath, null, null, null, default))
            .ReturnsAsync(new List<Issue>());

        _mockSessionStore = new Mock<IClaudeSessionStore>();
        _mockSessionStore.Setup(s => s.GetByProjectId(_testProject.Id))
            .Returns(new List<ClaudeSession>());

        _mockWorkflowService = new Mock<PullRequestWorkflowService>(
            MockBehavior.Loose,
            _dataStore,
            null!,
            null!,
            null!,
            null!);

        // Cache service returns null (no cache) so tests fetch fresh data
        _mockCacheService = new Mock<IGraphCacheService>();
        _mockCacheService.Setup(s => s.GetCachedPRData(It.IsAny<string>()))
            .Returns((CachedPRData?)null);
        _mockCacheService.Setup(s => s.CachePRDataAsync(It.IsAny<string>(), It.IsAny<List<PullRequestInfo>>(), It.IsAny<List<PullRequestInfo>>()))
            .Returns(Task.CompletedTask);

        _mockLogger = new Mock<ILogger<GraphService>>();

        _service = new GraphService(
            _mockProjectService.Object,
            _mockGitHubService.Object,
            _mockFleeceService.Object,
            _mockSessionStore.Object,
            _dataStore,
            _mockWorkflowService.Object,
            _mockCacheService.Object,
            _mockLogger.Object);
    }

    [TearDown]
    public void TearDown()
    {
        _dataStore.Clear();

        // Clean up the temp directory
        if (_testProject != null && Directory.Exists(_testProject.LocalPath))
        {
            try
            {
                Directory.Delete(_testProject.LocalPath, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    #region Issue Agent Status Tests

    [Test]
    public async Task BuildGraphJsonAsync_IssueWithActiveSession_HasAgentStatus()
    {
        // Arrange - Create an issue with an active session
        var issue = CreateIssue("hsp-123");
        var session = CreateSession("hsp-123", ClaudeSessionStatus.Running);

        _mockFleeceService.Setup(s => s.ListIssuesAsync(_testProject.LocalPath, null, null, null, default))
            .ReturnsAsync(new List<Issue> { issue });
        _mockSessionStore.Setup(s => s.GetByProjectId(_testProject.Id))
            .Returns(new List<ClaudeSession> { session });

        // Act
        var jsonData = await _service.BuildGraphJsonAsync(_testProject.Id);

        // Assert - Issue commit should have agent status
        var issueCommit = jsonData.Commits.FirstOrDefault(c => c.IssueId == "hsp-123");
        Assert.That(issueCommit, Is.Not.Null);
        Assert.That(issueCommit!.AgentStatus, Is.Not.Null);
        Assert.That(issueCommit.AgentStatus!.IsActive, Is.True);
        Assert.That(issueCommit.AgentStatus.Status, Is.EqualTo("Running"));
        Assert.That(issueCommit.AgentStatus.SessionId, Is.EqualTo(session.Id));
    }

    [Test]
    public async Task BuildGraphJsonAsync_IssueWithoutSession_HasNoAgentStatus()
    {
        // Arrange - Create an issue without any session
        var issue = CreateIssue("hsp-123");

        _mockFleeceService.Setup(s => s.ListIssuesAsync(_testProject.LocalPath, null, null, null, default))
            .ReturnsAsync(new List<Issue> { issue });
        _mockSessionStore.Setup(s => s.GetByProjectId(_testProject.Id))
            .Returns(new List<ClaudeSession>());

        // Act
        var jsonData = await _service.BuildGraphJsonAsync(_testProject.Id);

        // Assert - Issue commit should have no agent status
        var issueCommit = jsonData.Commits.FirstOrDefault(c => c.IssueId == "hsp-123");
        Assert.That(issueCommit, Is.Not.Null);
        Assert.That(issueCommit!.AgentStatus, Is.Null);
    }

    #endregion

    #region PR Agent Status Tests

    [Test]
    public async Task BuildGraphJsonAsync_PRWithActiveSession_HasAgentStatus()
    {
        // Arrange - Create a tracked PR with GitHub number and an active session
        var trackedPr = await CreateTrackedPullRequest(_testProject.Id, gitHubPrNumber: 42);
        var session = CreateSession(trackedPr.Id, ClaudeSessionStatus.Running);

        var githubPr = CreateGitHubPullRequest(42, "Test PR");
        _mockGitHubService.Setup(s => s.GetOpenPullRequestsAsync(_testProject.Id))
            .ReturnsAsync(new List<PullRequestInfo> { githubPr });
        _mockSessionStore.Setup(s => s.GetByProjectId(_testProject.Id))
            .Returns(new List<ClaudeSession> { session });

        // Act
        var jsonData = await _service.BuildGraphJsonAsync(_testProject.Id);

        // Assert - PR commit should have agent status
        var prCommit = jsonData.Commits.FirstOrDefault(c => c.PullRequestNumber == 42);
        Assert.That(prCommit, Is.Not.Null, "PR commit should exist in graph");
        Assert.That(prCommit!.AgentStatus, Is.Not.Null, "PR commit should have agent status");
        Assert.That(prCommit.AgentStatus!.IsActive, Is.True);
        Assert.That(prCommit.AgentStatus.Status, Is.EqualTo("Running"));
        Assert.That(prCommit.AgentStatus.SessionId, Is.EqualTo(session.Id));
    }

    [Test]
    public async Task BuildGraphJsonAsync_PRWithoutSession_HasNoAgentStatus()
    {
        // Arrange - Create a tracked PR without any session
        await CreateTrackedPullRequest(_testProject.Id, gitHubPrNumber: 42);

        var githubPr = CreateGitHubPullRequest(42, "Test PR");
        _mockGitHubService.Setup(s => s.GetOpenPullRequestsAsync(_testProject.Id))
            .ReturnsAsync(new List<PullRequestInfo> { githubPr });
        _mockSessionStore.Setup(s => s.GetByProjectId(_testProject.Id))
            .Returns(new List<ClaudeSession>());

        // Act
        var jsonData = await _service.BuildGraphJsonAsync(_testProject.Id);

        // Assert - PR commit should have no agent status
        var prCommit = jsonData.Commits.FirstOrDefault(c => c.PullRequestNumber == 42);
        Assert.That(prCommit, Is.Not.Null);
        Assert.That(prCommit!.AgentStatus, Is.Null);
    }

    [Test]
    public async Task BuildGraphJsonAsync_PRNotTrackedInDataStore_HasNoAgentStatus()
    {
        // Arrange - GitHub PR exists but not tracked in our data store
        var githubPr = CreateGitHubPullRequest(42, "Test PR");
        _mockGitHubService.Setup(s => s.GetOpenPullRequestsAsync(_testProject.Id))
            .ReturnsAsync(new List<PullRequestInfo> { githubPr });

        // Session exists for some other entity
        var session = CreateSession("some-other-entity", ClaudeSessionStatus.Running);
        _mockSessionStore.Setup(s => s.GetByProjectId(_testProject.Id))
            .Returns(new List<ClaudeSession> { session });

        // Act
        var jsonData = await _service.BuildGraphJsonAsync(_testProject.Id);

        // Assert - PR commit should have no agent status (not tracked)
        var prCommit = jsonData.Commits.FirstOrDefault(c => c.PullRequestNumber == 42);
        Assert.That(prCommit, Is.Not.Null);
        Assert.That(prCommit!.AgentStatus, Is.Null);
    }

    [Test]
    public async Task BuildGraphJsonAsync_TrackedPRWithoutGitHubNumber_HasNoAgentStatus()
    {
        // Arrange - Tracked PR exists but hasn't been pushed to GitHub yet
        var trackedPr = await CreateTrackedPullRequest(_testProject.Id, gitHubPrNumber: null);
        var session = CreateSession(trackedPr.Id, ClaudeSessionStatus.Running);

        _mockSessionStore.Setup(s => s.GetByProjectId(_testProject.Id))
            .Returns(new List<ClaudeSession> { session });

        // Act
        var jsonData = await _service.BuildGraphJsonAsync(_testProject.Id);

        // Assert - No PR in graph (no GitHub PR to show)
        var prCommits = jsonData.Commits.Where(c => c.PullRequestNumber.HasValue).ToList();
        Assert.That(prCommits, Is.Empty);
    }

    #endregion

    #region Combined Tests

    [Test]
    public async Task BuildGraphJsonAsync_BothIssueAndPRWithSessions_BothHaveAgentStatus()
    {
        // Arrange - Create both an issue and a PR with active sessions
        var issue = CreateIssue("hsp-123");
        var issueSession = CreateSession("hsp-123", ClaudeSessionStatus.Running);

        var trackedPr = await CreateTrackedPullRequest(_testProject.Id, gitHubPrNumber: 42);
        var prSession = CreateSession(trackedPr.Id, ClaudeSessionStatus.WaitingForInput);

        var githubPr = CreateGitHubPullRequest(42, "Test PR");

        _mockFleeceService.Setup(s => s.ListIssuesAsync(_testProject.LocalPath, null, null, null, default))
            .ReturnsAsync(new List<Issue> { issue });
        _mockGitHubService.Setup(s => s.GetOpenPullRequestsAsync(_testProject.Id))
            .ReturnsAsync(new List<PullRequestInfo> { githubPr });
        _mockSessionStore.Setup(s => s.GetByProjectId(_testProject.Id))
            .Returns(new List<ClaudeSession> { issueSession, prSession });

        // Act
        var jsonData = await _service.BuildGraphJsonAsync(_testProject.Id);

        // Assert - Both should have agent status
        var issueCommit = jsonData.Commits.FirstOrDefault(c => c.IssueId == "hsp-123");
        Assert.That(issueCommit, Is.Not.Null);
        Assert.That(issueCommit!.AgentStatus, Is.Not.Null);
        Assert.That(issueCommit.AgentStatus!.Status, Is.EqualTo("Running"));

        var prCommit = jsonData.Commits.FirstOrDefault(c => c.PullRequestNumber == 42);
        Assert.That(prCommit, Is.Not.Null);
        Assert.That(prCommit!.AgentStatus, Is.Not.Null);
        Assert.That(prCommit.AgentStatus!.Status, Is.EqualTo("WaitingForInput"));
    }

    [Test]
    public async Task BuildGraphJsonAsync_MultiplePRsOnlyOneWithSession_OnlyOneHasAgentStatus()
    {
        // Arrange - Two PRs, only one has a session
        var trackedPr1 = await CreateTrackedPullRequest(_testProject.Id, gitHubPrNumber: 42);
        var trackedPr2 = await CreateTrackedPullRequest(_testProject.Id, gitHubPrNumber: 43);
        var session = CreateSession(trackedPr1.Id, ClaudeSessionStatus.Running);

        var githubPr1 = CreateGitHubPullRequest(42, "PR 1");
        var githubPr2 = CreateGitHubPullRequest(43, "PR 2");

        _mockGitHubService.Setup(s => s.GetOpenPullRequestsAsync(_testProject.Id))
            .ReturnsAsync(new List<PullRequestInfo> { githubPr1, githubPr2 });
        _mockSessionStore.Setup(s => s.GetByProjectId(_testProject.Id))
            .Returns(new List<ClaudeSession> { session });

        // Act
        var jsonData = await _service.BuildGraphJsonAsync(_testProject.Id);

        // Assert - Only PR 42 should have agent status
        var prCommit1 = jsonData.Commits.FirstOrDefault(c => c.PullRequestNumber == 42);
        Assert.That(prCommit1, Is.Not.Null);
        Assert.That(prCommit1!.AgentStatus, Is.Not.Null);

        var prCommit2 = jsonData.Commits.FirstOrDefault(c => c.PullRequestNumber == 43);
        Assert.That(prCommit2, Is.Not.Null);
        Assert.That(prCommit2!.AgentStatus, Is.Null);
    }

    #endregion

    #region Session Status Tests

    [Test]
    public async Task BuildGraphJsonAsync_PRWithStoppedSession_HasInactiveAgentStatus()
    {
        // Arrange - Create a tracked PR with a stopped session
        var trackedPr = await CreateTrackedPullRequest(_testProject.Id, gitHubPrNumber: 42);
        var session = CreateSession(trackedPr.Id, ClaudeSessionStatus.Stopped);

        var githubPr = CreateGitHubPullRequest(42, "Test PR");
        _mockGitHubService.Setup(s => s.GetOpenPullRequestsAsync(_testProject.Id))
            .ReturnsAsync(new List<PullRequestInfo> { githubPr });
        _mockSessionStore.Setup(s => s.GetByProjectId(_testProject.Id))
            .Returns(new List<ClaudeSession> { session });

        // Act
        var jsonData = await _service.BuildGraphJsonAsync(_testProject.Id);

        // Assert - PR commit should have inactive agent status
        var prCommit = jsonData.Commits.FirstOrDefault(c => c.PullRequestNumber == 42);
        Assert.That(prCommit, Is.Not.Null);
        Assert.That(prCommit!.AgentStatus, Is.Not.Null);
        Assert.That(prCommit.AgentStatus!.IsActive, Is.False);
        Assert.That(prCommit.AgentStatus.Status, Is.EqualTo("Stopped"));
    }

    #endregion

    #region Helper Methods

    private static Issue CreateIssue(string id, IssueStatus status = IssueStatus.Next)
    {
        return new Issue
        {
            Id = id,
            Title = $"Issue {id}",
            Status = status,
            Type = IssueType.Task,
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdate = DateTimeOffset.UtcNow
        };
    }

    private async Task<PullRequest> CreateTrackedPullRequest(string projectId, int? gitHubPrNumber)
    {
        var pr = new PullRequest
        {
            ProjectId = projectId,
            Title = gitHubPrNumber.HasValue ? $"PR #{gitHubPrNumber}" : "Local PR",
            BranchName = $"feature/test-{Guid.NewGuid():N}",
            GitHubPRNumber = gitHubPrNumber,
            Status = OpenPullRequestStatus.InDevelopment
        };
        await _dataStore.AddPullRequestAsync(pr);
        return pr;
    }

    private static PullRequestInfo CreateGitHubPullRequest(int number, string title)
    {
        return new PullRequestInfo
        {
            Number = number,
            Title = title,
            Status = PullRequestStatus.InProgress,
            BranchName = $"feature/pr-{number}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private ClaudeSession CreateSession(string entityId, ClaudeSessionStatus status)
    {
        return new ClaudeSession
        {
            Id = Guid.NewGuid().ToString(),
            EntityId = entityId,
            ProjectId = _testProject.Id,
            WorkingDirectory = _testProject.LocalPath,
            Model = "claude-sonnet-4-20250514",
            Mode = SessionMode.Build,
            Status = status
        };
    }

    #endregion
}
