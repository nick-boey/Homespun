using Homespun.Features.AgentOrchestration.Services;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.Commands;
using Homespun.Features.Design;
using Homespun.Features.Fleece.Services;
using Homespun.Features.Git;
using Homespun.Features.Gitgraph.Services;
using Homespun.Features.GitHub;
using Homespun.Features.Projects;
using Homespun.Features.PullRequests.Data;
using Homespun.Features.Testing.Services;
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
        // Register the mock data store as both concrete and interface type
        services.AddSingleton<MockDataStore>();
        services.AddSingleton<IDataStore>(sp => sp.GetRequiredService<MockDataStore>());

        // Register mock Fleece service (needs to be accessible by transition service)
        services.AddSingleton<MockFleeceService>();
        services.AddSingleton<IFleeceService>(sp => sp.GetRequiredService<MockFleeceService>());

        // Core services
        services.AddSingleton<ICommandRunner, MockCommandRunner>();
        services.AddSingleton<IGitHubEnvironmentService, MockGitHubEnvironmentService>();
        services.AddSingleton<IGitHubClientWrapper, MockGitHubClientWrapper>();

        // GitHub services
        services.AddScoped<IGitHubService, MockGitHubService>();
        services.AddScoped<IIssuePrLinkingService, MockIssuePrLinkingService>();

        // Project service
        services.AddScoped<IProjectService, MockProjectService>();

        // Fleece services (transition service depends on MockFleeceService)
        services.AddScoped<IFleeceIssueTransitionService, MockFleeceIssueTransitionService>();
        services.AddSingleton<IFleeceIssuesSyncService, MockFleeceIssuesSyncService>();

        // Git services
        services.AddSingleton<IGitWorktreeService, MockGitWorktreeService>();

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
        services.AddSingleton<IAgentPromptService, MockAgentPromptService>();

        // Agent Orchestration services - use real implementations
        // These are lightweight services that work with the Claude SDK
        services.AddSingleton<IMiniPromptService, MiniPromptService>();
        services.AddSingleton<IBranchIdGeneratorService, BranchIdGeneratorService>();

        // Message cache store - use real implementation
        // In container: /data/sessions, locally: ~/.homespun/sessions (consistent with Program.cs)
        var dataPath = Environment.GetEnvironmentVariable("HOMESPUN_DATA_PATH");
        string messageCacheDir;
        if (!string.IsNullOrEmpty(dataPath))
        {
            var dataDirectory = Path.GetDirectoryName(dataPath);
            messageCacheDir = Path.Combine(dataDirectory!, "sessions");
        }
        else
        {
            var homespunDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".homespun");
            messageCacheDir = Path.Combine(homespunDir, "sessions");
        }
        services.AddSingleton<IMessageCacheStore>(sp =>
            new MessageCacheStore(messageCacheDir, sp.GetRequiredService<ILogger<MessageCacheStore>>()));

        // Graph service
        services.AddScoped<IGraphService, MockGraphService>();

        // Design system services (only available in mock mode)
        services.AddSingleton<IComponentRegistryService, ComponentRegistryService>();

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
        // Register agent execution service based on configuration
        services.Configure<AgentExecutionOptions>(
            configuration.GetSection(AgentExecutionOptions.SectionName));

        var agentExecutionMode = configuration
            .GetSection(AgentExecutionOptions.SectionName)
            .GetValue<AgentExecutionMode>("Mode");

        Console.WriteLine($"[AgentExecution] MockLive mode: Configured mode = {agentExecutionMode}");

        switch (agentExecutionMode)
        {
            case AgentExecutionMode.Docker:
                Console.WriteLine("[AgentExecution] Registering DockerAgentExecutionService");
                services.Configure<DockerAgentExecutionOptions>(
                    configuration.GetSection(DockerAgentExecutionOptions.SectionName));
                services.AddSingleton<IAgentExecutionService, DockerAgentExecutionService>();
                break;
            case AgentExecutionMode.AzureContainerApps:
                Console.WriteLine("[AgentExecution] Registering AzureContainerAppsAgentExecutionService");
                services.Configure<AzureContainerAppsAgentExecutionOptions>(
                    configuration.GetSection(AzureContainerAppsAgentExecutionOptions.SectionName));
                services.AddSingleton<IAgentExecutionService, AzureContainerAppsAgentExecutionService>();
                break;
            default:
                Console.WriteLine("[AgentExecution] Registering LocalAgentExecutionService (default)");
                services.AddSingleton<IAgentExecutionService, LocalAgentExecutionService>();
                break;
        }

        // Determine working directory for live sessions
        // Use /data/test-workspace in container (via HOMESPUN_DATA_PATH), otherwise current directory
        var dataPath = Environment.GetEnvironmentVariable("HOMESPUN_DATA_PATH");
        string defaultWorkspace;
        if (!string.IsNullOrEmpty(dataPath))
        {
            var dataDirectory = Path.GetDirectoryName(dataPath);
            defaultWorkspace = Path.Combine(dataDirectory!, "test-workspace");
        }
        else
        {
            defaultWorkspace = Path.Combine(Directory.GetCurrentDirectory(), "test-workspace");
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

        // Use the real ClaudeSessionService
        services.AddSingleton<IClaudeSessionService, ClaudeSessionService>();

        // Store the test working directory in configuration for the MockGitWorktreeService
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
