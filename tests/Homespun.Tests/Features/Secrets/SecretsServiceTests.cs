using Homespun.Features.Projects;
using Homespun.Features.Secrets;
using Homespun.Features.Testing;
using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.Secrets;

[TestFixture]
public class SecretsServiceTests
{
    private MockDataStore _dataStore = null!;
    private Mock<IProjectService> _mockProjectService = null!;
    private Mock<ILogger<SecretsService>> _mockLogger = null!;
    private SecretsService _service = null!;
    private string _testProjectPath = null!;
    private string _testSecretsFilePath = null!;

    [SetUp]
    public void SetUp()
    {
        _dataStore = new MockDataStore();
        _mockProjectService = new Mock<IProjectService>();
        _mockLogger = new Mock<ILogger<SecretsService>>();

        _testProjectPath = Path.Combine(Path.GetTempPath(), "homespun-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_testProjectPath);

        // LocalPath is the branch folder (e.g., /projects/myproject/main), secrets.env is at project root
        var localPath = Path.Combine(_testProjectPath, "main");
        Directory.CreateDirectory(localPath);
        _testSecretsFilePath = Path.Combine(_testProjectPath, "secrets.env");

        _service = new SecretsService(_mockProjectService.Object, _mockLogger.Object);
    }

    [TearDown]
    public void TearDown()
    {
        _dataStore.Clear();
        if (Directory.Exists(_testProjectPath))
        {
            Directory.Delete(_testProjectPath, true);
        }
    }

    #region GetSecretsAsync Tests

    [Test]
    public async Task GetSecretsAsync_ProjectNotFound_ReturnsEmpty()
    {
        // Arrange
        _mockProjectService.Setup(p => p.GetByIdAsync("nonexistent")).ReturnsAsync((Project?)null);

        // Act
        var result = await _service.GetSecretsAsync("nonexistent");

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetSecretsAsync_NoSecretsFile_ReturnsEmpty()
    {
        // Arrange
        var project = CreateTestProject();
        _mockProjectService.Setup(p => p.GetByIdAsync(project.Id)).ReturnsAsync(project);

        // Act
        var result = await _service.GetSecretsAsync(project.Id);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetSecretsAsync_WithSecrets_ReturnsSecretNames()
    {
        // Arrange
        var project = CreateTestProject();
        _mockProjectService.Setup(p => p.GetByIdAsync(project.Id)).ReturnsAsync(project);

        // Create secrets file
        File.WriteAllText(_testSecretsFilePath, "API_KEY=secret123\nDATABASE_URL=postgres://localhost\n");

        // Act
        var result = await _service.GetSecretsAsync(project.Id);

        // Assert
        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result.Select(s => s.Name), Is.EquivalentTo(new[] { "API_KEY", "DATABASE_URL" }));
    }

    [Test]
    public async Task GetSecretsAsync_WithEmptyLines_IgnoresEmptyLines()
    {
        // Arrange
        var project = CreateTestProject();
        _mockProjectService.Setup(p => p.GetByIdAsync(project.Id)).ReturnsAsync(project);

        File.WriteAllText(_testSecretsFilePath, "API_KEY=secret123\n\n  \nDATABASE_URL=postgres://localhost\n");

        // Act
        var result = await _service.GetSecretsAsync(project.Id);

        // Assert
        Assert.That(result, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task GetSecretsAsync_WithComments_IgnoresComments()
    {
        // Arrange
        var project = CreateTestProject();
        _mockProjectService.Setup(p => p.GetByIdAsync(project.Id)).ReturnsAsync(project);

        File.WriteAllText(_testSecretsFilePath, "# This is a comment\nAPI_KEY=secret123\n");

        // Act
        var result = await _service.GetSecretsAsync(project.Id);

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result.First().Name, Is.EqualTo("API_KEY"));
    }

    #endregion

    #region SetSecretAsync Tests

    [Test]
    public async Task SetSecretAsync_ProjectNotFound_ReturnsFalse()
    {
        // Arrange
        _mockProjectService.Setup(p => p.GetByIdAsync("nonexistent")).ReturnsAsync((Project?)null);

        // Act
        var result = await _service.SetSecretAsync("nonexistent", "API_KEY", "secret");

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void SetSecretAsync_InvalidName_ThrowsArgumentException()
    {
        // Arrange
        var project = CreateTestProject();
        _mockProjectService.Setup(p => p.GetByIdAsync(project.Id)).ReturnsAsync(project);

        // Act & Assert
        Assert.ThrowsAsync<ArgumentException>(async () =>
            await _service.SetSecretAsync(project.Id, "invalid-name", "value"));
    }

    [Test]
    public void SetSecretAsync_NameWithSpaces_ThrowsArgumentException()
    {
        // Arrange
        var project = CreateTestProject();
        _mockProjectService.Setup(p => p.GetByIdAsync(project.Id)).ReturnsAsync(project);

        // Act & Assert
        Assert.ThrowsAsync<ArgumentException>(async () =>
            await _service.SetSecretAsync(project.Id, "MY SECRET", "value"));
    }

    [Test]
    public void SetSecretAsync_NameStartingWithNumber_ThrowsArgumentException()
    {
        // Arrange
        var project = CreateTestProject();
        _mockProjectService.Setup(p => p.GetByIdAsync(project.Id)).ReturnsAsync(project);

        // Act & Assert
        Assert.ThrowsAsync<ArgumentException>(async () =>
            await _service.SetSecretAsync(project.Id, "123_KEY", "value"));
    }

    [Test]
    public async Task SetSecretAsync_NewSecret_CreatesFile()
    {
        // Arrange
        var project = CreateTestProject();
        _mockProjectService.Setup(p => p.GetByIdAsync(project.Id)).ReturnsAsync(project);

        // Act
        var result = await _service.SetSecretAsync(project.Id, "API_KEY", "secret123");

        // Assert
        Assert.That(result, Is.True);
        Assert.That(File.Exists(_testSecretsFilePath), Is.True);
        var content = await File.ReadAllTextAsync(_testSecretsFilePath);
        Assert.That(content, Does.Contain("API_KEY=secret123"));
    }

    [Test]
    public async Task SetSecretAsync_ExistingSecret_UpdatesValue()
    {
        // Arrange
        var project = CreateTestProject();
        _mockProjectService.Setup(p => p.GetByIdAsync(project.Id)).ReturnsAsync(project);
        File.WriteAllText(_testSecretsFilePath, "API_KEY=old_value\nOTHER_KEY=other\n");

        // Act
        var result = await _service.SetSecretAsync(project.Id, "API_KEY", "new_value");

        // Assert
        Assert.That(result, Is.True);
        var content = await File.ReadAllTextAsync(_testSecretsFilePath);
        Assert.That(content, Does.Contain("API_KEY=new_value"));
        Assert.That(content, Does.Contain("OTHER_KEY=other"));
        Assert.That(content, Does.Not.Contain("old_value"));
    }

    [Test]
    public async Task SetSecretAsync_ValidUppercaseName_Succeeds()
    {
        // Arrange
        var project = CreateTestProject();
        _mockProjectService.Setup(p => p.GetByIdAsync(project.Id)).ReturnsAsync(project);

        // Act
        var result = await _service.SetSecretAsync(project.Id, "MY_API_KEY_123", "value");

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public async Task SetSecretAsync_ValueWithSpecialChars_PreservesValue()
    {
        // Arrange
        var project = CreateTestProject();
        _mockProjectService.Setup(p => p.GetByIdAsync(project.Id)).ReturnsAsync(project);
        var complexValue = "postgres://user:p@ss=word@host:5432/db?ssl=true";

        // Act
        var result = await _service.SetSecretAsync(project.Id, "DATABASE_URL", complexValue);

        // Assert
        Assert.That(result, Is.True);
        var content = await File.ReadAllTextAsync(_testSecretsFilePath);
        Assert.That(content, Does.Contain($"DATABASE_URL={complexValue}"));
    }

    #endregion

    #region DeleteSecretAsync Tests

    [Test]
    public async Task DeleteSecretAsync_SecretExists_RemovesFromFile()
    {
        // Arrange
        var project = CreateTestProject();
        _mockProjectService.Setup(p => p.GetByIdAsync(project.Id)).ReturnsAsync(project);
        File.WriteAllText(_testSecretsFilePath, "API_KEY=secret123\nOTHER_KEY=other\n");

        // Act
        var result = await _service.DeleteSecretAsync(project.Id, "API_KEY");

        // Assert
        Assert.That(result, Is.True);
        var content = await File.ReadAllTextAsync(_testSecretsFilePath);
        Assert.That(content, Does.Not.Contain("API_KEY"));
        Assert.That(content, Does.Contain("OTHER_KEY=other"));
    }

    [Test]
    public async Task DeleteSecretAsync_SecretNotExists_ReturnsFalse()
    {
        // Arrange
        var project = CreateTestProject();
        _mockProjectService.Setup(p => p.GetByIdAsync(project.Id)).ReturnsAsync(project);
        File.WriteAllText(_testSecretsFilePath, "OTHER_KEY=other\n");

        // Act
        var result = await _service.DeleteSecretAsync(project.Id, "NONEXISTENT");

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task DeleteSecretAsync_NoSecretsFile_ReturnsFalse()
    {
        // Arrange
        var project = CreateTestProject();
        _mockProjectService.Setup(p => p.GetByIdAsync(project.Id)).ReturnsAsync(project);

        // Act
        var result = await _service.DeleteSecretAsync(project.Id, "API_KEY");

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task DeleteSecretAsync_LastSecret_KeepsEmptyFile()
    {
        // Arrange
        var project = CreateTestProject();
        _mockProjectService.Setup(p => p.GetByIdAsync(project.Id)).ReturnsAsync(project);
        File.WriteAllText(_testSecretsFilePath, "API_KEY=secret123\n");

        // Act
        var result = await _service.DeleteSecretAsync(project.Id, "API_KEY");

        // Assert
        Assert.That(result, Is.True);
        Assert.That(File.Exists(_testSecretsFilePath), Is.True);
        var content = await File.ReadAllTextAsync(_testSecretsFilePath);
        Assert.That(content.Trim(), Is.Empty);
    }

    #endregion

    #region GetSecretsForInjectionAsync Tests

    [Test]
    public async Task GetSecretsForInjectionAsync_ReturnsKeyValuePairs()
    {
        // Arrange
        File.WriteAllText(_testSecretsFilePath, "API_KEY=secret123\nDATABASE_URL=postgres://localhost\n");

        // Act
        var result = await _service.GetSecretsForInjectionAsync(_testProjectPath);

        // Assert
        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result["API_KEY"], Is.EqualTo("secret123"));
        Assert.That(result["DATABASE_URL"], Is.EqualTo("postgres://localhost"));
    }

    [Test]
    public async Task GetSecretsForInjectionAsync_NoSecretsFile_ReturnsEmpty()
    {
        // Act
        var result = await _service.GetSecretsForInjectionAsync(_testProjectPath);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetSecretsForInjectionAsync_InvalidPath_ReturnsEmpty()
    {
        // Act
        var result = await _service.GetSecretsForInjectionAsync("/nonexistent/path");

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetSecretsForInjectionAsync_FromBranchPath_FindsProjectRoot()
    {
        // Arrange - create a clone path structure
        var branchPath = Path.Combine(_testProjectPath, "main");
        Directory.CreateDirectory(branchPath);
        File.WriteAllText(_testSecretsFilePath, "API_KEY=secret123\n");

        // Act - call with the branch path (child of project folder)
        var result = await _service.GetSecretsForInjectionAsync(branchPath);

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result["API_KEY"], Is.EqualTo("secret123"));
    }

    #endregion

    #region GetSecretsForInjectionByProjectIdAsync Tests

    [Test]
    public async Task GetSecretsForInjectionByProjectIdAsync_ReturnsKeyValuePairs()
    {
        // Arrange
        var project = CreateTestProject();
        _mockProjectService.Setup(p => p.GetByIdAsync(project.Id)).ReturnsAsync(project);
        File.WriteAllText(_testSecretsFilePath, "API_KEY=secret123\nDATABASE_URL=postgres://localhost\n");

        // Act
        var result = await _service.GetSecretsForInjectionByProjectIdAsync(project.Id);

        // Assert
        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result["API_KEY"], Is.EqualTo("secret123"));
        Assert.That(result["DATABASE_URL"], Is.EqualTo("postgres://localhost"));
    }

    [Test]
    public async Task GetSecretsForInjectionByProjectIdAsync_ProjectNotFound_ReturnsEmpty()
    {
        // Arrange
        _mockProjectService.Setup(p => p.GetByIdAsync("nonexistent")).ReturnsAsync((Project?)null);

        // Act
        var result = await _service.GetSecretsForInjectionByProjectIdAsync("nonexistent");

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetSecretsForInjectionByProjectIdAsync_NoSecretsFile_ReturnsEmpty()
    {
        // Arrange
        var project = CreateTestProject();
        _mockProjectService.Setup(p => p.GetByIdAsync(project.Id)).ReturnsAsync(project);
        // Don't create secrets file

        // Act
        var result = await _service.GetSecretsForInjectionByProjectIdAsync(project.Id);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetSecretsForInjectionByProjectIdAsync_WithCommentsAndEmptyLines_ParsesCorrectly()
    {
        // Arrange
        var project = CreateTestProject();
        _mockProjectService.Setup(p => p.GetByIdAsync(project.Id)).ReturnsAsync(project);
        File.WriteAllText(_testSecretsFilePath, "# This is a comment\nAPI_KEY=secret123\n\nDATABASE_URL=postgres://localhost\n");

        // Act
        var result = await _service.GetSecretsForInjectionByProjectIdAsync(project.Id);

        // Assert
        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result["API_KEY"], Is.EqualTo("secret123"));
        Assert.That(result["DATABASE_URL"], Is.EqualTo("postgres://localhost"));
    }

    #endregion

    #region Helper Methods

    private Project CreateTestProject()
    {
        var project = new Project
        {
            Id = Guid.NewGuid().ToString(),
            Name = "test-project",
            LocalPath = Path.Combine(_testProjectPath, "main"),
            DefaultBranch = "main"
        };
        return project;
    }

    #endregion
}
