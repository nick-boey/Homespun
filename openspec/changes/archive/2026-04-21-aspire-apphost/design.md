## Context

Dev launch today is split across `scripts/mock.sh`, `scripts/run.sh`, and their PowerShell mirrors. `mock.sh` runs `dotnet run --launch-profile mock` on the host (+ Vite on the side), optionally `docker compose up worker` when `--with-worker` is passed. `run.sh` wraps `docker compose -f docker-compose.yml` around GHCR or locally built images, toggling PLG via compose profile and Tailscale via a sidecar compose file. Three agent-execution paths exist in the server:

1. `DockerAgentExecutionService` — DooD, one sibling worker container per session (production default).
2. `SingleContainerAgentExecutionService` — forwards every session to a single pre-running worker (dev Windows fallback, currently dev-only, single active session at a time).
3. `MockAgentExecutionService` — emits canned A2A events, no worker.

An unused `Homespun.AppHost` project already exists, referencing the right Aspire packages (`Aspire.Hosting.AppHost`, `Aspire.Hosting.JavaScript` 13.2.0), but its `Program.cs` builds every service via `AddDockerfile` and has no profile/mode selection.

Constraint: Komodo is the prod hot-swap mechanism. If Aspire also owns container lifecycle in prod, Komodo and Aspire fight. Prod therefore stays on `docker compose` + Komodo; Aspire is a dev-only tool.

## Goals / Non-Goals

**Goals**
- Single dev entry point: `dotnet run --project src/Homespun.AppHost --launch-profile <name>`.
- Three profiles covering every combination the current scripts cover: `dev-mock`, `dev-live`, `dev-windows`.
- Secrets out of `.env` for dev, into `dotnet user-secrets` scoped to AppHost.
- PLG (Loki + Promtail + Grafana) always runs in dev.
- Server in dev runs via `AddProject` — debugger-attachable, fast inner loop.
- Web in dev runs via `AddNpmApp` — Vite HMR.
- Close the `AgentExecution:Mode=Docker` branch gap in `MockServiceExtensions` so dev-live actually works.

**Non-Goals**
- Changing prod deploy topology. `docker-compose.yml`, `scripts/run-komodo.sh`, `scripts/install-komodo.sh`, `scripts/deploy-infra.sh` unchanged.
- Running Aspire in prod. No `aspire publish` pipeline, no Aspire-managed prod containers.
- Replacing or redesigning `MockAgentExecutionService`, `DockerAgentExecutionService`, or `SingleContainerAgentExecutionService`.
- Redesigning the `TempDataFolderService` layout.
- Migrating Tailscale or Komodo secrets into user-secrets — they stay in `.env` since only prod scripts consume them.
- Instrumenting the TypeScript worker with OTLP.

## Decisions

### D1. AppHost dev-only; prod stays on docker compose

Aspire as runtime in prod would overlap with Komodo's hot-swap responsibility (pull new GHCR tag, stop old container, start new). Only one can own container lifecycle. Today Komodo works; displacing it to gain Aspire-in-prod has no upside. Keep prod path untouched, Aspire is a dev-only tool.

**Alternatives considered:**
- *Aspire everywhere via `aspire publish`*: generates a compose manifest, but downstream Komodo workflow expects hand-authored `docker-compose.yml` tags. Too much churn.
- *Aspire in prod replacing Komodo*: Aspire AppHost isn't designed as a long-lived supervised prod orchestrator; no drop-in hot-swap mechanism.

### D2. Server via `AddProject`, Web via `AddNpmApp`

