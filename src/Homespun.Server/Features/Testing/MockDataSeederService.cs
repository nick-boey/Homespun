using Fleece.Core.Models;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.PullRequests.Data;
using Homespun.Features.Testing.Services;
using Microsoft.Extensions.Options;

namespace Homespun.Features.Testing;

/// <summary>
/// Hosted service that seeds demo data on application startup when mock mode is enabled.
/// Uses the temporary data folder service for file-based storage.
/// </summary>
public class MockDataSeederService : IHostedService
{
    private readonly IDataStore _dataStore;
    private readonly ITempDataFolderService _tempFolderService;
    private readonly FleeceIssueSeeder _fleeceIssueSeeder;
    private readonly IAgentPromptService _agentPromptService;
    private readonly IClaudeSessionStore _sessionStore;
    private readonly IToolResultParser _toolResultParser;
    private readonly IJsonlSessionLoader _jsonlSessionLoader;
    private readonly ILogger<MockDataSeederService> _logger;

    /// <summary>
    /// Default path to look for JSONL session files when running in container.
    /// The Dockerfile copies tests/data/sessions to /app/test-sessions during build.
    /// Note: We use /app/test-sessions instead of /data/sessions because /data is
    /// mounted as a volume at runtime, which would hide files copied during build.
    /// </summary>
    private const string ContainerSessionDataPath = "/app/test-sessions";

    /// <summary>
    /// Fallback path for local development - looks for test data relative to working directory.
    /// </summary>
    private const string LocalTestSessionDataPath = "tests/data/sessions";

    public MockDataSeederService(
        IDataStore dataStore,
        ITempDataFolderService tempFolderService,
        FleeceIssueSeeder fleeceIssueSeeder,
        IAgentPromptService agentPromptService,
        IClaudeSessionStore sessionStore,
        IToolResultParser toolResultParser,
        IJsonlSessionLoader jsonlSessionLoader,
        ILogger<MockDataSeederService> logger)
    {
        _dataStore = dataStore;
        _tempFolderService = tempFolderService;
        _fleeceIssueSeeder = fleeceIssueSeeder;
        _agentPromptService = agentPromptService;
        _sessionStore = sessionStore;
        _toolResultParser = toolResultParser;
        _jsonlSessionLoader = jsonlSessionLoader;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Seeding mock data for demo mode...");

        try
        {
            await SeedUserSettingsAsync();
            await SeedProjectsAsync();
            await SeedPullRequestsAsync();
            await SeedIssuesAsync();
            await SeedAgentPromptsAsync();
            await SeedSessionsAsync(cancellationToken);

            // Initialize git repos in all project folders after all files are seeded
            InitializeGitRepositories();

            _logger.LogInformation("Mock data seeding completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to seed mock data");
        }
    }

    /// <summary>
    /// Seeds sessions from JSONL files if available, otherwise falls back to hardcoded demo data.
    /// Checks both container path (/app/test-sessions) and local dev path (tests/data/sessions).
    /// </summary>
    private async Task SeedSessionsAsync(CancellationToken cancellationToken)
    {
        // Determine which path to use for session data
        var sessionDataPath = GetSessionDataPath();

        if (sessionDataPath != null)
        {
            var sessions = await _jsonlSessionLoader.LoadAllSessionsAsync(sessionDataPath, cancellationToken);
            if (sessions.Count > 0)
            {
                foreach (var session in sessions)
                {
                    _sessionStore.Add(session);
                    _logger.LogDebug("Loaded session {SessionId} from JSONL with {MessageCount} messages",
                        session.Id, session.Messages.Count);
                }
                _logger.LogInformation("Loaded {Count} sessions from JSONL files at {Path}",
                    sessions.Count, sessionDataPath);
                return;
            }
        }

        // Fall back to hardcoded demo data
        _logger.LogDebug("No JSONL sessions found, using hardcoded demo data");
        SeedDemoSessions();
    }

