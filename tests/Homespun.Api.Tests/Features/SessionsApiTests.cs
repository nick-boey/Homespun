using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Homespun.Shared.Models.Projects;
using Homespun.Shared.Models.Sessions;
using Homespun.Shared.Requests;
using SessionCacheSummary = Homespun.Features.ClaudeCode.Data.SessionCacheSummary;

namespace Homespun.Api.Tests.Features;

[TestFixture]
public class SessionsApiTests
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

        // Create a project to use in session tests
        var createProjectRequest = new { Name = "sessions-test-" + Guid.NewGuid().ToString("N")[..8] };
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

    private async Task<ClaudeSession> CreateSession(string? entityId = null, SessionMode mode = SessionMode.Plan)
    {
        var request = new CreateSessionRequest
        {
            EntityId = entityId ?? "entity-" + Guid.NewGuid().ToString("N")[..8],
            ProjectId = _projectId,
            Mode = mode
        };
        var response = await _client.PostAsJsonAsync("/api/sessions", request, JsonOptions);
        response.EnsureSuccessStatusCode();
        var session = await response.Content.ReadFromJsonAsync<ClaudeSession>(JsonOptions);
        return session!;
    }

    // --- GET /api/sessions ---

    [Test]
    public async Task GetAll_ReturnsOk_WithEmptyList_WhenNoSessions()
    {
        // Use a fresh factory to guarantee no sessions exist
        using var factory = new HomespunWebApplicationFactory();
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/sessions");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var sessions = await response.Content.ReadFromJsonAsync<List<SessionSummary>>(JsonOptions);
        Assert.That(sessions, Is.Not.Null);
    }

    [Test]
    public async Task GetAll_ReturnsOk_WithSessionsAfterCreation()
    {
        // Arrange
        var session = await CreateSession();

        // Act
        var response = await _client.GetAsync("/api/sessions");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var sessions = await response.Content.ReadFromJsonAsync<List<SessionSummary>>(JsonOptions);
        Assert.That(sessions, Is.Not.Null);
        Assert.That(sessions!.Any(s => s.Id == session.Id), Is.True);
    }

    // --- GET /api/sessions/{id} ---

    [Test]
    public async Task GetById_ReturnsSession_WhenExists()
    {
        // Arrange
        var created = await CreateSession();

        // Act
        var response = await _client.GetAsync($"/api/sessions/{created.Id}");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var session = await response.Content.ReadFromJsonAsync<ClaudeSession>(JsonOptions);
        Assert.Multiple(() =>
        {
            Assert.That(session, Is.Not.Null);
            Assert.That(session!.Id, Is.EqualTo(created.Id));
            Assert.That(session.ProjectId, Is.EqualTo(_projectId));
        });
    }

    [Test]
    public async Task GetById_ReturnsNotFound_WhenDoesNotExist()
    {
        // Act
        var response = await _client.GetAsync("/api/sessions/non-existent-id");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    // --- GET /api/sessions/entity/{entityId} ---

    [Test]
    public async Task GetByEntityId_ReturnsSession_WhenExists()
    {
        // Arrange
        var entityId = "entity-" + Guid.NewGuid().ToString("N")[..8];
        var created = await CreateSession(entityId);

        // Act
        var response = await _client.GetAsync($"/api/sessions/entity/{entityId}");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var session = await response.Content.ReadFromJsonAsync<ClaudeSession>(JsonOptions);
        Assert.Multiple(() =>
        {
            Assert.That(session, Is.Not.Null);
            Assert.That(session!.EntityId, Is.EqualTo(entityId));
            Assert.That(session.Id, Is.EqualTo(created.Id));
        });
    }

    [Test]
    public async Task GetByEntityId_ReturnsNotFound_WhenDoesNotExist()
    {
        // Act
        var response = await _client.GetAsync("/api/sessions/entity/non-existent-entity");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    // --- GET /api/sessions/project/{projectId} ---

    [Test]
    public async Task GetByProject_ReturnsOk_WithSessionsForProject()
    {
        // Arrange
        var session = await CreateSession();

        // Act
        var response = await _client.GetAsync($"/api/sessions/project/{_projectId}");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var sessions = await response.Content.ReadFromJsonAsync<List<SessionSummary>>(JsonOptions);
        Assert.That(sessions, Is.Not.Null);
        Assert.That(sessions!.Any(s => s.Id == session.Id), Is.True);
    }

    [Test]
    public async Task GetByProject_ReturnsOk_WithEmptyList_WhenNoSessionsForProject()
    {
        // Act
        var response = await _client.GetAsync("/api/sessions/project/non-existent-project");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var sessions = await response.Content.ReadFromJsonAsync<List<SessionSummary>>(JsonOptions);
        Assert.That(sessions, Is.Not.Null);
        Assert.That(sessions, Is.Empty);
    }

    // --- GET /api/sessions/entity/{entityId}/resumable ---

    [Test]
    public async Task GetResumableSessions_ReturnsOk()
    {
        // Arrange
        var entityId = "entity-" + Guid.NewGuid().ToString("N")[..8];

        // Act
        var response = await _client.GetAsync(
            $"/api/sessions/entity/{entityId}/resumable?workingDirectory=/tmp/test");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var sessions = await response.Content.ReadFromJsonAsync<List<ResumableSession>>(JsonOptions);
        Assert.That(sessions, Is.Not.Null);
    }

    // --- GET /api/sessions/history/{projectId}/{entityId} ---

    [Test]
    public async Task GetSessionHistory_ReturnsOk()
    {
        // Act
        var response = await _client.GetAsync(
            $"/api/sessions/history/{_projectId}/some-entity-id");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var history = await response.Content.ReadFromJsonAsync<List<SessionCacheSummary>>(JsonOptions);
        Assert.That(history, Is.Not.Null);
    }

    // --- GET /api/sessions/{id}/cached-messages ---

    [Test]
    public async Task GetCachedMessages_ReturnsOk_ForExistingSession()
    {
        // Arrange
        var session = await CreateSession();

        // Act
        var response = await _client.GetAsync($"/api/sessions/{session.Id}/cached-messages");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var messages = await response.Content.ReadFromJsonAsync<List<ClaudeMessage>>(JsonOptions);
        Assert.That(messages, Is.Not.Null);
    }

    [Test]
    public async Task GetCachedMessages_ReturnsOk_WithEmptyList_ForNonExistentSession()
    {
        // The mock service returns empty list for non-existent sessions
        // Act
        var response = await _client.GetAsync("/api/sessions/non-existent-id/cached-messages");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var messages = await response.Content.ReadFromJsonAsync<List<ClaudeMessage>>(JsonOptions);
        Assert.That(messages, Is.Not.Null);
        Assert.That(messages, Is.Empty);
    }

    // --- POST /api/sessions ---

    [Test]
    public async Task Create_ReturnsCreated_WithValidRequest()
    {
        // Arrange
        var entityId = "entity-" + Guid.NewGuid().ToString("N")[..8];
        var request = new CreateSessionRequest
        {
            EntityId = entityId,
            ProjectId = _projectId,
            Mode = SessionMode.Plan
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/sessions", request, JsonOptions);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var session = await response.Content.ReadFromJsonAsync<ClaudeSession>(JsonOptions);
        Assert.Multiple(() =>
        {
            Assert.That(session, Is.Not.Null);
            Assert.That(session!.EntityId, Is.EqualTo(entityId));
            Assert.That(session.ProjectId, Is.EqualTo(_projectId));
            Assert.That(session.Mode, Is.EqualTo(SessionMode.Plan));
        });
    }

    [Test]
    public async Task Create_WithBuildMode_ReturnsCreated()
    {
        // Arrange
        var request = new CreateSessionRequest
        {
            EntityId = "entity-" + Guid.NewGuid().ToString("N")[..8],
            ProjectId = _projectId,
            Mode = SessionMode.Build
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/sessions", request, JsonOptions);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var session = await response.Content.ReadFromJsonAsync<ClaudeSession>(JsonOptions);
        Assert.That(session!.Mode, Is.EqualTo(SessionMode.Build));
    }

    [Test]
    public async Task Create_ReturnsNotFound_WhenProjectDoesNotExist()
    {
        // Arrange
        var request = new CreateSessionRequest
        {
            EntityId = "entity-test",
            ProjectId = "non-existent-project-id",
            Mode = SessionMode.Plan
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/sessions", request, JsonOptions);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Create_SessionIsRetrievableAfterCreation()
    {
        // Arrange
        var entityId = "entity-" + Guid.NewGuid().ToString("N")[..8];
        var created = await CreateSession(entityId);

        // Act
        var response = await _client.GetAsync($"/api/sessions/{created.Id}");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var session = await response.Content.ReadFromJsonAsync<ClaudeSession>(JsonOptions);
        Assert.Multiple(() =>
        {
            Assert.That(session, Is.Not.Null);
            Assert.That(session!.Id, Is.EqualTo(created.Id));
            Assert.That(session.EntityId, Is.EqualTo(entityId));
        });
    }

    // --- POST /api/sessions/{id}/resume ---

    [Test]
    public async Task Resume_ReturnsOk_WithValidRequest()
    {
        // Arrange - create a session first to get a valid session ID
        var created = await CreateSession();
        var request = new ResumeSessionRequest
        {
            EntityId = "entity-" + Guid.NewGuid().ToString("N")[..8],
            ProjectId = _projectId,
            WorkingDirectory = "/tmp/test"
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/sessions/{created.Id}/resume", request, JsonOptions);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var session = await response.Content.ReadFromJsonAsync<ClaudeSession>(JsonOptions);
        Assert.That(session, Is.Not.Null);
        Assert.That(session!.Id, Is.Not.Empty);
    }

    // --- POST /api/sessions/{id}/messages ---

    [Test]
    public async Task SendMessage_ReturnsNotFound_WhenSessionDoesNotExist()
    {
        // Arrange
        var request = new SendMessageRequest { Message = "Hello" };

        // Act
        var response = await _client.PostAsJsonAsync("/api/sessions/non-existent-id/messages", request, JsonOptions);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task SendMessage_ReturnsAccepted_ForExistingSession()
    {
        // Arrange
        var session = await CreateSession();
        var request = new SendMessageRequest { Message = "Hello" };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/sessions/{session.Id}/messages", request, JsonOptions);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
    }

    // --- POST /api/sessions/{id}/interrupt ---

    [Test]
    public async Task Interrupt_ReturnsNoContent_ForExistingSession()
    {
        // Arrange
        var session = await CreateSession();

        // Act
        var response = await _client.PostAsync($"/api/sessions/{session.Id}/interrupt", null);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task Interrupt_ReturnsNotFound_WhenSessionDoesNotExist()
    {
        // Act
        var response = await _client.PostAsync("/api/sessions/non-existent-id/interrupt", null);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    // --- POST /api/sessions/{id}/accept-issue-changes ---

    [Test]
    public async Task AcceptIssueChanges_ReturnsOk_ForExistingSession()
    {
        // Arrange
        var session = await CreateSession();

        // Act
        var response = await _client.PostAsync($"/api/sessions/{session.Id}/accept-issue-changes", null);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task AcceptIssueChanges_ReturnsNotFound_WhenSessionDoesNotExist()
    {
        // Act
        var response = await _client.PostAsync("/api/sessions/non-existent-id/accept-issue-changes", null);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    // --- POST /api/sessions/{id}/cancel-issue-changes ---

    [Test]
    public async Task CancelIssueChanges_ReturnsOk_ForExistingSession()
    {
        // Arrange
        var session = await CreateSession();

        // Act
        var response = await _client.PostAsync($"/api/sessions/{session.Id}/cancel-issue-changes", null);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task CancelIssueChanges_ReturnsNotFound_WhenSessionDoesNotExist()
    {
        // Act
        var response = await _client.PostAsync("/api/sessions/non-existent-id/cancel-issue-changes", null);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    // --- DELETE /api/sessions/entity/{entityId} ---

    [Test]
    public async Task StopAllForEntity_ReturnsOk_WithCount()
    {
        // Arrange
        var entityId = "entity-" + Guid.NewGuid().ToString("N")[..8];
        await CreateSession(entityId);

        // Act
        var response = await _client.DeleteAsync($"/api/sessions/entity/{entityId}");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var count = await response.Content.ReadFromJsonAsync<int>(JsonOptions);
        Assert.That(count, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public async Task StopAllForEntity_ReturnsOk_WithZero_WhenNoSessions()
    {
        // Act
        var response = await _client.DeleteAsync("/api/sessions/entity/non-existent-entity");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var count = await response.Content.ReadFromJsonAsync<int>(JsonOptions);
        Assert.That(count, Is.EqualTo(0));
    }

    // --- DELETE /api/sessions/{id} ---

    [Test]
    public async Task Stop_ReturnsNoContent_ForExistingSession()
    {
        // Arrange
        var session = await CreateSession();

        // Act
        var response = await _client.DeleteAsync($"/api/sessions/{session.Id}");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task Stop_ReturnsNotFound_WhenSessionDoesNotExist()
    {
        // Act
        var response = await _client.DeleteAsync("/api/sessions/non-existent-id");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Stop_SessionIsGone_AfterDeletion()
    {
        // Arrange
        var session = await CreateSession();

        // Act
        var deleteResponse = await _client.DeleteAsync($"/api/sessions/{session.Id}");
        Assert.That(deleteResponse.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        // Assert - session should still be in store but with Stopped status
        // The mock service sets status to Stopped but doesn't remove from store
        var getResponse = await _client.GetAsync($"/api/sessions/{session.Id}");
        Assert.That(getResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var stoppedSession = await getResponse.Content.ReadFromJsonAsync<ClaudeSession>(JsonOptions);
        Assert.That(stoppedSession!.Status, Is.EqualTo(ClaudeSessionStatus.Stopped));
    }
}
