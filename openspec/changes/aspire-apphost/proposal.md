## Why

Twelve shell scripts in `scripts/` (plus PowerShell mirrors) drive local dev and prod launch, each re-implementing env-var loading, data-dir setup, docker-compose plumbing, and mode selection. New contributors wade through `mock.sh` vs `run.sh --local` vs `run.sh --local-agents` vs `run.sh --mock` vs `mock.sh --with-worker` to understand how to run the app. Mock-mode + live-agent combinations are inconsistently supported (e.g., `AgentExecution:Mode=Docker` has no branch in `MockServiceExtensions`, only `SingleContainer` and Mock). Aspire AppHost already exists in-tree but is unused; wiring the dev experience through it gives one entry point, built-in dashboard, and first-class secret handling — while leaving prod on the battle-tested `docker compose` + Komodo path.

## What Changes

- Add four Aspire launch profiles driving all dev scenarios: `dev-mock` (temp data + mock agents), `dev-live` (temp data + Docker DooD agents), `dev-windows` (temp data + SingleContainer worker), `dev-container` (temp data + Docker DooD agents, server and web built from Dockerfiles for prod parity).
- Rewrite `Homespun.AppHost/Program.cs` to wire server (`AddProject` for host-process profiles, `AddDockerfile` for `dev-container`), web (`AddNpmApp` Vite HMR for host profiles, `AddDockerfile` nginx for `dev-container`), PLG stack (`AddContainer` Loki/Promtail/Grafana), and — profile-permitting — a pre-run worker container for SingleContainer mode.
- PLG (Loki + Promtail + Grafana) always starts alongside the app on every dev profile.
- Extend `MockServiceExtensions.AddLiveClaudeSessionServices` to register `DockerAgentExecutionService` when `AgentExecution:Mode=Docker` — currently the only branches are `SingleContainer` and Mock, which blocks the dev-live profile.
- Migrate dev secrets (`GITHUB_TOKEN`, `CLAUDE_CODE_OAUTH_TOKEN`) from `.env` to `dotnet user-secrets` scoped to `Homespun.AppHost`. Add `scripts/set-user-secrets.sh` and `scripts/set-user-secrets.ps1` to migrate `.env` values one-shot.
- Extend `AgentExecutionModeStartupTests` to cover the new mock-mode + `AgentExecution:Mode=Docker` registration path.
- Update `CLAUDE.md` dev-loop instructions and `src/Homespun.Web/playwright.config.ts` `webServer` command to launch via `dotnet run --project src/Homespun.AppHost --launch-profile dev-mock`.
- **BREAKING (dev-only):** Delete `scripts/mock.sh`, `scripts/mock.ps1`, `scripts/run.sh`, `scripts/run.ps1`. Local dev loops, editor tasks, and CI invocations must switch to `dotnet run --project src/Homespun.AppHost --launch-profile <profile>`.

### Out of scope (unchanged)

- Prod deployment: `docker-compose.yml`, `scripts/run-komodo.sh`, `scripts/install-komodo.sh`, `scripts/sync-komodo-vars.sh`, `scripts/deploy-infra.sh`, `scripts/teardown-infra.sh`, `scripts/build-containers.sh`, `scripts/coverage-*.sh`, `.env` (still consumed by prod scripts and Komodo).
- Komodo stays external to Aspire — prod hot-swap stream remains independent.
- Server mock-mode data layout (`TempDataFolderService`) and agent-execution service implementations beyond the registration-branch gap.

## Capabilities

### New Capabilities
- `dev-orchestration`: How local development is bootstrapped — launch profiles, service wiring, data/agent mode selection, PLG, secrets, and the retirement of the script-based dev entry points.

### Modified Capabilities
<!-- No existing spec governs dev-launch scripts; behavior captured entirely under the new dev-orchestration capability. -->

## Impact

- **New project surface**: `src/Homespun.AppHost/Program.cs` (rewritten), `src/Homespun.AppHost/Properties/launchSettings.json` (new).
- **Touched code**:
  - `src/Homespun.Server/Features/Testing/MockServiceExtensions.cs` — add `Docker`-mode registration branch.
  - `tests/Homespun.Api.Tests/Features/AgentExecutionModeStartupTests.cs` — new coverage for Mock+Docker branch.
- **New scripts**: `scripts/set-user-secrets.sh`, `scripts/set-user-secrets.ps1`.
- **Deleted scripts**: `scripts/mock.sh`, `scripts/mock.ps1`, `scripts/run.sh`, `scripts/run.ps1`.
- **Docs**: `CLAUDE.md` (dev workflow, Pre-PR checklist commentary, mock.sh references), `src/Homespun.Web/playwright.config.ts` (webServer command).
- **Dependencies**: `Aspire.Hosting.AppHost` 13.2.0 and `Aspire.Hosting.JavaScript` 13.2.0 already referenced; no new NuGet packages. Dev hosts need `dotnet user-secrets` (shipped with SDK) and Aspire workload (already required to build AppHost today).
- **CI**: any pipeline invoking `mock.sh` for e2e (e.g., Playwright job) swaps to AppHost command; secret injection moves from `.env` piping to user-secrets bootstrap step.
- **Runtime invariants preserved**: `HOMESPUN_MOCK_MODE`, `MockMode:UseLiveClaudeSessions`, `AgentExecution:Mode`, `AgentExecution:SingleContainer:WorkerUrl`, and `HSP_HOST_DATA_PATH` config keys unchanged — AppHost sets them per profile instead of scripts.