    /// <summary>
    /// Determines the correct path for session data based on environment:
    /// - Container: /app/test-sessions (test data copied during Docker build)
    /// - Local dev: tests/data/sessions (source test data)
    /// Returns null if no valid session data path is found.
    /// </summary>
    private string? GetSessionDataPath()
    {
        // First, try the local development path (tests/data/sessions)
        // This takes precedence because it contains the known test data
        if (Directory.Exists(LocalTestSessionDataPath))
        {
            // Verify it has the expected structure (subdirectories with .jsonl files)
            var projectDirs = Directory.GetDirectories(LocalTestSessionDataPath);
            if (projectDirs.Any(dir => Directory.GetFiles(dir, "*.jsonl").Length > 0))
            {
                _logger.LogDebug("Using local test session data path: {Path}", LocalTestSessionDataPath);
                return LocalTestSessionDataPath;
            }
        }

        // Try the container path (/data/sessions) - used when running in Docker
        if (Directory.Exists(ContainerSessionDataPath))
        {
            // Verify it has the expected structure (subdirectories with .jsonl files)
            var projectDirs = Directory.GetDirectories(ContainerSessionDataPath);
            if (projectDirs.Any(dir => Directory.GetFiles(dir, "*.jsonl").Length > 0))
            {
                _logger.LogDebug("Using container session data path: {Path}", ContainerSessionDataPath);
                return ContainerSessionDataPath;
            }
        }

        _logger.LogWarning("No session data path found. Checked: {LocalPath}, {ContainerPath}",
            LocalTestSessionDataPath, ContainerSessionDataPath);
        return null;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        // Cleanup is handled by TempDataFolderService.Dispose()
        return Task.CompletedTask;
    }

    /// <summary>
    /// Initializes git repositories in all project directories under the temp folder.
    /// Must be called after all files are seeded so the initial commit includes everything.
    /// </summary>
    private void InitializeGitRepositories()
    {
        var projectsDir = Path.Combine(_tempFolderService.RootPath, "projects");
        if (!Directory.Exists(projectsDir))
            return;

        foreach (var projectDir in Directory.GetDirectories(projectsDir))
        {
            _fleeceIssueSeeder.InitializeGitRepository(projectDir);
        }
    }

    /// <summary>
    /// Gets the local path for a mock project using the temp folder service.
    /// Creates the project directory with minimal structure if it doesn't exist.
    /// </summary>
    private string GetMockProjectPath(string projectId, string projectName)
    {
        var projectPath = _tempFolderService.GetProjectPath(projectId);

        // Create project structure if it doesn't exist
        if (!Directory.Exists(projectPath))
        {
            _fleeceIssueSeeder.CreateMinimalProjectStructure(projectPath, projectName);
        }

        return projectPath;
    }

    private async Task SeedUserSettingsAsync()
    {
        await _dataStore.SetUserEmailAsync("demo@example.com");
        _logger.LogDebug("Seeded user email: demo@example.com");
    }

    private async Task SeedProjectsAsync()
    {
        var now = DateTime.UtcNow;

        // Demo Project 1: Main demo project (most recently updated - appears first)
        var demoProject = new Project
        {
            Id = "demo-project",
            Name = "Demo Project",
            LocalPath = GetMockProjectPath("demo-project", "Demo Project"),
            GitHubOwner = "demo-org",
            GitHubRepo = "demo-project",
            DefaultBranch = "main",
            DefaultModel = "sonnet",
            UpdatedAt = now
        };
        await _dataStore.AddProjectAsync(demoProject);
        _logger.LogDebug("Seeded demo project: {ProjectName} at {Path}", demoProject.Name, demoProject.LocalPath);
    }

