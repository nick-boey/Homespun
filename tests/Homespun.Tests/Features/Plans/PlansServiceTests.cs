using System.Security;
using Homespun.Features.Plans;
using Homespun.Shared.Models.Plans;
using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.Plans;

[TestFixture]
public class PlansServiceTests
{
    private string _tempDir = null!;
    private Mock<ILogger<PlansService>> _mockLogger = null!;
    private PlansService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"plans-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _mockLogger = new Mock<ILogger<PlansService>>();
        _service = new PlansService(_mockLogger.Object);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    #region ListPlanFilesAsync Tests

    [Test]
    public async Task ListPlanFilesAsync_WithPlansDirectory_ReturnsAllMarkdownFiles()
    {
        // Arrange
        var plansDir = Path.Combine(_tempDir, ".claude", "plans");
        Directory.CreateDirectory(plansDir);
        await File.WriteAllTextAsync(Path.Combine(plansDir, "plan1.md"), "# Plan 1\nContent line 1\nContent line 2");
        await File.WriteAllTextAsync(Path.Combine(plansDir, "plan2.md"), "# Plan 2\nSome content");

        // Act
        var result = await _service.ListPlanFilesAsync(_tempDir);

        // Assert
        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result.Select(p => p.FileName), Is.EquivalentTo(new[] { "plan1.md", "plan2.md" }));
    }

    [Test]
    public async Task ListPlanFilesAsync_NoPlansDirectory_ReturnsEmptyList()
    {
        // Act
        var result = await _service.ListPlanFilesAsync(_tempDir);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task ListPlanFilesAsync_OrdersByLastModifiedDescending()
    {
        // Arrange
        var plansDir = Path.Combine(_tempDir, ".claude", "plans");
        Directory.CreateDirectory(plansDir);

        var oldFile = Path.Combine(plansDir, "old-plan.md");
        var newFile = Path.Combine(plansDir, "new-plan.md");

        await File.WriteAllTextAsync(oldFile, "# Old Plan");
        File.SetLastWriteTime(oldFile, DateTime.Now.AddHours(-1));

        await File.WriteAllTextAsync(newFile, "# New Plan");
        File.SetLastWriteTime(newFile, DateTime.Now);

        // Act
        var result = await _service.ListPlanFilesAsync(_tempDir);

        // Assert
        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result[0].FileName, Is.EqualTo("new-plan.md"));
        Assert.That(result[1].FileName, Is.EqualTo("old-plan.md"));
    }

    [Test]
    public async Task ListPlanFilesAsync_IncludesPreview()
    {
        // Arrange
        var plansDir = Path.Combine(_tempDir, ".claude", "plans");
        Directory.CreateDirectory(plansDir);
        await File.WriteAllTextAsync(Path.Combine(plansDir, "plan.md"), "Line 1\nLine 2\nLine 3\nLine 4\nLine 5");

        // Act
        var result = await _service.ListPlanFilesAsync(_tempDir);

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Preview, Is.EqualTo("Line 1\nLine 2\nLine 3"));
    }

    [Test]
    public async Task ListPlanFilesAsync_IgnoresNonMarkdownFiles()
    {
        // Arrange
        var plansDir = Path.Combine(_tempDir, ".claude", "plans");
        Directory.CreateDirectory(plansDir);
        await File.WriteAllTextAsync(Path.Combine(plansDir, "plan.md"), "# Plan");
        await File.WriteAllTextAsync(Path.Combine(plansDir, "notes.txt"), "Some notes");
        await File.WriteAllTextAsync(Path.Combine(plansDir, "data.json"), "{}");

        // Act
        var result = await _service.ListPlanFilesAsync(_tempDir);

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].FileName, Is.EqualTo("plan.md"));
    }

    [Test]
    public async Task ListPlanFilesAsync_IncludesFileSizeBytes()
    {
        // Arrange
        var plansDir = Path.Combine(_tempDir, ".claude", "plans");
        Directory.CreateDirectory(plansDir);
        var content = "# Plan Content Here";
        await File.WriteAllTextAsync(Path.Combine(plansDir, "plan.md"), content);

        // Act
        var result = await _service.ListPlanFilesAsync(_tempDir);

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].FileSizeBytes, Is.GreaterThan(0));
    }

    #endregion

    #region GetPlanContentAsync Tests

    [Test]
    public async Task GetPlanContentAsync_ValidFile_ReturnsContent()
    {
        // Arrange
        var plansDir = Path.Combine(_tempDir, ".claude", "plans");
        Directory.CreateDirectory(plansDir);
        var expectedContent = "# My Plan\n\nThis is the plan content.";
        await File.WriteAllTextAsync(Path.Combine(plansDir, "plan.md"), expectedContent);

        // Act
        var result = await _service.GetPlanContentAsync(_tempDir, "plan.md");

        // Assert
        Assert.That(result, Is.EqualTo(expectedContent));
    }

    [Test]
    public async Task GetPlanContentAsync_NonExistentFile_ReturnsNull()
    {
        // Arrange
        var plansDir = Path.Combine(_tempDir, ".claude", "plans");
        Directory.CreateDirectory(plansDir);

        // Act
        var result = await _service.GetPlanContentAsync(_tempDir, "nonexistent.md");

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void GetPlanContentAsync_InvalidPath_ThrowsSecurityException()
    {
        // Arrange
        var plansDir = Path.Combine(_tempDir, ".claude", "plans");
        Directory.CreateDirectory(plansDir);

        // Act & Assert
        Assert.ThrowsAsync<SecurityException>(async () =>
            await _service.GetPlanContentAsync(_tempDir, "../../../etc/passwd"));
    }

    [Test]
    public void GetPlanContentAsync_PathWithTraversal_ThrowsSecurityException()
    {
        // Arrange
        var plansDir = Path.Combine(_tempDir, ".claude", "plans");
        Directory.CreateDirectory(plansDir);

        // Act & Assert
        Assert.ThrowsAsync<SecurityException>(async () =>
            await _service.GetPlanContentAsync(_tempDir, "..\\..\\sensitive.md"));
    }

    [Test]
    public void GetPlanContentAsync_PathWithSlash_ThrowsSecurityException()
    {
        // Arrange
        var plansDir = Path.Combine(_tempDir, ".claude", "plans");
        Directory.CreateDirectory(plansDir);

        // Act & Assert
        Assert.ThrowsAsync<SecurityException>(async () =>
            await _service.GetPlanContentAsync(_tempDir, "subdir/plan.md"));
    }

    #endregion
}
