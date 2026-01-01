using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using Octokit;
using TreeAgent.Web.Data;
using TreeAgent.Web.Data.Entities;
using TreeAgent.Web.Services;
using Project = TreeAgent.Web.Data.Entities.Project;

namespace TreeAgent.Web.Tests.Services;

public class GitHubServiceTests : IDisposable
{
    private readonly TreeAgentDbContext _db;
    private readonly Mock<ICommandRunner> _mockRunner;
    private readonly Mock<IConfiguration> _mockConfig;
    private readonly Mock<IGitHubClientWrapper> _mockGitHubClient;
    private readonly GitHubService _service;

    public GitHubServiceTests()
    {
        var options = new DbContextOptionsBuilder<TreeAgentDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _db = new TreeAgentDbContext(options);
        _mockRunner = new Mock<ICommandRunner>();
        _mockConfig = new Mock<IConfiguration>();
        _mockGitHubClient = new Mock<IGitHubClientWrapper>();

        _mockConfig.Setup(c => c["GITHUB_TOKEN"]).Returns("test-token");

        _service = new GitHubService(_db, _mockRunner.Object, _mockConfig.Object, _mockGitHubClient.Object);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    private async Task<Project> CreateTestProject(bool withGitHub = true)
    {
        var project = new Project
        {
            Name = "Test Project",
            LocalPath = "/test/path",
            GitHubOwner = withGitHub ? "test-owner" : null,
            GitHubRepo = withGitHub ? "test-repo" : null,
            DefaultBranch = "main"
        };

        _db.Projects.Add(project);
        await _db.SaveChangesAsync();
        return project;
    }

    private async Task<Feature> CreateTestFeature(string projectId, string? branchName = "feature/test", int? prNumber = null)
    {
        var feature = new Feature
        {
            ProjectId = projectId,
            Title = "Test Feature",
            Description = "Test Description",
            BranchName = branchName,
            GitHubPRNumber = prNumber,
            Status = FeatureStatus.InDevelopment
        };

        _db.Features.Add(feature);
        await _db.SaveChangesAsync();
        return feature;
    }

    [Fact]
    public async Task IsConfigured_WithGitHubSettings_ReturnsTrue()
    {
        // Arrange
        var project = await CreateTestProject();

        // Act
        var result = await _service.IsConfiguredAsync(project.Id);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task IsConfigured_WithoutGitHubSettings_ReturnsFalse()
    {
        // Arrange
        var project = await CreateTestProject(withGitHub: false);

        // Act
        var result = await _service.IsConfiguredAsync(project.Id);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task IsConfigured_WithoutToken_ReturnsFalse()
    {
        // Arrange
        var project = await CreateTestProject();
        _mockConfig.Setup(c => c["GITHUB_TOKEN"]).Returns((string?)null);

        // Act
        var result = await _service.IsConfiguredAsync(project.Id);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task IsConfigured_ProjectNotFound_ReturnsFalse()
    {
        // Act
        var result = await _service.IsConfiguredAsync("nonexistent");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task GetOpenPullRequests_ReturnsOpenPRs()
    {
        // Arrange
        var project = await CreateTestProject();

        var mockPrs = new List<PullRequest>
        {
            CreateMockPullRequest(1, "PR 1", ItemState.Open, "feature/one"),
            CreateMockPullRequest(2, "PR 2", ItemState.Open, "feature/two")
        };

        _mockGitHubClient.Setup(c => c.GetPullRequestsAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.Is<PullRequestRequest>(r => r.State == ItemStateFilter.Open)))
            .ReturnsAsync(mockPrs);

        // Act
        var result = await _service.GetOpenPullRequestsAsync(project.Id);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal("PR 1", result[0].Title);
        Assert.Equal("PR 2", result[1].Title);
    }

    [Fact]
    public async Task GetOpenPullRequests_ProjectNotConfigured_ReturnsEmpty()
    {
        // Arrange
        var project = await CreateTestProject(withGitHub: false);

        // Act
        var result = await _service.GetOpenPullRequestsAsync(project.Id);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetClosedPullRequests_ReturnsClosedPRs()
    {
        // Arrange
        var project = await CreateTestProject();

        var mockPrs = new List<PullRequest>
        {
            CreateMockPullRequest(1, "PR 1", ItemState.Closed, "feature/one", merged: true)
        };

        _mockGitHubClient.Setup(c => c.GetPullRequestsAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.Is<PullRequestRequest>(r => r.State == ItemStateFilter.Closed)))
            .ReturnsAsync(mockPrs);

        // Act
        var result = await _service.GetClosedPullRequestsAsync(project.Id);

        // Assert
        Assert.Single(result);
        Assert.True(result[0].Merged);
    }

    [Fact]
    public async Task GetPullRequest_ReturnsSpecificPR()
    {
        // Arrange
        var project = await CreateTestProject();
        var mockPr = CreateMockPullRequest(42, "Specific PR", ItemState.Open, "feature/specific");

        _mockGitHubClient.Setup(c => c.GetPullRequestAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            42))
            .ReturnsAsync(mockPr);

        // Act
        var result = await _service.GetPullRequestAsync(project.Id, 42);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(42, result.Number);
        Assert.Equal("Specific PR", result.Title);
    }

    [Fact]
    public async Task GetPullRequest_NotFound_ReturnsNull()
    {
        // Arrange
        var project = await CreateTestProject();

        _mockGitHubClient.Setup(c => c.GetPullRequestAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            999))
            .ThrowsAsync(new NotFoundException("Not found", System.Net.HttpStatusCode.NotFound));

        // Act
        var result = await _service.GetPullRequestAsync(project.Id, 999);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task PushBranch_Success_ReturnsTrue()
    {
        // Arrange
        var project = await CreateTestProject();

        _mockRunner.Setup(r => r.RunAsync("git", "push -u origin \"feature/test\"", project.LocalPath))
            .ReturnsAsync(new CommandResult { Success = true });

        // Act
        var result = await _service.PushBranchAsync(project.Id, "feature/test");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task PushBranch_Failure_ReturnsFalse()
    {
        // Arrange
        var project = await CreateTestProject();

        _mockRunner.Setup(r => r.RunAsync("git", It.IsAny<string>(), project.LocalPath))
            .ReturnsAsync(new CommandResult { Success = false, Error = "Push failed" });

        // Act
        var result = await _service.PushBranchAsync(project.Id, "feature/test");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task CreatePullRequest_Success_CreatesPRAndUpdatesFeature()
    {
        // Arrange
        var project = await CreateTestProject();
        var feature = await CreateTestFeature(project.Id);

        _mockRunner.Setup(r => r.RunAsync("git", It.IsAny<string>(), project.LocalPath))
            .ReturnsAsync(new CommandResult { Success = true });

        var mockPr = CreateMockPullRequest(123, feature.Title, ItemState.Open, feature.BranchName!);
        _mockGitHubClient.Setup(c => c.CreatePullRequestAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.IsAny<NewPullRequest>()))
            .ReturnsAsync(mockPr);

        // Act
        var result = await _service.CreatePullRequestAsync(project.Id, feature.Id);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(123, result.Number);

        // Verify feature was updated
        var updatedFeature = await _db.Features.FindAsync(feature.Id);
        Assert.Equal(123, updatedFeature!.GitHubPRNumber);
        Assert.Equal(FeatureStatus.ReadyForReview, updatedFeature.Status);
    }

    [Fact]
    public async Task CreatePullRequest_PushFails_ReturnsNull()
    {
        // Arrange
        var project = await CreateTestProject();
        var feature = await CreateTestFeature(project.Id);

        _mockRunner.Setup(r => r.RunAsync("git", It.IsAny<string>(), project.LocalPath))
            .ReturnsAsync(new CommandResult { Success = false });

        // Act
        var result = await _service.CreatePullRequestAsync(project.Id, feature.Id);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task CreatePullRequest_NoBranch_ReturnsNull()
    {
        // Arrange
        var project = await CreateTestProject();
        var feature = await CreateTestFeature(project.Id, branchName: null);

        // Act
        var result = await _service.CreatePullRequestAsync(project.Id, feature.Id);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task SyncPullRequests_ImportsNewPRs()
    {
        // Arrange
        var project = await CreateTestProject();

        var openPrs = new List<PullRequest>
        {
            CreateMockPullRequest(1, "New Feature", ItemState.Open, "feature/new")
        };
        var closedPrs = new List<PullRequest>();

        _mockGitHubClient.Setup(c => c.GetPullRequestsAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.Is<PullRequestRequest>(r => r.State == ItemStateFilter.Open)))
            .ReturnsAsync(openPrs);

        _mockGitHubClient.Setup(c => c.GetPullRequestsAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.Is<PullRequestRequest>(r => r.State == ItemStateFilter.Closed)))
            .ReturnsAsync(closedPrs);

        // Act
        var result = await _service.SyncPullRequestsAsync(project.Id);

        // Assert
        Assert.Equal(1, result.Imported);
        Assert.Equal(0, result.Updated);
        Assert.Empty(result.Errors);

        var features = await _db.Features.Where(f => f.ProjectId == project.Id).ToListAsync();
        Assert.Single(features);
        Assert.Equal("New Feature", features[0].Title);
        Assert.Equal(1, features[0].GitHubPRNumber);
    }

    [Fact]
    public async Task SyncPullRequests_UpdatesExistingFeatures()
    {
        // Arrange
        var project = await CreateTestProject();
        var existingFeature = await CreateTestFeature(project.Id, "feature/existing", prNumber: 1);
        existingFeature.Status = FeatureStatus.ReadyForReview;
        await _db.SaveChangesAsync();

        var openPrs = new List<PullRequest>();
        var closedPrs = new List<PullRequest>
        {
            CreateMockPullRequest(1, "Existing Feature", ItemState.Closed, "feature/existing", merged: true)
        };

        _mockGitHubClient.Setup(c => c.GetPullRequestsAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.Is<PullRequestRequest>(r => r.State == ItemStateFilter.Open)))
            .ReturnsAsync(openPrs);

        _mockGitHubClient.Setup(c => c.GetPullRequestsAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.Is<PullRequestRequest>(r => r.State == ItemStateFilter.Closed)))
            .ReturnsAsync(closedPrs);

        // Act
        var result = await _service.SyncPullRequestsAsync(project.Id);

        // Assert
        Assert.Equal(0, result.Imported);
        Assert.Equal(1, result.Updated);

        var updatedFeature = await _db.Features.FindAsync(existingFeature.Id);
        Assert.Equal(FeatureStatus.Merged, updatedFeature!.Status);
    }

    [Fact]
    public async Task SyncPullRequests_ProjectNotConfigured_ReturnsError()
    {
        // Act
        var result = await _service.SyncPullRequestsAsync("nonexistent");

        // Assert
        Assert.Single(result.Errors);
        Assert.Contains("not found", result.Errors[0].ToLower());
    }

    [Fact]
    public async Task LinkPullRequest_Success_UpdatesFeature()
    {
        // Arrange
        var project = await CreateTestProject();
        var feature = await CreateTestFeature(project.Id);

        // Act
        var result = await _service.LinkPullRequestAsync(feature.Id, 42);

        // Assert
        Assert.True(result);
        var updatedFeature = await _db.Features.FindAsync(feature.Id);
        Assert.Equal(42, updatedFeature!.GitHubPRNumber);
    }

    [Fact]
    public async Task LinkPullRequest_FeatureNotFound_ReturnsFalse()
    {
        // Act
        var result = await _service.LinkPullRequestAsync("nonexistent", 42);

        // Assert
        Assert.False(result);
    }

    // Helper to create mock PullRequest objects
    private static PullRequest CreateMockPullRequest(int number, string title, ItemState state, string branchName, bool merged = false)
    {
        // Using reflection to create PullRequest since it has no public constructor
        var headRef = new GitReference(
            nodeId: "node1",
            url: "url",
            label: "label",
            @ref: branchName,
            sha: "sha",
            user: null,
            repository: null
        );

        return new PullRequest(
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
            createdAt: DateTimeOffset.UtcNow,
            updatedAt: DateTimeOffset.UtcNow,
            closedAt: state == ItemState.Closed ? DateTimeOffset.UtcNow : null,
            mergedAt: merged ? DateTimeOffset.UtcNow : null,
            head: headRef,
            @base: headRef,
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
}

/// <summary>
/// Integration tests that require real GitHub authentication.
/// These tests are marked with [Ignore] and should be run manually.
/// </summary>
public class GitHubServiceIntegrationTests : IDisposable
{
    private readonly TreeAgentDbContext _db;

    public GitHubServiceIntegrationTests()
    {
        var options = new DbContextOptionsBuilder<TreeAgentDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _db = new TreeAgentDbContext(options);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetOpenPullRequests_RealGitHub_ReturnsData()
    {
        // This test requires:
        // 1. GITHUB_TOKEN environment variable set
        // 2. A real GitHub repository configured

        // Skip if not configured
        var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (string.IsNullOrEmpty(token))
        {
            return; // Skip test
        }

        // Add a project pointing to a real repo
        var project = new Project
        {
            Name = "Test",
            LocalPath = ".",
            GitHubOwner = "octocat",  // Change to your test repo
            GitHubRepo = "Hello-World"
        };
        _db.Projects.Add(project);
        await _db.SaveChangesAsync();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GITHUB_TOKEN"] = token
            })
            .Build();

        var runner = new CommandRunner();
        var client = new GitHubClientWrapper();
        var service = new GitHubService(_db, runner, config, client);

        // Act
        var result = await service.GetOpenPullRequestsAsync(project.Id);

        // Assert - just verify it doesn't throw
        Assert.NotNull(result);
    }
}
