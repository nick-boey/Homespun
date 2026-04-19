## ADDED Requirements

### Requirement: Dev worker container image always built from this repo

The AppHost SHALL build the worker container image from `src/Homespun.Worker/Dockerfile` via `AddDockerfile` in every dev profile that requires a worker container, whether pre-running (`dev-windows`, `dev-container` on Windows) or DooD-spawned (`dev-live`, `dev-container` on non-Windows). No AppHost code path SHALL reference a GHCR image string for the worker (e.g. `ghcr.io/nick-boey/homespun-worker`). Production deployment (`docker-compose.yml` + Komodo) continues to pull from GHCR unchanged.

#### Scenario: dev-windows pre-run worker is built locally
- **WHEN** a developer runs `dotnet run --project src/Homespun.AppHost --launch-profile dev-windows`
- **THEN** the AppHost builds the worker container image from `src/Homespun.Worker/Dockerfile` via `AddDockerfile`
- **AND** no `AddContainer` call in the AppHost references any `ghcr.io/*` string
- **AND** the built image's HTTP endpoint is injected into `AgentExecution:SingleContainer:WorkerUrl` on the server resource

#### Scenario: dev-live injects the locally-built worker image into DooD spawns
- **WHEN** a developer runs `dotnet run --project src/Homespun.AppHost --launch-profile dev-live`
- **THEN** the AppHost builds the worker container image from `src/Homespun.Worker/Dockerfile` via `AddDockerfile`
- **AND** the server resource receives `AgentExecution__Docker__WorkerImage=<locally-built-tag>` in its environment
- **AND** `DockerAgentExecutionService`'s `docker run` commands start containers from that locally-built tag, not from `ghcr.io/nick-boey/homespun-worker:latest`

#### Scenario: dev-container worker is built locally on every host OS
- **WHEN** a developer runs `dotnet run --project src/Homespun.AppHost --launch-profile dev-container` on either macOS/Linux or Windows
- **THEN** the AppHost builds the worker container image from `src/Homespun.Worker/Dockerfile` via `AddDockerfile`
- **AND** on Windows hosts the pre-run worker container uses that locally-built image
- **AND** on non-Windows hosts the server receives `AgentExecution__Docker__WorkerImage` pointing at that same locally-built image for DooD sibling spawns

#### Scenario: AppHost source contains no GHCR reference for worker
- **WHEN** the repo tree is inspected after this change lands
- **THEN** `src/Homespun.AppHost/Program.cs` contains no literal string matching `ghcr.io/nick-boey/homespun-worker`
- **AND** the `AppHostTests` suite asserts no resource in the built distributed application model references a GHCR image for the worker

#### Scenario: First AppHost boot rebuilds the worker image when the Dockerfile changes
- **WHEN** a developer edits `src/Homespun.Worker/Dockerfile` or its copied sources and re-runs any dev profile
- **THEN** the AppHost rebuilds the worker image before starting the worker or server resource that depends on it
- **AND** subsequent boots without source changes reuse the Docker layer cache (no full rebuild)

## MODIFIED Requirements

### Requirement: Single dev launch entry point via Aspire AppHost

The system SHALL provide a single command to launch the full dev stack in any supported mode: `dotnet run --project src/Homespun.AppHost --launch-profile <profile>`. No alternative dev bootstrap scripts SHALL exist in the `scripts/` folder for launching the app locally.

#### Scenario: dev-mock profile launches full local dev stack with mock agents
- **WHEN** a developer runs `dotnet run --project src/Homespun.AppHost --launch-profile dev-mock`
- **THEN** the AppHost starts the server (`AddProject` of `Homespun.Server`), the web frontend (`AddNpmApp` of `Homespun.Web` running Vite), and the PLG stack (Loki, Promtail, Grafana containers)
- **AND** the server runs in mock mode (`HOMESPUN_MOCK_MODE=true`, `ASPNETCORE_ENVIRONMENT=Mock`, `MockMode:UseLiveClaudeSessions=false`)
- **AND** no worker container is started
- **AND** agent sessions use `MockAgentExecutionService` emitting canned A2A events

#### Scenario: dev-live profile launches with real Docker-spawned agents
- **WHEN** a developer runs `dotnet run --project src/Homespun.AppHost --launch-profile dev-live`
- **THEN** the AppHost starts server, web, and PLG as in dev-mock
- **AND** the AppHost additionally builds the worker container image from `src/Homespun.Worker/Dockerfile` via `AddDockerfile` so the image tag is addressable by the server's DooD calls
- **AND** the server runs with `HOMESPUN_MOCK_MODE=true`, `MockMode:UseLiveClaudeSessions=true`, `AgentExecution:Mode=Docker`
- **AND** the server resource receives `AgentExecution__Docker__WorkerImage` set to the AppHost-built worker image tag
- **AND** agent sessions spawn sibling worker containers via DooD using the host's Docker socket and that locally-built image
- **AND** no worker container is pre-started by the AppHost

