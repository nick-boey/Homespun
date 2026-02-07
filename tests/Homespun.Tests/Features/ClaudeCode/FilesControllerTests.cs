using Homespun.AgentWorker.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Homespun.Tests.Features.ClaudeCode;

/// <summary>
/// Tests for the AgentWorker FilesController.
/// Verifies path security, file reading, and plan file listing.
/// </summary>
[TestFixture]
public class FilesControllerTests
{
    private FilesController _controller = null!;
    private Mock<ILogger<FilesController>> _loggerMock = null!;
    private string _testPlansDir = null!;

    [SetUp]
    public void SetUp()
    {
        _loggerMock = new Mock<ILogger<FilesController>>();
        _controller = new FilesController(_loggerMock.Object);

        // Create a temp plans directory for testing
        _testPlansDir = Path.Combine(Path.GetTempPath(), $"plans-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testPlansDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testPlansDir))
        {
            Directory.Delete(_testPlansDir, recursive: true);
        }
    }

    [Test]
    public async Task ReadFile_WithEmptyFilePath_ReturnsBadRequest()
    {
        // Arrange
        var request = new FileReadRequest { FilePath = "" };

        // Act
        var result = await _controller.ReadFile(request);

        // Assert
        Assert.That(result, Is.TypeOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task ReadFile_WithDisallowedPath_ReturnsForbid()
    {
        // Arrange - try to read outside allowed directories
        var request = new FileReadRequest { FilePath = "/etc/passwd" };

        // Act
        var result = await _controller.ReadFile(request);

        // Assert
        Assert.That(result, Is.TypeOf<ForbidResult>());
    }

    [Test]
    public async Task ReadFile_WithDirectoryTraversal_ReturnsForbid()
    {
        // Arrange - try directory traversal attack
        var request = new FileReadRequest
        {
            FilePath = "/home/homespun/.claude/plans/../../../etc/passwd"
        };

        // Act
        var result = await _controller.ReadFile(request);

        // Assert
        Assert.That(result, Is.TypeOf<ForbidResult>());
    }

    [Test]
    public async Task ReadFile_WithNonExistentAllowedPath_ReturnsNotFound()
    {
        // Arrange - valid path pattern but file doesn't exist
        var request = new FileReadRequest
        {
            FilePath = "/home/homespun/.claude/plans/nonexistent-plan.md"
        };

        // Act
        var result = await _controller.ReadFile(request);

        // Assert
        Assert.That(result, Is.TypeOf<NotFoundObjectResult>());
    }

    [TestCase("/tmp/test.md")]
    [TestCase("/var/log/syslog")]
    [TestCase("/home/homespun/.ssh/id_rsa")]
    [TestCase("/home/homespun/.claude/settings.json")]
    [TestCase("/data/sessions/index.json")]
    public async Task ReadFile_WithVariousDisallowedPaths_ReturnsForbid(string path)
    {
        // Arrange
        var request = new FileReadRequest { FilePath = path };

        // Act
        var result = await _controller.ReadFile(request);

        // Assert
        Assert.That(result, Is.TypeOf<ForbidResult>(),
            $"Path '{path}' should be forbidden");
    }
}
