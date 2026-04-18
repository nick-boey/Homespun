## ADDED Requirements

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
- **AND** the server runs with `HOMESPUN_MOCK_MODE=true`, `MockMode:UseLiveClaudeSessions=true`, `AgentExecution:Mode=Docker`
- **AND** agent sessions spawn sibling worker containers via DooD using the host's Docker socket
- **AND** no worker container is pre-started by the AppHost

#### Scenario: dev-container profile launches full stack in containers for prod parity
- **WHEN** a developer runs `dotnet run --project src/Homespun.AppHost --launch-profile dev-container`
- **THEN** the AppHost builds and starts server, web, and worker via `AddDockerfile` (not `AddProject` / `AddNpmApp`)
- **AND** PLG (Loki, Promtail, Grafana) starts as on every dev profile
- **AND** the server runs in mock mode (`HOMESPUN_MOCK_MODE=true`, `ASPNETCORE_ENVIRONMENT=Mock`, `MockMode:UseLiveClaudeSessions=true`)
- **AND** `AgentExecution:Mode` is set to `Docker` on non-Windows hosts and `SingleContainer` on Windows hosts (selected at AppHost boot via `RuntimeInformation.IsOSPlatform`)
- **AND** when `AgentExecution:Mode=Docker`, the server container mounts `/var/run/docker.sock` and joins the docker group so DooD sibling spawns continue to work
- **AND** when `AgentExecution:Mode=SingleContainer` (Windows), the pre-run worker container's HTTP endpoint is injected into `AgentExecution:SingleContainer:WorkerUrl`

#### Scenario: dev-windows profile launches with single pre-run worker
- **WHEN** a developer runs `dotnet run --project src/Homespun.AppHost --launch-profile dev-windows`
- **THEN** the AppHost starts server, web, PLG, AND a single worker container (`AddContainer` for the worker image)
- **AND** the server runs with `HOMESPUN_MOCK_MODE=true`, `MockMode:UseLiveClaudeSessions=true`, `AgentExecution:Mode=SingleContainer`
- **AND** `AgentExecution:SingleContainer:WorkerUrl` resolves to the AppHost-managed worker resource's HTTP endpoint
- **AND** agent sessions are forwarded to the single pre-running worker (single active session at a time)

#### Scenario: prod launch path is unchanged
- **WHEN** a developer runs `scripts/run.sh` or `scripts/run-komodo.sh` in production
- **THEN** the existing `docker-compose.yml` + Komodo flow executes unchanged
- **AND** the AppHost is not invoked

### Requirement: Temp-dir mock data isolation across dev profiles

Every dev profile SHALL provision a hermetic temporary data folder at server boot via the existing `TempDataFolderService`. No dev profile SHALL read from or write to `~/.homespun-container` or any other persistent data directory.

#### Scenario: Each dev launch gets fresh mock data
- **WHEN** a developer starts any dev profile (`dev-mock`, `dev-live`, or `dev-windows`)
- **THEN** the server creates `{temp}/homespun-mock-{guid}/` on boot
- **AND** seeded demo data (projects, issues, sessions cache) is written under that folder
- **AND** on shutdown, the folder is deleted

### Requirement: PLG stack starts with every dev profile

The AppHost SHALL start Loki, Promtail, and Grafana containers on every dev profile, including `dev-mock`. Ports and configuration files SHALL match those used by `docker-compose.yml` (Grafana at `3000`, Loki at `3100`, config from `config/loki-config.yml`, `config/promtail-config.yml`, `config/grafana/`).

#### Scenario: PLG reachable in every profile
- **WHEN** any dev profile has started successfully
- **THEN** `curl -s http://localhost:3100/ready` returns a ready response
- **AND** Grafana is reachable at `http://localhost:3000`

#### Scenario: Server logs surface via Aspire dashboard (not Loki) in dev
- **WHEN** the server is running under `AddProject` in any dev profile
- **THEN** server logs, traces, and metrics are visible in the Aspire dashboard via OTLP
- **AND** Promtail's docker-label scrape captures only containerized resources (worker containers when present, PLG containers themselves)

### Requirement: Dev secrets stored in dotnet user-secrets, not `.env`

Dev profiles SHALL obtain `GITHUB_TOKEN` and `CLAUDE_CODE_OAUTH_TOKEN` from `dotnet user-secrets` scoped to the `Homespun.AppHost` project via Aspire parameters (`builder.AddParameter(name, secret: true)`). The AppHost SHALL NOT read these values from `.env` at repo root.

#### Scenario: Parameters wired through Aspire secrets
- **WHEN** the AppHost starts any dev profile
- **THEN** the server receives `GITHUB_TOKEN` and `CLAUDE_CODE_OAUTH_TOKEN` environment variables whose values are sourced from user-secrets keys `Parameters:github-token` and `Parameters:claude-oauth-token`
- **AND** no `.env` file is loaded by the AppHost

#### Scenario: One-shot migration script populates user-secrets from .env
- **WHEN** a developer runs `scripts/set-user-secrets.sh` (or `.ps1` on Windows)
- **THEN** the script reads `GITHUB_TOKEN` and `CLAUDE_CODE_OAUTH_TOKEN` from `.env` at repo root
- **AND** calls `dotnet user-secrets set "Parameters:github-token" <value> --project src/Homespun.AppHost`
- **AND** calls `dotnet user-secrets set "Parameters:claude-oauth-token" <value> --project src/Homespun.AppHost`
- **AND** if `.env` is missing or a key is blank, prints a warning and leaves existing user-secret entries untouched

