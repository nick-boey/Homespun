var builder = DistributedApplication.CreateBuilder(args);

// Secrets as parameters (set via user-secrets, env vars, or azd env)
var githubToken = builder.AddParameter("github-token", secret: true);
var claudeOauthToken = builder.AddParameter("claude-oauth-token", secret: true);

// Worker — TypeScript Hono sidecar for mini-prompts and lightweight AI tasks
// Uses a custom Dockerfile for both local Docker and ACA deployment
var worker = builder.AddDockerfile("worker", "../Homespun.Worker")
    .WithHttpEndpoint(targetPort: 8080)
    .WithEnvironment("PORT", "8080")
    .WithEnvironment("CLAUDE_CODE_OAUTH_TOKEN", claudeOauthToken);

// Server — ASP.NET Core API + SignalR hubs
// Uses a custom Dockerfile with a shared base image (Dockerfile.base)
var server = builder.AddDockerfile("server", "../../", "../../Dockerfile")
    .WithHttpEndpoint(targetPort: 8080)
    .WithVolume("homespun-data", "/data")
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Production")
    .WithEnvironment("GITHUB_TOKEN", githubToken)
    .WithEnvironment("CLAUDE_CODE_OAUTH_TOKEN", claudeOauthToken)
    .WithEnvironment("MiniPrompt__SidecarUrl", worker.GetEndpoint("http"))
    .WaitFor(worker);

// Web — React frontend served by nginx
var web = builder.AddDockerfile("web", "../Homespun.Web")
    .WithHttpEndpoint(targetPort: 80)
    .WithEnvironment("VITE_API_URL", server.GetEndpoint("http"))
    .WaitFor(server);

builder.Build().Run();
