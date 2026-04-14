using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Homespun.Features.Projects.Controllers;
using Homespun.Shared.Models.Git;
using Homespun.Shared.Models.Projects;
using Homespun.Shared.Models.Sessions;
using Homespun.Shared.Requests;

using CreateProjectRequest = Homespun.Features.Projects.Controllers.CreateProjectRequest;

namespace Homespun.Api.Tests.Features;

[TestFixture]
public class ClonesApiTests
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

    private async Task<Project> CreateProject(string? name = null)
    {
        name ??= "clone-test-" + Guid.NewGuid().ToString("N")[..8];
        var request = new CreateProjectRequest { Name = name };
        var response = await _client.PostAsJsonAsync("/api/projects", request, JsonOptions);
        response.EnsureSuccessStatusCode();
        var project = await response.Content.ReadFromJsonAsync<Project>(JsonOptions);
        return project!;
    }

    private async Task<CreateCloneResponse> CreateClone(string projectId, string? branchName = null)
    {
        branchName ??= "feature/test-" + Guid.NewGuid().ToString("N")[..8];
        var request = new CreateCloneRequest
        {
            BranchName = branchName,
            CreateBranch = true,
            BaseBranch = "main"
        };
        var response = await _client.PostAsJsonAsync(
            $"/api/projects/{projectId}/clones", request, JsonOptions);
        response.EnsureSuccessStatusCode();
        var clone = await response.Content.ReadFromJsonAsync<CreateCloneResponse>(JsonOptions);
        return clone!;
    }

    // --- ClonesController: GET /api/clones/branches ---

    [Test]
    public async Task GetBranches_ReturnsOk_WithBranchList()
    {
        // Arrange
        var project = await CreateProject();

        // Act
        var response = await _client.GetAsync(
            $"/api/clones/branches?repoPath={Uri.EscapeDataString(project.LocalPath)}");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var branches = await response.Content.ReadFromJsonAsync<List<BranchInfo>>(JsonOptions);
        Assert.That(branches, Is.Not.Null);
        Assert.That(branches, Is.Not.Empty);
    }

    // --- ClonesController: GET /api/clones/changed-files ---

    [Test]
    public async Task GetChangedFiles_ReturnsOk_WithFileList()
    {
        // Arrange
        var project = await CreateProject();
        var clone = await CreateClone(project.Id);

        // Act
        var response = await _client.GetAsync(
            $"/api/clones/changed-files?workingDirectory={Uri.EscapeDataString(clone.Path)}&targetBranch=main");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var files = await response.Content.ReadFromJsonAsync<List<FileChangeInfo>>(JsonOptions);
        Assert.That(files, Is.Not.Null);
    }

    // --- ClonesController: POST /api/clones/pull ---

    [Test]
    public async Task Pull_ReturnsNoContent_ForValidClonePath()
    {
        // Arrange
        var project = await CreateProject();
        var clone = await CreateClone(project.Id);

        // Act
        var response = await _client.PostAsync(
            $"/api/clones/pull?clonePath={Uri.EscapeDataString(clone.Path)}", null);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    // --- ClonesController: GET /api/clones/session-branch-info ---

    [Test]
    public async Task GetSessionBranchInfo_ReturnsOk_ForValidWorkingDirectory()
    {
        // Arrange
        var project = await CreateProject();
        var clone = await CreateClone(project.Id);

        // Act
        var response = await _client.GetAsync(
            $"/api/clones/session-branch-info?workingDirectory={Uri.EscapeDataString(clone.Path)}");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var info = await response.Content.ReadFromJsonAsync<SessionBranchInfo>(JsonOptions);
        Assert.Multiple(() =>
        {
            Assert.That(info, Is.Not.Null);
            Assert.That(info!.CommitSha, Is.Not.Null.And.Not.Empty);
            Assert.That(info.CommitMessage, Is.Not.Null.And.Not.Empty);
        });
    }

    [Test]
    public async Task GetSessionBranchInfo_ReturnsOk_ForUnknownWorkingDirectory()
    {
        // Act - mock mode returns data for any path
        var response = await _client.GetAsync(
            "/api/clones/session-branch-info?workingDirectory=/nonexistent/path");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var info = await response.Content.ReadFromJsonAsync<SessionBranchInfo>(JsonOptions);
        Assert.That(info, Is.Not.Null);
    }

    // --- ClonesController: POST /api/clones/session ---

    [Test]
    public async Task CreateBranchSession_ReturnsCreated_WithValidRequest()
    {
        // Arrange
        var project = await CreateProject();
        var branchName = "feature/session-" + Guid.NewGuid().ToString("N")[..8];
        var request = new CreateBranchSessionRequest
        {
            ProjectId = project.Id,
            BranchName = branchName
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/clones/session", request, JsonOptions);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var session = await response.Content.ReadFromJsonAsync<CreateBranchSessionResponse>(JsonOptions);
        Assert.Multiple(() =>
        {
            Assert.That(session, Is.Not.Null);
            Assert.That(session!.SessionId, Is.Not.Null.And.Not.Empty);
            Assert.That(session.BranchName, Is.EqualTo(branchName));
            Assert.That(session.ClonePath, Is.Not.Null.And.Not.Empty);
        });
    }

    [Test]
    public async Task CreateBranchSession_ReturnsNotFound_WhenProjectDoesNotExist()
    {
        // Arrange
        var request = new CreateBranchSessionRequest
        {
            ProjectId = "non-existent-project",
            BranchName = "feature/test"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/clones/session", request, JsonOptions);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task CreateBranchSession_ReturnsBadRequest_WhenBranchNameEmpty()
    {
        // Arrange
        var project = await CreateProject();
        var request = new CreateBranchSessionRequest
        {
            ProjectId = project.Id,
            BranchName = "   "
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/clones/session", request, JsonOptions);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task CreateBranchSession_WithCustomBaseBranch_ReturnsCreated()
    {
        // Arrange
        var project = await CreateProject();
        var branchName = "feature/custom-base-" + Guid.NewGuid().ToString("N")[..8];
        var request = new CreateBranchSessionRequest
        {
            ProjectId = project.Id,
            BranchName = branchName,
            BaseBranch = "develop"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/clones/session", request, JsonOptions);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var session = await response.Content.ReadFromJsonAsync<CreateBranchSessionResponse>(JsonOptions);
        Assert.That(session!.BranchName, Is.EqualTo(branchName));
    }
}

[TestFixture]
public class ProjectClonesApiTests
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

    private async Task<Project> CreateProject(string? name = null)
    {
        name ??= "pclone-test-" + Guid.NewGuid().ToString("N")[..8];
        var request = new CreateProjectRequest { Name = name };
        var response = await _client.PostAsJsonAsync("/api/projects", request, JsonOptions);
        response.EnsureSuccessStatusCode();
        var project = await response.Content.ReadFromJsonAsync<Project>(JsonOptions);
        return project!;
    }

    private async Task<CreateCloneResponse> CreateClone(string projectId, string? branchName = null)
    {
        branchName ??= "feature/test-" + Guid.NewGuid().ToString("N")[..8];
        var request = new CreateCloneRequest
        {
            BranchName = branchName,
            CreateBranch = true,
            BaseBranch = "main"
        };
        var response = await _client.PostAsJsonAsync(
            $"/api/projects/{projectId}/clones", request, JsonOptions);
        response.EnsureSuccessStatusCode();
        var clone = await response.Content.ReadFromJsonAsync<CreateCloneResponse>(JsonOptions);
        return clone!;
    }

    // --- GET /api/projects/{projectId}/clones ---

    [Test]
    public async Task List_ReturnsOk_WithCloneList()
    {
        // Arrange
        var project = await CreateProject();

        // Act
        var response = await _client.GetAsync($"/api/projects/{project.Id}/clones");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var clones = await response.Content.ReadFromJsonAsync<List<CloneInfo>>(JsonOptions);
        Assert.That(clones, Is.Not.Null);
    }

    [Test]
    public async Task List_ReturnsNotFound_WhenProjectDoesNotExist()
    {
        // Act
        var response = await _client.GetAsync("/api/projects/non-existent-id/clones");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task List_ReturnsClones_AfterCreatingOne()
    {
        // Arrange
        var project = await CreateProject();
        var clone = await CreateClone(project.Id, "feature/list-test");

        // Act
        var response = await _client.GetAsync($"/api/projects/{project.Id}/clones");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var clones = await response.Content.ReadFromJsonAsync<List<CloneInfo>>(JsonOptions);
        Assert.That(clones, Is.Not.Null);
        Assert.That(clones!.Any(c => c.Path == clone.Path), Is.True);
    }

    // --- GET /api/projects/{projectId}/clones/enriched ---

    [Test]
    public async Task ListEnriched_ReturnsOk_WithEnrichedCloneList()
    {
        // Arrange
        var project = await CreateProject();

        // Act
        var response = await _client.GetAsync($"/api/projects/{project.Id}/clones/enriched");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var clones = await response.Content.ReadFromJsonAsync<List<EnrichedCloneInfo>>(JsonOptions);
        Assert.That(clones, Is.Not.Null);
    }

    [Test]
    public async Task ListEnriched_ReturnsNotFound_WhenProjectDoesNotExist()
    {
        // Act
        var response = await _client.GetAsync("/api/projects/non-existent-id/clones/enriched");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task ListEnriched_ReturnsEnrichedData_AfterCreatingClone()
    {
        // Arrange
        var project = await CreateProject();
        await CreateClone(project.Id, "feature/enriched-test");

        // Act
        var response = await _client.GetAsync($"/api/projects/{project.Id}/clones/enriched");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var clones = await response.Content.ReadFromJsonAsync<List<EnrichedCloneInfo>>(JsonOptions);
        Assert.That(clones, Is.Not.Null);
        Assert.That(clones, Is.Not.Empty);
        Assert.That(clones![0].Clone, Is.Not.Null);
    }

    // --- POST /api/projects/{projectId}/clones ---

    [Test]
    public async Task Create_ReturnsCreated_WithValidRequest()
    {
        // Arrange
        var project = await CreateProject();
        var branchName = "feature/create-" + Guid.NewGuid().ToString("N")[..8];
        var request = new CreateCloneRequest
        {
            BranchName = branchName,
            CreateBranch = true,
            BaseBranch = "main"
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/projects/{project.Id}/clones", request, JsonOptions);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var clone = await response.Content.ReadFromJsonAsync<CreateCloneResponse>(JsonOptions);
        Assert.Multiple(() =>
        {
            Assert.That(clone, Is.Not.Null);
            Assert.That(clone!.Path, Is.Not.Null.And.Not.Empty);
            Assert.That(clone.BranchName, Is.EqualTo(branchName));
        });
    }

    [Test]
    public async Task Create_ReturnsNotFound_WhenProjectDoesNotExist()
    {
        // Arrange
        var request = new CreateCloneRequest
        {
            BranchName = "feature/orphan",
            CreateBranch = true
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/projects/non-existent-id/clones", request, JsonOptions);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    // --- DELETE /api/projects/{projectId}/clones ---

    [Test]
    public async Task Delete_ReturnsNoContent_WhenCloneExists()
    {
        // Arrange
        var project = await CreateProject();
        var clone = await CreateClone(project.Id);

        // Act
        var response = await _client.DeleteAsync(
            $"/api/projects/{project.Id}/clones?clonePath={Uri.EscapeDataString(clone.Path)}");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task Delete_ReturnsNotFound_WhenProjectDoesNotExist()
    {
        // Act
        var response = await _client.DeleteAsync(
            "/api/projects/non-existent-id/clones?clonePath=/some/path");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Delete_CloneIsGone_AfterDeletion()
    {
        // Arrange
        var project = await CreateProject();
        var branchName = "feature/delete-verify-" + Guid.NewGuid().ToString("N")[..8];
        var clone = await CreateClone(project.Id, branchName);

        // Act
        var deleteResponse = await _client.DeleteAsync(
            $"/api/projects/{project.Id}/clones?clonePath={Uri.EscapeDataString(clone.Path)}");
        Assert.That(deleteResponse.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        // Assert - verify clone is removed from list
        var existsResponse = await _client.GetAsync(
            $"/api/projects/{project.Id}/clones/exists?branchName={Uri.EscapeDataString(branchName)}");
        var existsResult = await existsResponse.Content.ReadFromJsonAsync<CloneExistsResponse>(JsonOptions);
        Assert.That(existsResult!.Exists, Is.False);
    }

    // --- DELETE /api/projects/{projectId}/clones/bulk ---

    [Test]
    public async Task BulkDelete_ReturnsOk_WithResults()
    {
        // Arrange
        var project = await CreateProject();
        var clone1 = await CreateClone(project.Id);
        var clone2 = await CreateClone(project.Id);
        var request = new BulkDeleteClonesRequest
        {
            ClonePaths = [clone1.Path, clone2.Path]
        };

        // Act
        var response = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Delete,
            $"/api/projects/{project.Id}/clones/bulk")
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        });

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<BulkDeleteClonesResponse>(JsonOptions);
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Results, Has.Count.EqualTo(2));
            Assert.That(result.Results.All(r => r.Success), Is.True);
        });
    }

    [Test]
    public async Task BulkDelete_ReturnsNotFound_WhenProjectDoesNotExist()
    {
        // Arrange
        var request = new BulkDeleteClonesRequest
        {
            ClonePaths = ["/some/path"]
        };

        // Act
        var response = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Delete,
            "/api/projects/non-existent-id/clones/bulk")
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        });

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    // --- GET /api/projects/{projectId}/clones/exists ---

    [Test]
    public async Task Exists_ReturnsFalse_WhenCloneDoesNotExist()
    {
        // Arrange
        var project = await CreateProject();

        // Act
        var response = await _client.GetAsync(
            $"/api/projects/{project.Id}/clones/exists?branchName=nonexistent-branch");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<CloneExistsResponse>(JsonOptions);
        Assert.That(result!.Exists, Is.False);
    }

    [Test]
    public async Task Exists_ReturnsTrue_WhenCloneExists()
    {
        // Arrange
        var project = await CreateProject();
        var branchName = "feature/exists-" + Guid.NewGuid().ToString("N")[..8];
        await CreateClone(project.Id, branchName);

        // Act
        var response = await _client.GetAsync(
            $"/api/projects/{project.Id}/clones/exists?branchName={Uri.EscapeDataString(branchName)}");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<CloneExistsResponse>(JsonOptions);
        Assert.That(result!.Exists, Is.True);
    }

    [Test]
    public async Task Exists_ReturnsNotFound_WhenProjectDoesNotExist()
    {
        // Act
        var response = await _client.GetAsync(
            "/api/projects/non-existent-id/clones/exists?branchName=some-branch");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    // --- POST /api/projects/{projectId}/clones/prune ---

    [Test]
    public async Task Prune_ReturnsNoContent_ForExistingProject()
    {
        // Arrange
        var project = await CreateProject();

        // Act
        var response = await _client.PostAsync(
            $"/api/projects/{project.Id}/clones/prune", null);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task Prune_ReturnsNotFound_WhenProjectDoesNotExist()
    {
        // Act
        var response = await _client.PostAsync(
            "/api/projects/non-existent-id/clones/prune", null);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
