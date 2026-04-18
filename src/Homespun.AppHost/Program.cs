using System.Runtime.InteropServices;

var builder = DistributedApplication.CreateBuilder(args);

// ─── Mode knobs (read from launch profile environment) ────────────────────────
// HOMESPUN_AGENT_MODE           = unset | Docker | SingleContainer
// MOCK_MODE_USE_LIVE_SESSIONS   = "true" to wire real Claude SDK via worker
// HOMESPUN_DEV_HOSTING_MODE     = "host" (default) | "container"
var agentMode = Environment.GetEnvironmentVariable("HOMESPUN_AGENT_MODE");
var useLiveSessions =
    string.Equals(Environment.GetEnvironmentVariable("MOCK_MODE_USE_LIVE_SESSIONS"), "true", StringComparison.OrdinalIgnoreCase);
var hostingMode =
    Environment.GetEnvironmentVariable("HOMESPUN_DEV_HOSTING_MODE") ?? "host";
var isContainerHosting = string.Equals(hostingMode, "container", StringComparison.OrdinalIgnoreCase);

// In dev-container the mode auto-selects by host OS when not set.
if (isContainerHosting && string.IsNullOrEmpty(agentMode))
{
    agentMode = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "SingleContainer" : "Docker";
}

var isSingleContainer = string.Equals(agentMode, "SingleContainer", StringComparison.OrdinalIgnoreCase);

// ─── Secret parameters (user-secrets, env vars, azd env) ──────────────────────
var githubToken = builder.AddParameter("github-token", secret: true);
var claudeOauthToken = builder.AddParameter("claude-oauth-token", secret: true);

// ─── PLG stack (always on) ────────────────────────────────────────────────────
var loki = builder.AddContainer("loki", "grafana/loki", "3.6.6")
    .WithBindMount("../../config/loki-config.yml", "/etc/loki/local-config.yaml", isReadOnly: true)
    .WithVolume("homespun-loki-data", "/loki")
    .WithArgs("-config.file=/etc/loki/local-config.yaml")
    .WithHttpEndpoint(targetPort: 3100, port: 3100, name: "http");

var promtail = builder.AddContainer("promtail", "grafana/promtail", "3.6.6")
    .WithBindMount("../../config/promtail-config.yml", "/etc/promtail/config.yml", isReadOnly: true)
    .WithBindMount("/var/run/docker.sock", "/var/run/docker.sock", isReadOnly: true)
    .WithBindMount("/var/lib/docker/containers", "/var/lib/docker/containers", isReadOnly: true)
    .WithArgs("-config.file=/etc/promtail/config.yml")
    .WaitFor(loki);

var grafana = builder.AddContainer("grafana", "grafana/grafana", "12.3.3")
    .WithBindMount("../../config/grafana/grafana.ini", "/etc/grafana/grafana.ini", isReadOnly: true)
    .WithBindMount("../../config/grafana/provisioning", "/etc/grafana/provisioning", isReadOnly: true)
    .WithVolume("homespun-grafana-data", "/var/lib/grafana")
    .WithEnvironment("GF_SECURITY_ADMIN_USER", "admin")
    .WithEnvironment("GF_SECURITY_ADMIN_PASSWORD", "admin")
    .WithEnvironment("GF_AUTH_ANONYMOUS_ENABLED", "true")
    .WithEnvironment("GF_AUTH_ANONYMOUS_ORG_ROLE", "Viewer")
    .WithHttpEndpoint(targetPort: 3000, port: 3000, name: "http")
    .WaitFor(loki);

// ─── Optional pre-run worker (SingleContainer only) ───────────────────────────
IResourceBuilder<ContainerResource>? workerContainer = null;
EndpointReference? workerEndpoint = null;

if (isSingleContainer)
{
    workerContainer = builder.AddContainer("worker", "ghcr.io/nick-boey/homespun-worker", "latest")
        .WithHttpEndpoint(targetPort: 8080, name: "http")
        .WithEnvironment("PORT", "8080")
        .WithEnvironment("CLAUDE_CODE_OAUTH_TOKEN", claudeOauthToken)
        .WithEnvironment("DEBUG_AGENT_SDK", "true");
    workerEndpoint = workerContainer.GetEndpoint("http");
}

