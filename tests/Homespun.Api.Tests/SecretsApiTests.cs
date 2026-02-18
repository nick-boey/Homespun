using System.Net;
using System.Net.Http.Json;
using Homespun.Shared.Models.Secrets;
using Homespun.Shared.Requests;

namespace Homespun.Api.Tests;

/// <summary>
/// Integration tests for the Secrets API endpoints.
/// </summary>
[TestFixture]
public class SecretsApiTests
{
    private HomespunWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;
    private string _testProjectPath = null!;
    private string _testSecretsFilePath = null!;

    [SetUp]
    public void SetUp()
    {
        _factory = new HomespunWebApplicationFactory();
        _client = _factory.CreateClient();

        // Create a temp directory for the test project
        _testProjectPath = Path.Combine(Path.GetTempPath(), "homespun-api-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_testProjectPath);
        var branchPath = Path.Combine(_testProjectPath, "main");
        Directory.CreateDirectory(branchPath);
        _testSecretsFilePath = Path.Combine(_testProjectPath, "secrets.env");
    }

    [TearDown]
    public void TearDown()
    {
        _client.Dispose();
        _factory.Dispose();
        if (Directory.Exists(_testProjectPath))
        {
            Directory.Delete(_testProjectPath, true);
        }
    }

    #region GetSecrets Tests

    [Test]
    public async Task GetSecrets_ReturnsEmptyList_WhenNoSecrets()
    {
        // Arrange
        var project = new Project
        {
            Id = "test-project",
            Name = "TestProject",
            LocalPath = Path.Combine(_testProjectPath, "main"),
            DefaultBranch = "main"
        };
        _factory.MockDataStore.SeedProject(project);

        // Act
        var response = await _client.GetAsync("/api/projects/test-project/secrets");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<SecretsListResponse>();
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Secrets, Is.Empty);
    }

