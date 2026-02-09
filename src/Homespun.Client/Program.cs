using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Homespun.Client;
using Homespun.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Configure HttpClient to point to the server API
builder.Services.AddScoped(sp =>
    new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// Register HTTP API services
builder.Services.AddScoped<HttpProjectApiService>();
builder.Services.AddScoped<HttpSessionApiService>();
builder.Services.AddScoped<HttpPullRequestApiService>();
builder.Services.AddScoped<HttpIssueApiService>();
builder.Services.AddScoped<HttpCloneApiService>();
builder.Services.AddScoped<HttpNotificationApiService>();
builder.Services.AddScoped<HttpOrchestrationApiService>();
builder.Services.AddScoped<HttpAgentPromptApiService>();
builder.Services.AddScoped<HttpGitHubInfoApiService>();
builder.Services.AddScoped<HttpGraphApiService>();
builder.Services.AddScoped<HttpSessionCacheApiService>();
builder.Services.AddScoped<HttpIssuePrStatusApiService>();
builder.Services.AddScoped<HttpFleeceSyncApiService>();
builder.Services.AddScoped<HttpComponentRegistryService>();

// Register SignalR services
builder.Services.AddScoped<ClaudeCodeSignalRService>();
builder.Services.AddScoped<NotificationSignalRService>();

// Register client-side services
builder.Services.AddScoped<IBreadcrumbService, BreadcrumbService>();
builder.Services.AddSingleton<IMarkdownRenderingService, MarkdownRenderingService>();
builder.Services.AddSingleton<IAgentStartupTracker, AgentStartupTracker>();

await builder.Build().RunAsync();
