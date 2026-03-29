using System.Text.Json;
using System.Text.Json.Serialization;
using Homespun.Features.AgentOrchestration.Services;
using Homespun.Features.ClaudeCode.Hubs;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.Commands;
using Homespun.Features.Containers.Services;
using Homespun.Features.Fleece.Services;
using Homespun.Features.Git;
using Homespun.Features.GitHub;
using Homespun.Features.Gitgraph.Services;
using Homespun.Features.Navigation;
using Homespun.Features.Notifications;
using Homespun.Features.Observability;
using Homespun.Features.Observability.HealthChecks;
using Homespun.Features.Plans;
using Homespun.Features.Projects;
using Homespun.Features.PullRequests;
using Homespun.Features.Search;
using Homespun.Features.Secrets;
using Homespun.Features.PullRequests.Data;
using Homespun.Features.Shared;
using Homespun.Features.Shared.Services;
using Homespun.Features.SignalR;
using Homespun.Features.Testing;
using Homespun.Features.Workflows.Hubs;
using Homespun.Features.Workflows.Services;
using Homespun.ServiceDefaults;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Console;

var builder = WebApplication.CreateBuilder(args);

// Enable static web assets resolution for non-production environments (e.g. Mock)
// By default, only the Development environment activates this automatically
if (!builder.Environment.IsProduction())
{
    builder.WebHost.UseStaticWebAssets();
}

// Check for mock mode
var mockModeOptions = new MockModeOptions();
builder.Configuration.GetSection(MockModeOptions.SectionName).Bind(mockModeOptions);

// Allow environment variable override
if (Environment.GetEnvironmentVariable("HOMESPUN_MOCK_MODE") == "true")
{
    mockModeOptions.Enabled = true;
}

// Configure console logging with JSON format for Promtail/Loki
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.FormatterName = JsonConsoleFormatter.FormatterName)
    .AddConsoleFormatter<JsonConsoleFormatter, PromtailJsonFormatterOptions>(options =>
    {
        options.UseUtcTimestamp = true;
    });

// Add Aspire ServiceDefaults (OpenTelemetry, service discovery, resilience)
builder.AddServiceDefaults();

// Register custom Homespun activity sources for tracing
builder.Services.AddHomespunInstrumentation();

// Resolve data path from configuration or use default (used by production and for data protection keys)
var homespunDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".homespun");
var defaultDataPath = Path.Combine(homespunDir, "homespun-data.json");
var dataPath = builder.Configuration["HOMESPUN_DATA_PATH"] ?? defaultDataPath;

// Ensure the data directory exists
var dataDirectory = Path.GetDirectoryName(dataPath);
if (!string.IsNullOrEmpty(dataDirectory) && !Directory.Exists(dataDirectory))
{
    Directory.CreateDirectory(dataDirectory);
}

// Configure Data Protection to persist keys in the data directory
var dataProtectionKeysPath = Path.Combine(dataDirectory!, "DataProtection-Keys");
if (!Directory.Exists(dataProtectionKeysPath))
{
    Directory.CreateDirectory(dataProtectionKeysPath);
}

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath))
    .SetApplicationName("Homespun");