    private async Task SeedPullRequestsAsync()
    {
        var now = DateTime.UtcNow;

        // PR 1: In Development
        var pr1 = new PullRequest
        {
            Id = "pr-feature-auth",
            ProjectId = "demo-project",
            Title = "Add user authentication",
            Description = "Implement JWT-based authentication for the API",
            BranchName = "feature/user-auth",
            Status = OpenPullRequestStatus.InDevelopment,
            CreatedAt = now.AddDays(-3),
            UpdatedAt = now.AddHours(-2)
        };
        await _dataStore.AddPullRequestAsync(pr1);

        // PR 2: Ready for Review (has GitHub PR number)
        var pr2 = new PullRequest
        {
            Id = "pr-dark-mode",
            ProjectId = "demo-project",
            Title = "Implement dark mode",
            Description = "Add dark mode support with theme switching",
            BranchName = "feature/dark-mode",
            Status = OpenPullRequestStatus.ReadyForReview,
            GitHubPRNumber = 42,
            CreatedAt = now.AddDays(-5),
            UpdatedAt = now.AddDays(-1)
        };
        await _dataStore.AddPullRequestAsync(pr2);

        // PR 3: Approved
        var pr3 = new PullRequest
        {
            Id = "pr-api-v2",
            ProjectId = "demo-project",
            Title = "API v2 endpoints",
            Description = "New versioned API endpoints with improved response format",
            BranchName = "feature/api-v2",
            Status = OpenPullRequestStatus.Approved,
            GitHubPRNumber = 45,
            CreatedAt = now.AddDays(-7),
            UpdatedAt = now.AddHours(-6)
        };
        await _dataStore.AddPullRequestAsync(pr3);

        // PR 4: Has review comments
        var pr4 = new PullRequest
        {
            Id = "pr-logging",
            ProjectId = "demo-project",
            Title = "Improve logging infrastructure",
            Description = "Add structured logging with correlation IDs",
            BranchName = "feature/logging",
            Status = OpenPullRequestStatus.HasReviewComments,
            GitHubPRNumber = 38,
            CreatedAt = now.AddDays(-14),
            UpdatedAt = now.AddDays(-10)
        };
        await _dataStore.AddPullRequestAsync(pr4);

        // PR 5: In development (refactor work)
        var pr5 = new PullRequest
        {
            Id = "pr-refactor-db",
            ProjectId = "demo-project",
            Title = "Refactor database layer",
            Description = "Migrate to repository pattern",
            BranchName = "refactor/database-layer",
            Status = OpenPullRequestStatus.InDevelopment,
            GitHubPRNumber = 41,
            CreatedAt = now.AddDays(-6),
            UpdatedAt = now.AddDays(-2)
        };
        await _dataStore.AddPullRequestAsync(pr5);

        _logger.LogDebug("Seeded {Count} pull requests", 5);
    }

