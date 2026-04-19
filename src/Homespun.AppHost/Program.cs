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
var isDockerAgent = string.Equals(agentMode, "Docker", StringComparison.OrdinalIgnoreCase);

// Deterministic tag for the locally-built worker image. The AppHost builds
// `src/Homespun.Worker/Dockerfile` via AddDockerfile below and Docker Desktop's
// daemon stores it as `worker:dev`, addressable both by Aspire (for pre-run
// SingleContainer profiles) and by sibling `docker run` calls the server
// issues in Docker agent mode (DooD).
const string localWorkerImageTag = "worker:dev";

// ─── Secret parameters (user-secrets, env vars, azd env) ──────────────────────
var githubToken = builder.AddParameter("github-token", secret: true);
var claudeOauthToken = builder.AddParameter("claude-oauth-token", secret: true);

// ─── PLG stack (always on) ────────────────────────────────────────────────────
var loki = builder.AddContainer("loki", "grafana/loki", "3.6.6")
    .WithBindMount("../../config/loki-config.yml", "/etc/loki/local-config.yaml", isReadOnly: true)
    .WithVolume("homespun-loki-data", "/loki")
    .WithArgs("-config.file=/etc/loki/local-config.yaml")
    .WithHttpEndpoint(targetPort: 3100, port: 3100, name: "http");

// Promtail relies on the Docker socket (docker_sd_configs) for container
// discovery AND log streaming. The additional /var/lib/docker/containers
// mount that the prod compose file uses is a performance optimisation for
// Linux hosts that doesn't exist on macOS Docker Desktop (Docker's state
// lives inside the Linux VM, not the host FS) — mounting it there makes DCP
// abort container creation with FailedToStart. Keep the socket mount only;
// promtail still resolves logs through Docker's HTTP API.
var promtail = builder.AddContainer("promtail", "grafana/promtail", "3.6.6")
    .WithBindMount("../../config/promtail-config.yml", "/etc/promtail/config.yml", isReadOnly: true)
    .WithBindMount("/var/run/docker.sock", "/var/run/docker.sock", isReadOnly: true)
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

// ─── Worker image (always built from this repo in dev) ────────────────────────
// SingleContainer profiles pre-run the worker and inject its endpoint into the
// server. Docker-agent profiles only need the image built; the server spawns
// siblings via DooD using `localWorkerImageTag`.
IResourceBuilder<ContainerResource>? workerContainer = null;
EndpointReference? workerEndpoint = null;

if (isSingleContainer)
{
    workerContainer = builder.AddDockerfile("worker", "../Homespun.Worker")
        .WithImageTag("dev")
        .WithContainerRuntimeArgs("--label", "logging=promtail")
        .WithHttpEndpoint(targetPort: 8080, name: "http")
        .WithEnvironment("PORT", "8080")
        .WithEnvironment("CLAUDE_CODE_OAUTH_TOKEN", claudeOauthToken)
        .WithEnvironment("DEBUG_AGENT_SDK", "true");
    workerEndpoint = workerContainer.GetEndpoint("http");
}
else if (isDockerAgent)
{
    // Docker agent mode (dev-live host + dev-container non-Windows): the
    // server spawns sibling workers via DooD. No endpoint is published — the
    // container idles; its purpose is to drive the image build so the
    // `worker:dev` tag is available on the host daemon before sessions start.
    workerContainer = builder.AddDockerfile("worker", "../Homespun.Worker")
        .WithImageTag("dev")
        .WithContainerRuntimeArgs("--label", "logging=promtail");
}

// ─── Server + web — two hosting paths ─────────────────────────────────────────
if (isContainerHosting)
{
    // dev-container: server + web built via Dockerfile for prod parity. Worker
    // is already wired above (SingleContainer → pre-run; Docker → build-only).
    var serverContainer = builder.AddDockerfile("server", "../../")
        .WithContainerRuntimeArgs("--label", "logging=promtail")
        .WithHttpEndpoint(targetPort: 8080, port: 5101, name: "http")
        // DooD mount — sibling worker spawns rely on the host docker socket.
        .WithBindMount("/var/run/docker.sock", "/var/run/docker.sock")
        // The Dockerfile's USER homespun (non-root) can't access the bind-mounted
        // docker socket on Docker Desktop for Mac/Windows — the socket lives inside
        // the Linux VM with root ownership and the homespun UID isn't in any group
        // that grants access. Run the server as root in dev-container so sibling
        // worker `docker run` invocations from DockerAgentExecutionService succeed.
        // Dev-only concession; prod deploys via docker-compose+Komodo keep the
        // non-root USER directive.
        .WithContainerRuntimeArgs("--user", "0:0")
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
    if (isDockerAgent)
    {
        serverContainer.WithEnvironment("AgentExecution__Docker__WorkerImage", localWorkerImageTag);
        // Aspire's DCP owns the server's session network and strips non-DCP
        // endpoints from it shortly after docker run. Bypass network routing
        // entirely: publish sibling 8080 to host 0.0.0.0 and have the server
        // reach it via host.docker.internal. Works without needing siblings to
        // share a network with the server.
        serverContainer.WithEnvironment("AgentExecution__Docker__UseLoopbackPortMapping", "true");
        serverContainer.WithEnvironment("AgentExecution__Docker__LoopbackBindHost", "0.0.0.0");
        serverContainer.WithEnvironment("AgentExecution__Docker__WorkerHost", "host.docker.internal");
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
        .WithContainerRuntimeArgs("--label", "logging=promtail")
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
    if (isDockerAgent)
    {
        server.WithEnvironment("AgentExecution__Docker__WorkerImage", localWorkerImageTag);
        // Host-mode server (dev-live) on macOS / Windows Docker Desktop can't
        // reach sibling workers via their bridge-network IPs. Publish the
        // worker's 8080 to a random host loopback port so the server talks
        // to http://127.0.0.1:{hostPort}.
        server.WithEnvironment("AgentExecution__Docker__UseLoopbackPortMapping", "true");
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