// Register services based on mock mode
if (mockModeOptions.Enabled)
{
    // Mock mode - use in-memory mock services
    builder.Services.AddMockServices(mockModeOptions, builder.Configuration);

    // Services that are shared between mock and production mode
    builder.Services.AddSingleton<IMarkdownRenderingService, MarkdownRenderingService>();
    builder.Services.AddSingleton<INotificationService, NotificationService>();
    builder.Services.AddScoped<IBreadcrumbService, BreadcrumbService>();
    builder.Services.AddSingleton<IAgentStartupTracker, AgentStartupTracker>();
    builder.Services.AddScoped<PullRequestDataService>();
    builder.Services.AddScoped<PullRequestWorkflowService>();
    builder.Services.AddSingleton<ITodoParser, TodoParser>();
}
else
{
    // Production mode - use real services with external dependencies

    // Register JSON data store as singleton
    builder.Services.AddSingleton<IDataStore>(sp =>
    {
        var logger = sp.GetRequiredService<ILogger<JsonDataStore>>();
        return new JsonDataStore(dataPath, logger);
    });

    // Core services
    builder.Services.AddScoped<IProjectService, ProjectService>();
    builder.Services.AddScoped<ISecretsService, SecretsService>();
    builder.Services.AddScoped<IContainerQueryService, ContainerQueryService>();
    builder.Services.AddSingleton<IGitHubEnvironmentService, GitHubEnvironmentService>();
    builder.Services.AddSingleton<ICommandRunner, CommandRunner>();
    builder.Services.AddSingleton<IGitCloneService, GitCloneService>();
    builder.Services.AddSingleton<IMergeStatusCacheService, MergeStatusCacheService>();
    builder.Services.AddScoped<ICloneEnrichmentService, CloneEnrichmentService>();
    builder.Services.AddScoped<PullRequestDataService>();
    builder.Services.AddSingleton<IGitHubClientWrapper, GitHubClientWrapper>();
    builder.Services.AddScoped<IGitHubService, GitHubService>();
    builder.Services.AddScoped<PullRequestWorkflowService>();

    // Search services (for @ and # mention autocomplete)
    builder.Services.AddScoped<IProjectFileService, ProjectFileService>();
    builder.Services.AddScoped<IPrDataProvider, PrDataProvider>();
    builder.Services.AddScoped<ISearchablePrService, SearchablePrService>();

    // Fleece services (file-based issue tracking)
    builder.Services.AddSingleton<Fleece.Core.Serialization.IJsonlSerializer, Fleece.Core.Serialization.JsonlSerializer>();
    builder.Services.AddSingleton<Fleece.Core.Services.Interfaces.IDiffService, Fleece.Core.Services.DiffService>();
    builder.Services.AddSingleton<IssueSerializationQueueService>();
    builder.Services.AddSingleton<IIssueSerializationQueue>(sp => sp.GetRequiredService<IssueSerializationQueueService>());
    builder.Services.AddHostedService(sp => sp.GetRequiredService<IssueSerializationQueueService>());
    builder.Services.AddSingleton<IIssueHistoryService, IssueHistoryService>();
    builder.Services.AddSingleton<IFleeceService, FleeceService>();
    builder.Services.AddScoped<IFleeceIssueTransitionService, FleeceIssueTransitionService>();
    builder.Services.AddSingleton<IFleeceIssuesSyncService, FleeceIssuesSyncService>();
    builder.Services.AddScoped<IIssueBranchResolverService, IssueBranchResolverService>();
    builder.Services.AddScoped<IFleeceIssueDiffService, FleeceIssueDiffService>();
    builder.Services.AddScoped<IFleeceChangeDetectionService, FleeceChangeDetectionService>();
    builder.Services.AddScoped<IFleeceConflictDetectionService, FleeceConflictDetectionService>();
    builder.Services.AddScoped<IFleeceChangeApplicationService, FleeceChangeApplicationService>();
    builder.Services.AddScoped<IFleecePostMergeService, FleecePostMergeService>();

    // Markdown rendering service
    builder.Services.AddSingleton<IMarkdownRenderingService, MarkdownRenderingService>();

    // Issue PR status service (for getting PR status linked to issues)
    builder.Services.AddScoped<IIssuePrStatusService, IssuePrStatusService>();

    // Gitgraph services - cache stored as JSONL files alongside project data
    builder.Services.AddSingleton<IGraphCacheService>(sp =>
        new GraphCacheService(sp.GetRequiredService<ILogger<GraphCacheService>>()));
    builder.Services.AddScoped<IGraphService, GraphService>();

    // PR status resolver (for resolving merged/closed PR statuses in the graph cache)
    builder.Services.AddScoped<IPRStatusResolver, PRStatusResolver>();

    // Issue-PR linking service (must be registered before GitHubService as it depends on it)
    builder.Services.AddScoped<IIssuePrLinkingService, IssuePrLinkingService>();

    // Notification services
    builder.Services.AddSingleton<INotificationService, NotificationService>();

    // Navigation services
    builder.Services.AddScoped<IBreadcrumbService, BreadcrumbService>();

    // Claude Code SDK services
    builder.Services.AddSingleton<IClaudeSessionStore, ClaudeSessionStore>();

    // Agent Execution service - Docker mode with container discovery and recovery
    builder.Services.Configure<DockerAgentExecutionOptions>(
        builder.Configuration.GetSection(DockerAgentExecutionOptions.SectionName));
    builder.Services.PostConfigure<DockerAgentExecutionOptions>(options =>
    {
        var hostPath = Environment.GetEnvironmentVariable("HSP_HOST_DATA_PATH");
        if (!string.IsNullOrEmpty(hostPath))
            options.HostDataPath = hostPath;
    });
    builder.Services.AddSingleton<IAgentExecutionService, DockerAgentExecutionService>();
    builder.Services.AddSingleton<IContainerDiscoveryService, ContainerDiscoveryService>();
    builder.Services.AddHostedService(sp =>
    {
        var discoveryService = sp.GetRequiredService<IContainerDiscoveryService>();
        var executionService = sp.GetRequiredService<IAgentExecutionService>() as DockerAgentExecutionService;
        var logger = sp.GetRequiredService<ILogger<ContainerRecoveryHostedService>>();
        return new ContainerRecoveryHostedService(
            discoveryService,
            container => executionService?.RegisterDiscoveredContainer(container),
            logger);
    });

    // Session discovery service - reads from Claude's native session storage at ~/.claude/projects/
    builder.Services.AddSingleton<IClaudeSessionDiscovery>(sp =>
    {
        var claudeDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude", "projects");
        return new ClaudeSessionDiscovery(claudeDir, sp.GetRequiredService<ILogger<ClaudeSessionDiscovery>>());
    });

    // Session metadata store - maps Claude sessions to our entities (PR/issue)
    var metadataPath = Path.Combine(homespunDir, "session-metadata.json");
    builder.Services.AddSingleton<ISessionMetadataStore>(sp =>
        new SessionMetadataStore(metadataPath, sp.GetRequiredService<ILogger<SessionMetadataStore>>()));

    // Message cache store - persists session messages to JSONL files
    var messageCacheDir = Path.Combine(dataDirectory!, "sessions");
    builder.Services.AddSingleton<IMessageCacheStore>(sp =>
        new MessageCacheStore(messageCacheDir, sp.GetRequiredService<ILogger<MessageCacheStore>>()));

    // Issue workspace service - manages per-issue folder structure for agent isolation
    var projectsBaseDir = builder.Configuration["HOMESPUN_PROJECTS_PATH"]
        ?? Path.Combine(dataDirectory!, "projects");
    builder.Services.AddSingleton<IIssueWorkspaceService>(sp =>
        new IssueWorkspaceService(
            projectsBaseDir,
            sp.GetRequiredService<ICommandRunner>(),
            sp.GetRequiredService<IFleeceIssuesSyncService>(),
            sp.GetRequiredService<ILogger<IssueWorkspaceService>>()));

    builder.Services.AddSingleton<IToolResultParser, ToolResultParser>();
    builder.Services.AddSingleton<IHooksService, HooksService>();
    builder.Services.AddSingleton<IAGUIEventService, AGUIEventService>();
    builder.Services.AddSingleton<IClaudeSessionService, ClaudeSessionService>();
    builder.Services.AddSingleton<IAgentStartupTracker, AgentStartupTracker>();
    builder.Services.AddSingleton<IAgentPromptService, AgentPromptService>();
    builder.Services.AddSingleton<IRebaseAgentService, RebaseAgentService>();
    builder.Services.AddSingleton<ITodoParser, TodoParser>();

    // Agent Orchestration services (mini-prompts, branch ID generation, agent startup)
    builder.Services.Configure<MiniPromptOptions>(
        builder.Configuration.GetSection(MiniPromptOptions.SectionName));
    builder.Services.AddHttpClient("MiniPrompt");
    builder.Services.AddSingleton<IMiniPromptService, MiniPromptService>();
    builder.Services.AddSingleton<IBranchIdGeneratorService, BranchIdGeneratorService>();
    builder.Services.AddSingleton<IBranchIdBackgroundService, BranchIdBackgroundService>();
    builder.Services.AddScoped<IBaseBranchResolver, BaseBranchResolver>();
    builder.Services.AddSingleton<IAgentStartBackgroundService, AgentStartBackgroundService>();
    builder.Services.AddSingleton<IQueueCoordinator, QueueCoordinator>();

    // GitHub sync polling service (PR sync, review polling, issue linking)
    builder.Services.Configure<GitHubSyncPollingOptions>(
        builder.Configuration.GetSection(GitHubSyncPollingOptions.SectionName));
    builder.Services.AddHostedService<GitHubSyncPollingService>();

    // Workflow services
    builder.Services.AddSingleton<IWorkflowTemplateService, WorkflowTemplateService>();
    builder.Services.AddScoped<IWorkflowService, WorkflowService>();
    builder.Services.AddSingleton<IWorkflowStorageService, WorkflowStorageService>();
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddSingleton<IServerActionHandler, CiMergeStepExecutor>();
    builder.Services.AddSingleton<IStepExecutor, AgentStepExecutor>();
    builder.Services.AddSingleton<IStepExecutor, ServerActionStepExecutor>();
    builder.Services.AddSingleton<IStepExecutor, GateStepExecutor>();
    builder.Services.AddSingleton<IWorkflowExecutionService, WorkflowExecutionService>();
    builder.Services.AddSingleton<IWorkflowContextStore, WorkflowContextStore>();
    builder.Services.AddSingleton<IWorkflowSessionCallback, WorkflowSessionCallback>();
    builder.Services.AddSingleton(sp =>
        new Lazy<IWorkflowSessionCallback>(() => sp.GetRequiredService<IWorkflowSessionCallback>()));
}

