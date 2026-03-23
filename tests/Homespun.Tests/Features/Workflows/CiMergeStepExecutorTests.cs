using System.Text.Json;
using Homespun.Features.GitHub;
using Homespun.Features.Workflows.Services;
using Homespun.Shared.Models.Workflows;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Octokit;
using Octokit.Internal;

namespace Homespun.Tests.Features.Workflows;

[TestFixture]
public class CiMergeStepExecutorTests
{
    private Mock<IGitHubClientWrapper> _mockGithubClient = null!;
    private FakeTimeProvider _timeProvider = null!;
    private CiMergeStepExecutor _executor = null!;

    [SetUp]
    public void SetUp()
    {
        _mockGithubClient = new Mock<IGitHubClientWrapper>();
        _timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        _executor = new CiMergeStepExecutor(
            _mockGithubClient.Object,
            _timeProvider,
            new Mock<ILogger<CiMergeStepExecutor>>().Object);
    }

    [Test]
    public void ActionType_ReturnsCiMerge()
    {
        Assert.That(_executor.ActionType, Is.EqualTo("ci_merge"));
    }

    [Test]
    public async Task ExecuteAsync_AllChecksPassed_MergesSuccessfully()
    {
        // Arrange
        var step = CreateStep();
        var context = CreateContext(prNumber: 42, owner: "test-owner", repo: "test-repo");

        SetupPullRequest("test-owner", "test-repo", 42, "abc123", "Fix the bug");
        SetupAllChecksPassed("test-owner", "test-repo", "abc123");
        SetupMergeSuccess("test-owner", "test-repo", 42, "merge-sha-456");

        // Act
        var result = await _executor.ExecuteAsync(step, context, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.Output, Is.Not.Null);
            Assert.That(result.Output!["prNumber"], Is.EqualTo(42));
            Assert.That(result.Output["merged"], Is.EqualTo(true));
            Assert.That(result.Output["sha"], Is.EqualTo("merge-sha-456"));
            Assert.That(result.Output["mergeStrategy"], Is.EqualTo("squash"));
        });

        _mockGithubClient.Verify(
            c => c.MergePullRequestAsync("test-owner", "test-repo", 42, It.Is<MergePullRequest>(m => m.MergeMethod == PullRequestMergeMethod.Squash)),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_CheckFailure_ReturnsFailedWithDetails()
    {
        // Arrange
        var step = CreateStep();
        var context = CreateContext(prNumber: 42, owner: "test-owner", repo: "test-repo");

        SetupPullRequest("test-owner", "test-repo", 42, "abc123", "Fix the bug");
        SetupCheckRunsWithFailure("test-owner", "test-repo", "abc123", "build", CheckConclusion.Failure);
        SetupEmptyCommitStatuses("test-owner", "test-repo", "abc123");

        // Act
        var result = await _executor.ExecuteAsync(step, context, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("CI checks failed"));
            Assert.That(result.ErrorMessage, Does.Contain("build"));
        });

        _mockGithubClient.Verify(
            c => c.MergePullRequestAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<MergePullRequest>()),
            Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_Timeout_ReturnsFailedWithTimeoutMessage()
    {
        // Arrange
        var config = JsonSerializer.SerializeToElement(new
        {
            actionType = "ci_merge",
            pollIntervalSeconds = 5,
            timeoutMinutes = 1
        });

        var step = CreateStep(config);
        var context = CreateContext(prNumber: 42, owner: "test-owner", repo: "test-repo");

        SetupPullRequest("test-owner", "test-repo", 42, "abc123", "Fix the bug");
        SetupChecksPending("test-owner", "test-repo", "abc123");

        // Act - advance time past the timeout
        var executeTask = _executor.ExecuteAsync(step, context, CancellationToken.None);
        _timeProvider.Advance(TimeSpan.FromMinutes(2));
        var result = await executeTask;

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("Timed out"));
            Assert.That(result.ErrorMessage, Does.Contain("1 minutes"));
        });
    }

    [Test]
    public async Task ExecuteAsync_PollsUntilChecksPass_ThenMerges()
    {
        // Arrange
        var config = JsonSerializer.SerializeToElement(new
        {
            actionType = "ci_merge",
            pollIntervalSeconds = 10,
            timeoutMinutes = 5
        });

        var step = CreateStep(config);
        var context = CreateContext(prNumber: 42, owner: "test-owner", repo: "test-repo");

        SetupPullRequest("test-owner", "test-repo", 42, "abc123", "Fix the bug");
        SetupMergeSuccess("test-owner", "test-repo", 42, "merge-sha");

        // First call: pending, second call: all passed
        var callCount = 0;
        _mockGithubClient.Setup(c => c.GetCheckRunsForReferenceAsync("test-owner", "test-repo", "abc123"))
            .ReturnsAsync(() =>
            {
                callCount++;
                return callCount == 1
                    ? CreateCheckRunsResponse(("build", CheckStatus.InProgress, null))
                    : CreateCheckRunsResponse(("build", CheckStatus.Completed, CheckConclusion.Success));
            });

        _mockGithubClient.Setup(c => c.GetCombinedCommitStatusAsync("test-owner", "test-repo", "abc123"))
            .ReturnsAsync(CreateCombinedCommitStatus());

        // Act
        var executeTask = _executor.ExecuteAsync(step, context, CancellationToken.None);
        _timeProvider.Advance(TimeSpan.FromSeconds(10));
        var result = await executeTask;

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(callCount, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task ExecuteAsync_MergeStrategyMerge_UsesMergeMethod()
    {
        // Arrange
        var config = JsonSerializer.SerializeToElement(new
        {
            actionType = "ci_merge",
            mergeStrategy = "merge"
        });

        var step = CreateStep(config);
        var context = CreateContext(prNumber: 42, owner: "test-owner", repo: "test-repo");

        SetupPullRequest("test-owner", "test-repo", 42, "abc123", "Fix the bug");
        SetupAllChecksPassed("test-owner", "test-repo", "abc123");
        SetupMergeSuccess("test-owner", "test-repo", 42, "merge-sha");

        // Act
        var result = await _executor.ExecuteAsync(step, context, CancellationToken.None);

        // Assert
        Assert.That(result.Success, Is.True);
        _mockGithubClient.Verify(
            c => c.MergePullRequestAsync("test-owner", "test-repo", 42, It.Is<MergePullRequest>(m => m.MergeMethod == PullRequestMergeMethod.Merge)),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_MergeStrategyRebase_UsesRebaseMethod()
    {
        // Arrange
        var config = JsonSerializer.SerializeToElement(new
        {
            actionType = "ci_merge",
            mergeStrategy = "rebase"
        });

        var step = CreateStep(config);
        var context = CreateContext(prNumber: 42, owner: "test-owner", repo: "test-repo");

        SetupPullRequest("test-owner", "test-repo", 42, "abc123", "Fix the bug");
        SetupAllChecksPassed("test-owner", "test-repo", "abc123");
        SetupMergeSuccess("test-owner", "test-repo", 42, "merge-sha");

        // Act
        var result = await _executor.ExecuteAsync(step, context, CancellationToken.None);

        // Assert
        Assert.That(result.Success, Is.True);
        _mockGithubClient.Verify(
            c => c.MergePullRequestAsync("test-owner", "test-repo", 42, It.Is<MergePullRequest>(m => m.MergeMethod == PullRequestMergeMethod.Rebase)),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_NoPrNumberInContext_ReturnsFailed()
    {
        // Arrange
        var step = CreateStep();
        var context = new WorkflowContext
        {
            Variables = new Dictionary<string, object>
            {
                ["owner"] = "test-owner",
                ["repo"] = "test-repo"
            }
        };

        // Act
        var result = await _executor.ExecuteAsync(step, context, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("PR number not found"));
        });
    }

    [Test]
    public async Task ExecuteAsync_NoOwnerRepoInContext_ReturnsFailed()
    {
        // Arrange
        var step = CreateStep();
        var context = new WorkflowContext
        {
            Variables = new Dictionary<string, object>
            {
                ["prNumber"] = 42
            }
        };

        // Act
        var result = await _executor.ExecuteAsync(step, context, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("owner and repo not found"));
        });
    }

    [Test]
    public async Task ExecuteAsync_PrNumberFromNodeOutput_Succeeds()
    {
        // Arrange
        var step = CreateStep();
        var context = new WorkflowContext
        {
            Variables = new Dictionary<string, object>
            {
                ["owner"] = "test-owner",
                ["repo"] = "test-repo"
            },
            NodeOutputs = new Dictionary<string, NodeOutput>
            {
                ["implement"] = new()
                {
                    Status = "completed",
                    Data = new Dictionary<string, object> { ["prNumber"] = 99 }
                }
            }
        };

        SetupPullRequest("test-owner", "test-repo", 99, "sha-99", "New feature");
        SetupAllChecksPassed("test-owner", "test-repo", "sha-99");
        SetupMergeSuccess("test-owner", "test-repo", 99, "merge-sha");

        // Act
        var result = await _executor.ExecuteAsync(step, context, CancellationToken.None);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.Output!["prNumber"], Is.EqualTo(99));
    }

    [Test]
    public async Task ExecuteAsync_CommitStatusFailure_ReturnsFailedWithDetails()
    {
        // Arrange
        var step = CreateStep();
        var context = CreateContext(prNumber: 42, owner: "test-owner", repo: "test-repo");

        SetupPullRequest("test-owner", "test-repo", 42, "abc123", "Fix the bug");

        _mockGithubClient.Setup(c => c.GetCheckRunsForReferenceAsync("test-owner", "test-repo", "abc123"))
            .ReturnsAsync(CreateCheckRunsResponse());

        _mockGithubClient.Setup(c => c.GetCombinedCommitStatusAsync("test-owner", "test-repo", "abc123"))
            .ReturnsAsync(CreateCombinedCommitStatus(("ci/build", CommitState.Failure)));

        // Act
        var result = await _executor.ExecuteAsync(step, context, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("ci/build"));
        });
    }

    [Test]
    public async Task ExecuteAsync_MergeApiFails_ReturnsFailedWithError()
    {
        // Arrange
        var step = CreateStep();
        var context = CreateContext(prNumber: 42, owner: "test-owner", repo: "test-repo");

        SetupPullRequest("test-owner", "test-repo", 42, "abc123", "Fix the bug");
        SetupAllChecksPassed("test-owner", "test-repo", "abc123");

        _mockGithubClient.Setup(c => c.MergePullRequestAsync("test-owner", "test-repo", 42, It.IsAny<MergePullRequest>()))
            .ThrowsAsync(new ApiException("Merge conflict", System.Net.HttpStatusCode.Conflict));

        // Act
        var result = await _executor.ExecuteAsync(step, context, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("Failed to merge PR #42"));
        });
    }

    [Test]
    public async Task ExecuteAsync_OwnerRepoFromInput_Succeeds()
    {
        // Arrange
        var step = CreateStep();
        var context = new WorkflowContext
        {
            Input = new Dictionary<string, object>
            {
                ["owner"] = "input-owner",
                ["repo"] = "input-repo"
            },
            Variables = new Dictionary<string, object>
            {
                ["prNumber"] = 42
            }
        };

        SetupPullRequest("input-owner", "input-repo", 42, "abc123", "Fix the bug");
        SetupAllChecksPassed("input-owner", "input-repo", "abc123");
        SetupMergeSuccess("input-owner", "input-repo", 42, "merge-sha");

        // Act
        var result = await _executor.ExecuteAsync(step, context, CancellationToken.None);

        // Assert
        Assert.That(result.Success, Is.True);
    }

    [Test]
    public async Task ExecuteAsync_SkippedCheckRun_TreatedAsPassed()
    {
        // Arrange
        var step = CreateStep();
        var context = CreateContext(prNumber: 42, owner: "test-owner", repo: "test-repo");

        SetupPullRequest("test-owner", "test-repo", 42, "abc123", "Fix the bug");
        SetupMergeSuccess("test-owner", "test-repo", 42, "merge-sha");

        _mockGithubClient.Setup(c => c.GetCheckRunsForReferenceAsync("test-owner", "test-repo", "abc123"))
            .ReturnsAsync(CreateCheckRunsResponse(("build", CheckStatus.Completed, CheckConclusion.Skipped)));

        _mockGithubClient.Setup(c => c.GetCombinedCommitStatusAsync("test-owner", "test-repo", "abc123"))
            .ReturnsAsync(CreateCombinedCommitStatus());

        // Act
        var result = await _executor.ExecuteAsync(step, context, CancellationToken.None);

        // Assert
        Assert.That(result.Success, Is.True);
    }

    #region Helper Methods

    private static WorkflowStep CreateStep(JsonElement? config = null)
    {
        return new WorkflowStep
        {
            Id = "ci-merge-step",
            Name = "CI Merge",
            StepType = WorkflowStepType.ServerAction,
            Config = config ?? JsonSerializer.SerializeToElement(new
            {
                actionType = "ci_merge",
                pollIntervalSeconds = 1,
                timeoutMinutes = 30
            })
        };
    }

    private static WorkflowContext CreateContext(int prNumber, string owner, string repo)
    {
        return new WorkflowContext
        {
            Variables = new Dictionary<string, object>
            {
                ["prNumber"] = prNumber,
                ["owner"] = owner,
                ["repo"] = repo
            }
        };
    }

    private void SetupPullRequest(string owner, string repo, int number, string headSha, string title)
    {
        var pr = CreateMockPullRequest(number, headSha, title);
        _mockGithubClient.Setup(c => c.GetPullRequestAsync(owner, repo, number))
            .ReturnsAsync(pr);
    }

    private void SetupAllChecksPassed(string owner, string repo, string reference)
    {
        _mockGithubClient.Setup(c => c.GetCheckRunsForReferenceAsync(owner, repo, reference))
            .ReturnsAsync(CreateCheckRunsResponse(("build", CheckStatus.Completed, CheckConclusion.Success)));

        _mockGithubClient.Setup(c => c.GetCombinedCommitStatusAsync(owner, repo, reference))
            .ReturnsAsync(CreateCombinedCommitStatus(("ci/test", CommitState.Success)));
    }

    private void SetupCheckRunsWithFailure(string owner, string repo, string reference, string checkName, CheckConclusion conclusion)
    {
        _mockGithubClient.Setup(c => c.GetCheckRunsForReferenceAsync(owner, repo, reference))
            .ReturnsAsync(CreateCheckRunsResponse((checkName, CheckStatus.Completed, conclusion)));

        _mockGithubClient.Setup(c => c.GetCombinedCommitStatusAsync(owner, repo, reference))
            .ReturnsAsync(CreateCombinedCommitStatus());
    }

    private void SetupEmptyCommitStatuses(string owner, string repo, string reference)
    {
        _mockGithubClient.Setup(c => c.GetCombinedCommitStatusAsync(owner, repo, reference))
            .ReturnsAsync(CreateCombinedCommitStatus());
    }

    private void SetupChecksPending(string owner, string repo, string reference)
    {
        _mockGithubClient.Setup(c => c.GetCheckRunsForReferenceAsync(owner, repo, reference))
            .ReturnsAsync(CreateCheckRunsResponse(("build", CheckStatus.InProgress, null)));

        _mockGithubClient.Setup(c => c.GetCombinedCommitStatusAsync(owner, repo, reference))
            .ReturnsAsync(CreateCombinedCommitStatus());
    }

    private void SetupMergeSuccess(string owner, string repo, int number, string sha)
    {
        var mergeResult = CreateMockPullRequestMerge(true, sha);
        _mockGithubClient.Setup(c => c.MergePullRequestAsync(owner, repo, number, It.IsAny<MergePullRequest>()))
            .ReturnsAsync(mergeResult);
    }

    private static Octokit.PullRequest CreateMockPullRequest(int number, string headSha, string title)
    {
        var headRef = new GitReference(
            nodeId: "node1",
            url: "url",
            label: "label",
            @ref: "feature-branch",
            sha: headSha,
            user: null,
            repository: null
        );

        var baseRef = new GitReference(
            nodeId: "node2",
            url: "url",
            label: "label",
            @ref: "main",
            sha: "base-sha",
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
            state: ItemState.Open,
            title: title,
            body: "",
            createdAt: DateTimeOffset.UtcNow,
            updatedAt: DateTimeOffset.UtcNow,
            closedAt: null,
            mergedAt: null,
            head: headRef,
            @base: baseRef,
            user: null,
            assignee: null,
            assignees: null,
            draft: false,
            mergeable: true,
            mergeableState: null,
            mergedBy: null,
            mergeCommitSha: null,
            comments: 0,
            commits: 1,
            additions: 0,
            deletions: 0,
            changedFiles: 0,
            milestone: null,
            locked: false,
            maintainerCanModify: null,
            requestedReviewers: null,
            requestedTeams: null,
            labels: null,
            activeLockReason: null
        );
    }

    private static CheckRunsResponse CreateCheckRunsResponse(params (string Name, CheckStatus Status, CheckConclusion? Conclusion)[] runs)
    {
        var serializer = new SimpleJsonSerializer();
        var checkRuns = runs.Select(r =>
        {
            var statusStr = r.Status == CheckStatus.Completed ? "completed" : r.Status == CheckStatus.InProgress ? "in_progress" : "queued";
            var conclusionStr = r.Conclusion.HasValue ? r.Conclusion.Value switch
            {
                CheckConclusion.Success => "success",
                CheckConclusion.Failure => "failure",
                CheckConclusion.Skipped => "skipped",
                CheckConclusion.Neutral => "neutral",
                _ => "failure"
            } : null;
            var conclusionJson = conclusionStr != null ? $", \"conclusion\": \"{conclusionStr}\"" : "";
            var json = $"{{\"id\": 1, \"name\": \"{r.Name}\", \"status\": \"{statusStr}\"{conclusionJson}}}";
            return serializer.Deserialize<CheckRun>(json);
        }).ToList();

        return new CheckRunsResponse(checkRuns.Count, checkRuns);
    }

    private static CombinedCommitStatus CreateCombinedCommitStatus(params (string Context, CommitState State)[] statuses)
    {
        var commitStatuses = statuses.Select(s => new CommitStatus(
            createdAt: DateTimeOffset.UtcNow,
            updatedAt: DateTimeOffset.UtcNow,
            state: s.State,
            targetUrl: "https://ci.example.com/build/1",
            description: "CI Status",
            id: 1,
            nodeId: "status-1",
            url: "https://api.github.com/repos/owner/repo/statuses/abc123",
            context: s.Context,
            creator: null
        )).ToList();

        return new CombinedCommitStatus(
            state: CommitState.Pending,
            sha: "abc123",
            totalCount: commitStatuses.Count,
            statuses: commitStatuses,
            repository: null
        );
    }

    private static PullRequestMerge CreateMockPullRequestMerge(bool merged, string sha)
    {
        // PullRequestMerge uses internal setters, need Octokit's JSON deserialization
        var serializer = new Octokit.Internal.SimpleJsonSerializer();
        var json = $"{{\"merged\": {(merged ? "true" : "false")}, \"sha\": \"{sha}\", \"message\": \"{(merged ? "Pull Request successfully merged" : "Merge failed")}\"}}";
        return serializer.Deserialize<PullRequestMerge>(json);
    }

    #endregion
}
