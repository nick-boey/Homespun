using Homespun.Features.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Octokit;

namespace Homespun.Tests.Features.GitHub;

[TestFixture]
public class IssuePrStatusServiceTests
{
    private MockDataStore _dataStore = null!;
    private Mock<IConfiguration> _mockConfig = null!;
    private Mock<IGitHubClientWrapper> _mockGitHubClient = null!;
    private Mock<ICommandRunner> _mockCommandRunner = null!;
    private PullRequestWorkflowService _workflowService = null!;
    private IssuePrStatusService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _dataStore = new MockDataStore();
        _mockConfig = new Mock<IConfiguration>();
        _mockGitHubClient = new Mock<IGitHubClientWrapper>();
        _mockCommandRunner = new Mock<ICommandRunner>();

        _mockConfig.Setup(c => c["GITHUB_TOKEN"]).Returns("test-token");

        _workflowService = new PullRequestWorkflowService(
            _dataStore,
            _mockCommandRunner.Object,
            _mockConfig.Object,
            _mockGitHubClient.Object,
            new NullLogger<PullRequestWorkflowService>());

        _service = new IssuePrStatusService(
            _dataStore,
            _workflowService,
            new NullLogger<IssuePrStatusService>());
    }

    [TearDown]
    public void TearDown()
    {
        _dataStore.Clear();
    }

    private async Task<Project> CreateTestProject()
    {
        var project = new Project
        {
            Name = "test-repo",
            LocalPath = "/test/path",
            GitHubOwner = "test-owner",
            GitHubRepo = "test-repo",
            DefaultBranch = "main"
        };

        await _dataStore.AddProjectAsync(project);
        return project;
    }

    [Test]
    public async Task GetPullRequestStatusForIssueAsync_NoLinkedPr_ReturnsNull()
    {
        // Arrange
        var project = await CreateTestProject();

        // Act
        var result = await _service.GetPullRequestStatusForIssueAsync(project.Id, "issue-123");

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetPullRequestStatusForIssueAsync_LinkedPr_ReturnsStatus()
    {
        // Arrange
        var project = await CreateTestProject();

        // Create a tracked PR linked to an issue
        var trackedPr = new PullRequest
        {
            ProjectId = project.Id,
            Title = "Test PR",
            BranchName = "issues/feature/test+issue-123",
            GitHubPRNumber = 42,
            BeadsIssueId = "issue-123",
            Status = OpenPullRequestStatus.ReadyForReview
        };
        await _dataStore.AddPullRequestAsync(trackedPr);

        // Mock GitHub PR data
        var mockPr = CreateMockPullRequest(42, "Test PR", ItemState.Open, "issues/feature/test+issue-123");
        _mockGitHubClient.Setup(c => c.GetPullRequestsAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.Is<PullRequestRequest>(r => r.State == ItemStateFilter.Open)))
            .ReturnsAsync([mockPr]);

        _mockGitHubClient.Setup(c => c.GetPullRequestReviewsAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            42))
            .ReturnsAsync([]);

        _mockGitHubClient.Setup(c => c.GetCombinedCommitStatusAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.IsAny<string>()))
            .ReturnsAsync(CreateMockCombinedStatus(CommitState.Success));

        // Act
        var result = await _service.GetPullRequestStatusForIssueAsync(project.Id, "issue-123");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.PrNumber, Is.EqualTo(42));
        Assert.That(result.Status, Is.EqualTo(PullRequestStatus.ReadyForReview));
        Assert.That(result.PrUrl, Does.Contain("/pull/42"));
    }

    [Test]
    public async Task GetPullRequestStatusForIssueAsync_ChecksPassing_PopulatesChecksPassing()
    {
        // Arrange
        var project = await CreateTestProject();

        var trackedPr = new PullRequest
        {
            ProjectId = project.Id,
            Title = "Test PR",
            BranchName = "issues/feature/test+issue-123",
            GitHubPRNumber = 42,
            BeadsIssueId = "issue-123",
            Status = OpenPullRequestStatus.ReadyForReview
        };
        await _dataStore.AddPullRequestAsync(trackedPr);

        var mockPr = CreateMockPullRequest(42, "Test PR", ItemState.Open, "issues/feature/test+issue-123");
        _mockGitHubClient.Setup(c => c.GetPullRequestsAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.Is<PullRequestRequest>(r => r.State == ItemStateFilter.Open)))
            .ReturnsAsync([mockPr]);

        _mockGitHubClient.Setup(c => c.GetPullRequestReviewsAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            42))
            .ReturnsAsync([]);

        // Checks are passing
        _mockGitHubClient.Setup(c => c.GetCombinedCommitStatusAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.IsAny<string>()))
            .ReturnsAsync(CreateMockCombinedStatus(CommitState.Success));

        // Act
        var result = await _service.GetPullRequestStatusForIssueAsync(project.Id, "issue-123");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ChecksPassing, Is.True, "ChecksPassing should be true when commit status is Success");
        Assert.That(result.ChecksRunning, Is.False, "ChecksRunning should be false when checks have passed");
    }

    [Test]
    public async Task GetPullRequestStatusForIssueAsync_ChecksFailing_PopulatesChecksPassing()
    {
        // Arrange
        var project = await CreateTestProject();

        var trackedPr = new PullRequest
        {
            ProjectId = project.Id,
            Title = "Failing PR",
            BranchName = "issues/feature/failing+issue-456",
            GitHubPRNumber = 99,
            BeadsIssueId = "issue-456",
            Status = OpenPullRequestStatus.InDevelopment
        };
        await _dataStore.AddPullRequestAsync(trackedPr);

        var mockPr = CreateMockPullRequest(99, "Failing PR", ItemState.Open, "issues/feature/failing+issue-456");
        _mockGitHubClient.Setup(c => c.GetPullRequestsAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.Is<PullRequestRequest>(r => r.State == ItemStateFilter.Open)))
            .ReturnsAsync([mockPr]);

        _mockGitHubClient.Setup(c => c.GetPullRequestReviewsAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            99))
            .ReturnsAsync([]);

        // Checks are failing
        _mockGitHubClient.Setup(c => c.GetCombinedCommitStatusAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.IsAny<string>()))
            .ReturnsAsync(CreateMockCombinedStatus(CommitState.Failure));

        // Act
        var result = await _service.GetPullRequestStatusForIssueAsync(project.Id, "issue-456");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ChecksPassing, Is.False, "ChecksPassing should be false when commit status is Failure");
        Assert.That(result.ChecksFailing, Is.True, "ChecksFailing should be true when checks have failed");
    }

    [Test]
    public async Task GetPullRequestStatusForIssueAsync_ChecksPending_HasNullChecksPassing()
    {
        // Arrange
        var project = await CreateTestProject();

        var trackedPr = new PullRequest
        {
            ProjectId = project.Id,
            Title = "Pending PR",
            BranchName = "issues/feature/pending+issue-789",
            GitHubPRNumber = 55,
            BeadsIssueId = "issue-789",
            Status = OpenPullRequestStatus.InDevelopment
        };
        await _dataStore.AddPullRequestAsync(trackedPr);

        var mockPr = CreateMockPullRequest(55, "Pending PR", ItemState.Open, "issues/feature/pending+issue-789");
        _mockGitHubClient.Setup(c => c.GetPullRequestsAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.Is<PullRequestRequest>(r => r.State == ItemStateFilter.Open)))
            .ReturnsAsync([mockPr]);

        _mockGitHubClient.Setup(c => c.GetPullRequestReviewsAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            55))
            .ReturnsAsync([]);

        // Checks are pending
        _mockGitHubClient.Setup(c => c.GetCombinedCommitStatusAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.IsAny<string>()))
            .ReturnsAsync(CreateMockCombinedStatus(CommitState.Pending));

        // Act
        var result = await _service.GetPullRequestStatusForIssueAsync(project.Id, "issue-789");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ChecksPassing, Is.Null, "ChecksPassing should be null when commit status is Pending");
        Assert.That(result.ChecksRunning, Is.True, "ChecksRunning should be true when checks are pending");
    }

    [Test]
    public async Task GetPullRequestStatusForIssueAsync_WithApprovals_PopulatesApprovalData()
    {
        // Arrange
        var project = await CreateTestProject();

        var trackedPr = new PullRequest
        {
            ProjectId = project.Id,
            Title = "Approved PR",
            BranchName = "issues/feature/approved+issue-abc",
            GitHubPRNumber = 77,
            BeadsIssueId = "issue-abc",
            Status = OpenPullRequestStatus.Approved
        };
        await _dataStore.AddPullRequestAsync(trackedPr);

        var mockPr = CreateMockPullRequest(77, "Approved PR", ItemState.Open, "issues/feature/approved+issue-abc");
        _mockGitHubClient.Setup(c => c.GetPullRequestsAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.Is<PullRequestRequest>(r => r.State == ItemStateFilter.Open)))
            .ReturnsAsync([mockPr]);

        // Has two approvals
        _mockGitHubClient.Setup(c => c.GetPullRequestReviewsAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            77))
            .ReturnsAsync([
                CreateMockReview(PullRequestReviewState.Approved, 1),
                CreateMockReview(PullRequestReviewState.Approved, 2)
            ]);

        _mockGitHubClient.Setup(c => c.GetCombinedCommitStatusAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.IsAny<string>()))
            .ReturnsAsync(CreateMockCombinedStatus(CommitState.Success));

        // Act
        var result = await _service.GetPullRequestStatusForIssueAsync(project.Id, "issue-abc");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.IsApproved, Is.True, "IsApproved should be true when PR has approvals");
        Assert.That(result.ApprovalCount, Is.EqualTo(2), "ApprovalCount should reflect number of approvals");
    }

    [Test]
    public async Task GetPullRequestStatusForIssueAsync_WithChangesRequested_PopulatesChangesRequestedCount()
    {
        // Arrange
        var project = await CreateTestProject();

        var trackedPr = new PullRequest
        {
            ProjectId = project.Id,
            Title = "Changes Requested PR",
            BranchName = "issues/feature/changes+issue-def",
            GitHubPRNumber = 88,
            BeadsIssueId = "issue-def",
            Status = OpenPullRequestStatus.HasReviewComments
        };
        await _dataStore.AddPullRequestAsync(trackedPr);

        var mockPr = CreateMockPullRequest(88, "Changes Requested PR", ItemState.Open, "issues/feature/changes+issue-def");
        _mockGitHubClient.Setup(c => c.GetPullRequestsAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.Is<PullRequestRequest>(r => r.State == ItemStateFilter.Open)))
            .ReturnsAsync([mockPr]);

        // Has changes requested from one reviewer
        _mockGitHubClient.Setup(c => c.GetPullRequestReviewsAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            88))
            .ReturnsAsync([
                CreateMockReview(PullRequestReviewState.ChangesRequested, 1)
            ]);

        _mockGitHubClient.Setup(c => c.GetCombinedCommitStatusAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.IsAny<string>()))
            .ReturnsAsync(CreateMockCombinedStatus(CommitState.Success));

        // Act
        var result = await _service.GetPullRequestStatusForIssueAsync(project.Id, "issue-def");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ChangesRequestedCount, Is.EqualTo(1), "ChangesRequestedCount should reflect number of change requests");
    }

    [Test]
    public async Task GetPullRequestStatusForIssueAsync_ChecksFailing_ReturnsChecksFailingStatus()
    {
        // Arrange
        var project = await CreateTestProject();

        var trackedPr = new PullRequest
        {
            ProjectId = project.Id,
            Title = "Failing PR",
            BranchName = "issues/feature/failing+issue-456",
            GitHubPRNumber = 99,
            BeadsIssueId = "issue-456",
            Status = OpenPullRequestStatus.InDevelopment
        };
        await _dataStore.AddPullRequestAsync(trackedPr);

        var mockPr = CreateMockPullRequest(99, "Failing PR", ItemState.Open, "issues/feature/failing+issue-456");
        _mockGitHubClient.Setup(c => c.GetPullRequestsAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.Is<PullRequestRequest>(r => r.State == ItemStateFilter.Open)))
            .ReturnsAsync([mockPr]);

        _mockGitHubClient.Setup(c => c.GetPullRequestReviewsAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            99))
            .ReturnsAsync([]);

        // Checks are failing
        _mockGitHubClient.Setup(c => c.GetCombinedCommitStatusAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.IsAny<string>()))
            .ReturnsAsync(CreateMockCombinedStatus(CommitState.Failure));

        // Act
        var result = await _service.GetPullRequestStatusForIssueAsync(project.Id, "issue-456");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Status, Is.EqualTo(PullRequestStatus.ChecksFailing));
        Assert.That(result.ChecksFailing, Is.True);
    }

    [Test]
    public async Task GetPullRequestStatusForIssueAsync_Approved_ReturnsReadyForMerging()
    {
        // Arrange
        var project = await CreateTestProject();

        var trackedPr = new PullRequest
        {
            ProjectId = project.Id,
            Title = "Approved PR",
            BranchName = "issues/feature/approved+issue-789",
            GitHubPRNumber = 77,
            BeadsIssueId = "issue-789",
            Status = OpenPullRequestStatus.Approved
        };
        await _dataStore.AddPullRequestAsync(trackedPr);

        var mockPr = CreateMockPullRequest(77, "Approved PR", ItemState.Open, "issues/feature/approved+issue-789");
        _mockGitHubClient.Setup(c => c.GetPullRequestsAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.Is<PullRequestRequest>(r => r.State == ItemStateFilter.Open)))
            .ReturnsAsync([mockPr]);

        // Has approval
        _mockGitHubClient.Setup(c => c.GetPullRequestReviewsAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            77))
            .ReturnsAsync([CreateMockReview(PullRequestReviewState.Approved)]);

        // Checks passing
        _mockGitHubClient.Setup(c => c.GetCombinedCommitStatusAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.IsAny<string>()))
            .ReturnsAsync(CreateMockCombinedStatus(CommitState.Success));

        // Act
        var result = await _service.GetPullRequestStatusForIssueAsync(project.Id, "issue-789");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Status, Is.EqualTo(PullRequestStatus.ReadyForMerging));
        Assert.That(result.IsMergeable, Is.True);
    }

    [Test]
    public async Task GetPullRequestStatusForIssueAsync_MissingGitHubConfig_ReturnsNull()
    {
        // Arrange
        var project = new Project
        {
            Name = "no-github-repo",
            LocalPath = "/test/path",
            DefaultBranch = "main"
            // No GitHubOwner or GitHubRepo
        };
        await _dataStore.AddProjectAsync(project);

        var trackedPr = new PullRequest
        {
            ProjectId = project.Id,
            Title = "Test PR",
            BranchName = "issues/feature/test+issue-123",
            GitHubPRNumber = 42,
            BeadsIssueId = "issue-123",
            Status = OpenPullRequestStatus.ReadyForReview
        };
        await _dataStore.AddPullRequestAsync(trackedPr);

        // Act
        var result = await _service.GetPullRequestStatusForIssueAsync(project.Id, "issue-123");

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetPullRequestStatusForIssueAsync_PrWithoutGitHubNumber_ReturnsNull()
    {
        // Arrange
        var project = await CreateTestProject();

        // Create a tracked PR without GitHub PR number
        var trackedPr = new PullRequest
        {
            ProjectId = project.Id,
            Title = "Local PR",
            BranchName = "issues/feature/local+issue-123",
            BeadsIssueId = "issue-123",
            Status = OpenPullRequestStatus.InDevelopment
            // No GitHubPRNumber
        };
        await _dataStore.AddPullRequestAsync(trackedPr);

        // Act
        var result = await _service.GetPullRequestStatusForIssueAsync(project.Id, "issue-123");

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetPullRequestStatusForIssueAsync_MergeConflicts_ShowsHasConflicts()
    {
        // Arrange
        var project = await CreateTestProject();

        var trackedPr = new PullRequest
        {
            ProjectId = project.Id,
            Title = "PR with conflicts",
            BranchName = "issues/feature/conflicts+issue-conflict",
            GitHubPRNumber = 101,
            BeadsIssueId = "issue-conflict",
            Status = OpenPullRequestStatus.InDevelopment
        };
        await _dataStore.AddPullRequestAsync(trackedPr);

        // PR has merge conflicts: mergeable=false, mergeableState=dirty
        var mockPr = CreateMockPullRequest(
            101,
            "PR with conflicts",
            ItemState.Open,
            "issues/feature/conflicts+issue-conflict",
            mergeable: false,
            mergeableState: "dirty");

        _mockGitHubClient.Setup(c => c.GetPullRequestsAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.Is<PullRequestRequest>(r => r.State == ItemStateFilter.Open)))
            .ReturnsAsync([mockPr]);

        _mockGitHubClient.Setup(c => c.GetPullRequestReviewsAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            101))
            .ReturnsAsync([]);

        _mockGitHubClient.Setup(c => c.GetCombinedCommitStatusAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.IsAny<string>()))
            .ReturnsAsync(CreateMockCombinedStatus(CommitState.Success));

        // Act
        var result = await _service.GetPullRequestStatusForIssueAsync(project.Id, "issue-conflict");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.IsMergeableByGitHub, Is.False, "IsMergeableByGitHub should be false when PR has conflicts");
        Assert.That(result.MergeableState, Is.EqualTo("dirty"), "MergeableState should be 'dirty' when PR has conflicts");
        Assert.That(result.HasConflicts, Is.True, "HasConflicts should be true when PR has merge conflicts");
    }

    [Test]
    public async Task GetPullRequestStatusForIssueAsync_NoMergeConflicts_ShowsNoConflicts()
    {
        // Arrange
        var project = await CreateTestProject();

        var trackedPr = new PullRequest
        {
            ProjectId = project.Id,
            Title = "Clean PR",
            BranchName = "issues/feature/clean+issue-clean",
            GitHubPRNumber = 102,
            BeadsIssueId = "issue-clean",
            Status = OpenPullRequestStatus.ReadyForReview
        };
        await _dataStore.AddPullRequestAsync(trackedPr);

        // PR is clean: mergeable=true, mergeableState=clean
        var mockPr = CreateMockPullRequest(
            102,
            "Clean PR",
            ItemState.Open,
            "issues/feature/clean+issue-clean",
            mergeable: true,
            mergeableState: "clean");

        _mockGitHubClient.Setup(c => c.GetPullRequestsAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.Is<PullRequestRequest>(r => r.State == ItemStateFilter.Open)))
            .ReturnsAsync([mockPr]);

        _mockGitHubClient.Setup(c => c.GetPullRequestReviewsAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            102))
            .ReturnsAsync([]);

        _mockGitHubClient.Setup(c => c.GetCombinedCommitStatusAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.IsAny<string>()))
            .ReturnsAsync(CreateMockCombinedStatus(CommitState.Success));

        // Act
        var result = await _service.GetPullRequestStatusForIssueAsync(project.Id, "issue-clean");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.IsMergeableByGitHub, Is.True, "IsMergeableByGitHub should be true when PR is clean");
        Assert.That(result.MergeableState, Is.EqualTo("clean"), "MergeableState should be 'clean' when PR has no conflicts");
        Assert.That(result.HasConflicts, Is.False, "HasConflicts should be false when PR is clean");
    }

    [Test]
    public async Task GetPullRequestStatusForIssueAsync_UnknownMergeState_HandlesGracefully()
    {
        // Arrange
        var project = await CreateTestProject();

        var trackedPr = new PullRequest
        {
            ProjectId = project.Id,
            Title = "Unknown merge state PR",
            BranchName = "issues/feature/unknown+issue-unknown",
            GitHubPRNumber = 103,
            BeadsIssueId = "issue-unknown",
            Status = OpenPullRequestStatus.InDevelopment
        };
        await _dataStore.AddPullRequestAsync(trackedPr);

        // PR merge state not computed yet: mergeable=null, mergeableState=unknown
        var mockPr = CreateMockPullRequest(
            103,
            "Unknown merge state PR",
            ItemState.Open,
            "issues/feature/unknown+issue-unknown",
            mergeable: null,
            mergeableState: "unknown");

        _mockGitHubClient.Setup(c => c.GetPullRequestsAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.Is<PullRequestRequest>(r => r.State == ItemStateFilter.Open)))
            .ReturnsAsync([mockPr]);

        _mockGitHubClient.Setup(c => c.GetPullRequestReviewsAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            103))
            .ReturnsAsync([]);

        _mockGitHubClient.Setup(c => c.GetCombinedCommitStatusAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.IsAny<string>()))
            .ReturnsAsync(CreateMockCombinedStatus(CommitState.Pending));

        // Act
        var result = await _service.GetPullRequestStatusForIssueAsync(project.Id, "issue-unknown");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.IsMergeableByGitHub, Is.Null, "IsMergeableByGitHub should be null when GitHub hasn't computed it");
        Assert.That(result.MergeableState, Is.EqualTo("unknown"), "MergeableState should be 'unknown' when not computed");
        Assert.That(result.HasConflicts, Is.False, "HasConflicts should be false when merge state is unknown");
    }

    #region Helper Methods

    private static Octokit.PullRequest CreateMockPullRequest(
        int number,
        string title,
        ItemState state,
        string branchName,
        bool merged = false,
        bool draft = false,
        bool? mergeable = true,
        string? mergeableState = null)
    {
        var headRef = new GitReference(
            nodeId: "node1",
            url: "url",
            label: "label",
            @ref: branchName,
            sha: "abc123",
            user: null,
            repository: null
        );

        return new Octokit.PullRequest(
            id: number,
            nodeId: $"node-{number}",
            url: $"https://api.github.com/repos/owner/repo/pulls/{number}",
            htmlUrl: $"https://github.com/owner/repo/pull/{number}",
            diffUrl: $"https://github.com/owner/repo/pull/{number}.diff",
            patchUrl: $"https://github.com/owner/repo/pull/{number}.patch",
            issueUrl: $"https://api.github.com/repos/owner/repo/issues/{number}",
            statusesUrl: $"https://api.github.com/repos/owner/repo/statuses/sha",
            number: number,
            state: state,
            title: title,
            body: "Description",
            createdAt: DateTimeOffset.UtcNow.AddDays(-7),
            updatedAt: DateTimeOffset.UtcNow,
            closedAt: state == ItemState.Closed ? DateTimeOffset.UtcNow : null,
            mergedAt: merged ? DateTimeOffset.UtcNow : null,
            head: headRef,
            @base: headRef,
            user: null,
            assignee: null,
            assignees: null,
            draft: draft,
            mergeable: mergeable,
            mergeableState: mergeableState != null ? ParseMergeableState(mergeableState) : null,
            mergedBy: null,
            mergeCommitSha: null,
            comments: 0,
            commits: 1,
            additions: 10,
            deletions: 5,
            changedFiles: 2,
            milestone: null,
            locked: false,
            maintainerCanModify: null,
            requestedReviewers: null,
            requestedTeams: null,
            labels: null,
            activeLockReason: null
        );
    }

    private static MergeableState ParseMergeableState(string state)
    {
        return state.ToLowerInvariant() switch
        {
            "clean" => MergeableState.Clean,
            "dirty" => MergeableState.Dirty,
            "blocked" => MergeableState.Blocked,
            "unstable" => MergeableState.Unstable,
            "unknown" => MergeableState.Unknown,
            _ => MergeableState.Unknown
        };
    }

    private static CombinedCommitStatus CreateMockCombinedStatus(CommitState state)
    {
        return new CombinedCommitStatus(
            state: state,
            sha: "abc123",
            totalCount: 1,
            statuses: [],
            repository: null
        );
    }

    private static PullRequestReview CreateMockReview(PullRequestReviewState state, int userId = 1)
    {
        var user = new User(
            avatarUrl: null,
            bio: null,
            blog: null,
            collaborators: 0,
            company: null,
            createdAt: DateTimeOffset.UtcNow,
            updatedAt: DateTimeOffset.UtcNow,
            diskUsage: 0,
            email: null,
            followers: 0,
            following: 0,
            hireable: null,
            htmlUrl: null,
            totalPrivateRepos: 0,
            id: userId,
            location: null,
            login: $"user-{userId}",
            name: null,
            nodeId: $"user-node-{userId}",
            ownedPrivateRepos: 0,
            plan: null,
            privateGists: 0,
            publicGists: 0,
            publicRepos: 0,
            url: null,
            permissions: null,
            siteAdmin: false,
            ldapDistinguishedName: null,
            suspendedAt: null
        );

        return new PullRequestReview(
            id: userId,
            nodeId: $"review-{userId}",
            commitId: "abc123",
            user: user,
            body: "Review comment",
            htmlUrl: "https://github.com/owner/repo/pull/1#pullrequestreview-1",
            pullRequestUrl: "https://api.github.com/repos/owner/repo/pulls/1",
            state: state,
            authorAssociation: AuthorAssociation.Contributor,
            submittedAt: DateTimeOffset.UtcNow
        );
    }

    #endregion
}
