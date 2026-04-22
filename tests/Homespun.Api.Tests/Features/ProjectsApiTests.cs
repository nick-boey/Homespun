using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Homespun.Shared.Models.Projects;
using Homespun.Shared.Requests;

namespace Homespun.Api.Tests.Features;

[TestFixture]
public class ProjectsApiTests
{
    private HomespunWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _factory = new HomespunWebApplicationFactory();
        _client = _factory.CreateClient();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private async Task<Project> CreateLocalProject(string name, string? defaultBranch = null)
    {
        var request = new CreateProjectRequest { Name = name, DefaultBranch = defaultBranch };
        var response = await _client.PostAsJsonAsync("/api/projects", request, JsonOptions);
        response.EnsureSuccessStatusCode();
        var project = await response.Content.ReadFromJsonAsync<Project>(JsonOptions);
        return project!;
    }

    // --- GET /api/projects ---

    [Test]
    public async Task GetAll_ReturnsOk_WithListOfProjects()
    {
        // Arrange
        var projectName = "getall-test-" + Guid.NewGuid().ToString("N")[..8];
        await CreateLocalProject(projectName);

        // Act
        var response = await _client.GetAsync("/api/projects");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var projects = await response.Content.ReadFromJsonAsync<List<Project>>(JsonOptions);
        Assert.That(projects, Is.Not.Null);
        Assert.That(projects!.Any(p => p.Name == projectName), Is.True);
    }

    // --- GET /api/projects/{id} ---

    [Test]
    public async Task GetById_ReturnsProject_WhenExists()
    {
        // Arrange
        var projectName = "getbyid-test-" + Guid.NewGuid().ToString("N")[..8];
        var created = await CreateLocalProject(projectName);

        // Act
        var response = await _client.GetAsync($"/api/projects/{created.Id}");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var project = await response.Content.ReadFromJsonAsync<Project>(JsonOptions);
        Assert.Multiple(() =>
        {
            Assert.That(project, Is.Not.Null);
            Assert.That(project!.Id, Is.EqualTo(created.Id));
            Assert.That(project.Name, Is.EqualTo(projectName));
            Assert.That(project.DefaultBranch, Is.EqualTo("main"));
        });
    }

    [Test]
    public async Task GetById_ReturnsNotFound_WhenDoesNotExist()
    {
        // Act
        var response = await _client.GetAsync("/api/projects/non-existent-id");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    // --- POST /api/projects ---

    [Test]
    public async Task Create_WithLocalName_ReturnsCreated()
    {
        // Arrange
        var projectName = "create-test-" + Guid.NewGuid().ToString("N")[..8];
        var request = new CreateProjectRequest { Name = projectName };

        // Act
        var response = await _client.PostAsJsonAsync("/api/projects", request, JsonOptions);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var project = await response.Content.ReadFromJsonAsync<Project>(JsonOptions);
        Assert.Multiple(() =>
        {
            Assert.That(project, Is.Not.Null);
            Assert.That(project!.Name, Is.EqualTo(projectName));
            Assert.That(project.DefaultBranch, Is.EqualTo("main"));
            Assert.That(project.GitHubOwner, Is.Null);
            Assert.That(project.GitHubRepo, Is.Null);
        });
    }

    [Test]
    public async Task Create_WithCustomDefaultBranch_UsesProvidedBranch()
    {
        // Arrange
        var projectName = "branch-test-" + Guid.NewGuid().ToString("N")[..8];
        var request = new CreateProjectRequest { Name = projectName, DefaultBranch = "develop" };

        // Act
        var response = await _client.PostAsJsonAsync("/api/projects", request, JsonOptions);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var project = await response.Content.ReadFromJsonAsync<Project>(JsonOptions);
        Assert.That(project!.DefaultBranch, Is.EqualTo("develop"));
    }

    [Test]
    public async Task Create_WithNoOwnerRepoOrName_ReturnsBadRequest()
    {
        // Arrange
        var request = new CreateProjectRequest();

        // Act
        var response = await _client.PostAsJsonAsync("/api/projects", request, JsonOptions);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Create_WithInvalidName_ReturnsBadRequest()
    {
        // Arrange - name with invalid characters
        var request = new CreateProjectRequest { Name = "invalid name!" };

        // Act
        var response = await _client.PostAsJsonAsync("/api/projects", request, JsonOptions);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    // --- PUT /api/projects/{id} ---

    [Test]
    public async Task Update_ReturnsUpdatedProject_WhenExists()
    {
        // Arrange
        var projectName = "update-test-" + Guid.NewGuid().ToString("N")[..8];
        var created = await CreateLocalProject(projectName);
        var updateRequest = new UpdateProjectRequest { DefaultModel = "sonnet" };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/projects/{created.Id}", updateRequest, JsonOptions);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var project = await response.Content.ReadFromJsonAsync<Project>(JsonOptions);
        Assert.Multiple(() =>
        {
            Assert.That(project, Is.Not.Null);
            Assert.That(project!.Id, Is.EqualTo(created.Id));
            Assert.That(project.DefaultModel, Is.EqualTo("sonnet"));
        });
    }

    [Test]
    public async Task Update_ReturnsNotFound_WhenDoesNotExist()
    {
        // Arrange
        var updateRequest = new UpdateProjectRequest { DefaultModel = "sonnet" };

        // Act
        var response = await _client.PutAsJsonAsync("/api/projects/non-existent-id", updateRequest, JsonOptions);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    // --- DELETE /api/projects/{id} ---

    [Test]
    public async Task Delete_ReturnsNoContent_WhenExists()
    {
        // Arrange
        var projectName = "delete-test-" + Guid.NewGuid().ToString("N")[..8];
        var created = await CreateLocalProject(projectName);

        // Act
        var response = await _client.DeleteAsync($"/api/projects/{created.Id}");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task Delete_ReturnsNotFound_WhenDoesNotExist()
    {
        // Act
        var response = await _client.DeleteAsync("/api/projects/non-existent-id");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Delete_ProjectIsGone_AfterDeletion()
    {
        // Arrange
        var projectName = "delete-verify-" + Guid.NewGuid().ToString("N")[..8];
        var created = await CreateLocalProject(projectName);

        // Act
        var deleteResponse = await _client.DeleteAsync($"/api/projects/{created.Id}");
        Assert.That(deleteResponse.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        // Assert - verify project is gone
        var getResponse = await _client.GetAsync($"/api/projects/{created.Id}");
        Assert.That(getResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
