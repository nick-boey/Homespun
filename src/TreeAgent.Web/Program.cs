using TreeAgent.Web.Components;
using TreeAgent.Web.Features.Commands;
using TreeAgent.Web.Features.Git;
using TreeAgent.Web.Features.GitHub;
using TreeAgent.Web.Features.OpenCode;
using TreeAgent.Web.Features.OpenCode.Hubs;
using TreeAgent.Web.Features.OpenCode.Services;
using TreeAgent.Web.Features.Projects;
using TreeAgent.Web.Features.PullRequests;
using TreeAgent.Web.Features.PullRequests.Data;
using TreeAgent.Web.Features.Roadmap;

var builder = WebApplication.CreateBuilder(args);

// Configure console logging with readable output
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// Add services to the container.
var dataPath = builder.Configuration["TREEAGENT_DATA_PATH"] ?? "treeagent-data.json";

// Ensure the data directory exists
var dataDirectory = Path.GetDirectoryName(dataPath);
if (!string.IsNullOrEmpty(dataDirectory) && !Directory.Exists(dataDirectory))
{
    Directory.CreateDirectory(dataDirectory);
}

// Register JSON data store as singleton
builder.Services.AddSingleton<IDataStore>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<JsonDataStore>>();
    return new JsonDataStore(dataPath, logger);
});

// Core services
builder.Services.AddScoped<ProjectService>();
builder.Services.AddSingleton<ICommandRunner, CommandRunner>();
builder.Services.AddSingleton<IGitWorktreeService, GitWorktreeService>();
builder.Services.AddScoped<PullRequestDataService>();
builder.Services.AddSingleton<IGitHubClientWrapper, GitHubClientWrapper>();
builder.Services.AddScoped<IGitHubService, GitHubService>();
builder.Services.AddScoped<PullRequestWorkflowService>();
builder.Services.AddScoped<IRoadmapService, RoadmapService>();

// OpenCode services
builder.Services.Configure<OpenCodeOptions>(
    builder.Configuration.GetSection(OpenCodeOptions.SectionName));
builder.Services.AddHttpClient<IOpenCodeClient, OpenCodeClient>();
builder.Services.AddSingleton<IOpenCodeServerManager, OpenCodeServerManager>();
builder.Services.AddScoped<IOpenCodeConfigGenerator, OpenCodeConfigGenerator>();
builder.Services.AddScoped<IAgentWorkflowService, AgentWorkflowService>();

builder.Services.AddSignalR();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Map SignalR hubs
app.MapHub<AgentHub>("/hubs/agent");

app.Run();
