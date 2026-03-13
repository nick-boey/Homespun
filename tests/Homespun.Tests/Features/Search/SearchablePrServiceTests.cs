using Homespun.Features.Projects;
using Homespun.Features.Search;
using Homespun.Features.Testing;
using Homespun.Shared.Models.PullRequests;
using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.Search;

[TestFixture]
public class SearchablePrServiceTests
{
    private MockDataStore _dataStore = null!;
    private Mock<IPrDataProvider> _mockPrDataProvider = null!;
    private Mock<ILogger<SearchablePrService>> _mockLogger = null!;
    private SearchablePrService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _dataStore = new MockDataStore();
        _mockPrDataProvider = new Mock<IPrDataProvider>();
        _mockLogger = new Mock<ILogger<SearchablePrService>>();

        _service = new SearchablePrService(
            _dataStore,
            _mockPrDataProvider.Object,
            _mockLogger.Object);
    }

    [TearDown]
    public void TearDown()
    {
        _dataStore.Clear();
    }

    #region GetPrsAsync Tests

    [Test]
    public async Task GetPrsAsync_ValidProject_ReturnsPrList()
    {
        // Arrange
        var project = CreateTestProject();
        await _dataStore.AddProjectAsync(project);

        var openPrs = new List<PullRequestWithStatus>
        {
            CreateOpenPr(101, "Add feature X", "feature/x"),
            CreateOpenPr(102, "Fix bug Y", "fix/y")
        };
        var mergedPrs = new List<PullRequestWithTime>
        {
            CreateMergedPr(50, "Initial commit", "main-setup")
        };

        _mockPrDataProvider.Setup(s => s.GetOpenPullRequestsWithStatusAsync(project.Id))
            .ReturnsAsync(openPrs);
        _mockPrDataProvider.Setup(s => s.GetMergedPullRequestsWithTimeAsync(project.Id))
            .ReturnsAsync(mergedPrs);

        // Act
        var result = await _service.GetPrsAsync(project.Id);

        // Assert
        Assert.That(result.Prs, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task GetPrsAsync_ValidProject_ReturnsCorrectPrData()
    {
        // Arrange
        var project = CreateTestProject();
        await _dataStore.AddProjectAsync(project);

        var openPrs = new List<PullRequestWithStatus>
        {
            CreateOpenPr(123, "Feature title", "feature/test")
        };

        _mockPrDataProvider.Setup(s => s.GetOpenPullRequestsWithStatusAsync(project.Id))
            .ReturnsAsync(openPrs);
        _mockPrDataProvider.Setup(s => s.GetMergedPullRequestsWithTimeAsync(project.Id))
            .ReturnsAsync(new List<PullRequestWithTime>());

        // Act
        var result = await _service.GetPrsAsync(project.Id);

        // Assert
        Assert.That(result.Prs, Has.Count.EqualTo(1));
        Assert.That(result.Prs[0].Number, Is.EqualTo(123));
        Assert.That(result.Prs[0].Title, Is.EqualTo("Feature title"));
        Assert.That(result.Prs[0].BranchName, Is.EqualTo("feature/test"));
    }

    [Test]
    public async Task GetPrsAsync_ValidProject_ReturnsConsistentHash()
    {
        // Arrange
        var project = CreateTestProject();
        await _dataStore.AddProjectAsync(project);

        var openPrs = new List<PullRequestWithStatus>
        {
            CreateOpenPr(101, "Add feature", "feature/test")
        };

        _mockPrDataProvider.Setup(s => s.GetOpenPullRequestsWithStatusAsync(project.Id))
            .ReturnsAsync(openPrs);
        _mockPrDataProvider.Setup(s => s.GetMergedPullRequestsWithTimeAsync(project.Id))
            .ReturnsAsync(new List<PullRequestWithTime>());

        // Act
        var result1 = await _service.GetPrsAsync(project.Id);
        var result2 = await _service.GetPrsAsync(project.Id);

        // Assert
        Assert.That(result1.Hash, Is.Not.Empty);
        Assert.That(result1.Hash, Is.EqualTo(result2.Hash));
    }

    [Test]
    public async Task GetPrsAsync_DifferentPrs_ReturnsDifferentHash()
    {
        // Arrange
        var project = CreateTestProject();
        await _dataStore.AddProjectAsync(project);

        _mockPrDataProvider.SetupSequence(s => s.GetOpenPullRequestsWithStatusAsync(project.Id))
            .ReturnsAsync(new List<PullRequestWithStatus> { CreateOpenPr(1, "PR 1", "branch-1") })
            .ReturnsAsync(new List<PullRequestWithStatus> { CreateOpenPr(2, "PR 2", "branch-2") });
        _mockPrDataProvider.Setup(s => s.GetMergedPullRequestsWithTimeAsync(project.Id))
            .ReturnsAsync(new List<PullRequestWithTime>());

        // Act
        var result1 = await _service.GetPrsAsync(project.Id);
        var result2 = await _service.GetPrsAsync(project.Id);

        // Assert
        Assert.That(result1.Hash, Is.Not.EqualTo(result2.Hash));
    }

    [Test]
    public async Task GetPrsAsync_NonExistentProject_ThrowsException()
    {
        // Act & Assert
        var ex = Assert.ThrowsAsync<KeyNotFoundException>(
            async () => await _service.GetPrsAsync("nonexistent"));
        Assert.That(ex!.Message, Does.Contain("not found"));
    }

    [Test]
    public async Task GetPrsAsync_NoPrs_ReturnsEmptyListWithHash()
    {
        // Arrange
        var project = CreateTestProject();
        await _dataStore.AddProjectAsync(project);

        _mockPrDataProvider.Setup(s => s.GetOpenPullRequestsWithStatusAsync(project.Id))
            .ReturnsAsync(new List<PullRequestWithStatus>());
        _mockPrDataProvider.Setup(s => s.GetMergedPullRequestsWithTimeAsync(project.Id))
            .ReturnsAsync(new List<PullRequestWithTime>());

        // Act
        var result = await _service.GetPrsAsync(project.Id);

        // Assert
        Assert.That(result.Prs, Is.Empty);
        Assert.That(result.Hash, Is.Not.Empty);
    }

    [Test]
    public async Task GetPrsAsync_SortsByPrNumber()
    {
        // Arrange
        var project = CreateTestProject();
        await _dataStore.AddProjectAsync(project);

        var openPrs = new List<PullRequestWithStatus>
        {
            CreateOpenPr(300, "Third PR", "branch-3"),
            CreateOpenPr(100, "First PR", "branch-1"),
            CreateOpenPr(200, "Second PR", "branch-2")
        };

        _mockPrDataProvider.Setup(s => s.GetOpenPullRequestsWithStatusAsync(project.Id))
            .ReturnsAsync(openPrs);
        _mockPrDataProvider.Setup(s => s.GetMergedPullRequestsWithTimeAsync(project.Id))
            .ReturnsAsync(new List<PullRequestWithTime>());

        // Act
        var result = await _service.GetPrsAsync(project.Id);

        // Assert - should be sorted by number ascending for consistency
        Assert.That(result.Prs[0].Number, Is.EqualTo(100));
        Assert.That(result.Prs[1].Number, Is.EqualTo(200));
        Assert.That(result.Prs[2].Number, Is.EqualTo(300));
    }

    [Test]
    public async Task GetPrsAsync_DeduplicatesSamePrInOpenAndMerged()
    {
        // Arrange - PR appears in both open and merged (edge case during sync)
        var project = CreateTestProject();
        await _dataStore.AddProjectAsync(project);

        var openPrs = new List<PullRequestWithStatus>
        {
            CreateOpenPr(100, "Feature", "branch")
        };
        var mergedPrs = new List<PullRequestWithTime>
        {
            CreateMergedPr(100, "Feature", "branch")
        };

        _mockPrDataProvider.Setup(s => s.GetOpenPullRequestsWithStatusAsync(project.Id))
            .ReturnsAsync(openPrs);
        _mockPrDataProvider.Setup(s => s.GetMergedPullRequestsWithTimeAsync(project.Id))
            .ReturnsAsync(mergedPrs);

        // Act
        var result = await _service.GetPrsAsync(project.Id);

        // Assert - should deduplicate by PR number
        Assert.That(result.Prs, Has.Count.EqualTo(1));
        Assert.That(result.Prs[0].Number, Is.EqualTo(100));
    }

    #endregion

    #region Helpers

    private Project CreateTestProject(string name = "test-repo")
    {
        return new Project
        {
            Name = name,
            LocalPath = $"/path/to/{name}",
            GitHubOwner = "owner",
            GitHubRepo = name,
            DefaultBranch = "main"
        };
    }

    private static PullRequestWithStatus CreateOpenPr(int number, string title, string branchName)
    {
        return new PullRequestWithStatus(
            new PullRequestInfo
            {
                Number = number,
                Title = title,
                BranchName = branchName,
                Status = PullRequestStatus.InProgress,
                CreatedAt = DateTime.UtcNow
            },
            PullRequestStatus.InProgress,
            1);
    }

    private static PullRequestWithTime CreateMergedPr(int number, string title, string branchName)
    {
        return new PullRequestWithTime(
            new PullRequestInfo
            {
                Number = number,
                Title = title,
                BranchName = branchName,
                Status = PullRequestStatus.Merged,
                CreatedAt = DateTime.UtcNow,
                MergedAt = DateTime.UtcNow
            },
            -1);
    }

    #endregion
}
