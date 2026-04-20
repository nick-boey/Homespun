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

// ─── Seq (always on) ──────────────────────────────────────────────────────────
// Single long-lived observability sink — logs + traces flow here via OTLP.
// Dev port pinned to 5341 for stable bookmarks / Playwright / smoke tests.
// Aspire's default DCP port proxy remaps dynamic host ports; mutate the
// endpoint in place with IsProxied=false so Docker binds 5341:80 directly.
var seq = builder.AddSeq("seq", port: 5341)
    .WithLifetime(ContainerLifetime.Persistent)
    .WithEnvironment("ACCEPT_EULA", "Y")
    .WithEndpoint("http", e =>
    {
        e.Port = 5341;
        e.TargetPort = 80;
        e.IsProxied = false;
    });

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
        .WithHttpEndpoint(targetPort: 8080, name: "http")
        .WithEnvironment("PORT", "8080")
        .WithEnvironment("CLAUDE_CODE_OAUTH_TOKEN", claudeOauthToken)
        .WithEnvironment("DEBUG_AGENT_SDK", "true")
        .WithEnvironment("OTEL_SERVICE_NAME", "homespun.worker")
        .WithReference(seq)
        .WaitFor(seq);
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
        .WithEnvironment("OTEL_SERVICE_NAME", "homespun.worker")
        .WithReference(seq)
        .WaitFor(seq);
}

// ─── Server + web — two hosting paths ─────────────────────────────────────────
if (isContainerHosting)
{
    // dev-container: server + web built via Dockerfile for prod parity. Worker
    // is already wired above (SingleContainer → pre-run; Docker → build-only).
    var serverContainer = builder.AddDockerfile("server", "../../")
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
        .WithEnvironment("OTEL_SERVICE_NAME", "homespun.server")
        // OtlpFanout receiver ships worker/client OTLP into Seq via the
        // container's ingest endpoint. Aspire-leg URL is injected separately
        // by Aspire itself as OTEL_EXPORTER_OTLP_ENDPOINT.
        .WithEnvironment(
            "OtlpFanout__SeqBaseUrl",
            ReferenceExpression.Create($"{seq.GetEndpoint("http")}/ingest/otlp"))
        .WithReference(seq)
        .WaitFor(seq);

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
        // Container-mode: sibling workers reach the server over the compose
        // network using the service hostname `server`. The host-mode default
        // (`host.docker.internal`) does not resolve between sibling containers.
        serverContainer.WithEnvironment(
            "AgentExecution__Docker__ServerOtlpProxyUrl",
            "http://server:8080/api/otlp/v1");
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
        .WithEnvironment("OTEL_SERVICE_NAME", "homespun.web")
        .WithReference(seq)
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
        .WithEnvironment("OTEL_SERVICE_NAME", "homespun.server")
        // OtlpFanout receiver ships worker/client OTLP into Seq. Aspire-leg URL
        // is injected separately by Aspire itself as OTEL_EXPORTER_OTLP_ENDPOINT.
        .WithEnvironment(
            "OtlpFanout__SeqBaseUrl",
            ReferenceExpression.Create($"{seq.GetEndpoint("http")}/ingest/otlp"))
        .WithReference(seq)
        .WaitFor(seq);

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
