using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Homespun.Features.GitHub.Controllers;
using GenerateBranchIdRequest = Homespun.Features.AgentOrchestration.Controllers.GenerateBranchIdRequest;
using GenerateBranchIdResponse = Homespun.Features.AgentOrchestration.Controllers.GenerateBranchIdResponse;
using FileListResponse = Homespun.Features.Search.Controllers.FileListResponse;
using PrListResponse = Homespun.Features.Search.Controllers.PrListResponse;
using Homespun.Shared.Models.Containers;
using Homespun.Shared.Models.GitHub;
using Homespun.Shared.Models.Notifications;
using Homespun.Shared.Models.Plans;
using Homespun.Shared.Models.Projects;
using Homespun.Shared.Models.Secrets;
using Homespun.Shared.Requests;

namespace Homespun.Api.Tests.Features;

[TestFixture]
public class NotificationsApiTests
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

    [Test]
    public async Task GetAll_ReturnsOk_WithEmptyList()
    {
        var response = await _client.GetAsync("/api/notifications");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var notifications = await response.Content.ReadFromJsonAsync<List<NotificationDto>>(JsonOptions);
        Assert.That(notifications, Is.Not.Null);
    }

    [Test]
    public async Task Create_ReturnsCreated_WithValidRequest()
    {
        var request = new CreateNotificationRequest
        {
            Type = NotificationType.Info,
            Title = "Test Notification",
            Message = "This is a test notification"
        };

        var response = await _client.PostAsJsonAsync("/api/notifications", request, JsonOptions);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var notification = await response.Content.ReadFromJsonAsync<NotificationDto>(JsonOptions);
        Assert.Multiple(() =>
        {
            Assert.That(notification, Is.Not.Null);
            Assert.That(notification!.Title, Is.EqualTo("Test Notification"));
            Assert.That(notification.Message, Is.EqualTo("This is a test notification"));
            Assert.That(notification.Type, Is.EqualTo(NotificationType.Info));
        });
    }

    [Test]
    public async Task Create_WithDeduplicationKey_ReturnsCreated()
    {
        var request = new CreateNotificationRequest
        {
            Type = NotificationType.Warning,
            Title = "Dedup Test",
            Message = "Test message",
            DeduplicationKey = "test-key-" + Guid.NewGuid().ToString("N")[..8]
        };

        var response = await _client.PostAsJsonAsync("/api/notifications", request, JsonOptions);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var notification = await response.Content.ReadFromJsonAsync<NotificationDto>(JsonOptions);
        Assert.That(notification!.DeduplicationKey, Is.EqualTo(request.DeduplicationKey));
    }

    [Test]
    public async Task CrudFlow_CreateListDismiss()
    {
        // Create
        var request = new CreateNotificationRequest
        {
            Type = NotificationType.ActionRequired,
            Title = "CRUD Test",
            Message = "Will be dismissed"
        };
        var createResponse = await _client.PostAsJsonAsync("/api/notifications", request, JsonOptions);
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<NotificationDto>(JsonOptions);

        // List and verify it exists
        var listResponse = await _client.GetAsync("/api/notifications");
        Assert.That(listResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var notifications = await listResponse.Content.ReadFromJsonAsync<List<NotificationDto>>(JsonOptions);
        Assert.That(notifications!.Any(n => n.Id == created!.Id), Is.True);

        // Dismiss by ID
        var dismissResponse = await _client.DeleteAsync($"/api/notifications/{created!.Id}");
        Assert.That(dismissResponse.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task DismissById_ReturnsNoContent_EvenWhenNotFound()
    {
        var response = await _client.DeleteAsync("/api/notifications/non-existent-id");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task DismissByKey_ReturnsNoContent()
    {
        // Create with a key first
        var key = "dismiss-key-" + Guid.NewGuid().ToString("N")[..8];
        var request = new CreateNotificationRequest
        {
            Title = "Key Test",
            Message = "Will be dismissed by key",
            DeduplicationKey = key
        };
        await _client.PostAsJsonAsync("/api/notifications", request, JsonOptions);

        // Dismiss by key
        var response = await _client.DeleteAsync($"/api/notifications/by-key/{key}");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task GetAll_WithProjectIdFilter_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/notifications?projectId=some-project");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var notifications = await response.Content.ReadFromJsonAsync<List<NotificationDto>>(JsonOptions);
        Assert.That(notifications, Is.Not.Null);
    }
}

[TestFixture]
public class SettingsApiTests
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

    [Test]
    public async Task GetUserSettings_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/settings/user");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var settings = await response.Content.ReadFromJsonAsync<UserSettingsResponse>(JsonOptions);
        Assert.That(settings, Is.Not.Null);
    }

    [Test]
    public async Task UpdateUserEmail_ReturnsOk_WithValidEmail()
    {
        var request = new UpdateUserEmailRequest { Email = "test@example.com" };

        var response = await _client.PutAsJsonAsync("/api/settings/user/email", request, JsonOptions);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var settings = await response.Content.ReadFromJsonAsync<UserSettingsResponse>(JsonOptions);
        Assert.That(settings!.UserEmail, Is.EqualTo("test@example.com"));
    }

    [Test]
    public async Task UpdateUserEmail_ReturnsBadRequest_WithEmptyEmail()
    {
        var request = new UpdateUserEmailRequest { Email = "" };

        var response = await _client.PutAsJsonAsync("/api/settings/user/email", request, JsonOptions);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task UpdateUserEmail_ReturnsBadRequest_WithInvalidFormat()
    {
        var request = new UpdateUserEmailRequest { Email = "not-an-email" };

        var response = await _client.PutAsJsonAsync("/api/settings/user/email", request, JsonOptions);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task UpdateUserEmail_PersistsChange()
    {
        var email = $"persist-{Guid.NewGuid().ToString("N")[..8]}@example.com";
        var request = new UpdateUserEmailRequest { Email = email };
        await _client.PutAsJsonAsync("/api/settings/user/email", request, JsonOptions);

        var response = await _client.GetAsync("/api/settings/user");
        var settings = await response.Content.ReadFromJsonAsync<UserSettingsResponse>(JsonOptions);
        Assert.That(settings!.UserEmail, Is.EqualTo(email));
    }
}

[TestFixture]
public class SecretsApiTests
{
    private HomespunWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;
    private string _projectId = null!;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _factory = new HomespunWebApplicationFactory();
        _client = _factory.CreateClient();

        var createProjectRequest = new { Name = "secrets-test-" + Guid.NewGuid().ToString("N")[..8] };
        var projectResponse = await _client.PostAsJsonAsync("/api/projects", createProjectRequest, JsonOptions);
        projectResponse.EnsureSuccessStatusCode();
        var project = await projectResponse.Content.ReadFromJsonAsync<Project>(JsonOptions);
        _projectId = project!.Id;
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Test]
    public async Task GetSecrets_ReturnsOk_WithEmptyList()
    {
        var response = await _client.GetAsync($"/api/projects/{_projectId}/secrets");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var secrets = await response.Content.ReadFromJsonAsync<SecretsListResponse>(JsonOptions);
        Assert.That(secrets, Is.Not.Null);
        Assert.That(secrets!.Secrets, Is.Not.Null);
    }

    [Test]
    public async Task CreateSecret_ReturnsCreated()
    {
        var request = new CreateSecretRequest
        {
            Name = "TEST_SECRET_" + Guid.NewGuid().ToString("N")[..8].ToUpper(),
            Value = "secret-value"
        };

        var response = await _client.PostAsJsonAsync($"/api/projects/{_projectId}/secrets", request, JsonOptions);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
    }

    [Test]
    public async Task CrudFlow_CreateUpdateDelete()
    {
        var secretName = "CRUD_SECRET_" + Guid.NewGuid().ToString("N")[..8].ToUpper();

        // Create
        var createRequest = new CreateSecretRequest { Name = secretName, Value = "initial-value" };
        var createResponse = await _client.PostAsJsonAsync($"/api/projects/{_projectId}/secrets", createRequest, JsonOptions);
        Assert.That(createResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        // Verify it appears in list
        var listResponse = await _client.GetAsync($"/api/projects/{_projectId}/secrets");
        var secrets = await listResponse.Content.ReadFromJsonAsync<SecretsListResponse>(JsonOptions);
        Assert.That(secrets!.Secrets.Any(s => s.Name == secretName), Is.True);

        // Update
        var updateRequest = new UpdateSecretRequest { Value = "updated-value" };
        var updateResponse = await _client.PutAsJsonAsync($"/api/projects/{_projectId}/secrets/{secretName}", updateRequest, JsonOptions);
        Assert.That(updateResponse.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        // Delete
        var deleteResponse = await _client.DeleteAsync($"/api/projects/{_projectId}/secrets/{secretName}");
        Assert.That(deleteResponse.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task DeleteSecret_ReturnsNotFound_WhenDoesNotExist()
    {
        var response = await _client.DeleteAsync($"/api/projects/{_projectId}/secrets/NONEXISTENT_SECRET");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}

[TestFixture]
public class GitHubInfoApiTests
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

    [Test]
    public async Task GetStatus_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/github/status");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var status = await response.Content.ReadFromJsonAsync<GitHubStatusResponse>(JsonOptions);
        Assert.That(status, Is.Not.Null);
    }

    [Test]
    public async Task GetAuthStatus_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/github/auth-status");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var status = await response.Content.ReadFromJsonAsync<GitHubAuthStatus>(JsonOptions);
        Assert.That(status, Is.Not.Null);
    }

    [Test]
    public async Task GetGitConfig_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/github/git-config");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var config = await response.Content.ReadFromJsonAsync<GitConfigResponse>(JsonOptions);
        Assert.That(config, Is.Not.Null);
    }
}

[TestFixture]
public class ContainersApiTests
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

    [Test]
    public async Task GetAll_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/containers");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var containers = await response.Content.ReadFromJsonAsync<List<WorkerContainerDto>>(JsonOptions);
        Assert.That(containers, Is.Not.Null);
    }

    [Test]
    public async Task Delete_ReturnsNotFound_WhenDoesNotExist()
    {
        var response = await _client.DeleteAsync("/api/containers/non-existent-container-id");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}

[TestFixture]
public class PlansApiTests
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

    [Test]
    public async Task GetPlanFiles_ReturnsOk_WithValidDirectory()
    {
        var response = await _client.GetAsync("/api/plans?workingDirectory=/tmp/test");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var plans = await response.Content.ReadFromJsonAsync<List<PlanFileInfo>>(JsonOptions);
        Assert.That(plans, Is.Not.Null);
    }

    [Test]
    public async Task GetPlanFiles_ReturnsBadRequest_WithEmptyDirectory()
    {
        var response = await _client.GetAsync("/api/plans?workingDirectory=");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task GetPlanContent_ReturnsBadRequest_WithEmptyDirectory()
    {
        var response = await _client.GetAsync("/api/plans/content?workingDirectory=&fileName=test.md");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task GetPlanContent_ReturnsBadRequest_WithEmptyFileName()
    {
        var response = await _client.GetAsync("/api/plans/content?workingDirectory=/tmp/test&fileName=");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task GetPlanContent_ReturnsNotFound_WhenFileDoesNotExist()
    {
        var response = await _client.GetAsync("/api/plans/content?workingDirectory=/tmp/test&fileName=nonexistent.md");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}

[TestFixture]
public class ProjectSearchApiTests
{
    private HomespunWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;
    private string _projectId = null!;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _factory = new HomespunWebApplicationFactory();
        _client = _factory.CreateClient();

        var createProjectRequest = new { Name = "search-test-" + Guid.NewGuid().ToString("N")[..8] };
        var projectResponse = await _client.PostAsJsonAsync("/api/projects", createProjectRequest, JsonOptions);
        projectResponse.EnsureSuccessStatusCode();
        var project = await projectResponse.Content.ReadFromJsonAsync<Project>(JsonOptions);
        _projectId = project!.Id;
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Test]
    public async Task SearchFiles_ReturnsOk_ForExistingProject()
    {
        var response = await _client.GetAsync($"/api/projects/{_projectId}/search/files");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<FileListResponse>(JsonOptions);
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Files, Is.Not.Null);
            Assert.That(result.Hash, Is.Not.Null);
        });
    }

    [Test]
    public async Task SearchFiles_ReturnsNotFound_ForNonExistentProject()
    {
        var response = await _client.GetAsync("/api/projects/non-existent-project/search/files");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task SearchPrs_ReturnsOk_ForExistingProject()
    {
        var response = await _client.GetAsync($"/api/projects/{_projectId}/search/prs");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<PrListResponse>(JsonOptions);
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Prs, Is.Not.Null);
            Assert.That(result.Hash, Is.Not.Null);
        });
    }

    [Test]
    public async Task SearchPrs_ReturnsNotFound_ForNonExistentProject()
    {
        var response = await _client.GetAsync("/api/projects/non-existent-project/search/prs");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}