    [Test]
    public async Task GetSecrets_ReturnsSecretNames_WhenSecretsExist()
    {
        // Arrange
        var project = new Project
        {
            Id = "test-project",
            Name = "TestProject",
            LocalPath = Path.Combine(_testProjectPath, "main"),
            DefaultBranch = "main"
        };
        _factory.MockDataStore.SeedProject(project);
        await File.WriteAllTextAsync(_testSecretsFilePath, "API_KEY=secret123\nDATABASE_URL=postgres://localhost\n");

        // Act
        var response = await _client.GetAsync("/api/projects/test-project/secrets");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<SecretsListResponse>();
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Secrets, Has.Count.EqualTo(2));
        Assert.That(result.Secrets.Select(s => s.Name), Is.EquivalentTo(new[] { "API_KEY", "DATABASE_URL" }));
    }

    [Test]
    public async Task GetSecrets_ReturnsEmptyList_WhenProjectNotFound()
    {
        // Act
        var response = await _client.GetAsync("/api/projects/nonexistent/secrets");

        // Assert - still returns 200 with empty list (not 404)
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<SecretsListResponse>();
        Assert.That(result!.Secrets, Is.Empty);
    }

    #endregion

    #region CreateSecret Tests

    [Test]
    public async Task CreateSecret_ReturnsCreated_WhenValidRequest()
    {
        // Arrange
        var project = new Project
        {
            Id = "test-project",
            Name = "TestProject",
            LocalPath = Path.Combine(_testProjectPath, "main"),
            DefaultBranch = "main"
        };
        _factory.MockDataStore.SeedProject(project);
        var request = new CreateSecretRequest { Name = "API_KEY", Value = "secret123" };

        // Act
        var response = await _client.PostAsJsonAsync("/api/projects/test-project/secrets", request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        Assert.That(File.Exists(_testSecretsFilePath), Is.True);
        var content = await File.ReadAllTextAsync(_testSecretsFilePath);
        Assert.That(content, Does.Contain("API_KEY=secret123"));
    }

    [Test]
    public async Task CreateSecret_ReturnsBadRequest_WhenInvalidName()
    {
        // Arrange
        var project = new Project
        {
            Id = "test-project",
            Name = "TestProject",
            LocalPath = Path.Combine(_testProjectPath, "main"),
            DefaultBranch = "main"
        };
        _factory.MockDataStore.SeedProject(project);
        var request = new CreateSecretRequest { Name = "invalid-name", Value = "secret123" };

        // Act
        var response = await _client.PostAsJsonAsync("/api/projects/test-project/secrets", request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task CreateSecret_ReturnsNotFound_WhenProjectNotFound()
    {
        // Arrange
        var request = new CreateSecretRequest { Name = "API_KEY", Value = "secret123" };

        // Act
        var response = await _client.PostAsJsonAsync("/api/projects/nonexistent/secrets", request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    #endregion

    #region UpdateSecret Tests

    [Test]
    public async Task UpdateSecret_ReturnsNoContent_WhenSuccessful()
    {
        // Arrange
        var project = new Project
        {
            Id = "test-project",
            Name = "TestProject",
            LocalPath = Path.Combine(_testProjectPath, "main"),
            DefaultBranch = "main"
        };
        _factory.MockDataStore.SeedProject(project);
        await File.WriteAllTextAsync(_testSecretsFilePath, "API_KEY=old_value\n");
        var request = new UpdateSecretRequest { Value = "new_value" };

        // Act
        var response = await _client.PutAsJsonAsync("/api/projects/test-project/secrets/API_KEY", request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        var content = await File.ReadAllTextAsync(_testSecretsFilePath);
        Assert.That(content, Does.Contain("API_KEY=new_value"));
        Assert.That(content, Does.Not.Contain("old_value"));
    }

    [Test]
    public async Task UpdateSecret_ReturnsNotFound_WhenProjectNotFound()
    {
        // Arrange
        var request = new UpdateSecretRequest { Value = "new_value" };

        // Act
        var response = await _client.PutAsJsonAsync("/api/projects/nonexistent/secrets/API_KEY", request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    #endregion

    #region DeleteSecret Tests

    [Test]
    public async Task DeleteSecret_ReturnsNoContent_WhenSecretExists()
    {
        // Arrange
        var project = new Project
        {
            Id = "test-project",
            Name = "TestProject",
            LocalPath = Path.Combine(_testProjectPath, "main"),
            DefaultBranch = "main"
        };
        _factory.MockDataStore.SeedProject(project);
        await File.WriteAllTextAsync(_testSecretsFilePath, "API_KEY=secret123\nOTHER_KEY=other\n");

        // Act
        var response = await _client.DeleteAsync("/api/projects/test-project/secrets/API_KEY");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        var content = await File.ReadAllTextAsync(_testSecretsFilePath);
        Assert.That(content, Does.Not.Contain("API_KEY"));
        Assert.That(content, Does.Contain("OTHER_KEY=other"));
    }

    [Test]
    public async Task DeleteSecret_ReturnsNotFound_WhenSecretNotExists()
    {
        // Arrange
        var project = new Project
        {
            Id = "test-project",
            Name = "TestProject",
            LocalPath = Path.Combine(_testProjectPath, "main"),
            DefaultBranch = "main"
        };
        _factory.MockDataStore.SeedProject(project);
        await File.WriteAllTextAsync(_testSecretsFilePath, "OTHER_KEY=other\n");

        // Act
        var response = await _client.DeleteAsync("/api/projects/test-project/secrets/NONEXISTENT");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task DeleteSecret_ReturnsNotFound_WhenNoSecretsFile()
    {
        // Arrange
        var project = new Project
        {
            Id = "test-project",
            Name = "TestProject",
            LocalPath = Path.Combine(_testProjectPath, "main"),
            DefaultBranch = "main"
        };
        _factory.MockDataStore.SeedProject(project);

        // Act
        var response = await _client.DeleteAsync("/api/projects/test-project/secrets/API_KEY");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    #endregion
}
