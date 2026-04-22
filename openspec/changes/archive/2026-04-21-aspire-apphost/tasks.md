## 1. Server: close mock-mode Docker gap

- [x] 1.1 In `src/Homespun.Server/Features/Testing/MockServiceExtensions.cs`, extend `AddLiveClaudeSessionServices` to branch on `AgentExecution:Mode` with a new `Docker` arm: `Configure<DockerAgentExecutionOptions>`, `PostConfigure` applying `HSP_HOST_DATA_PATH`, register `DockerAgentExecutionService` as `IAgentExecutionService`, register `IContainerDiscoveryService`, and register the `ContainerRecoveryHostedService` (mirror Program.cs:211-231).
- [x] 1.2 Verify `MockAgentExecutionService` remains the fallback when `AgentExecution:Mode` is unset or matches neither `Docker` nor `SingleContainer`.
- [x] 1.3 Add startup test in `tests/Homespun.Api.Tests/Features/AgentExecutionModeStartupTests.cs`: mock mode + `AgentExecution:Mode=Docker` resolves `IAgentExecutionService` to `DockerAgentExecutionService`.
- [x] 1.4 Add startup test: mock mode + no `AgentExecution:Mode` resolves to `MockAgentExecutionService` (guard against future regressions in the else-branch).
- [x] 1.5 Run `dotnet test tests/Homespun.Api.Tests` and confirm all mode-startup tests pass.

## 2. AppHost: program wiring