[TestFixture]
public class OrchestrationApiTests
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

    [Test]
    public async Task GenerateBranchId_ReturnsOk_WithValidTitle()
    {
        var request = new GenerateBranchIdRequest("Add user authentication feature");

        var response = await _client.PostAsJsonAsync("/api/orchestration/generate-branch-id", request, JsonOptions);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<GenerateBranchIdResponse>(JsonOptions);
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Success, Is.True);
            Assert.That(result.BranchId, Is.Not.Null.And.Not.Empty);
        });
    }

    [Test]
    public async Task GenerateBranchId_ReturnsBadRequest_WithEmptyTitle()
    {
        var request = new GenerateBranchIdRequest("");

        var response = await _client.PostAsJsonAsync("/api/orchestration/generate-branch-id", request, JsonOptions);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }
}

[TestFixture]
public class IssuePrStatusApiTests
{
    private HomespunWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;
    private string _projectId = null!;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _factory = new HomespunWebApplicationFactory();
        _client = _factory.CreateClient();

        var createProjectRequest = new { Name = "pr-status-test-" + Guid.NewGuid().ToString("N")[..8] };
        var projectResponse = await _client.PostAsJsonAsync("/api/projects", createProjectRequest, JsonOptions);
        projectResponse.EnsureSuccessStatusCode();
        var project = await projectResponse.Content.ReadFromJsonAsync<Project>(JsonOptions);
        _projectId = project!.Id;
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Test]
    public async Task GetPrStatus_ReturnsNotFound_WhenIssueHasNoPr()
    {
        var response = await _client.GetAsync($"/api/projects/{_projectId}/issues/non-existent-issue/pr-status");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
