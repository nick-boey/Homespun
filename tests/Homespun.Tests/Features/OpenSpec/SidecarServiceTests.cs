using Homespun.Features.OpenSpec.Services;
using Homespun.Shared.Models.OpenSpec;
using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.OpenSpec;

[TestFixture]
public class SidecarServiceTests
{
    private string _tempDir = null!;
    private Mock<ILogger<SidecarService>> _mockLogger = null!;
    private SidecarService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"sidecar-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _mockLogger = new Mock<ILogger<SidecarService>>();
        _service = new SidecarService(_mockLogger.Object);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Test]
    public async Task ReadSidecarAsync_ExistingSidecar_ReturnsParsedModel()
    {
        // Arrange
        var path = Path.Combine(_tempDir, ".homespun.yaml");
        await File.WriteAllTextAsync(path, "fleeceId: abc123\ncreatedBy: agent\n");

        // Act
        var result = await _service.ReadSidecarAsync(_tempDir);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.FleeceId, Is.EqualTo("abc123"));
        Assert.That(result.CreatedBy, Is.EqualTo("agent"));
    }

    [Test]
    public async Task ReadSidecarAsync_MissingFile_ReturnsNull()
    {
        // Act
        var result = await _service.ReadSidecarAsync(_tempDir);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task ReadSidecarAsync_MissingDirectory_ReturnsNull()
    {
        // Act
        var result = await _service.ReadSidecarAsync(Path.Combine(_tempDir, "does-not-exist"));

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task ReadSidecarAsync_MalformedYaml_ReturnsNull()
    {
        // Arrange - unclosed bracket is invalid YAML
        var path = Path.Combine(_tempDir, ".homespun.yaml");
        await File.WriteAllTextAsync(path, "fleeceId: [abc\ncreatedBy: agent\n");

        // Act
        var result = await _service.ReadSidecarAsync(_tempDir);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task ReadSidecarAsync_MissingRequiredField_ReturnsNull()
    {
        // Arrange - missing createdBy
        var path = Path.Combine(_tempDir, ".homespun.yaml");
        await File.WriteAllTextAsync(path, "fleeceId: abc123\n");

        // Act
        var result = await _service.ReadSidecarAsync(_tempDir);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task ReadSidecarAsync_ExtraFields_AreIgnored()
    {
        // Arrange
        var path = Path.Combine(_tempDir, ".homespun.yaml");
        await File.WriteAllTextAsync(path, "fleeceId: abc123\ncreatedBy: server\nfutureField: value\n");

        // Act
        var result = await _service.ReadSidecarAsync(_tempDir);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.FleeceId, Is.EqualTo("abc123"));
        Assert.That(result.CreatedBy, Is.EqualTo("server"));
    }

    [Test]
    public async Task WriteSidecarAsync_WritesCamelCaseYaml()
    {
        // Arrange
        var sidecar = new ChangeSidecar { FleeceId = "xyz789", CreatedBy = "server" };

        // Act
        await _service.WriteSidecarAsync(_tempDir, sidecar);

        // Assert
        var path = Path.Combine(_tempDir, ".homespun.yaml");
        Assert.That(File.Exists(path), Is.True);

        var contents = await File.ReadAllTextAsync(path);
        Assert.That(contents, Does.Contain("fleeceId: xyz789"));
        Assert.That(contents, Does.Contain("createdBy: server"));
    }

    [Test]
    public async Task WriteSidecarAsync_RoundTripsThroughRead()
    {
        // Arrange
        var original = new ChangeSidecar { FleeceId = "round-trip-id", CreatedBy = "agent" };

        // Act
        await _service.WriteSidecarAsync(_tempDir, original);
        var readBack = await _service.ReadSidecarAsync(_tempDir);

        // Assert
        Assert.That(readBack, Is.Not.Null);
        Assert.That(readBack!.FleeceId, Is.EqualTo(original.FleeceId));
        Assert.That(readBack.CreatedBy, Is.EqualTo(original.CreatedBy));
    }

    [Test]
    public async Task WriteSidecarAsync_OverwritesExistingFile()
    {
        // Arrange
        var first = new ChangeSidecar { FleeceId = "first", CreatedBy = "server" };
        var second = new ChangeSidecar { FleeceId = "second", CreatedBy = "agent" };

        // Act
        await _service.WriteSidecarAsync(_tempDir, first);
        await _service.WriteSidecarAsync(_tempDir, second);

        var result = await _service.ReadSidecarAsync(_tempDir);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.FleeceId, Is.EqualTo("second"));
        Assert.That(result.CreatedBy, Is.EqualTo("agent"));
    }

    [Test]
    public void WriteSidecarAsync_MissingDirectory_Throws()
    {
        // Arrange
        var missing = Path.Combine(_tempDir, "does-not-exist");
        var sidecar = new ChangeSidecar { FleeceId = "id", CreatedBy = "agent" };

        // Act + Assert
        Assert.ThrowsAsync<DirectoryNotFoundException>(async () =>
            await _service.WriteSidecarAsync(missing, sidecar));
    }
}