#### Scenario: Prod tooling keeps consuming .env
- **WHEN** `scripts/run.sh`, `scripts/run-komodo.sh`, or any other prod script is invoked
- **THEN** it reads `GITHUB_TOKEN`, `CLAUDE_CODE_OAUTH_TOKEN`, `TAILSCALE_AUTH_KEY`, `HSP_EXTERNAL_HOSTNAME`, and Komodo keys from `.env` unchanged

### Requirement: Mock-mode supports all three agent-execution modes

`MockServiceExtensions.AddLiveClaudeSessionServices` SHALL register the correct `IAgentExecutionService` implementation based on `AgentExecution:Mode`:

| `AgentExecution:Mode` | Registered implementation |
|---|---|
| `Docker` | `DockerAgentExecutionService` |
| `SingleContainer` | `SingleContainerAgentExecutionService` |
| unset / other | `MockAgentExecutionService` |

The `Docker` branch SHALL also register `IContainerDiscoveryService` and the `ContainerRecoveryHostedService` matching the non-mock path, and SHALL apply the `HSP_HOST_DATA_PATH` post-configure override to `DockerAgentExecutionOptions`.

#### Scenario: Mock mode + Docker wires real DooD agent execution
- **WHEN** the server starts with `HOMESPUN_MOCK_MODE=true`, `MockMode:UseLiveClaudeSessions=true`, `AgentExecution:Mode=Docker`
- **THEN** `IAgentExecutionService` resolves to `DockerAgentExecutionService`
- **AND** `IContainerDiscoveryService` is registered
- **AND** the container-recovery hosted service is running
- **AND** `DockerAgentExecutionOptions.HostDataPath` is populated from `HSP_HOST_DATA_PATH` when set

#### Scenario: Mock mode + SingleContainer continues to work unchanged
- **WHEN** the server starts with `HOMESPUN_MOCK_MODE=true`, `MockMode:UseLiveClaudeSessions=true`, `AgentExecution:Mode=SingleContainer`, and a valid `AgentExecution:SingleContainer:WorkerUrl`
- **THEN** `IAgentExecutionService` resolves to `SingleContainerAgentExecutionService`

#### Scenario: Mock mode without AgentExecution:Mode falls through to MockAgentExecutionService
- **WHEN** the server starts with `HOMESPUN_MOCK_MODE=true`, `MockMode:UseLiveClaudeSessions=true`, and no `AgentExecution:Mode` set (or a value not matching `Docker`/`SingleContainer`)
- **THEN** `IAgentExecutionService` resolves to `MockAgentExecutionService`

### Requirement: Legacy dev-launch scripts removed

The following scripts SHALL be deleted from `scripts/` and removed from all documentation and CI references: `mock.sh`, `mock.ps1`, `run.sh`, `run.ps1`.

The following scripts SHALL remain unchanged: `install-komodo.sh`, `run-komodo.sh`, `sync-komodo-vars.sh`, `deploy-infra.sh`, `teardown-infra.sh`, `build-containers.sh`, `coverage-gate.sh`, `coverage-ratchet-update.sh`.

#### Scenario: Legacy dev scripts are absent
- **WHEN** the repo tree is inspected after this change lands
- **THEN** `scripts/mock.sh`, `scripts/mock.ps1`, `scripts/run.sh`, `scripts/run.ps1` do not exist

#### Scenario: Prod scripts are preserved
- **WHEN** the repo tree is inspected after this change lands
- **THEN** `scripts/install-komodo.sh`, `scripts/run-komodo.sh`, `scripts/sync-komodo-vars.sh`, `scripts/deploy-infra.sh`, `scripts/teardown-infra.sh`, `scripts/build-containers.sh`, `scripts/coverage-gate.sh`, and `scripts/coverage-ratchet-update.sh` exist with behavior unchanged

### Requirement: Dev-loop documentation and tooling reference AppHost

`CLAUDE.md` and `src/Homespun.Web/playwright.config.ts` SHALL reference the AppHost launch command and no longer reference `mock.sh` or `run.sh` as dev entry points.

#### Scenario: Playwright e2e uses AppHost as webServer
- **WHEN** Playwright e2e tests run
- **THEN** `playwright.config.ts` `webServer` invokes `dotnet run --project src/Homespun.AppHost --launch-profile dev-mock`
- **AND** the server is reachable at the stable dev-mock port (preserving the existing `5101` port choice)

#### Scenario: CLAUDE.md reflects new dev workflow
- **WHEN** a developer reads `CLAUDE.md` for onboarding or inspection instructions
- **THEN** the "Inspection with Playwright MCP and Mock Mode" section references `dotnet run --project src/Homespun.AppHost --launch-profile dev-mock`
- **AND** references to `./scripts/mock.sh` / `./scripts/mock.ps1` are removed or redirected to the AppHost command
- **AND** the "Critical Shell Management Rules" section continues to warn against killing the long-lived dev process
