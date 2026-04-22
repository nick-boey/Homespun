using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Homespun.Features.PullRequests.Controllers;
using Homespun.Shared.Models.GitHub;
using Homespun.Shared.Models.Projects;
using Homespun.Shared.Models.PullRequests;

using CreateProjectRequest = Homespun.Shared.Requests.CreateProjectRequest;

namespace Homespun.Api.Tests.Features;

[TestFixture]
public class PullRequestsApiTests
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
        name ??= "pr-test-" + Guid.NewGuid().ToString("N")[..8];
        var request = new CreateProjectRequest { Name = name };
        var response = await _client.PostAsJsonAsync("/api/projects", request, JsonOptions);
        response.EnsureSuccessStatusCode();
        var project = await response.Content.ReadFromJsonAsync<Project>(JsonOptions);
        return project!;
    }

    private async Task<PullRequest> CreatePullRequest(
        string projectId,
        string? title = null,
        string? description = null,
        string? branchName = null,
        OpenPullRequestStatus? status = null,
        string? parentId = null)
    {
        title ??= "Test PR " + Guid.NewGuid().ToString("N")[..8];
        var request = new CreatePullRequestRequest
        {
            ProjectId = projectId,
            Title = title,
            Description = description,
            BranchName = branchName,
            Status = status,
            ParentId = parentId
        };
        var response = await _client.PostAsJsonAsync("/api/pull-requests", request, JsonOptions);
        response.EnsureSuccessStatusCode();
        var pr = await response.Content.ReadFromJsonAsync<PullRequest>(JsonOptions);
        return pr!;
    }

    // --- GET /api/projects/{projectId}/pull-requests ---

    [Test]
    public async Task GetByProject_ReturnsEmptyList_WhenNoPullRequests()
    {
        // Arrange
        var project = await CreateProject();

        // Act
        var response = await _client.GetAsync($"/api/projects/{project.Id}/pull-requests");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var prs = await response.Content.ReadFromJsonAsync<List<PullRequest>>(JsonOptions);
        Assert.That(prs, Is.Not.Null);
        Assert.That(prs, Is.Empty);
    }

    [Test]
    public async Task GetByProject_ReturnsPullRequests_WhenPopulated()
    {
        // Arrange
        var project = await CreateProject();
        var pr1 = await CreatePullRequest(project.Id, title: "First PR");
        var pr2 = await CreatePullRequest(project.Id, title: "Second PR");

        // Act
        var response = await _client.GetAsync($"/api/projects/{project.Id}/pull-requests");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var prs = await response.Content.ReadFromJsonAsync<List<PullRequest>>(JsonOptions);
        Assert.That(prs, Is.Not.Null);
        Assert.That(prs, Has.Count.GreaterThanOrEqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(prs!.Any(p => p.Id == pr1.Id), Is.True);
            Assert.That(prs!.Any(p => p.Id == pr2.Id), Is.True);
        });
    }

    [Test]
    public async Task GetByProject_ReturnsNotFound_WhenProjectDoesNotExist()
    {
        // Act
        var response = await _client.GetAsync("/api/projects/non-existent-id/pull-requests");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    // --- GET /api/pull-requests/{id} ---

    [Test]
    public async Task GetById_ReturnsPullRequest_WhenExists()
    {
        // Arrange
        var project = await CreateProject();
        var created = await CreatePullRequest(project.Id, title: "Get By Id Test", description: "desc", branchName: "feature/test");

        // Act
        var response = await _client.GetAsync($"/api/pull-requests/{created.Id}");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var pr = await response.Content.ReadFromJsonAsync<PullRequest>(JsonOptions);
        Assert.Multiple(() =>
        {
            Assert.That(pr, Is.Not.Null);
            Assert.That(pr!.Id, Is.EqualTo(created.Id));
            Assert.That(pr.Title, Is.EqualTo("Get By Id Test"));
            Assert.That(pr.Description, Is.EqualTo("desc"));
            Assert.That(pr.BranchName, Is.EqualTo("feature/test"));
            Assert.That(pr.ProjectId, Is.EqualTo(project.Id));
            Assert.That(pr.Status, Is.EqualTo(OpenPullRequestStatus.InDevelopment));
        });
    }

    [Test]
    public async Task GetById_ReturnsNotFound_WhenDoesNotExist()
    {
        // Act
        var response = await _client.GetAsync("/api/pull-requests/non-existent-id");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    // --- POST /api/pull-requests ---

    [Test]
    public async Task Create_ReturnsCreated_WithValidData()
    {
        // Arrange
        var project = await CreateProject();
        var request = new CreatePullRequestRequest
        {
            ProjectId = project.Id,
            Title = "New Feature PR",
            Description = "Implements a new feature",
            BranchName = "feature/new-feature"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/pull-requests", request, JsonOptions);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var pr = await response.Content.ReadFromJsonAsync<PullRequest>(JsonOptions);
        Assert.Multiple(() =>
        {
            Assert.That(pr, Is.Not.Null);
            Assert.That(pr!.Title, Is.EqualTo("New Feature PR"));
            Assert.That(pr.Description, Is.EqualTo("Implements a new feature"));
            Assert.That(pr.BranchName, Is.EqualTo("feature/new-feature"));
            Assert.That(pr.ProjectId, Is.EqualTo(project.Id));
            Assert.That(pr.Status, Is.EqualTo(OpenPullRequestStatus.InDevelopment));
            Assert.That(pr.Id, Is.Not.Null.And.Not.Empty);
        });
    }

    [Test]
    public async Task Create_WithCustomStatus_UsesProvidedStatus()
    {
        // Arrange
        var project = await CreateProject();
        var request = new CreatePullRequestRequest
        {
            ProjectId = project.Id,
            Title = "Ready PR",
            Status = OpenPullRequestStatus.ReadyForReview
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/pull-requests", request, JsonOptions);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var pr = await response.Content.ReadFromJsonAsync<PullRequest>(JsonOptions);
        Assert.That(pr!.Status, Is.EqualTo(OpenPullRequestStatus.ReadyForReview));
    }

    [Test]
    public async Task Create_ReturnsNotFound_WhenProjectDoesNotExist()
    {
        // Arrange
        var request = new CreatePullRequestRequest
        {
            ProjectId = "non-existent-project",
            Title = "Orphan PR"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/pull-requests", request, JsonOptions);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    // --- PUT /api/pull-requests/{id} ---

    [Test]
    public async Task Update_ReturnsUpdatedPullRequest_WhenExists()
    {
        // Arrange
        var project = await CreateProject();
        var created = await CreatePullRequest(project.Id, title: "Original Title");
        var updateRequest = new UpdatePullRequestRequest
        {
            Title = "Updated Title",
            Description = "Updated description",
            BranchName = "feature/updated",
            Status = OpenPullRequestStatus.Approved
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/pull-requests/{created.Id}", updateRequest, JsonOptions);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var pr = await response.Content.ReadFromJsonAsync<PullRequest>(JsonOptions);
        Assert.Multiple(() =>
        {
            Assert.That(pr, Is.Not.Null);
            Assert.That(pr!.Id, Is.EqualTo(created.Id));
            Assert.That(pr.Title, Is.EqualTo("Updated Title"));
            Assert.That(pr.Description, Is.EqualTo("Updated description"));
            Assert.That(pr.BranchName, Is.EqualTo("feature/updated"));
            Assert.That(pr.Status, Is.EqualTo(OpenPullRequestStatus.Approved));
        });
    }

    [Test]
    public async Task Update_PartialUpdate_OnlyChangesProvidedFields()
    {
        // Arrange
        var project = await CreateProject();
        var created = await CreatePullRequest(project.Id, title: "Keep This Title", description: "Keep This Desc");
        var updateRequest = new UpdatePullRequestRequest
        {
            Status = OpenPullRequestStatus.HasReviewComments
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/pull-requests/{created.Id}", updateRequest, JsonOptions);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var pr = await response.Content.ReadFromJsonAsync<PullRequest>(JsonOptions);
        Assert.Multiple(() =>
        {
            Assert.That(pr!.Title, Is.EqualTo("Keep This Title"));
            Assert.That(pr.Description, Is.EqualTo("Keep This Desc"));
            Assert.That(pr.Status, Is.EqualTo(OpenPullRequestStatus.HasReviewComments));
        });
    }

    [Test]
    public async Task Update_ReturnsNotFound_WhenDoesNotExist()
    {
        // Arrange
        var updateRequest = new UpdatePullRequestRequest { Title = "Won't Work" };

        // Act
        var response = await _client.PutAsJsonAsync("/api/pull-requests/non-existent-id", updateRequest, JsonOptions);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    // --- DELETE /api/pull-requests/{id} ---

    [Test]
    public async Task Delete_ReturnsNoContent_WhenExists()
    {
        // Arrange
        var project = await CreateProject();
        var created = await CreatePullRequest(project.Id);

        // Act
        var response = await _client.DeleteAsync($"/api/pull-requests/{created.Id}");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task Delete_ReturnsNotFound_WhenDoesNotExist()
    {
        // Act
        var response = await _client.DeleteAsync("/api/pull-requests/non-existent-id");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Delete_PullRequestIsGone_AfterDeletion()
    {
        // Arrange
        var project = await CreateProject();
        var created = await CreatePullRequest(project.Id);

        // Act
        var deleteResponse = await _client.DeleteAsync($"/api/pull-requests/{created.Id}");
        Assert.That(deleteResponse.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        // Assert - verify PR is gone
        var getResponse = await _client.GetAsync($"/api/pull-requests/{created.Id}");
        Assert.That(getResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    // --- GET /api/projects/{projectId}/pull-requests/open ---

    [Test]
    public async Task GetOpen_ReturnsOk_WithList()
    {
        // Arrange
        var project = await CreateProject();
        await CreatePullRequest(project.Id, title: "Open PR");

        // Act
        var response = await _client.GetAsync($"/api/projects/{project.Id}/pull-requests/open");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var prs = await response.Content.ReadFromJsonAsync<List<PullRequestWithStatus>>(JsonOptions);
        Assert.That(prs, Is.Not.Null);
    }

    [Test]
    public async Task GetOpen_ReturnsNotFound_WhenProjectDoesNotExist()
    {
        // Act
        var response = await _client.GetAsync("/api/projects/non-existent-id/pull-requests/open");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    // --- GET /api/projects/{projectId}/pull-requests/merged ---

    [Test]
    public async Task GetMerged_ReturnsOk_WithList()
    {
        // Arrange
        var project = await CreateProject();

        // Act
        var response = await _client.GetAsync($"/api/projects/{project.Id}/pull-requests/merged");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var prs = await response.Content.ReadFromJsonAsync<List<PullRequestWithTime>>(JsonOptions);
        Assert.That(prs, Is.Not.Null);
    }

    [Test]
    public async Task GetMerged_ReturnsNotFound_WhenProjectDoesNotExist()
    {
        // Act
        var response = await _client.GetAsync("/api/projects/non-existent-id/pull-requests/merged");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    // --- GET /api/projects/{projectId}/pull-requests/merged/{prNumber} ---

    [Test]
    public async Task GetMergedByNumber_ReturnsNotFound_WhenProjectDoesNotExist()
    {
        // Act
        var response = await _client.GetAsync("/api/projects/non-existent-id/pull-requests/merged/999");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GetMergedByNumber_ReturnsNotFound_WhenPrDoesNotExist()
    {
        // Arrange
        var project = await CreateProject();

        // Act
        var response = await _client.GetAsync($"/api/projects/{project.Id}/pull-requests/merged/99999");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    // --- POST /api/projects/{projectId}/sync ---

    [Test]
    public async Task Sync_ReturnsOk_ForExistingProject()
    {
        // Arrange
        var project = await CreateProject();

        // Act
        var response = await _client.PostAsync($"/api/projects/{project.Id}/sync", null);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<SyncResult>(JsonOptions);
        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public async Task Sync_ReturnsNotFound_WhenProjectDoesNotExist()
    {
        // Act
        var response = await _client.PostAsync("/api/projects/non-existent-id/sync", null);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    // --- POST /api/projects/{projectId}/full-refresh ---

    [Test]
    public async Task FullRefresh_ReturnsOk_ForExistingProject()
    {
        // Arrange
        var project = await CreateProject();

        // Act
        var response = await _client.PostAsync($"/api/projects/{project.Id}/full-refresh", null);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<FullRefreshResult>(JsonOptions);
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.RefreshedAt, Is.Not.EqualTo(default(DateTime)));
        });
    }

    [Test]
    public async Task FullRefresh_ReturnsNotFound_WhenProjectDoesNotExist()
    {
        // Act
        var response = await _client.PostAsync("/api/projects/non-existent-id/full-refresh", null);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