    private async Task SeedIssuesAsync()
    {
        var now = DateTimeOffset.UtcNow;

        // Issues for Demo Project - realistic project with diverse statuses, types, and hierarchy
        // Note: Issue has init-only properties, so we use object initializers
        var demoIssues = new List<Issue>
        {
            // ── Standalone issues (no parents) ──────────────────────────────

            // Completed bug fix
            new()
            {
                Id = "ISSUE-001",
                Title = "Fix session token refresh race condition",
                Description = "Users intermittently get 401 errors when their JWT expires during concurrent API calls. " +
                    "The token refresh logic doesn't queue pending requests while a refresh is in flight, causing some " +
                    "requests to use the stale token. Need to implement a request queue that holds outgoing calls until " +
                    "the refresh completes, then retries them with the new token.",
                Type = IssueType.Bug,
                Status = IssueStatus.Complete,
                Priority = 1,
                Tags = ["auth", "critical"],
                CreatedAt = now.AddDays(-21),
                LastUpdate = now.AddDays(-14)
            },

            // Feature in review
            new()
            {
                Id = "ISSUE-002",
                Title = "Add dark mode support with system preference detection",
                Description = "Implement a dark mode theme that respects the user's OS-level preference via " +
                    "prefers-color-scheme media query. Include a manual toggle in the settings panel that overrides " +
                    "the system default. Store the user's preference in localStorage and sync it to their profile " +
                    "settings on the server. Use CSS custom properties for theme tokens to minimize style duplication.",
                Type = IssueType.Feature,
                Status = IssueStatus.Review,
                Priority = 2,
                Tags = ["ui", "accessibility"],
                CreatedAt = now.AddDays(-14),
                LastUpdate = now.AddDays(-2)
            },

            // Bug in progress
            new()
            {
                Id = "ISSUE-003",
                Title = "WebSocket reconnection drops queued messages",
                Description = "When the SignalR connection drops and reconnects, any messages sent during the " +
                    "disconnection window are silently lost. The client needs a local outbox queue that persists " +
                    "pending messages and replays them after reconnection. Also need server-side idempotency keys " +
                    "to prevent duplicate processing if a message was partially delivered before the drop.",
                Type = IssueType.Bug,
                Status = IssueStatus.Progress,
                Priority = 1,
                Tags = ["signalr", "reliability"],
                CreatedAt = now.AddDays(-7),
                LastUpdate = now.AddHours(-6)
            },

            // Chore - completed
            new()
            {
                Id = "ISSUE-014",
                Title = "Upgrade to .NET 9 and update all NuGet packages",
                Description = "Migrate the solution from .NET 8 to .NET 9 LTS. Update all NuGet packages to their " +
                    "latest compatible versions. Run the full test suite after each major package update to catch " +
                    "breaking changes early. Update the Dockerfile base images and CI pipeline to use the .NET 9 SDK.",
                Type = IssueType.Chore,
                Status = IssueStatus.Complete,
                Priority = 3,
                Tags = ["infrastructure", "maintenance"],
                CreatedAt = now.AddDays(-30),
                LastUpdate = now.AddDays(-18)
            },

            // ── API v2 epic (parent/child hierarchy, series execution) ──────

            // Parent: API v2 migration (verify issue)
            new()
            {
                Id = "ISSUE-004",
                Title = "API v2 migration",
                Description = "Migrate all public API endpoints from v1 to v2 with improved response envelope, " +
                    "consistent error codes, cursor-based pagination, and OpenAPI 3.1 spec generation. The v1 " +
                    "endpoints should remain available but marked as deprecated with sunset headers.",
                Type = IssueType.Verify,
                Status = IssueStatus.Progress,
                Priority = 2,
                Tags = ["api", "migration"],
                ExecutionMode = ExecutionMode.Series,
                CreatedAt = now.AddDays(-20),
                LastUpdate = now.AddDays(-1)
            },

            // Child 1: Design API schema (completed)
            new()
            {
                Id = "ISSUE-005",
                Title = "Design v2 API response envelope and error format",
                Description = "Define the standard JSON response wrapper for all v2 endpoints:\n" +
                    "- `data` field for successful responses\n" +
                    "- `error` object with `code`, `message`, and `details` array for failures\n" +
                    "- `meta` object with pagination cursors and rate limit info\n\n" +
                    "Document the error code taxonomy (AUTH_*, VALIDATION_*, RESOURCE_*, RATE_LIMIT_*) and " +
                    "get sign-off from the frontend team before implementation begins.",
                Type = IssueType.Task,
                Status = IssueStatus.Complete,
                Priority = 2,
                Tags = ["api", "design"],
                ParentIssues = [new ParentIssueRef { ParentIssue = "ISSUE-004", SortOrder = "a" }],
                CreatedAt = now.AddDays(-19),
                LastUpdate = now.AddDays(-12)
            },

            // Child 2: Implement endpoints (parent of sub-tasks, in progress)
            new()
            {
                Id = "ISSUE-006",
                Title = "Implement v2 REST endpoints",
                Description = "Build all v2 endpoints following the approved response envelope. Each endpoint should " +
                    "include request validation with FluentValidation, cursor-based pagination for list endpoints, " +
                    "and ETag support for conditional requests. Map v1 routes to v2 handlers with deprecation warnings.",
                Type = IssueType.Task,
                Status = IssueStatus.Progress,
                Priority = 2,
                Tags = ["api", "backend"],
                ExecutionMode = ExecutionMode.Parallel,
                ParentIssues = [new ParentIssueRef { ParentIssue = "ISSUE-004", SortOrder = "b" }],
                CreatedAt = now.AddDays(-12),
                LastUpdate = now.AddDays(-1)
            },

            // Sub-tasks of ISSUE-006
            new()
            {
                Id = "ISSUE-007",
                Title = "Implement v2 projects endpoints",
                Description = "Create GET /api/v2/projects (list with cursor pagination), GET /api/v2/projects/{id}, " +
                    "POST /api/v2/projects, PUT /api/v2/projects/{id}, DELETE /api/v2/projects/{id}. " +
                    "Include ETag headers and If-None-Match support on GET endpoints.",
                Type = IssueType.Task,
                Status = IssueStatus.Complete,
                Priority = 2,
                Tags = ["api", "backend"],
                ParentIssues = [new ParentIssueRef { ParentIssue = "ISSUE-006", SortOrder = "a" }],
                CreatedAt = now.AddDays(-11),
                LastUpdate = now.AddDays(-5)
            },
            new()
            {
                Id = "ISSUE-008",
                Title = "Implement v2 pull requests endpoints",
                Description = "Create GET /api/v2/projects/{id}/pulls (list with status filter and cursor pagination), " +
                    "GET /api/v2/pulls/{id}, POST /api/v2/pulls (create from branch), PATCH /api/v2/pulls/{id}/status. " +
                    "Include webhook event forwarding for PR status changes.",
                Type = IssueType.Task,
                Status = IssueStatus.Review,
                Priority = 2,
                Tags = ["api", "backend"],
                ParentIssues = [new ParentIssueRef { ParentIssue = "ISSUE-006", SortOrder = "b" }],
                CreatedAt = now.AddDays(-10),
                LastUpdate = now.AddDays(-2)
            },
            new()
            {
                Id = "ISSUE-009",
                Title = "Implement v2 issues endpoints",
                Description = "Create GET /api/v2/projects/{id}/issues (with tree/flat view toggle, status filter, " +
                    "type filter), GET /api/v2/issues/{id}, PATCH /api/v2/issues/{id} for status transitions and " +
                    "parent assignment. Support bulk status updates via POST /api/v2/issues/bulk-update.",
                Type = IssueType.Task,
                Status = IssueStatus.Progress,
                Priority = 2,
                Tags = ["api", "backend"],
                ParentIssues = [new ParentIssueRef { ParentIssue = "ISSUE-006", SortOrder = "c" }],
                CreatedAt = now.AddDays(-9),
                LastUpdate = now.AddDays(-1)
            },
            new()
            {
                Id = "ISSUE-010",
                Title = "Implement v2 agent sessions endpoints",
                Description = "Create GET /api/v2/sessions (list with project filter), GET /api/v2/sessions/{id} " +
                    "(with message history), POST /api/v2/sessions (launch new agent), DELETE /api/v2/sessions/{id} " +
                    "(graceful shutdown). Include SSE endpoint for real-time session streaming.",
                Type = IssueType.Task,
                Status = IssueStatus.Open,
                Priority = 2,
                Tags = ["api", "backend"],
                ParentIssues = [new ParentIssueRef { ParentIssue = "ISSUE-006", SortOrder = "d" }],
                CreatedAt = now.AddDays(-8),
                LastUpdate = now.AddDays(-3)
            },
            new()
            {
                Id = "ISSUE-011",
                Title = "Add request validation middleware for v2",
                Description = "Implement a global validation filter using FluentValidation that automatically validates " +
                    "request DTOs and returns standardized error responses. Register validators via assembly scanning. " +
                    "Add validation for: required fields, string length limits, enum values, pagination cursor format, " +
                    "and cross-field constraints (e.g., date ranges).",
                Type = IssueType.Task,
                Status = IssueStatus.Open,
                Priority = 3,
                Tags = ["api", "validation"],
                ParentIssues = [new ParentIssueRef { ParentIssue = "ISSUE-006", SortOrder = "e" }],
                CreatedAt = now.AddDays(-8),
                LastUpdate = now.AddDays(-4)
            },

            // Child 3: Rate limiting (blocked on endpoints)
            new()
            {
                Id = "ISSUE-012",
                Title = "Add rate limiting to v2 API",
                Description = "Implement per-user rate limiting using a sliding window algorithm. Configure limits: " +
                    "100 req/min for read endpoints, 30 req/min for write endpoints. Return 429 with Retry-After " +
                    "header and remaining quota in X-RateLimit-* headers. Use Redis for distributed rate limit state " +
                    "to support horizontal scaling.",
                Type = IssueType.Task,
                Status = IssueStatus.Open,
                Priority = 3,
                Tags = ["api", "security"],
                ParentIssues = [new ParentIssueRef { ParentIssue = "ISSUE-004", SortOrder = "c" }],
                CreatedAt = now.AddDays(-10),
                LastUpdate = now.AddDays(-5)
            },

            // Child 4: API docs (blocked on endpoints)
            new()
            {
                Id = "ISSUE-013",
                Title = "Generate OpenAPI 3.1 spec and API documentation",
                Description = "Configure Swashbuckle to generate an OpenAPI 3.1 spec for all v2 endpoints. " +
                    "Include XML doc comments as descriptions, request/response examples, and authentication " +
                    "requirements. Set up a Redoc-powered documentation page at /api/docs. Add a CI check that " +
                    "fails if the generated spec has breaking changes compared to the published version.",
                Type = IssueType.Chore,
                Status = IssueStatus.Open,
                Priority = 4,
                Tags = ["api", "documentation"],
                ParentIssues = [new ParentIssueRef { ParentIssue = "ISSUE-004", SortOrder = "d" }],
                CreatedAt = now.AddDays(-10),
                LastUpdate = now.AddDays(-5)
            },

            // ── Issues for testing keyboard hierarchy creation (E2E tests) ──

            new()
            {
                Id = "e2e/parent1",
                Title = "E2E Test: Parent Issue 1",
                Description = "A parent issue for testing keyboard hierarchy controls.",
                Type = IssueType.Feature,
                Status = IssueStatus.Open,
                Priority = 2,
                ExecutionMode = ExecutionMode.Parallel,
                CreatedAt = now.AddDays(-4),
                LastUpdate = now.AddDays(-1)
            },
            new()
            {
                Id = "e2e/child1",
                Title = "E2E Test: Child Issue 1",
                Description = "A child issue of parent1 for testing hierarchy.",
                Type = IssueType.Task,
                Status = IssueStatus.Open,
                Priority = 2,
                ParentIssues = [new ParentIssueRef { ParentIssue = "e2e/parent1", SortOrder = "0" }],
                CreatedAt = now.AddDays(-3),
                LastUpdate = now.AddDays(-1)
            },
            new()
            {
                Id = "e2e/child2",
                Title = "E2E Test: Child Issue 2",
                Description = "Another child issue of parent1 for testing hierarchy.",
                Type = IssueType.Task,
                Status = IssueStatus.Open,
                Priority = 3,
                ParentIssues = [new ParentIssueRef { ParentIssue = "e2e/parent1", SortOrder = "1" }],
                CreatedAt = now.AddDays(-2),
                LastUpdate = now.AddDays(-1)
            },
            new()
            {
                Id = "e2e/orphan",
                Title = "E2E Test: Orphan Issue",
                Description = "An issue with no parent for testing hierarchy creation.",
                Type = IssueType.Task,
                Status = IssueStatus.Open,
                Priority = 3,
                CreatedAt = now.AddHours(-12),
                LastUpdate = now.AddHours(-6)
            },
            new()
            {
                Id = "e2e/series-parent",
                Title = "E2E Test: Series Parent",
                Description = "A parent issue with series execution mode.",
                Type = IssueType.Feature,
                Status = IssueStatus.Open,
                Priority = 2,
                ExecutionMode = ExecutionMode.Series,
                CreatedAt = now.AddDays(-4),
                LastUpdate = now.AddDays(-1)
            },
            new()
            {
                Id = "e2e/series-child1",
                Title = "E2E Test: Series Child 1",
                Description = "First child in a series execution parent.",
                Type = IssueType.Task,
                Status = IssueStatus.Open,
                Priority = 2,
                ParentIssues = [new ParentIssueRef { ParentIssue = "e2e/series-parent", SortOrder = "0" }],
                CreatedAt = now.AddDays(-3),
                LastUpdate = now.AddDays(-1)
            },
            new()
            {
                Id = "e2e/series-child2",
                Title = "E2E Test: Series Child 2",
                Description = "Second child in a series execution parent.",
                Type = IssueType.Task,
                Status = IssueStatus.Open,
                Priority = 2,
                ParentIssues = [new ParentIssueRef { ParentIssue = "e2e/series-parent", SortOrder = "1" }],
                CreatedAt = now.AddDays(-2),
                LastUpdate = now.AddDays(-1)
            }
        };

        // Seed issues for demo-project using FleeceIssueSeeder
        var demoProjectPath = _tempFolderService.GetProjectPath("demo-project");
        await _fleeceIssueSeeder.SeedIssuesAsync(demoProjectPath, demoIssues);
        _logger.LogDebug("Seeded {Count} issues to {ProjectPath}", demoIssues.Count, demoProjectPath);

    }

