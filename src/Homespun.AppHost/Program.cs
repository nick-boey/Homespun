var builder = DistributedApplication.CreateBuilder(args);

// Secrets as parameters (set via user-secrets or env vars)
var githubToken = builder.AddParameter("github-token", secret: true);
var claudeOauthToken = builder.AddParameter("claude-oauth-token", secret: true);

// Worker (Node.js/Hono)
var worker = builder.AddNodeApp("worker", "../Homespun.Worker", "src/index.ts")
    .WithNpm(install: false)
    .WithRunScript("dev")
    .WithHttpEndpoint(port: 8080, env: "PORT")
    .WithEnvironment("CLAUDE_CODE_OAUTH_TOKEN", claudeOauthToken)
    .WithExternalHttpEndpoints();

// Server (ASP.NET)
var server = builder.AddProject<Projects.Homespun_Server>("server")
    .WithReference(worker)
    .WithEnvironment("MiniPrompt__SidecarUrl", worker.GetEndpoint("http"))
    .WithEnvironment("GITHUB_TOKEN", githubToken)
    .WithEnvironment("CLAUDE_CODE_OAUTH_TOKEN", claudeOauthToken)
    .WithExternalHttpEndpoints();

// Web (React/Vite)
builder.AddViteApp("web", "../Homespun.Web")
    .WithNpm(install: false)
    .WithReference(server)
    .WithHttpEndpoint(port: 5173, name: "vite")
    .WithEnvironment("VITE_API_URL", server.GetEndpoint("http"))
    .WithExternalHttpEndpoints();

builder.Build().Run();
