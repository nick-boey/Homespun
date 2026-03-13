using Homespun.Features.Commands;
using Homespun.Features.Projects;
using Homespun.Features.Search;
using Homespun.Features.Testing;
using Homespun.Shared.Models.Commands;
using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.Search;

[TestFixture]
public class ProjectFileServiceTests
{
    private MockDataStore _dataStore = null!;
    private Mock<ICommandRunner> _mockCommandRunner = null!;
    private Mock<ILogger<ProjectFileService>> _mockLogger = null!;
    private ProjectFileService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _dataStore = new MockDataStore();
        _mockCommandRunner = new Mock<ICommandRunner>();
        _mockLogger = new Mock<ILogger<ProjectFileService>>();

        _service = new ProjectFileService(
            _dataStore,
            _mockCommandRunner.Object,
            _mockLogger.Object);
    }

    [TearDown]
    public void TearDown()
    {
        _dataStore.Clear();
    }

    #region GetFilesAsync Tests

    [Test]
    public async Task GetFilesAsync_ValidProject_ReturnsFileList()
    {
        // Arrange
        var project = CreateTestProject();
        await _dataStore.AddProjectAsync(project);

        var gitOutput = "src/index.ts\nsrc/utils.ts\npackage.json";
        _mockCommandRunner.Setup(r => r.RunAsync("git", "ls-files", project.LocalPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = gitOutput });

        // Act
        var result = await _service.GetFilesAsync(project.Id);

        // Assert
        Assert.That(result.Files, Has.Count.EqualTo(3));
        Assert.That(result.Files, Does.Contain("src/index.ts"));
        Assert.That(result.Files, Does.Contain("src/utils.ts"));
        Assert.That(result.Files, Does.Contain("package.json"));
    }

    [Test]
    public async Task GetFilesAsync_ValidProject_ReturnsSortedFiles()
    {
        // Arrange
        var project = CreateTestProject();
        await _dataStore.AddProjectAsync(project);

        var gitOutput = "z-file.ts\na-file.ts\nm-file.ts";
        _mockCommandRunner.Setup(r => r.RunAsync("git", "ls-files", project.LocalPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = gitOutput });

        // Act
        var result = await _service.GetFilesAsync(project.Id);

        // Assert
        Assert.That(result.Files[0], Is.EqualTo("a-file.ts"));
        Assert.That(result.Files[1], Is.EqualTo("m-file.ts"));
        Assert.That(result.Files[2], Is.EqualTo("z-file.ts"));
    }

    [Test]
    public async Task GetFilesAsync_ValidProject_ReturnsConsistentHash()
    {
        // Arrange
        var project = CreateTestProject();
        await _dataStore.AddProjectAsync(project);

        var gitOutput = "src/index.ts\nsrc/utils.ts";
        _mockCommandRunner.Setup(r => r.RunAsync("git", "ls-files", project.LocalPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = gitOutput });

        // Act
        var result1 = await _service.GetFilesAsync(project.Id);
        var result2 = await _service.GetFilesAsync(project.Id);

        // Assert
        Assert.That(result1.Hash, Is.Not.Empty);
        Assert.That(result1.Hash, Is.EqualTo(result2.Hash));
    }

    [Test]
    public async Task GetFilesAsync_DifferentFiles_ReturnsDifferentHash()
    {
        // Arrange
        var project = CreateTestProject();
        await _dataStore.AddProjectAsync(project);

        _mockCommandRunner.SetupSequence(r => r.RunAsync("git", "ls-files", project.LocalPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "file1.ts" })
            .ReturnsAsync(new CommandResult { Success = true, Output = "file2.ts" });

        // Act
        var result1 = await _service.GetFilesAsync(project.Id);
        var result2 = await _service.GetFilesAsync(project.Id);

        // Assert
        Assert.That(result1.Hash, Is.Not.EqualTo(result2.Hash));
    }

    [Test]
    public async Task GetFilesAsync_SameFilesInDifferentOrder_ReturnsSameHash()
    {
        // Arrange - Files in different order should produce same hash after sorting
        var project1 = CreateTestProject("project1");
        var project2 = CreateTestProject("project2");
        await _dataStore.AddProjectAsync(project1);
        await _dataStore.AddProjectAsync(project2);

        _mockCommandRunner.Setup(r => r.RunAsync("git", "ls-files", project1.LocalPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "z.ts\na.ts" });
        _mockCommandRunner.Setup(r => r.RunAsync("git", "ls-files", project2.LocalPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "a.ts\nz.ts" });

        // Act
        var result1 = await _service.GetFilesAsync(project1.Id);
        var result2 = await _service.GetFilesAsync(project2.Id);

        // Assert
        Assert.That(result1.Hash, Is.EqualTo(result2.Hash));
    }

    [Test]
    public async Task GetFilesAsync_NonExistentProject_ThrowsException()
    {
        // Act & Assert
        var ex = Assert.ThrowsAsync<KeyNotFoundException>(
            async () => await _service.GetFilesAsync("nonexistent"));
        Assert.That(ex!.Message, Does.Contain("not found"));
    }

    [Test]
    public async Task GetFilesAsync_GitCommandFails_ThrowsException()
    {
        // Arrange
        var project = CreateTestProject();
        await _dataStore.AddProjectAsync(project);

        _mockCommandRunner.Setup(r => r.RunAsync("git", "ls-files", project.LocalPath))
            .ReturnsAsync(new CommandResult { Success = false, Error = "Not a git repository" });

        // Act & Assert
        var ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _service.GetFilesAsync(project.Id));
        Assert.That(ex!.Message, Does.Contain("Failed to list files"));
    }

    [Test]
    public async Task GetFilesAsync_EmptyRepository_ReturnsEmptyList()
    {
        // Arrange
        var project = CreateTestProject();
        await _dataStore.AddProjectAsync(project);

        _mockCommandRunner.Setup(r => r.RunAsync("git", "ls-files", project.LocalPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "" });

        // Act
        var result = await _service.GetFilesAsync(project.Id);

        // Assert
        Assert.That(result.Files, Is.Empty);
        Assert.That(result.Hash, Is.Not.Empty);
    }

    [Test]
    public async Task GetFilesAsync_FilesWithEmptyLines_IgnoresEmptyLines()
    {
        // Arrange
        var project = CreateTestProject();
        await _dataStore.AddProjectAsync(project);

        var gitOutput = "file1.ts\n\nfile2.ts\n\n";
        _mockCommandRunner.Setup(r => r.RunAsync("git", "ls-files", project.LocalPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = gitOutput });

        // Act
        var result = await _service.GetFilesAsync(project.Id);

        // Assert
        Assert.That(result.Files, Has.Count.EqualTo(2));
    }

    #endregion

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
}