// SignalR URL provider (uses internal URL in Docker, localhost in development)
builder.Services.Configure<SignalROptions>(
    builder.Configuration.GetSection(SignalROptions.SectionName));
builder.Services.AddSingleton<ISignalRUrlProvider, SignalRUrlProvider>();

// Plans service (reads plan files from .claude/plans directory)
builder.Services.AddSingleton<IPlansService, PlansService>();

builder.Services.AddSignalR()
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.Converters.Add(
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    });
builder.Services.AddHomespunHealthChecks(dataDirectory!);
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    });

// Add Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SchemaFilter<EnumSchemaFilter>();
});

// Configure CORS for Blazor WASM client
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });

    // Named policy for SignalR (requires credentials)
    options.AddPolicy("SignalR", policy =>
    {
        policy.SetIsOriginAllowed(_ => true)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

// Enable Swagger in all environments for API testing
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Homespun API v1");
});

app.UseCors();

// Map SignalR hubs
app.MapHub<NotificationHub>("/hubs/notifications");
app.MapHub<ClaudeCodeHub>("/hubs/claudecode");
app.MapHub<WorkflowHub>("/hubs/workflows");

// Map health check endpoints (/health for readiness, /alive for liveness)
app.MapDefaultEndpoints();

// Map API controllers
app.MapControllers();

// MapStaticAssets serves static files via endpoint routing
app.MapStaticAssets();

app.Run();

// Make the implicit Program class public so WebApplicationFactory<Program> can reference it
public partial class Program { }