#### Scenario: dev-container profile launches full stack in containers for prod parity
- **WHEN** a developer runs `dotnet run --project src/Homespun.AppHost --launch-profile dev-container`
- **THEN** the AppHost builds and starts server, web, and worker via `AddDockerfile` (not `AddProject` / `AddNpmApp`)
- **AND** PLG (Loki, Promtail, Grafana) starts as on every dev profile
- **AND** the server runs in mock mode (`HOMESPUN_MOCK_MODE=true`, `ASPNETCORE_ENVIRONMENT=Mock`, `MockMode:UseLiveClaudeSessions=true`)
- **AND** `AgentExecution:Mode` is set to `Docker` on non-Windows hosts and `SingleContainer` on Windows hosts (selected at AppHost boot via `RuntimeInformation.IsOSPlatform`)
- **AND** when `AgentExecution:Mode=Docker`, the server container mounts `/var/run/docker.sock`, joins the docker group, and receives `AgentExecution__Docker__WorkerImage` pointing at the AppHost-built worker image so DooD sibling spawns continue to work without touching GHCR
- **AND** when `AgentExecution:Mode=SingleContainer` (Windows), the pre-run worker container is built from `src/Homespun.Worker/Dockerfile` and its HTTP endpoint is injected into `AgentExecution:SingleContainer:WorkerUrl`

#### Scenario: dev-windows profile launches with single pre-run worker
- **WHEN** a developer runs `dotnet run --project src/Homespun.AppHost --launch-profile dev-windows`
- **THEN** the AppHost starts server, web, PLG, AND a single worker container built from `src/Homespun.Worker/Dockerfile` via `AddDockerfile`
- **AND** the server runs with `HOMESPUN_MOCK_MODE=true`, `MockMode:UseLiveClaudeSessions=true`, `AgentExecution:Mode=SingleContainer`
- **AND** `AgentExecution:SingleContainer:WorkerUrl` resolves to the AppHost-managed worker resource's HTTP endpoint
- **AND** agent sessions are forwarded to the single pre-running worker (single active session at a time)
- **AND** the AppHost does NOT pull any image from `ghcr.io/nick-boey/homespun-worker`

#### Scenario: prod launch path is unchanged
- **WHEN** a developer runs `scripts/run.sh` or `scripts/run-komodo.sh` in production
- **THEN** the existing `docker-compose.yml` + Komodo flow executes unchanged
- **AND** the AppHost is not invoked
- **AND** production continues to pull worker, server, and web images from GHCR

### Requirement: PLG stack starts with every dev profile

The AppHost SHALL start Loki, Promtail (or an equivalent log-shipper), and Grafana containers on every dev profile, including `dev-mock`. Ports and configuration files SHALL match those used by `docker-compose.yml` (Grafana at `3000`, Loki at `3100`, config from `config/loki-config.yml`, `config/grafana/`, and either `config/promtail-config.yml` or an OTLP-collector config in its place). Log ingestion into Loki SHALL function on both macOS/Docker-Desktop and Linux dev hosts.

#### Scenario: PLG reachable in every profile
- **WHEN** any dev profile has started successfully
- **THEN** `curl -s http://localhost:3100/ready` returns a ready response
- **AND** Grafana is reachable at `http://localhost:3000`
- **AND** the Grafana Loki datasource provisioning at `config/grafana/provisioning/datasources/datasources.yml` resolves to the Loki container over the Aspire-managed network via the `loki` alias

#### Scenario: Server logs reach the Aspire dashboard via OTLP
- **WHEN** the server is running under `AddProject` in any dev profile and a request has been made against it
- **THEN** `aspire otel logs server` returns at least one structured log entry for that request
- **AND** the server's logging pipeline preserves both the OTLP exporter wired by `ServiceDefaults.AddServiceDefaults` and the JSON console formatter used for stdout scraping, in that order (i.e. `Logging.ClearProviders()` does not run after `AddServiceDefaults`)

#### Scenario: Log shipper operational on macOS Docker Desktop
- **WHEN** any dev profile has started on a macOS host running Docker Desktop
- **THEN** the log-shipper resource (Promtail or its OTLP-collector replacement) reaches the `Running` state within the normal startup window
- **AND** the resource does NOT depend on the host path `/var/lib/docker/containers`, which does not exist on macOS Docker Desktop
- **AND** `aspire describe --format Json` reports `state: "Running"` (not `FailedToStart`) for the log-shipper resource

#### Scenario: Containerised dev resources are scraped by the log shipper
- **WHEN** a dev profile that starts containerised resources (worker in `dev-windows`, server/web/worker in `dev-container`) is running
- **THEN** those Aspire-managed containers carry the label `logging=promtail` (or whatever selector the chosen shipper uses)
- **AND** `{container=~"worker.*|server.*|web.*"}` in Loki returns at least one log line per container within 30s of boot

#### Scenario: Server logs surface via Aspire dashboard even when log shipper is down
- **WHEN** the log-shipper resource has not yet reached `Running` or has failed for an unrelated reason
- **THEN** the Aspire dashboard still displays structured logs for the server resource via OTLP
- **AND** visibility into server behaviour is not blocked by log-shipper health
