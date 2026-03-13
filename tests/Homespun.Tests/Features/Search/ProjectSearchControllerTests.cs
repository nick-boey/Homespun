using Homespun.Features.Search;
using Homespun.Features.Search.Controllers;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Homespun.Tests.Features.Search;

[TestFixture]
public class ProjectSearchControllerTests
{
    private Mock<IProjectFileService> _mockFileService = null!;
    private Mock<ISearchablePrService> _mockPrService = null!;
    private ProjectSearchController _controller = null!;

    [SetUp]
    public void SetUp()
    {
        _mockFileService = new Mock<IProjectFileService>();
        _mockPrService = new Mock<ISearchablePrService>();
        _controller = new ProjectSearchController(_mockFileService.Object, _mockPrService.Object);
    }

    #region GetFiles Tests

    [Test]
    public async Task GetFiles_ValidProject_ReturnsOkWithFiles()
    {
        // Arrange
        var projectId = "test-project";
        var files = new List<string> { "file1.txt", "file2.txt" };
        var hash = "abc123";

        _mockFileService.Setup(s => s.GetFilesAsync(projectId))
            .ReturnsAsync(new FileListResult(files.AsReadOnly(), hash));

        // Act
        var result = await _controller.GetFiles(projectId, null);

        // Assert
        var okResult = result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        Assert.That(okResult!.StatusCode, Is.EqualTo(200));

        var response = okResult.Value as FileListResponse;
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Files, Has.Count.EqualTo(2));
        Assert.That(response.Hash, Is.EqualTo(hash));
    }

    [Test]
    public async Task GetFiles_WithMatchingHash_ReturnsNotModified()
    {
        // Arrange
        var projectId = "test-project";
        var hash = "abc123";

        _mockFileService.Setup(s => s.GetFilesAsync(projectId))
            .ReturnsAsync(new FileListResult(new List<string>().AsReadOnly(), hash));

        // Act
        var result = await _controller.GetFiles(projectId, hash);

        // Assert
        var statusResult = result as StatusCodeResult;
        Assert.That(statusResult, Is.Not.Null);
        Assert.That(statusResult!.StatusCode, Is.EqualTo(304));
    }

    [Test]
    public async Task GetFiles_WithNonMatchingHash_ReturnsOkWithFiles()
    {
        // Arrange
        var projectId = "test-project";
        var serverHash = "abc123";
        var clientHash = "old-hash";

        _mockFileService.Setup(s => s.GetFilesAsync(projectId))
            .ReturnsAsync(new FileListResult(new List<string> { "file.txt" }.AsReadOnly(), serverHash));

        // Act
        var result = await _controller.GetFiles(projectId, clientHash);

        // Assert
        var okResult = result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        Assert.That(okResult!.StatusCode, Is.EqualTo(200));
    }

    [Test]
    public async Task GetFiles_NonExistentProject_ReturnsNotFound()
    {
        // Arrange
        var projectId = "nonexistent";

        _mockFileService.Setup(s => s.GetFilesAsync(projectId))
            .ThrowsAsync(new KeyNotFoundException());

        // Act
        var result = await _controller.GetFiles(projectId, null);

        // Assert
        var notFoundResult = result as NotFoundResult;
        Assert.That(notFoundResult, Is.Not.Null);
        Assert.That(notFoundResult!.StatusCode, Is.EqualTo(404));
    }

    #endregion

    #region GetPrs Tests

    [Test]
    public async Task GetPrs_ValidProject_ReturnsOkWithPrs()
    {
        // Arrange
        var projectId = "test-project";
        var prs = new List<SearchablePr>
        {
            new(101, "Feature A", "feature/a"),
            new(102, "Fix B", "fix/b")
        };
        var hash = "def456";

        _mockPrService.Setup(s => s.GetPrsAsync(projectId))
            .ReturnsAsync(new PrListResult(prs.AsReadOnly(), hash));

        // Act
        var result = await _controller.GetPrs(projectId, null);

        // Assert
        var okResult = result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        Assert.That(okResult!.StatusCode, Is.EqualTo(200));

        var response = okResult.Value as PrListResponse;
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Prs, Has.Count.EqualTo(2));
        Assert.That(response.Hash, Is.EqualTo(hash));
    }

    [Test]
    public async Task GetPrs_WithMatchingHash_ReturnsNotModified()
    {
        // Arrange
        var projectId = "test-project";
        var hash = "def456";

        _mockPrService.Setup(s => s.GetPrsAsync(projectId))
            .ReturnsAsync(new PrListResult(new List<SearchablePr>().AsReadOnly(), hash));

        // Act
        var result = await _controller.GetPrs(projectId, hash);

        // Assert
        var statusResult = result as StatusCodeResult;
        Assert.That(statusResult, Is.Not.Null);
        Assert.That(statusResult!.StatusCode, Is.EqualTo(304));
    }

    [Test]
    public async Task GetPrs_NonExistentProject_ReturnsNotFound()
    {
        // Arrange
        var projectId = "nonexistent";

        _mockPrService.Setup(s => s.GetPrsAsync(projectId))
            .ThrowsAsync(new KeyNotFoundException());

        // Act
        var result = await _controller.GetPrs(projectId, null);

        // Assert
        var notFoundResult = result as NotFoundResult;
        Assert.That(notFoundResult, Is.Not.Null);
        Assert.That(notFoundResult!.StatusCode, Is.EqualTo(404));
    }

    [Test]
    public async Task GetPrs_ResponseMapsSearchablePrToResponse()
    {
        // Arrange
        var projectId = "test-project";
        var prs = new List<SearchablePr>
        {
            new(123, "Test PR", "test-branch")
        };
        var hash = "xyz789";

        _mockPrService.Setup(s => s.GetPrsAsync(projectId))
            .ReturnsAsync(new PrListResult(prs.AsReadOnly(), hash));

        // Act
        var result = await _controller.GetPrs(projectId, null);

        // Assert
        var okResult = result as OkObjectResult;
        var response = okResult!.Value as PrListResponse;
        Assert.That(response!.Prs[0].Number, Is.EqualTo(123));
        Assert.That(response.Prs[0].Title, Is.EqualTo("Test PR"));
        Assert.That(response.Prs[0].BranchName, Is.EqualTo("test-branch"));
    }

    #endregion
}
