## Why

Runtime verification of the `aspire-apphost` change (via `aspire describe`, `aspire otel logs`, `aspire otel traces` against a booted AppHost) surfaced four defects that together break the two things a developer actually needs from local dev: **seeing logs** and **running a worker**. Server logs never reach the Aspire dashboard (the server's `ClearProviders()` call wipes the OTLP log provider wired by `ServiceDefaults`). Promtail fails to start on macOS so nothing reaches Grafana/Loki either. The `dev-windows` profile cannot boot on Apple Silicon because the GHCR worker image has no arm64 manifest. Dev uses GHCR images at all — unnecessarily, since prod-only Komodo owns GHCR deployment — which costs a pull on every first boot and couples dev to a publish cadence that has nothing to do with the dev inner loop.

## What Changes

- **Restore OTLP log flow to Aspire dashboard.** Reorder logging configuration in `src/Homespun.Server/Program.cs` so `AddServiceDefaults()`'s OTLP log provider is not wiped by the subsequent `ClearProviders()`. Both the JSON console formatter (for stdout → promtail) and the OTLP exporter (for Aspire dashboard) must be registered.
- **Make the worker always build from this repo in every dev profile.** `AddDockerfile("../Homespun.Worker")` replaces every `AddContainer("ghcr.io/…/homespun-worker", …)` reference in `src/Homespun.AppHost/Program.cs`. The AppHost builds the worker image locally for `dev-live`, `dev-windows`, and `dev-container` (both Windows and non-Windows branches). **No AppHost path pulls from GHCR.**
- **Wire the Aspire-built worker image into DooD sibling spawns.** The AppHost injects `AgentExecution__Docker__WorkerImage=<aspire-built-tag>` into the server resource's environment, so `DockerAgentExecutionService`'s `docker run` commands pick up the locally-built image rather than the GHCR default. Applies to every profile where the server uses `AgentExecution:Mode=Docker` (`dev-live`, `dev-container` on non-Windows).
- **Fix promtail on macOS.** Either (a) replace the docker-socket scrape with an OTLP log pipeline (Loki 3.6 natively accepts OTLP, `ServiceDefaults` already exports OTLP) so promtail is no longer required in dev, OR (b) drop the `/var/lib/docker/containers` bind mount entirely and rely on the Docker daemon socket for container metadata. Direction chosen in design.md.
- **Apply the `logging=promtail` label to every Aspire-managed container that should be scraped.** Only relevant if the promtail path is kept. Via `WithContainerRuntimeArgs("--label", "logging=promtail")` or the Aspire annotation equivalent on the worker / server / web container resources.
- **Prod path remains untouched.** `docker-compose.yml`, Komodo, and `scripts/run-komodo.sh` continue to pull from GHCR. The AppHost is dev-only (D1 from the parent `aspire-apphost` change). Only dev behavior changes here.

## Capabilities

### New Capabilities
<!-- none — this change refines behavior captured by dev-orchestration -->

### Modified Capabilities
- `dev-orchestration`: the four launch-profile scenarios already define *that* the stack boots and which services register. This change tightens the scenarios to require that (a) server logs reach the Aspire dashboard, (b) every worker container in dev is built from this repo's `src/Homespun.Worker/Dockerfile`, and (c) the dev stack is log-observable on macOS (whether via promtail or OTLP, per the design).

## Impact

- **Files touched:**
  - `src/Homespun.AppHost/Program.cs` — swap worker `AddContainer` → `AddDockerfile`; build + tag worker image in host-mode profiles; inject `AgentExecution__Docker__WorkerImage` env on server; optionally add promtail label.
  - `src/Homespun.Server/Program.cs` — reorder logging setup (lines 33 + 52-58) to preserve OTLP logging provider.
  - `src/Homespun.AppHost/Homespun.AppHost.csproj` — possibly add OTLP-collector / Loki-OTLP integration if design picks the OTLP replacement path for promtail.
  - `config/promtail-config.yml` — adjusted (or replaced by `config/otel-collector.yml`) depending on design direction for promtail fix.
  - `tests/Homespun.AppHost.Tests/AppHostTests.cs` — new tests asserting (1) no AppHost resource references a GHCR image string, (2) every profile wires the worker via `ProjectResource`/`ContainerImageAnnotation` sourced from the local Dockerfile, (3) server env annotation contains `AgentExecution__Docker__WorkerImage` when `AgentExecution__Mode=Docker`.
  - `tests/Homespun.Api.Tests/Features/AgentExecutionModeStartupTests.cs` — existing coverage sufficient; no change.
  - `CLAUDE.md` — update "Running the Application" + observability sections to reflect the fix.

- **Dependencies:** no new runtime NuGet or npm packages unless the design picks an OTLP-collector-based promtail replacement, in which case a Grafana Alloy / otel-collector-contrib container resource is added to the AppHost.

- **Risk surface:**
  - The worker Dockerfile build adds ~30–60s to the first `dev-live`/`dev-windows`/`dev-container` boot per worktree (one-time per image cache generation). Incremental boots after that hit the Docker layer cache.
  - Changing the log-provider order in `Program.cs` could alter stdout format in prod (since `Program.cs` is shared between mock and prod paths). Tests must verify the JSON console formatter still emits for promtail consumption in production.

- **Rollback:** single revert of the change's commit. Prod is unaffected by design; reverting removes the dev-side fixes only.