// ─── Server + web — two hosting paths ─────────────────────────────────────────
if (isContainerHosting)
{
    // dev-container: server + web + worker all via Dockerfile, for prod parity.
    if (isSingleContainer)
    {
        // Worker is already wired above via AddContainer; no Dockerfile build.
    }
    else
    {
        // Non-Windows container path still spawns sibling workers via DooD;
        // no pre-run worker resource needed, but we still build the worker
        // image so `dev-container` exercises the Dockerfile.
        builder.AddDockerfile("worker", "../Homespun.Worker")
            .WithHttpEndpoint(targetPort: 8080, name: "http")
            .WithEnvironment("PORT", "8080")
            .WithEnvironment("CLAUDE_CODE_OAUTH_TOKEN", claudeOauthToken);
    }

    var serverContainer = builder.AddDockerfile("server", "../../")
        .WithHttpEndpoint(targetPort: 8080, port: 5101, name: "http")
        // DooD mount — sibling worker spawns rely on the host docker socket.
        .WithBindMount("/var/run/docker.sock", "/var/run/docker.sock")
        .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Mock")
        .WithEnvironment("HOMESPUN_MOCK_MODE", "true")
        .WithEnvironment("MockMode__UseLiveClaudeSessions", useLiveSessions ? "true" : "false")
        .WithEnvironment("GITHUB_TOKEN", githubToken)
        .WithEnvironment("CLAUDE_CODE_OAUTH_TOKEN", claudeOauthToken)
        .WaitFor(loki);

    if (!string.IsNullOrEmpty(agentMode))
    {
        serverContainer.WithEnvironment("AgentExecution__Mode", agentMode);
    }
    if (workerEndpoint is not null)
    {
        serverContainer.WithEnvironment("AgentExecution__SingleContainer__WorkerUrl", workerEndpoint);
    }

    // nginx.conf.template in Homespun.Web uses ${UPSTREAM_HOST}:${UPSTREAM_PORT}
    // via the nginx:alpine envsubst entrypoint. In docker-compose the default
    // is `homespun:8080`; for dev-container we point it at the Aspire server
    // resource's container-network hostname (`server`).
    builder.AddDockerfile("web", "../Homespun.Web")
        .WithHttpEndpoint(targetPort: 80, name: "http")
        .WithEnvironment("UPSTREAM_HOST", "server")
        .WithEnvironment("UPSTREAM_PORT", "8080")
        .WithEnvironment("VITE_API_URL", serverContainer.GetEndpoint("http"))
        .WaitFor(serverContainer);
}
else
{
    // Host mode: server via AddProject (Homespun.Server's "mock" launch profile
    // pins applicationUrl=http://localhost:5101, so we don't need to add a
    // second http endpoint here), web via AddViteApp (Vite HMR).
    var server = builder.AddProject<Projects.Homespun_Server>("server", launchProfileName: "mock")
        .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Mock")
        .WithEnvironment("HOMESPUN_MOCK_MODE", "true")
        .WithEnvironment("MockMode__UseLiveClaudeSessions", useLiveSessions ? "true" : "false")
        .WithEnvironment("GITHUB_TOKEN", githubToken)
        .WithEnvironment("CLAUDE_CODE_OAUTH_TOKEN", claudeOauthToken)
        .WaitFor(loki);

    if (!string.IsNullOrEmpty(agentMode))
    {
        server.WithEnvironment("AgentExecution__Mode", agentMode);
    }
    if (workerEndpoint is not null)
    {
        server.WithEnvironment("AgentExecution__SingleContainer__WorkerUrl", workerEndpoint);
    }

    // Pin Vite to 5173 so Playwright + bookmarked URLs stay stable.
    // AddViteApp auto-allocates a dynamic port and passes it to vite via
    // `npm run dev -- --port <N>`. Override the endpoint's allocated port
    // (callback-mode WithEndpoint mutates an existing endpoint in place)
    // and disable the Aspire dev proxy so the host port is direct.
    builder.AddViteApp("web", "../Homespun.Web", "dev")
        .WithEndpoint("http", e =>
        {
            e.Port = 5173;
            e.TargetPort = 5173;
            e.IsProxied = false;
        })
        .WithEnvironment("VITE_API_URL", server.GetEndpoint("http"))
        .WaitFor(server);
}

builder.Build().Run();
