using Fleece.Core.Models;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.Testing.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Homespun.Features.Testing;

/// <summary>
/// Hosted service that seeds demo data on application startup when mock mode is enabled.
/// </summary>
public class MockDataSeederService : IHostedService
{
    private readonly MockDataStore _dataStore;
    private readonly MockFleeceService _fleeceService;
    private readonly IAgentPromptService _agentPromptService;
    private readonly IClaudeSessionStore _sessionStore;
    private readonly IToolResultParser _toolResultParser;
    private readonly IJsonlSessionLoader _jsonlSessionLoader;
    private readonly ILogger<MockDataSeederService> _logger;
    private readonly LiveClaudeTestOptions? _liveTestOptions;
    private readonly List<string> _createdTempDirectories = [];

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
        MockDataStore dataStore,
        MockFleeceService fleeceService,
        IAgentPromptService agentPromptService,
        IClaudeSessionStore sessionStore,
        IToolResultParser toolResultParser,
        IJsonlSessionLoader jsonlSessionLoader,
        ILogger<MockDataSeederService> logger,
        IOptions<LiveClaudeTestOptions>? liveTestOptions = null)
    {
        _dataStore = dataStore;
        _fleeceService = fleeceService;
        _agentPromptService = agentPromptService;
        _sessionStore = sessionStore;
        _toolResultParser = toolResultParser;
        _jsonlSessionLoader = jsonlSessionLoader;
        _logger = logger;
        _liveTestOptions = liveTestOptions?.Value;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Seeding mock data for demo mode...");

        try
        {
            await SeedProjectsAsync();
            await SeedPullRequestsAsync();
            await SeedIssuesAsync();
            await SeedAgentPromptsAsync();
            await SeedSessionsAsync(cancellationToken);

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
        foreach (var dir in _createdTempDirectories)
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                    _logger.LogDebug("Cleaned up temp mock directory: {Path}", dir);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clean up temp mock directory: {Path}", dir);
            }
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Gets the local path for a mock project. When live Claude sessions are enabled,
    /// creates a real temporary directory so that SubprocessCliTransport can validate it.
    /// Otherwise returns a fake path for fully-mocked mode.
    /// </summary>
    private string GetMockProjectPath(string projectId)
    {
        if (!string.IsNullOrEmpty(_liveTestOptions?.TestWorkingDirectory))
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "homespun-mock", projectId);
            if (!Directory.Exists(tempDir))
            {
                Directory.CreateDirectory(tempDir);
                _createdTempDirectories.Add(tempDir);
                _logger.LogDebug("Created temp directory for mock project {ProjectId}: {Path}", projectId, tempDir);
            }
            return tempDir;
        }

        return $"/mock/projects/{projectId}";
    }

    private async Task SeedProjectsAsync()
    {
        // Demo Project 1: Main demo project
        var demoProject = new Project
        {
            Id = "demo-project",
            Name = "Demo Project",
            LocalPath = GetMockProjectPath("demo-project"),
            GitHubOwner = "demo-org",
            GitHubRepo = "demo-project",
            DefaultBranch = "main",
            DefaultModel = "sonnet"
        };
        await _dataStore.AddProjectAsync(demoProject);
        _logger.LogDebug("Seeded demo project: {ProjectName}", demoProject.Name);

        // Demo Project 2: A sample app
        var sampleApp = new Project
        {
            Id = "sample-app",
            Name = "Sample Application",
            LocalPath = GetMockProjectPath("sample-app"),
            GitHubOwner = "demo-org",
            GitHubRepo = "sample-app",
            DefaultBranch = "main",
            DefaultModel = "sonnet"
        };
        await _dataStore.AddProjectAsync(sampleApp);
        _logger.LogDebug("Seeded sample app project: {ProjectName}", sampleApp.Name);
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

        // Sample App PRs
        var pr6 = new PullRequest
        {
            Id = "pr-sample-feature",
            ProjectId = "sample-app",
            Title = "Add sample feature",
            Description = "A sample feature for demonstration",
            BranchName = "feature/sample",
            Status = OpenPullRequestStatus.ReadyForReview,
            GitHubPRNumber = 5,
            CreatedAt = now.AddDays(-2),
            UpdatedAt = now.AddHours(-5)
        };
        await _dataStore.AddPullRequestAsync(pr6);

        _logger.LogDebug("Seeded {Count} pull requests", 6);
    }

    private Task SeedIssuesAsync()
    {
        var now = DateTime.UtcNow;

        // Issues for Demo Project
        // Note: Issue has init-only properties, so we use object initializers
        var issues = new List<Issue>
        {
            new()
            {
                Id = "task/abc123",
                Title = "Create service mocks",
                Description = "Create mocks for all services with fake data sets to enable testing without production data.",
                Type = IssueType.Task,
                Status = IssueStatus.Open,
                Priority = 2,
                CreatedAt = now.AddDays(-5),
                LastUpdate = now.AddDays(-1)
            },
            new()
            {
                Id = "feat/def456",
                Title = "Add dashboard analytics",
                Description = "Implement analytics dashboard with charts showing project metrics.",
                Type = IssueType.Feature,
                Status = IssueStatus.Open,
                Priority = 3,
                ParentIssues = [new ParentIssueRef { ParentIssue = "task/abc123", SortOrder = "0" }],
                CreatedAt = now.AddDays(-10),
                LastUpdate = now.AddDays(-3)
            },
            new()
            {
                Id = "bug/ghi789",
                Title = "Fix navigation breadcrumb",
                Description = "Breadcrumb shows incorrect path when navigating from issue to PR.",
                Type = IssueType.Bug,
                Status = IssueStatus.Progress,
                Priority = 1,
                CreatedAt = now.AddDays(-2),
                LastUpdate = now.AddHours(-12)
            },
            new()
            {
                Id = "task/jkl012",
                Title = "Update dependencies",
                Description = "Update all npm and NuGet dependencies to latest stable versions.",
                Type = IssueType.Task,
                Status = IssueStatus.Complete,
                Priority = 4,
                CreatedAt = now.AddDays(-15),
                LastUpdate = now.AddDays(-8)
            },
            new()
            {
                Id = "task/mno345",
                Title = "Simplify state management",
                Description = "Refactor component state to use simpler patterns.",
                Type = IssueType.Task,
                Status = IssueStatus.Open,
                Priority = 3,
                ParentIssues = [new ParentIssueRef { ParentIssue = "feat/def456", SortOrder = "0" }],
                CreatedAt = now.AddDays(-7),
                LastUpdate = now.AddDays(-4)
            },
            // Issue X4LlBY - Referenced in issue 1JudQJ
            // This issue demonstrates the branch naming pattern with custom working branch ID
            new()
            {
                Id = "X4LlBY",
                Title = "Improve tool output formatting",
                Description = "Enhance the output formatting for CLI tools to be more readable and include color coding.",
                Type = IssueType.Feature,
                Status = IssueStatus.Progress,
                Priority = 2,
                WorkingBranchId = "improve-tool-output",
                CreatedAt = now.AddDays(-3),
                LastUpdate = now.AddHours(-6)
            },
            // Issue demonstrating the bug scenario (1JudQJ):
            // Originally created as a Feature, then changed to Bug type
            // The branch name should reflect the current type (Bug), not the original (Feature)
            new()
            {
                Id = "1JudQJ",
                Title = "Fix issues with clone and branch naming",
                Description = @"When a new clone is created for an issue, it currently creates the new clone with a default automatically generated branch name. It appears that this is being persisted independent to the {group}/{type}/{branch-id} pattern, so when these details are changed for an issue they are not recalculated and updated prior to creating the branch and clone.

Create an integration test to confirm that this does occur, then fix it - the branch name and clone folder name should always match and should always be recalculated just before creating the branch and clone.",
                Type = IssueType.Bug, // This was originally Feature, now Bug
                Status = IssueStatus.Progress,
                Priority = 1,
                WorkingBranchId = "fix-issues-with-clone-and-branch-naming",
                ParentIssues = [new ParentIssueRef { ParentIssue = "X4LlBY", SortOrder = "0" }],
                CreatedAt = now.AddDays(-1),
                LastUpdate = now
            },
            // Issues for testing keyboard hierarchy creation (E2E tests)
            // Parent issue (lane 1) - this issue will have children below it
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
            // Child issue of parent1 (lane 0)
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
            // Another child of parent1 (lane 0)
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
            // Standalone issue with no parents (orphan) - for testing creating hierarchy
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
            // Series parent - children execute in order
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
            // Series child 1
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
            // Series child 2
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

        var demoProjectPath = GetMockProjectPath("demo-project");
        foreach (var issue in issues)
        {
            _fleeceService.SeedIssue(demoProjectPath, issue);
        }

        _logger.LogDebug("Seeded {Count} issues", issues.Count);
        return Task.CompletedTask;
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
            Model = "claude-sonnet-4-20250514",
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
