using Homespun.Features.AgentOrchestration.Services;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.Containers.Services;
using Homespun.Features.Fleece.Services;
using Homespun.Features.Gitgraph.Services;
using Homespun.Features.GitHub;
using Homespun.Features.Projects;
using Homespun.Features.PullRequests;
using Homespun.Features.PullRequests.Data;
using Homespun.Features.Search;
using Homespun.Features.Secrets;
using Homespun.Features.Commands;
using Homespun.Features.Testing.Services;
using Homespun.Features.Workflows.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Homespun.Features.Testing;

/// <summary>
/// Extension methods for registering mock services.
/// </summary>
public static class MockServiceExtensions
{
    /// <summary>
    /// Adds all mock services to the service collection.
    /// Call this instead of registering production services when mock mode is enabled.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="options">Mock mode configuration options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddMockServices(
        this IServiceCollection services,
        MockModeOptions options,
        IConfiguration? configuration = null)
    {
        // Register the temporary data folder service - creates temp folder structure
        services.AddSingleton<TempDataFolderService>();
        services.AddSingleton<ITempDataFolderService>(sp => sp.GetRequiredService<TempDataFolderService>());

        // Register the FleeceIssueSeeder for seeding issues to JSONL files
        services.AddSingleton<FleeceIssueSeeder>();

        // Register real JsonDataStore with temp file path
        services.AddSingleton<IDataStore>(sp =>
        {
            var tempFolder = sp.GetRequiredService<ITempDataFolderService>();
            var logger = sp.GetRequiredService<ILogger<JsonDataStore>>();
            return new JsonDataStore(tempFolder.DataFilePath, logger);
        });

        // Register real FleeceService with supporting services
        // Issue serialization queue (background service for persistence)
        services.AddSingleton<IssueSerializationQueueService>();
        services.AddSingleton<IIssueSerializationQueue>(sp => sp.GetRequiredService<IssueSerializationQueueService>());
        services.AddHostedService(sp => sp.GetRequiredService<IssueSerializationQueueService>());

        // Issue history service (for undo/redo - uses real file-based implementation)
        services.AddSingleton<IIssueHistoryService, IssueHistoryService>();

        // Register real FleeceService (reads/writes to temp .fleece directories)
        services.AddSingleton<IProjectFleeceService, ProjectFleeceService>();

        // Core services
        services.AddSingleton<ICommandRunner, CommandRunner>();
        services.AddSingleton<IGitHubEnvironmentService, MockGitHubEnvironmentService>();
        services.AddSingleton<IGitHubClientWrapper, MockGitHubClientWrapper>();

        // GitHub services
        services.AddScoped<IGitHubService, MockGitHubService>();
        services.AddScoped<IIssuePrLinkingService, IssuePrLinkingService>();
        services.AddScoped<IPRStatusResolver, MockPRStatusResolver>();

        // Project service - use real ProjectService with temp folder path
        services.AddScoped<IProjectService>(sp =>
        {
            var dataStore = sp.GetRequiredService<IDataStore>();
            var gitHubService = sp.GetRequiredService<IGitHubService>();
            var commandRunner = sp.GetRequiredService<ICommandRunner>();
            var tempFolder = sp.GetRequiredService<ITempDataFolderService>();
            var logger = sp.GetRequiredService<ILogger<ProjectService>>();

            var projectsPath = Path.Combine(tempFolder.RootPath, "projects");
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["HOMESPUN_BASE_PATH"] = projectsPath
                })
                .Build();

            return new ProjectService(dataStore, gitHubService, commandRunner, config, logger);
        });

        // Secrets service (real implementation - uses secrets.env files, works with temp folder structure)
        services.AddScoped<ISecretsService, SecretsService>();

        // Container query service
        services.AddScoped<IContainerQueryService, MockContainerQueryService>();

        // Fleece services
        // Transition service depends on IProjectFleeceService (now using real FleeceService)
        services.AddScoped<IFleeceIssueTransitionService, FleeceIssueTransitionService>();
        services.AddSingleton<IFleeceIssuesSyncService, MockFleeceIssuesSyncService>();
        services.AddScoped<IIssueBranchResolverService, IssueBranchResolverService>();
        // IIssueHistoryService already registered above with FleeceService
        services.AddScoped<IFleeceIssueDiffService, FleeceIssueDiffService>();
        services.AddScoped<IFleeceChangeDetectionService, FleeceChangeDetectionService>();
        services.AddScoped<IFleeceConflictDetectionService, FleeceConflictDetectionService>();
        services.AddScoped<IFleeceChangeApplicationService, FleeceChangeApplicationService>();
        services.AddScoped<IFleecePostMergeService, FleecePostMergeService>();

        // Git services
        services.AddSingleton<IGitCloneService, MockGitCloneService>();

        // Search services (for @ and # mention autocomplete)
        services.AddScoped<IProjectFileService, ProjectFileService>();
        services.AddScoped<IPrDataProvider, PrDataProvider>();
        services.AddScoped<ISearchablePrService, SearchablePrService>();

        // Claude Code services - use the real session store (already in-memory)
        services.AddSingleton<IClaudeSessionStore, ClaudeSessionStore>();
        services.AddSingleton<IToolResultParser, ToolResultParser>();

        // Choose between live Claude sessions or mock based on configuration
        if (options.UseLiveClaudeSessions && configuration != null)
        {
            // Use real ClaudeSessionService with test working directory
            services.AddLiveClaudeSessionServices(options, configuration);
        }
        else
        {
            // Use mock service for simulated responses
            services.AddSingleton<IClaudeSessionService, MockClaudeSessionService>();
        }

        services.AddSingleton<IRebaseAgentService, MockRebaseAgentService>();
        services.AddSingleton<IAgentPromptService, AgentPromptService>();

        // Agent Orchestration services - configure options and HTTP client first
        services.Configure<MiniPromptOptions>(options => { });  // Empty config - uses defaults
        services.AddHttpClient("MiniPrompt");  // Register named HTTP client

        // Then register the services
        services.AddSingleton<IMiniPromptService, MiniPromptService>();
        services.AddSingleton<IBranchIdGeneratorService, BranchIdGeneratorService>();
        services.AddSingleton<IBranchIdBackgroundService, BranchIdBackgroundService>();
        services.AddScoped<IBaseBranchResolver, BaseBranchResolver>();
        services.AddSingleton<IAgentStartBackgroundService, MockAgentStartBackgroundService>();
        services.AddSingleton<IQueueCoordinator, QueueCoordinator>();

        // Message cache store - use temp folder's sessions directory
        services.AddSingleton<IMessageCacheStore>(sp =>
        {
            var tempFolder = sp.GetRequiredService<ITempDataFolderService>();
            var logger = sp.GetRequiredService<ILogger<MessageCacheStore>>();
            return new MessageCacheStore(tempFolder.SessionsPath, logger);
        });

        // A2A event store + translator — shared between mock and production modes.
        services.AddSingleton<IA2AEventStore>(sp =>
        {
            var tempFolder = sp.GetRequiredService<ITempDataFolderService>();
            var logger = sp.GetRequiredService<ILogger<A2AEventStore>>();
            return new A2AEventStore(tempFolder.SessionsPath, logger);
        });
        services.AddSingleton<IA2AToAGUITranslator, A2AToAGUITranslator>();
        services.Configure<Homespun.Features.ClaudeCode.Settings.SessionEventsOptions>(_ => { });

        // Pull request workflow service (needed by GraphService)
        services.AddScoped<PullRequestWorkflowService>();

        // Graph services
        services.AddSingleton<IGraphCacheService>(sp =>
            new GraphCacheService(sp.GetRequiredService<ILogger<GraphCacheService>>()));
        services.AddScoped<IGraphService, GraphService>();

        // Clone enrichment service
        services.AddScoped<ICloneEnrichmentService, CloneEnrichmentService>();

        // Issue PR status service
        services.AddScoped<IIssuePrStatusService, IssuePrStatusService>();

        // Workflow services
        services.AddSingleton<IWorkflowTemplateService, WorkflowTemplateService>();
        services.AddSingleton<IWorkflowStorageService, WorkflowStorageService>();
        services.AddSingleton<IStepExecutor, AgentStepExecutor>();
        services.AddSingleton<IStepExecutor, ServerActionStepExecutor>();
        services.AddSingleton<IStepExecutor, GateStepExecutor>();
        services.AddSingleton<IWorkflowExecutionService, WorkflowExecutionService>();
        services.AddSingleton<IWorkflowContextStore, WorkflowContextStore>();
        services.AddSingleton<IWorkflowSessionCallback, WorkflowSessionCallback>();
        services.AddSingleton(sp =>
            new Lazy<IWorkflowSessionCallback>(() => sp.GetRequiredService<IWorkflowSessionCallback>()));

        // JSONL session loader for loading real session data
        services.AddSingleton<IJsonlSessionLoader, JsonlSessionLoader>();

        // Seed data service (if enabled)
        if (options.SeedData)
        {
            services.AddHostedService<MockDataSeederService>();
        }

        return services;
    }

    /// <summary>
    /// Adds live Claude session services for testing with a real Claude Code agent.
    /// </summary>
    private static IServiceCollection AddLiveClaudeSessionServices(
        this IServiceCollection services,
        MockModeOptions options,
        IConfiguration configuration)
    {
        // Register MockAgentExecutionService for mock mode
        Console.WriteLine("[AgentExecution] MockLive mode: Registering MockAgentExecutionService");
        services.AddSingleton<IAgentExecutionService, MockAgentExecutionService>();

        // Determine working directory for live sessions
        // Use /data/test-workspace in container (via HOMESPUN_DATA_PATH), otherwise home directory
        var dataPath2 = Environment.GetEnvironmentVariable("HOMESPUN_DATA_PATH");
        string defaultWorkspace;
        if (!string.IsNullOrEmpty(dataPath2))
        {
            var dataDirectory = Path.GetDirectoryName(dataPath2);
            defaultWorkspace = Path.Combine(dataDirectory!, "test-workspace");
        }
        else
        {
            defaultWorkspace = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "test-workspace");
        }
        var workingDirectory = options.LiveClaudeSessionsWorkingDirectory ?? defaultWorkspace;

        // Ensure the test workspace directory exists
        if (!Directory.Exists(workingDirectory))
        {
            Directory.CreateDirectory(workingDirectory);
        }

        // Session discovery service - reads from Claude's native session storage
        var homespunDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".homespun");
        var claudeDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude", "projects");

        services.AddSingleton<IClaudeSessionDiscovery>(sp =>
            new ClaudeSessionDiscovery(claudeDir, sp.GetRequiredService<ILogger<ClaudeSessionDiscovery>>()));

        // Session metadata store - maps Claude sessions to our entities
        if (!Directory.Exists(homespunDir))
        {
            Directory.CreateDirectory(homespunDir);
        }
        var metadataPath = Path.Combine(homespunDir, "session-metadata-test.json");
        services.AddSingleton<ISessionMetadataStore>(sp =>
            new SessionMetadataStore(metadataPath, sp.GetRequiredService<ILogger<SessionMetadataStore>>()));

        // Tool result parser for rich display
        services.AddSingleton<IToolResultParser, ToolResultParser>();

        // Hooks service for session startup hooks
        services.AddSingleton<IHooksService, HooksService>();

        // AG-UI event service for translating A2A events
        services.AddSingleton<IAGUIEventService, AGUIEventService>();

        // Session state and decomposed services
        services.AddSingleton<ISessionStateManager, SessionStateManager>();
        services.AddSingleton<IToolInteractionService, ToolInteractionService>();
        services.AddSingleton<ISessionLifecycleService, SessionLifecycleService>();
        services.AddSingleton<IMessageProcessingService, MessageProcessingService>();
        services.AddSingleton(sp =>
            new Lazy<IMessageProcessingService>(() => sp.GetRequiredService<IMessageProcessingService>()));
        services.AddSingleton(sp =>
            new Lazy<ISessionLifecycleService>(() => sp.GetRequiredService<ISessionLifecycleService>()));
        services.AddSingleton<IClaudeSessionService, ClaudeSessionService>();

        // Store the test working directory in configuration for the MockGitCloneService
        services.Configure<LiveClaudeTestOptions>(opts =>
        {
            opts.TestWorkingDirectory = workingDirectory;
        });

        return services;
    }
}

/// <summary>
/// Options for live Claude testing in mock mode.
/// </summary>
public class LiveClaudeTestOptions
{
    /// <summary>
    /// The working directory used for live Claude test sessions.
    /// </summary>
    public string TestWorkingDirectory { get; set; } = "";
}
