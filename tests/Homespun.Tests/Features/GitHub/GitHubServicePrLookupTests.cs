using Homespun.Features.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.GitHub;

[TestFixture]
public class GitHubServicePrLookupTests
{
    private MockDataStore _dataStore = null!;
    private Mock<ICommandRunner> _mockRunner = null!;
    private Mock<IConfiguration> _mockConfig = null!;
    private Mock<IGitHubClientWrapper> _mockGitHubClient = null!;
    private Mock<IIssuePrLinkingService> _mockLinkingService = null!;
    private Mock<IGitCloneService> _mockCloneService = null!;
    private Mock<ILogger<GitHubService>> _mockLogger = null!;
    private GitHubService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _dataStore = new MockDataStore();
        _mockRunner = new Mock<ICommandRunner>();
        _mockConfig = new Mock<IConfiguration>();
        _mockGitHubClient = new Mock<IGitHubClientWrapper>();
        _mockLinkingService = new Mock<IIssuePrLinkingService>();
        _mockCloneService = new Mock<IGitCloneService>();
        _mockLogger = new Mock<ILogger<GitHubService>>();

        _mockConfig.Setup(c => c["GITHUB_TOKEN"]).Returns("test-token");

        _service = new GitHubService(_dataStore, _mockRunner.Object, _mockConfig.Object, _mockGitHubClient.Object, _mockLinkingService.Object, _mockCloneService.Object, _mockLogger.Object);
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

    private async Task<PullRequest> CreateTestPullRequest(
        string projectId,
        string? branchName = "feature/test",
        int? prNumber = null,
        string? fleeceIssueId = null)
    {
        var pullRequest = new PullRequest
        {
            ProjectId = projectId,
            Title = "Test Pull Request",
            Description = "Test Description",
            BranchName = branchName,
            GitHubPRNumber = prNumber,
            FleeceIssueId = fleeceIssueId,
            Status = OpenPullRequestStatus.InDevelopment
        };

        await _dataStore.AddPullRequestAsync(pullRequest);
        return pullRequest;
    }

    [Test]
    public async Task GetPullRequestForIssueAsync_WithMatchingPR_ReturnsPR()
    {
        // Arrange
        var project = await CreateTestProject();
        var issueId = "issue-123";
        var pr = await CreateTestPullRequest(project.Id, fleeceIssueId: issueId);

        // Act
        var result = await _service.GetPullRequestForIssueAsync(project.Id, issueId);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Id, Is.EqualTo(pr.Id));
        Assert.That(result.FleeceIssueId, Is.EqualTo(issueId));
    }

    [Test]
    public async Task GetPullRequestForIssueAsync_WithNoMatchingPR_ReturnsNull()
    {
        // Arrange
        var project = await CreateTestProject();
        await CreateTestPullRequest(project.Id, fleeceIssueId: "other-issue");

        // Act
        var result = await _service.GetPullRequestForIssueAsync(project.Id, "non-existent-issue");

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetPullRequestForIssueAsync_WithMultiplePRs_ReturnsCorrectOne()
    {
        // Arrange
        var project = await CreateTestProject();
        var issueId = "target-issue";

        await CreateTestPullRequest(project.Id, branchName: "feature/one", fleeceIssueId: "issue-1");
        var targetPr = await CreateTestPullRequest(project.Id, branchName: "feature/two", fleeceIssueId: issueId);
        await CreateTestPullRequest(project.Id, branchName: "feature/three", fleeceIssueId: "issue-3");

        // Act
        var result = await _service.GetPullRequestForIssueAsync(project.Id, issueId);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Id, Is.EqualTo(targetPr.Id));
        Assert.That(result.BranchName, Is.EqualTo("feature/two"));
    }

    [Test]
    public async Task GetPullRequestForIssueAsync_FiltersByProjectId()
    {
        // Arrange
        var project1 = await CreateTestProject();
        var project2 = new Project
        {
            Name = "other-repo",
            LocalPath = "/other/path",
            GitHubOwner = "other-owner",
            GitHubRepo = "other-repo",
            DefaultBranch = "main"
        };
        await _dataStore.AddProjectAsync(project2);

        var issueId = "shared-issue";

        // Create PR in project1 with the issue
        var pr1 = await CreateTestPullRequest(project1.Id, branchName: "feature/proj1", fleeceIssueId: issueId);

        // Create PR in project2 with different issue
        await CreateTestPullRequest(project2.Id, branchName: "feature/proj2", fleeceIssueId: "other-issue");

        // Act
        var result = await _service.GetPullRequestForIssueAsync(project1.Id, issueId);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Id, Is.EqualTo(pr1.Id));
        Assert.That(result.ProjectId, Is.EqualTo(project1.Id));
    }

    [Test]
    public async Task GetPullRequestForIssueAsync_WithNoProjectPRs_ReturnsNull()
    {
        // Arrange
        var project = await CreateTestProject();

        // Act
        var result = await _service.GetPullRequestForIssueAsync(project.Id, "any-issue");

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetPullRequestForIssueAsync_WithNonExistentProject_ReturnsNull()
    {
        // Act
        var result = await _service.GetPullRequestForIssueAsync("non-existent-project", "any-issue");

        // Assert
        Assert.That(result, Is.Null);
    }
}