`AddProject` runs the server as a native `dotnet run` — breakpoints, edit-and-continue, fast restart. DooD works natively on the host (server already has `/var/run/docker.sock` via the host's Docker group). `AddDockerfile` would give prod-parity at the cost of rebuild-per-change and extra mount config.

`AddNpmApp` runs `npm run dev` (Vite) — HMR, instant CSS reloads. The production nginx-served build is unneeded in dev.

**Alternatives considered:**
- *All `AddDockerfile`*: parity, slow. The current inner loop (`dotnet run` + `npm run dev`) is what devs expect.
- *`AddProject` server + `AddDockerfile` web*: pointlessly mixed.

### D3. Profile-driven mode selection via three env vars

AppHost launch profile sets env vars the server reads through existing config keys. Three knobs covering the mode matrix:

| Profile | `HOMESPUN_MOCK_MODE` | `MockMode__UseLiveClaudeSessions` | `AgentExecution__Mode` |
|---|---|---|---|
| `dev-mock` | `true` | `false` | *(unset)* |
| `dev-live` | `true` | `true` | `Docker` |
| `dev-windows` | `true` | `true` | `SingleContainer` |

`dev-windows` additionally gets `AgentExecution__SingleContainer__WorkerUrl` injected from the AppHost-managed worker resource's endpoint.

All three reuse `ASPNETCORE_ENVIRONMENT=Mock` + the existing `HOMESPUN_MOCK_MODE` + `MockMode` plumbing — no new config keys, no new enum values. The server's startup branching in `Program.cs` and `MockServiceExtensions.cs` changes only to add the missing Docker branch (D4).

**Alternatives considered:**
- *New top-level `HOMESPUN_PROFILE` string parsed once*: cleaner but requires server-side parsing code and diverges from existing config shape.
- *Separate launch profiles per OS*: dev-windows already implies Windows; no need to collapse with dev-mock.

### D4. Close the `AgentExecution:Mode=Docker` gap in mock mode

`MockServiceExtensions.AddLiveClaudeSessionServices` today branches on `AgentExecution:Mode`:

- `SingleContainer` → `SingleContainerAgentExecutionService`
- *(anything else)* → `MockAgentExecutionService`

`Docker` collapses into the else-branch, so dev-live silently gets mock agents. Add explicit `Docker` branch that:
- Reads `DockerAgentExecutionOptions` from configuration.
- Calls `services.AddSingleton<IAgentExecutionService, DockerAgentExecutionService>()` plus the startup-tracking hosted service already set up in the non-mock path.
- Applies the `HSP_HOST_DATA_PATH` post-configure (already present in the non-mock path at Program.cs:213-218).

Mirror the non-mock registrations so behaviour is identical except for the data-store layer (temp dir) and the mock-GitHub/Fleece wrappers.

**Alternatives considered:**
- *Factor agent-execution registration into one reusable method called from both mock and non-mock paths*: cleaner long-term but larger refactor; defer to future cleanup.

### D5. PLG always on, via `AddContainer`

Loki, Promtail, Grafana wired in AppHost on every profile. Mounts match `docker-compose.yml` paths (`./config/loki-config.yml`, `./config/promtail-config.yml`, `./config/grafana/*`). Published ports kept stable (Grafana 3000, Loki 3100) for existing bookmarks and the `/logs` skill's Loki URL.

Limitation acknowledged: when server runs via `AddProject` (not a container), Promtail's docker-label scrape captures **only the worker container** (dev-windows + dev-live), not server logs. Server logs/traces/metrics are still visible via the Aspire dashboard through the OTLP exporter already wired in `ServiceDefaults/Extensions.cs`. Accept this split — no Serilog→Loki sink added to the server.

**Alternatives considered:**
- *PLG conditional per profile*: honors "PLG always on" request verbatim even though it's partly redundant in dev-mock.
- *Add Serilog→Loki sink*: new dependency, duplication with Aspire dashboard. Not worth it.

### D6. Secrets in user-secrets; one-shot migration script

AppHost declares:
```csharp
var githubToken = builder.AddParameter("github-token", secret: true);
var claudeOauthToken = builder.AddParameter("claude-oauth-token", secret: true);
```

Aspire resolves these from (in order) user-secrets, env vars, other configured providers. `scripts/set-user-secrets.sh` (+ `.ps1`) parses `.env` at repo root, extracts `GITHUB_TOKEN` and `CLAUDE_CODE_OAUTH_TOKEN`, and calls:
```bash
dotnet user-secrets set "Parameters:github-token" "$GITHUB_TOKEN" \
  --project src/Homespun.AppHost
dotnet user-secrets set "Parameters:claude-oauth-token" "$CLAUDE_CODE_OAUTH_TOKEN" \
  --project src/Homespun.AppHost
```

Script is idempotent: if `.env` is missing or a key is blank, it warns and skips that key (existing user-secret untouched). Tailscale and Komodo secrets stay in `.env` — prod-only tooling (`run.sh`, `run-komodo.sh`) still reads `.env` directly.

**Alternatives considered:**
- *Load `.env` in AppHost via `DotNetEnv.Env.Load()`*: keeps `.env` as dev source-of-truth, but adds a NuGet dep and mixes secret strategies. User preferred user-secrets.

### D7. SingleContainer worker as AppHost resource, not separate compose

dev-windows needs a pre-running worker container at a known URL. Aspire manages this: `AddContainer("worker", "ghcr.io/nick-boey/homespun-worker:latest")` with port and env vars. The server gets `AgentExecution:SingleContainer:WorkerUrl` populated from `worker.GetEndpoint("http").Url`. Profile gate: only add this resource when `HOMESPUN_AGENT_MODE=SingleContainer` (read from env at AppHost boot).

**Alternatives considered:**
- *Keep `docker compose up worker` sidecar*: works but splits lifecycle ownership between Aspire and compose — exactly the thing this change is trying to eliminate.

### D9. One `dev-container` profile, not a container-dev triad

Deleting `scripts/run.sh --local` removes the only current path to exercise the app in containers against mock data. Container-only bugs (image-permission issues, native deps missing from the base image, entrypoint regressions) need a repro path. Add a single parity-check profile rather than containerized variants of all three host-process profiles.

- One profile: `dev-container`. Server + web + worker all via `AddDockerfile`. Data mode = mock (temp dir). Agent mode = `Docker` DooD on non-Windows hosts (mirrors what prod actually does), auto-swap to `SingleContainer` when `RuntimeInformation.IsOSPlatform(OSPlatform.Windows)` at AppHost boot.
- Not a daily driver. Inner loop is rebuild-per-change; expected usage is pre-PR parity check or chasing a container-only bug.
- PLG wired identically to host profiles. `VITE_API_URL` points at the server container endpoint.
- Worker: for the `Docker` agent-execution path, DooD still spawns sibling worker containers on the host's docker socket; the `dev-container` server container needs the same socket mount (`/var/run/docker.sock`) and docker-group membership that prod uses today.

Trap avoided: no `dev-mock-container`, `dev-live-container`, `dev-windows-container` triad. One profile keeps AppHost surface small. Devs needing combinatorial container-dev can override via env vars on the one profile.

**Alternatives considered:**
- *Triad of container profiles*: 4 profiles doubles to 7 — not worth the combinatorial explosion for a rarely-used parity check.
- *Keep `scripts/run.sh --local` alive*: fragments the mental model. Most dev = Aspire; container dev = script. No.
- *Don't add*: leaves the gap. Rare-but-real bugs would lack a first-class repro.

### D8. Playwright webServer rewires to AppHost command

`src/Homespun.Web/playwright.config.ts` currently uses `mock.sh` via a `webServer` entry. Rewire to `dotnet run --project ../Homespun.AppHost --launch-profile dev-mock`. The e2e tests need the server reachable at a stable URL — expose the server endpoint port deterministically in the `dev-mock` profile (either via `withHttpEndpoint(port: X)` or parsing Aspire output).

Practical: e2e tests today already assume a specific port from `mock` launch profile (`http://localhost:5101`). Pin the same port on AppHost's server resource for dev-mock to minimize test churn.

**Alternatives considered:**
- *Dynamic port discovery via Aspire service-discovery env var*: cleaner but requires Playwright config to read `DOTNET_DASHBOARD_*` env vars — more moving parts.

## Risks / Trade-offs

- **Dev/prod divergence widens.** Dev is `AddProject` + native npm; prod is containers. [Risk] Container-only bugs hide in dev. → [Mitigation] CI still exercises the full docker build via `scripts/build-containers.sh`, and prod smoke tests continue on the actual image.
- **`AddNpmApp` requires `npm` on PATH.** New devs without Node get an unclear error. → [Mitigation] Document prerequisites in CLAUDE.md (Node 20+, .NET 10 SDK, Aspire workload, Docker Desktop).
- **Promtail blind to server logs in dev.** [Risk] Dev devs forget and look for server logs in Grafana. → [Mitigation] Document in CLAUDE.md: "Aspire dashboard = server logs/traces/metrics; Grafana = worker + historical."
- **User-secrets is per-machine, per-user.** [Risk] New clones need to rerun `scripts/set-user-secrets.sh` once. → [Mitigation] Idempotent script, called out in CLAUDE.md onboarding; `.env` keeps its role for prod scripts so devs aren't juggling two formats.
- **Playwright e2e port lock.** [Risk] If `dev-mock` port conflicts with another service on dev machines, e2e fails. → [Mitigation] Keep the existing `5101` port choice so existing tests and bookmarks don't move.
- **Aspire workload upgrade burden.** [Risk] Aspire 13.2.0 is pinned; contributors on older workload versions get opaque errors. → [Mitigation] `global.json`-style pin or README note; out of scope to enforce here.
- **DI validation disabled.** `SingleContainerAgentExecutionService` registration currently disables `ValidateScopes` / `ValidateOnBuild` (Program.cs:203-207). When AppHost drives dev-windows, same disable-path applies — mock-mode + SingleContainer was the combo today, still is. No regression, but the underlying Singleton→Scoped consumption issues remain unfixed (out of scope).
- **Scripts removed in same commit that rewires callers.** [Risk] A half-applied change (CLAUDE.md updated, Playwright not) leaves devs or CI broken. → [Mitigation] Task ordering in tasks.md puts AppHost wiring + caller updates before script deletion.

## Migration Plan

1. Land AppHost rewrite, server `MockServiceExtensions` Docker branch, and tests — scripts still usable.
2. Add user-secrets migration scripts. Document in CLAUDE.md. Ask devs to run them on next pull.
3. Rewire Playwright `webServer` to AppHost command.
4. Update CLAUDE.md (dev workflow, playwright MCP section, mock.sh references).
5. Delete `scripts/mock.sh`, `scripts/mock.ps1`, `scripts/run.sh`, `scripts/run.ps1` in same commit as the CLAUDE.md update.
6. **Rollback:** revert the deletion commit — scripts come back unchanged; AppHost still builds but is unused.
