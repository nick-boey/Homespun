using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Shared.Models.Projects;
using Homespun.Shared.Models.Sessions;
using Homespun.Shared.Requests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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

    // GET /api/sessions/history/{projectId}/{entityId} and
    // GET /api/sessions/{id}/cached-messages were retired along with MessageCacheStore
    // and ClaudeMessage. Refresh-replay now goes through
    // GET /api/sessions/{id}/events (see SessionEventsApiTests).

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

        // Assert — SessionLifecycleService removes the session from the store on stop.
        var getResponse = await _client.GetAsync($"/api/sessions/{session.Id}");
        Assert.That(getResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    // -------------------------------------------------------------------
    // FI-4: POST /api/sessions surfaces initial-message dispatch failures
    // (close-out-claude-agent-sessions-migration-gaps).
    // -------------------------------------------------------------------

    [Test]
    public async Task Create_WithInitialMessage_ReturnsCreated_WhenDispatchSucceeds()
    {
        // Default mock pipeline accepts the initial message — happy path.
        var entityId = "entity-" + Guid.NewGuid().ToString("N")[..8];
        var request = new CreateSessionRequest
        {
            EntityId = entityId,
            ProjectId = _projectId,
            Mode = SessionMode.Build,
            InitialMessage = "Hello, Claude",
        };

        var response = await _client.PostAsJsonAsync("/api/sessions", request, JsonOptions);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var session = await response.Content.ReadFromJsonAsync<ClaudeSession>(JsonOptions);
        Assert.That(session, Is.Not.Null);
        Assert.That(session!.EntityId, Is.EqualTo(entityId));
    }

    [Test]
    public async Task Create_WithInitialMessage_Returns500WithSessionId_WhenDispatchThrows()
    {
        using var factory = new SessionsCreateOverrideFactory(SessionsCreateOverrideFactory.Behavior.Throw);
        using var client = factory.CreateClient();

        // Create the project anew on the override factory.
        var projectId = await CreateProjectAsync(client);

        var request = new CreateSessionRequest
        {
            EntityId = "entity-" + Guid.NewGuid().ToString("N")[..8],
            ProjectId = projectId,
            Mode = SessionMode.Build,
            InitialMessage = "Trigger a worker rejection",
        };

        var response = await client.PostAsJsonAsync("/api/sessions", request, JsonOptions);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError),
            "Dispatch failure must surface as a 5xx, not 201/202");
        var session = await response.Content.ReadFromJsonAsync<ClaudeSession>(JsonOptions);
        Assert.That(session, Is.Not.Null,
            "Response body should include the session DTO so the caller has the session id");
        Assert.That(session!.Id, Is.Not.Empty);
    }

    [Test]
    public async Task Create_WithInitialMessage_Returns202WithSessionId_WhenDispatchTimesOut()
    {
        using var factory = new SessionsCreateOverrideFactory(SessionsCreateOverrideFactory.Behavior.HangUntilCancel);
        using var client = factory.CreateClient();

        var projectId = await CreateProjectAsync(client);

        var request = new CreateSessionRequest
        {
            EntityId = "entity-" + Guid.NewGuid().ToString("N")[..8],
            ProjectId = projectId,
            Mode = SessionMode.Build,
            InitialMessage = "Worker is unreachable",
        };

        var response = await client.PostAsJsonAsync("/api/sessions", request, JsonOptions);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted),
            "Dispatch timeout must surface as 202 Accepted");
        var session = await response.Content.ReadFromJsonAsync<ClaudeSession>(JsonOptions);
        Assert.That(session, Is.Not.Null);
        Assert.That(session!.Id, Is.Not.Empty,
            "Response body should include the session id so the client can subscribe to its event stream");
    }

    private async Task<string> CreateProjectAsync(HttpClient client)
    {
        var createProjectRequest = new { Name = "sessions-test-" + Guid.NewGuid().ToString("N")[..8] };
        var projectResponse = await client.PostAsJsonAsync("/api/projects", createProjectRequest, JsonOptions);
        projectResponse.EnsureSuccessStatusCode();
        var project = await projectResponse.Content.ReadFromJsonAsync<Project>(JsonOptions);
        return project!.Id;
    }

    /// <summary>
    /// Test factory that swaps <see cref="IClaudeSessionService"/> for a stub that
    /// either throws on <c>SendMessageAsync</c> or hangs until cancelled, so the
    /// FI-4 dispatch-failure / dispatch-timeout paths in
    /// <c>SessionsController.Create</c> can be exercised under
    /// <see cref="WebApplicationFactory{TEntryPoint}"/>.
    /// </summary>
    private sealed class SessionsCreateOverrideFactory : HomespunWebApplicationFactory
    {
        public enum Behavior { Throw, HangUntilCancel }

        private readonly Behavior _behavior;

        public SessionsCreateOverrideFactory(Behavior behavior)
        {
            _behavior = behavior;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            // Tighten dispatch timeout to keep the timeout test fast. Use
            // ConfigureAppConfiguration so the override beats appsettings.json.
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["SessionEvents:DispatchTimeoutSeconds"] = "1",
                });
            });
            builder.ConfigureTestServices(services =>
            {
                // Drop the existing IClaudeSessionService → ClaudeSessionService
                // registration, register the concrete type so DI can still build
                // it (with all its inner deps), and re-bind the interface to the
                // stub that wraps the concrete instance.
                var existing = services.FirstOrDefault(s => s.ServiceType == typeof(IClaudeSessionService));
                if (existing != null) services.Remove(existing);
                services.AddSingleton<ClaudeSessionService>();
                services.AddSingleton<IClaudeSessionService>(sp =>
                    new StubSessionService(sp.GetRequiredService<ClaudeSessionService>(), _behavior));
            });
        }
    }

    /// <summary>
    /// Wraps the real <see cref="ClaudeSessionService"/> and overrides
    /// <c>SendMessageAsync</c> to simulate the FI-4 failure modes.
    /// </summary>
    private sealed class StubSessionService : IClaudeSessionService
    {
        private readonly IClaudeSessionService _inner;
        private readonly SessionsCreateOverrideFactory.Behavior _behavior;

        public StubSessionService(IClaudeSessionService inner, SessionsCreateOverrideFactory.Behavior behavior)
        {
            _inner = inner;
            _behavior = behavior;
        }

        public Task SendMessageAsync(string sessionId, string message, CancellationToken cancellationToken = default)
            => SendMessageAsync(sessionId, message, SessionMode.Build, null, cancellationToken);

        public Task SendMessageAsync(string sessionId, string message, SessionMode mode, CancellationToken cancellationToken = default)
            => SendMessageAsync(sessionId, message, mode, null, cancellationToken);

        public async Task SendMessageAsync(string sessionId, string message, SessionMode mode, string? model, CancellationToken cancellationToken = default)
        {
            if (_behavior == SessionsCreateOverrideFactory.Behavior.Throw)
            {
                throw new InvalidOperationException("simulated worker rejection");
            }

            // Hang until the caller's bounded-timeout fires.
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        public Task<ClaudeSession> StartSessionAsync(string entityId, string projectId, string workingDirectory, SessionMode mode, string model, string? systemPrompt = null, CancellationToken cancellationToken = default)
            => _inner.StartSessionAsync(entityId, projectId, workingDirectory, mode, model, systemPrompt, cancellationToken);
        public Task<ClaudeSession> ResumeSessionAsync(string sessionId, string entityId, string projectId, string workingDirectory, CancellationToken cancellationToken = default)
            => _inner.ResumeSessionAsync(sessionId, entityId, projectId, workingDirectory, cancellationToken);
        public Task<IReadOnlyList<ResumableSession>> GetResumableSessionsAsync(string entityId, string workingDirectory, CancellationToken cancellationToken = default)
            => _inner.GetResumableSessionsAsync(entityId, workingDirectory, cancellationToken);
        public Task ClearContextAsync(string sessionId, CancellationToken cancellationToken = default)
            => _inner.ClearContextAsync(sessionId, cancellationToken);
        public Task ExecutePlanAsync(string sessionId, bool clearContext = true, CancellationToken cancellationToken = default)
            => _inner.ExecutePlanAsync(sessionId, clearContext, cancellationToken);
        public Task StopSessionAsync(string sessionId, CancellationToken cancellationToken = default)
            => _inner.StopSessionAsync(sessionId, cancellationToken);
        public Task<int> StopAllSessionsForEntityAsync(string entityId, CancellationToken cancellationToken = default)
            => _inner.StopAllSessionsForEntityAsync(entityId, cancellationToken);
        public Task InterruptSessionAsync(string sessionId, CancellationToken cancellationToken = default)
            => _inner.InterruptSessionAsync(sessionId, cancellationToken);
        public ClaudeSession? GetSession(string sessionId) => _inner.GetSession(sessionId);
        public ClaudeSession? GetSessionByEntityId(string entityId) => _inner.GetSessionByEntityId(entityId);
        public IReadOnlyList<ClaudeSession> GetSessionsForProject(string projectId) => _inner.GetSessionsForProject(projectId);
        public IReadOnlyList<ClaudeSession> GetAllSessions() => _inner.GetAllSessions();
        public Task AnswerQuestionAsync(string sessionId, Dictionary<string, string> answers, CancellationToken cancellationToken = default)
            => _inner.AnswerQuestionAsync(sessionId, answers, cancellationToken);
        public Task ApprovePlanAsync(string sessionId, bool approved, bool keepContext, string? feedback = null, CancellationToken cancellationToken = default)
            => _inner.ApprovePlanAsync(sessionId, approved, keepContext, feedback, cancellationToken);
        public Task<AgentStartCheckResult> CheckCloneStateAsync(string workingDirectory, CancellationToken cancellationToken = default)
            => _inner.CheckCloneStateAsync(workingDirectory, cancellationToken);
        public Task<ClaudeSession> StartSessionWithTerminationAsync(string entityId, string projectId, string workingDirectory, SessionMode mode, string model, bool terminateExisting, string? systemPrompt = null, CancellationToken cancellationToken = default)
            => _inner.StartSessionWithTerminationAsync(entityId, projectId, workingDirectory, mode, model, terminateExisting, systemPrompt, cancellationToken);
        public Task<ClaudeSession?> RestartSessionAsync(string sessionId, CancellationToken cancellationToken = default)
            => _inner.RestartSessionAsync(sessionId, cancellationToken);
        public Task<string> AcceptIssueChangesAsync(string sessionId, CancellationToken cancellationToken = default)
            => _inner.AcceptIssueChangesAsync(sessionId, cancellationToken);
        public Task<string> CancelIssueChangesAsync(string sessionId, CancellationToken cancellationToken = default)
            => _inner.CancelIssueChangesAsync(sessionId, cancellationToken);
        public Task SetSessionModeAsync(string sessionId, SessionMode mode, CancellationToken cancellationToken = default)
            => _inner.SetSessionModeAsync(sessionId, mode, cancellationToken);
        public Task SetSessionModelAsync(string sessionId, string model, CancellationToken cancellationToken = default)
            => _inner.SetSessionModelAsync(sessionId, model, cancellationToken);
        public Task<ClaudeSession> ClearContextAndStartNewAsync(string currentSessionId, string? initialPrompt = null, CancellationToken cancellationToken = default)
            => _inner.ClearContextAndStartNewAsync(currentSessionId, initialPrompt, cancellationToken);
    }
}
