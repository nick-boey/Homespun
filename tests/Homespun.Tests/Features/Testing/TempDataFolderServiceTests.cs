using Homespun.Features.Testing.Services;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Moq;

namespace Homespun.Tests.Features.Testing;

[TestFixture]
public class TempDataFolderServiceTests
{
    private Mock<ILogger<TempDataFolderService>> _loggerMock = null!;

    [SetUp]
    public void SetUp()
    {
        _loggerMock = new Mock<ILogger<TempDataFolderService>>();
    }

    [Test]
    public void Constructor_CreatesRootDirectory()
    {
        // Act
        using var service = new TempDataFolderService(_loggerMock.Object);

        // Assert
        Assert.That(Directory.Exists(service.RootPath), Is.True);
    }

    [Test]
    public void RootPath_ContainsHomespunMockPrefix()
    {
        // Act
        using var service = new TempDataFolderService(_loggerMock.Object);

        // Assert
        Assert.That(service.RootPath, Does.Contain("homespun-mock-"));
    }

    [Test]
    public void DataFilePath_ReturnsExpectedPath()
    {
        // Act
        using var service = new TempDataFolderService(_loggerMock.Object);

        // Assert
        Assert.That(service.DataFilePath, Is.EqualTo(Path.Combine(service.RootPath, "homespun-data.json")));
    }

    [Test]
    public void SessionsPath_ReturnsExpectedPath()
    {
        // Act
        using var service = new TempDataFolderService(_loggerMock.Object);

        // Assert
        Assert.That(service.SessionsPath, Is.EqualTo(Path.Combine(service.RootPath, "sessions")));
    }

    [Test]
    public void SessionsPath_DirectoryExists()
    {
        // Act
        using var service = new TempDataFolderService(_loggerMock.Object);

        // Assert
        Assert.That(Directory.Exists(service.SessionsPath), Is.True);
    }

    [Test]
    public void GetProjectPath_ReturnsExpectedPath()
    {
        // Arrange
        using var service = new TempDataFolderService(_loggerMock.Object);

        // Act
        var projectPath = service.GetProjectPath("test-project");

        // Assert
        Assert.That(projectPath, Is.EqualTo(Path.Combine(service.RootPath, "projects", "test-project")));
    }

    [Test]
    public void EnsureDirectoriesExist_CreatesProjectsDirectory()
    {
        // Act
        using var service = new TempDataFolderService(_loggerMock.Object);

        // Assert
        var projectsPath = Path.Combine(service.RootPath, "projects");
        Assert.That(Directory.Exists(projectsPath), Is.True);
    }

    [Test]
    public void Dispose_RemovesRootDirectory()
    {
        // Arrange
        var service = new TempDataFolderService(_loggerMock.Object);
        var rootPath = service.RootPath;

        // Act
        service.Dispose();

        // Assert
        Assert.That(Directory.Exists(rootPath), Is.False);
    }

    [Test]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        // Arrange
        var service = new TempDataFolderService(_loggerMock.Object);
        var rootPath = service.RootPath;

        // Act & Assert - should not throw
        service.Dispose();
        service.Dispose();

        Assert.That(Directory.Exists(rootPath), Is.False);
    }
}