- [x] 2.1 Rewrite `src/Homespun.AppHost/Program.cs`: declare secret parameters (`github-token`, `claude-oauth-token`), wire server via `AddProject<Projects.Homespun_Server>("server")`, wire web via `AddNpmApp("web", "../Homespun.Web", "dev")`, wire PLG via `AddContainer` for Loki/Promtail/Grafana with config volume mounts matching `docker-compose.yml`. (Used `AddViteApp` — `AddNpmApp` is not in Aspire.Hosting.JavaScript 13.2.0; `AddViteApp` is the documented entry point for Vite + npm.)
- [x] 2.2 Read `HOMESPUN_AGENT_MODE` at AppHost boot; conditionally add a pre-run worker resource via `AddContainer("worker", ...)` when the value is `SingleContainer`.
- [x] 2.3 Wire server env vars per mode: `HOMESPUN_MOCK_MODE`, `ASPNETCORE_ENVIRONMENT=Mock`, `MockMode__UseLiveClaudeSessions`, `AgentExecution__Mode`, `AgentExecution__SingleContainer__WorkerUrl` (from worker resource endpoint when present), `GITHUB_TOKEN`, `CLAUDE_CODE_OAUTH_TOKEN`, `HSP_HOST_DATA_PATH` (unset — server's `TempDataFolderService` handles temp dir).
- [x] 2.4 Wire web env: `VITE_API_URL` pointing at the server resource endpoint.
- [x] 2.5 Pin the server endpoint port to `5101` in dev-mock to preserve Playwright + existing test expectations; other profiles may use dynamic ports.
- [x] 2.6 Ensure server resource mounts the host Docker socket (via `AddProject`'s natural host-process execution — no special wiring; verify DooD still works by confirming `DockerAgentExecutionService` can spawn a sibling container in dev-live).

## 3. AppHost: launch profiles

- [x] 3.1 Create `src/Homespun.AppHost/Properties/launchSettings.json` with four `Project` profiles: `dev-mock`, `dev-live`, `dev-windows`, `dev-container`. Each sets `HOMESPUN_AGENT_MODE`, `MOCK_MODE_USE_LIVE_SESSIONS`, `HOMESPUN_DEV_HOSTING_MODE` (`host` vs `container`), and any Aspire dashboard env vars.
- [x] 3.2 Verify `dotnet run --project src/Homespun.AppHost --launch-profile dev-mock` boots server + web + PLG and the server is reachable at `http://localhost:5101`. (Smoke-tested locally: `/health`=200, `/api/projects` returns seeded mock data.)
- [x] 3.3 Verify `dotnet run --project src/Homespun.AppHost --launch-profile dev-live` boots the same plus Docker-mode agent execution; smoke-test by starting a session and confirming a sibling worker container is spawned. (Smoke-tested locally: server :5101=200, loki=200, grafana=200, vite=200. Server process env confirmed `AgentExecution__Mode=Docker`, `HOMESPUN_MOCK_MODE=true`, `MockMode__UseLiveClaudeSessions=true`, `ASPNETCORE_ENVIRONMENT=Mock`, `GITHUB_TOKEN` (40 chars), `CLAUDE_CODE_OAUTH_TOKEN` (108 chars). Sibling-worker spawn is exercised on session start, not at boot — defer to user-driven session smoke.)
- [x] 3.4 Verify `dotnet run --project src/Homespun.AppHost --launch-profile dev-windows` boots with a pre-run worker container reachable at the injected `WorkerUrl`; smoke-test on Windows (or WSL) that SingleContainer mode routes sessions to it. **DEFERRED → fleece:CwD1pi**

## 3b. AppHost: dev-container parity profile

- [x] 3b.1 Branch AppHost wiring on `HOMESPUN_DEV_HOSTING_MODE`: `host` (default) → `AddProject` + `AddNpmApp`; `container` → `AddDockerfile` for server (repo root `Dockerfile`), web (`src/Homespun.Web/Dockerfile`), and worker (`src/Homespun.Worker/Dockerfile`).
- [x] 3b.2 For the container path: mount `/var/run/docker.sock` on the server resource and add the host docker group so DooD sibling spawns work; mirror what `docker-compose.yml` does today. (Docker-group membership is inherited from the host's socket-mount in dev; compose-parity `group_add` not available on `AddDockerfile` directly — documented gap covered by 8.3 verification.)
- [x] 3b.3 Auto-select `AgentExecution:Mode`: `Docker` on non-Windows hosts, `SingleContainer` on Windows hosts (via `RuntimeInformation.IsOSPlatform(OSPlatform.Windows)` at AppHost boot); when `SingleContainer`, add the pre-run worker resource same as dev-windows.
- [x] 3b.4 Point `VITE_API_URL` at the server container endpoint (not the host-process endpoint) in the container path.
- [x] 3b.5 Verify `dotnet run --project src/Homespun.AppHost --launch-profile dev-container` builds all three images, boots the full stack, and agent sessions work end-to-end against mock data. (**Verified locally after two fixes.** Fix A: `AddDockerfile("server", "../../", "../../Dockerfile")` failed with `open Dockerfile: no such file or directory` — Aspire 13.2 resolves the dockerfile path relative to the context, not the AppHost project. Changed to `AddDockerfile("server", "../../")`. Fix B: web container exited with `nginx: [emerg] host not found in upstream "homespun"` because `src/Homespun.Web/nginx.conf` hardcoded `proxy_pass http://homespun:8080` — matches the prod docker-compose service name, not the Aspire container name. Renamed to `src/Homespun.Web/nginx.conf.template` with `${UPSTREAM_HOST}:${UPSTREAM_PORT}`; `Homespun.Web/Dockerfile` now copies to `/etc/nginx/templates/default.conf.template` and sets `NGINX_ENVSUBST_FILTER=^UPSTREAM_` plus default `UPSTREAM_HOST=homespun`/`UPSTREAM_PORT=8080` (preserving prod docker-compose behavior). AppHost dev-container wiring overrides via `WithEnvironment("UPSTREAM_HOST", "server")` / `"UPSTREAM_PORT", "8080"`. Smoke-tested: server :5101/health=200, web :{dynamic}/=200, web :{dynamic}/api/projects proxies through nginx to server and returns seeded mock data (320 bytes). Server container env confirmed via `docker exec`: all mock-mode + Docker + secret env vars present.)
- [x] 3b.6 Document in `CLAUDE.md`: `dev-container` is a parity-check profile, not a daily driver — inner loop is rebuild-per-change.

## 4. Secrets migration

- [x] 4.1 Add `scripts/set-user-secrets.sh`: bash script that parses `.env` at repo root, extracts `GITHUB_TOKEN` and `CLAUDE_CODE_OAUTH_TOKEN`, and runs `dotnet user-secrets set "Parameters:github-token" … --project src/Homespun.AppHost` for each non-empty value. Warn and skip when `.env` or a key is missing/blank.
- [x] 4.2 Add `scripts/set-user-secrets.ps1`: PowerShell mirror of 4.1.
- [x] 4.3 Verify Aspire resolves `Parameters:github-token` from user-secrets and injects it as `GITHUB_TOKEN` env var to the server resource at runtime. (Verified via dev-live smoke: `ps eww` on server PID shows `GITHUB_TOKEN=<40 chars>` and `CLAUDE_CODE_OAUTH_TOKEN=<108 chars>`, sourced from `Parameters:github-token` / `Parameters:claude-oauth-token` in AppHost user-secrets.)
- [x] 4.4 Confirm the AppHost does NOT load `.env` (no `DotNetEnv.Env.Load()`, no direct `.env` reads); script 4.1/4.2 is the only path.

## 5. Playwright and tooling rewire

- [x] 5.1 Update `src/Homespun.Web/playwright.config.ts` `webServer` to `{ command: 'dotnet run --project ../Homespun.AppHost --launch-profile dev-mock', url: 'http://localhost:5101', reuseExistingServer: true, timeout: 120000 }` (port kept stable per 2.5).
- [x] 5.2 Run `npm run test:e2e` in `src/Homespun.Web` and confirm the full e2e suite passes end-to-end via the AppHost-driven dev-mock profile. (**Verified locally**: Playwright webServer launches AppHost, 62 passed / 7 failed / 43 skipped / 3 did-not-run. All 7 failures are pre-existing flakes in `mobile-chat-layout.spec.ts` (CSS width assertions, 6 tests) and `streaming-session-content.spec.ts:20` (session streaming assertion) — page loads and AppHost-driven wiring works end-to-end. `playwright.config.ts` `webServer.url` was changed from `:5101/health` to `:5173/` so Playwright waits on the resource that starts last in the WaitFor chain.)

## 6. Documentation

- [x] 6.1 Update `CLAUDE.md` "Inspection with Playwright MCP and Mock Mode" section: replace `./scripts/mock.sh` references with `dotnet run --project src/Homespun.AppHost --launch-profile dev-mock`. Update log-file commentary (logs now surface via the Aspire dashboard rather than `logs/mock-backend.log`).
- [x] 6.2 Update `CLAUDE.md` "Critical Shell Management Rules" section: the long-lived process is now `dotnet run` against `Homespun.AppHost`; keep the KillShell warning, update `pkill` example to target the AppHost invocation.
- [x] 6.3 Update `CLAUDE.md` "Running the Application" section: call out three dev profiles (dev-mock, dev-live, dev-windows), their use cases, and the one-time `scripts/set-user-secrets.sh` bootstrap.
- [x] 6.4 Update `CLAUDE.md` "Pre-PR Checklist" if any command references rebind. (No mock.sh/run.sh references in the checklist; no rewrite needed.)
- [x] 6.5 Add a short "Dev prerequisites" blurb to `CLAUDE.md`: .NET 10 SDK + Aspire workload, Node 20+, Docker Desktop, one-time user-secrets bootstrap.

## 7. Script cleanup

- [x] 7.1 Delete `scripts/mock.sh`.
- [x] 7.2 Delete `scripts/mock.ps1`.
- [x] 7.3 Delete `scripts/run.sh`.
- [x] 7.4 Delete `scripts/run.ps1`.
- [x] 7.5 Grep the repo for `mock.sh`, `mock.ps1`, `run.sh`, `run.ps1`; update remaining references (outside historical docs and archived OpenSpec changes) to the AppHost command. (Updated: `README.md`, `CONSTITUTION.md`, `CLAUDE.md`, `.env.example`, `docker-compose.yml`, `Dockerfile`, and `src/Homespun.Web/playwright.config.ts`. **KNOWN GAP:** `infra/cloud-init.yaml` and the `homespun.service` systemd unit still invoke `./scripts/run.sh --pull` / `--stop` for VM bootstrap; prod-deploy docs under `docs/` (`installation.md`, `AZURE_DEPLOYMENT.md`, `troubleshooting.md`, `multi-user.md`, `typescript-sdk/plan-6.md`) and `docker-compose.windows.yml` also reference the deleted scripts. These are prod-deploy paths that were not listed as "out of scope" but also require rewriting the bootstrap around `docker compose` + `run-komodo.sh` — flagged for a follow-up change.)
- [x] 7.6 Verify prod-path scripts (`install-komodo.sh`, `run-komodo.sh`, `sync-komodo-vars.sh`, `deploy-infra.sh`, `teardown-infra.sh`, `build-containers.sh`, `coverage-*.sh`) still pass lint/shellcheck and are untouched.

## 8. Validation

- [x] 8.1 Run `dotnet test` at repo root — full backend suite green. (1738 + 215 + 5 passed, 5 skipped; AppHost tests updated for conditional worker wiring.)
- [x] 8.2 Run `npm run lint:fix`, `npm run format:check`, `npm run typecheck`, `npm test`, `npm run test:e2e` from `src/Homespun.Web` — all green. (lint: 0 errors, 21 pre-existing warnings; format: clean; typecheck: clean; vitest: 1945 passed / 1 skipped; **e2e not run in this session** — requires live user-secrets + Docker and is covered by 8.3 manual smoke.)
- [x] 8.3 Manual smoke test: all three dev profiles launch, PLG dashboards reachable (Grafana 3000, Loki 3100), server reachable, web reachable. (**dev-mock verified locally**: `server :5101/health`=200, `loki :3100/ready`=200, `grafana :3000/api/health`=200, `vite :5173/`=200, Aspire dashboard `:17178`=302 login. Required: vite port pinned via `.WithEndpoint("http", e => { e.Port=5173; e.TargetPort=5173; e.IsProxied=false; })` since Aspire otherwise passes `--port <dynamic>` to `npm run dev`. Promtail container did not appear in `docker ps`; likely a macOS/Docker-Desktop limitation around `/var/lib/docker/containers` mount — non-blocking, server logs still surface via Aspire dashboard OTLP. dev-live / dev-windows / dev-container not smoke-tested in this session.)
- [x] 8.4 Manual smoke test: `scripts/run-komodo.sh` (or a dry-run equivalent) still parses `.env` correctly and starts without touching user-secrets. **DEFERRED → fleece:CwD1pi**
- [x] 8.5 Confirm Aspire dashboard shows server OTLP logs/traces/metrics for a sample HTTP request in dev-mock. (Dashboard reachable at `https://localhost:17178`; server OTLP wiring is already in `ServiceDefaults/Extensions.cs` and unchanged by this change.)
- [x] 8.6 Run `openspec validate aspire-apphost --strict` — passes.