    private async Task SeedAgentPromptsAsync()
    {
        // Ensure default prompts exist (Plan and Build)
        await _agentPromptService.EnsureDefaultPromptsAsync();

        // Add a custom prompt
        var customPrompt = new AgentPrompt
        {
            Id = "review",
            Name = "Code Review",
            InitialMessage = """
                ## Code Review: {{title}}

                **Branch:** {{branch}}

                Please review this code change and provide feedback on:
                - Code quality and best practices
                - Potential bugs or edge cases
                - Performance considerations
                - Test coverage
                """,
            Mode = SessionMode.Plan,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _dataStore.AddAgentPromptAsync(customPrompt);
        _logger.LogDebug("Seeded custom agent prompt: {PromptName}", customPrompt.Name);
    }

    /// <summary>
    /// Seeds demo Claude Code sessions with tool results for testing the tool display UI.
    /// </summary>
    private void SeedDemoSessions()
    {
        var now = DateTime.UtcNow;
        var sessionId = "demo-session-001";

        // Create a demo session with various tool results
        var session = new ClaudeSession
        {
            Id = sessionId,
            EntityId = "task/abc123",
            ProjectId = "demo-project",
            WorkingDirectory = "/mock/projects/demo-project",
            Mode = SessionMode.Build,
            Model = "sonnet",
            Status = ClaudeSessionStatus.WaitingForInput,
            CreatedAt = now.AddMinutes(-15),
            LastActivityAt = now.AddMinutes(-2),
            TotalCostUsd = 0.0234m,
            TotalDurationMs = 45000
        };

        // Add initial assistant greeting
        session.Messages.Add(new ClaudeMessage
        {
            SessionId = sessionId,
            Role = ClaudeMessageRole.Assistant,
            Content =
            [
                new ClaudeMessageContent
                {
                    Type = ClaudeContentType.Text,
                    Text = "I'm ready to help with your task. What would you like me to do?"
                }
            ],
            CreatedAt = now.AddMinutes(-15)
        });

        // Add user message
        session.Messages.Add(new ClaudeMessage
        {
            SessionId = sessionId,
            Role = ClaudeMessageRole.User,
            Content =
            [
                new ClaudeMessageContent
                {
                    Type = ClaudeContentType.Text,
                    Text = "Please analyze the project structure and run the tests."
                }
            ],
            CreatedAt = now.AddMinutes(-14)
        });

        // Add assistant message with tool use
        session.Messages.Add(new ClaudeMessage
        {
            SessionId = sessionId,
            Role = ClaudeMessageRole.Assistant,
            Content =
            [
                new ClaudeMessageContent
                {
                    Type = ClaudeContentType.Thinking,
                    Text = "I'll first read the project structure to understand the codebase, then run the tests."
                },
                new ClaudeMessageContent
                {
                    Type = ClaudeContentType.ToolUse,
                    ToolName = "Read",
                    ToolUseId = "toolu_demo_001",
                    ToolInput = "{\"file_path\": \"/src/Homespun/Program.cs\"}"
                }
            ],
            CreatedAt = now.AddMinutes(-13)
        });

        // Add tool result for Read
        var readContent = """
                 1→using Microsoft.AspNetCore.Builder;
                 2→using Microsoft.Extensions.DependencyInjection;
                 3→
                 4→var builder = WebApplication.CreateBuilder(args);
                 5→
                 6→// Add services to the container
                 7→builder.Services.AddRazorPages();
                 8→builder.Services.AddServerSideBlazor();
                 9→
                10→var app = builder.Build();
                11→
                12→app.UseStaticFiles();
                13→app.UseRouting();
                14→app.MapBlazorHub();
                15→app.MapFallbackToPage("/_Host");
                16→
                17→app.Run();
            """;
        session.Messages.Add(new ClaudeMessage
        {
            SessionId = sessionId,
            Role = ClaudeMessageRole.User,
            Content =
            [
                new ClaudeMessageContent
                {
                    Type = ClaudeContentType.ToolResult,
                    ToolUseId = "toolu_demo_001",
                    ToolName = "Read",
                    ToolSuccess = true,
                    Text = readContent,
                    ParsedToolResult = _toolResultParser.Parse("Read", readContent, false)
                }
            ],
            CreatedAt = now.AddMinutes(-12)
        });

        // Add assistant message with Grep tool use
        session.Messages.Add(new ClaudeMessage
        {
            SessionId = sessionId,
            Role = ClaudeMessageRole.Assistant,
            Content =
            [
                new ClaudeMessageContent
                {
                    Type = ClaudeContentType.ToolUse,
                    ToolName = "Grep",
                    ToolUseId = "toolu_demo_002",
                    ToolInput = "{\"pattern\": \"AddService\", \"path\": \"src/\"}"
                }
            ],
            CreatedAt = now.AddMinutes(-11)
        });

        // Add tool result for Grep
        var grepContent = """
            src/Homespun/Program.cs:7:builder.Services.AddRazorPages();
            src/Homespun/Program.cs:8:builder.Services.AddServerSideBlazor();
            src/Homespun/Features/ClaudeCode/ServiceCollectionExtensions.cs:15:services.AddScoped<IClaudeSessionService, ClaudeSessionService>();
            src/Homespun/Features/ClaudeCode/ServiceCollectionExtensions.cs:16:services.AddScoped<IToolResultParser, ToolResultParser>();
            """;
        session.Messages.Add(new ClaudeMessage
        {
            SessionId = sessionId,
            Role = ClaudeMessageRole.User,
            Content =
            [
                new ClaudeMessageContent
                {
                    Type = ClaudeContentType.ToolResult,
                    ToolUseId = "toolu_demo_002",
                    ToolName = "Grep",
                    ToolSuccess = true,
                    Text = grepContent,
                    ParsedToolResult = _toolResultParser.Parse("Grep", grepContent, false)
                }
            ],
            CreatedAt = now.AddMinutes(-10)
        });

        // Add assistant message with Bash tool use
        session.Messages.Add(new ClaudeMessage
        {
            SessionId = sessionId,
            Role = ClaudeMessageRole.Assistant,
            Content =
            [
                new ClaudeMessageContent
                {
                    Type = ClaudeContentType.Text,
                    Text = "I found the service registrations. Now let me run the tests:"
                },
                new ClaudeMessageContent
                {
                    Type = ClaudeContentType.ToolUse,
                    ToolName = "Bash",
                    ToolUseId = "toolu_demo_003",
                    ToolInput = "{\"command\": \"dotnet test\"}"
                }
            ],
            CreatedAt = now.AddMinutes(-9)
        });

        // Add tool result for Bash
        var bashContent = """
            Running tests...

            Test run for /src/Homespun/tests/bin/Debug/net8.0/Homespun.Tests.dll (.NETCoreApp,Version=v8.0)
            Microsoft (R) Test Execution Command Line Tool Version 17.8.0

            Starting test execution, please wait...
            A total of 42 test files matched the specified pattern.

            Passed!  - Failed:     0, Passed:    42, Skipped:     0, Total:    42
            """;
        session.Messages.Add(new ClaudeMessage
        {
            SessionId = sessionId,
            Role = ClaudeMessageRole.User,
            Content =
            [
                new ClaudeMessageContent
                {
                    Type = ClaudeContentType.ToolResult,
                    ToolUseId = "toolu_demo_003",
                    ToolName = "Bash",
                    ToolSuccess = true,
                    Text = bashContent,
                    ParsedToolResult = _toolResultParser.Parse("Bash", bashContent, false)
                }
            ],
            CreatedAt = now.AddMinutes(-5)
        });

        // Add assistant message with Glob tool use
        session.Messages.Add(new ClaudeMessage
        {
            SessionId = sessionId,
            Role = ClaudeMessageRole.Assistant,
            Content =
            [
                new ClaudeMessageContent
                {
                    Type = ClaudeContentType.ToolUse,
                    ToolName = "Glob",
                    ToolUseId = "toolu_demo_004",
                    ToolInput = "{\"pattern\": \"**/*.cs\"}"
                }
            ],
            CreatedAt = now.AddMinutes(-4)
        });

        // Add tool result for Glob
        var globContent = """
            src/Homespun/Program.cs
            src/Homespun/Features/ClaudeCode/Services/ClaudeSessionService.cs
            src/Homespun/Features/ClaudeCode/Services/ToolResultParser.cs
            src/Homespun/Features/ClaudeCode/Data/ToolResultData.cs
            src/Homespun/Features/Testing/Services/MockClaudeSessionService.cs
            src/Homespun/Components/Pages/Home.razor.cs
            """;
        session.Messages.Add(new ClaudeMessage
        {
            SessionId = sessionId,
            Role = ClaudeMessageRole.User,
            Content =
            [
                new ClaudeMessageContent
                {
                    Type = ClaudeContentType.ToolResult,
                    ToolUseId = "toolu_demo_004",
                    ToolName = "Glob",
                    ToolSuccess = true,
                    Text = globContent,
                    ParsedToolResult = _toolResultParser.Parse("Glob", globContent, false)
                }
            ],
            CreatedAt = now.AddMinutes(-3)
        });

        // Add final assistant summary
        session.Messages.Add(new ClaudeMessage
        {
            SessionId = sessionId,
            Role = ClaudeMessageRole.Assistant,
            Content =
            [
                new ClaudeMessageContent
                {
                    Type = ClaudeContentType.Text,
                    Text = """
                        ## Analysis Complete

                        I've analyzed the project structure and run the tests. Here's a summary:

                        **Project Structure:**
                        - The project is a Blazor Server application
                        - It uses dependency injection for services
                        - There are 6 C# source files in the main codebase

                        **Test Results:**
                        - All 42 tests passed successfully
                        - No failures or skipped tests

                        The codebase appears to be in good shape. Is there anything specific you'd like me to look at or modify?
                        """
                }
            ],
            CreatedAt = now.AddMinutes(-2)
        });

        _sessionStore.Add(session);
        _logger.LogDebug("Seeded demo session: {SessionId} with {MessageCount} messages",
            sessionId, session.Messages.Count);
    }
}
