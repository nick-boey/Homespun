## Context

Four defects surfaced while runtime-verifying the parent `aspire-apphost` change against a live AppHost on Apple Silicon:

1. **Server logs missing from Aspire dashboard.** `src/Homespun.Server/Program.cs:33` calls `AddServiceDefaults()` which wires `Logging.AddOpenTelemetry(…)` plus the OTLP exporter. `Program.cs:53` then calls `builder.Logging.ClearProviders()` — wiping the OTLP log provider. `Program.cs:54-58` re-adds only the JSON console formatter. Result: `aspire otel logs server` returns "No logs found" while `aspire otel traces server` returns spans.

2. **Promtail FailedToStart on macOS.** `src/Homespun.AppHost/Program.cs:35-40` declares Promtail with a bind mount of `/var/lib/docker/containers`, which doesn't exist on macOS Docker Desktop (Docker's state lives inside the Linux VM). Aspire DCP aborts container creation with `container.id: null` and no Docker container is ever launched. Grafana and Loki remain healthy, but nothing ingests.

3. **Aspire-managed containers lack `logging=promtail` label.** `config/promtail-config.yml:18-20` filters `label=logging=promtail`. Even if Promtail ran, none of the AppHost's `AddContainer` / `AddDockerfile` calls add this label, so Promtail's docker_sd would skip them. Prod `docker-compose.yml` applies this label on homespun/worker/web services; the AppHost doesn't replicate that.

4. **GHCR worker image has no arm64 manifest.** `dev-windows` on Apple Silicon: `[worker] Error response from daemon: no matching manifest for linux/arm64/v8`. Independently, the user decided that prod alone — which deploys via Komodo + GHCR — should own the GHCR pull path; dev should build from source so the inner loop is self-contained and unaffected by GHCR publish cadence.

The parent change's `dev-orchestration` spec already asserts "server logs, traces, and metrics are visible in the Aspire dashboard via OTLP" — a contract the code violates. This change reconciles implementation with the spec and tightens the spec where necessary.

## Goals / Non-Goals

**Goals:**
- Server logs reach the Aspire dashboard via OTLP in every dev profile.
- Every Aspire-managed dev profile builds the worker container from `src/Homespun.Worker/Dockerfile`; no AppHost code path pulls from GHCR.
- Log ingestion into Loki works on macOS Docker Desktop (where this repo's devs primarily work) and on Linux.
- `dev-windows` boots and is usable on Apple Silicon (contingent on local worker build succeeding).
- Existing `AgentExecutionModeStartupTests` coverage preserved; new `AppHostTests` coverage asserts the non-GHCR invariant and the server-env-injection invariant.

**Non-Goals:**
- Altering the prod deploy path (`docker-compose.yml`, Komodo, `scripts/run-komodo.sh`). GHCR remains the prod image source.
- Adding OpenTelemetry instrumentation to the TypeScript worker. It remains a stdout JSON emitter; whether its stdout is scraped from Loki via container label is addressed here, but SDK-level OTLP export from the worker is out of scope.
- Adding OTLP / browser-telemetry instrumentation to the Vite web app. That is a separate, larger piece of work.
- Changing the default `DockerAgentExecutionOptions.WorkerImage` value. The default stays pointed at GHCR so production behaviour is untouched; only the AppHost dev path injects an override.
- Removing Promtail from `docker-compose.yml` prod stack. Prod on Linux works fine with the docker-socket scrape; only dev needs the fix.

## Decisions

### D1. Swap `AddContainer(GHCR)` for `AddDockerfile("../Homespun.Worker")` in every dev-worker path

Three call sites in `src/Homespun.AppHost/Program.cs` reference the worker:
- line 59 (`isSingleContainer` branch, any hosting mode) — `AddContainer("ghcr.io/nick-boey/homespun-worker", "latest")`
- line 80 (`isContainerHosting` non-SingleContainer branch) — already `AddDockerfile("../Homespun.Worker")` ✓

After this change, all worker-resource registrations use `AddDockerfile`. The SingleContainer branch also needs to capture the image tag for the server's `AgentExecution:SingleContainer:WorkerUrl` injection, which it already does via `workerContainer.GetEndpoint("http")`; that endpoint survives the `AddDockerfile` swap because `AddDockerfile` returns an `IResourceBuilder<ContainerResource>` too.

**Alternatives considered:**
- *Parameterise the worker image via a build flag / env var with GHCR as fallback*: preserves the GHCR escape hatch but adds config surface nobody will use — the user's stated intent is never-GHCR-in-dev, full stop.
- *Build the image outside Aspire and reference it by tag*: requires a pre-`dotnet run` hook or a script, reintroducing the `scripts/*.sh` pattern the parent change just retired.

### D2. Inject `AgentExecution__Docker__WorkerImage` on the server resource in host-mode `dev-live`

Currently `dev-live` registers no worker resource at the AppHost level (the server spawns siblings via DooD at session start). The server's `DockerAgentExecutionOptions.WorkerImage` default is `"ghcr.io/nick-boey/homespun-worker:latest"` (`src/Homespun.Server/Features/ClaudeCode/Services/DockerAgentExecutionService.cs:26`) — so without intervention, dev-live still pulls from GHCR when a session starts.

Fix: in `dev-live` (host mode + `AgentExecution:Mode=Docker`) the AppHost:
- Calls `builder.AddDockerfile("worker-image", "../Homespun.Worker")` solely to trigger the build and tag the local image.
- Marks the resource as "build-only" so Aspire does not run it as a container (either via `.ExcludeFromManifest()` + no waiters, or by inspecting whether Aspire exposes a build-without-run idiom — see Open Questions).
- Reads the built image tag from the resource builder and sets `server.WithEnvironment("AgentExecution__Docker__WorkerImage", <tag>)`.

If Aspire's `AddDockerfile` does not cleanly support "build, don't run", fallback is to always run the worker container in dev-live too (treating dev-live like dev-container for the worker-image build specifically, and ignoring it at runtime since the server spawns its own siblings). This costs an idle worker container in dev-live but guarantees image availability.

**Alternatives considered:**
- *Pre-build via `scripts/build-worker.sh` invoked from a pre-build step of the AppHost csproj*: re-introduces scripted dev bootstrap.
- *Change `WorkerImage` default to a local name*: breaks prod (prod reads the same default when `AgentExecution__Docker__WorkerImage` env is unset). Keep the default GHCR-compatible.

### D3. Reorder logging setup in `src/Homespun.Server/Program.cs`

Replace the current sequence:
```csharp
builder.AddServiceDefaults();        // adds OTLP log provider
…
builder.Logging.ClearProviders();    // wipes it
builder.Logging.AddConsole(FormatterName = JsonConsoleFormatter.FormatterName)
```
with a configuration that keeps both providers. Options:

A. *Move `ClearProviders()` before `AddServiceDefaults()`* — calls `ClearProviders()` first (to kill defaults-defaults like Debug/EventSource), then `AddServiceDefaults()` (which adds OTLP on a clean slate), then `AddConsole(Json…)`. Net result: exactly the two providers we want.

B. *Drop `ClearProviders()` entirely*. The defaults `WebApplication.CreateBuilder` registers (Console, Debug, EventSource, EventLog on Windows) are largely benign. We'd then end up with Console (default text), OTLP, Console (Json), Debug, EventSource — a mess: two console providers fight over stdout, and we want the Json one to win.

C. *Use `builder.Logging.AddOpenTelemetry(…)` explicitly after ClearProviders and AddConsole*, duplicating what ServiceDefaults does. More code, more drift risk.

Pick **A**. Minimal diff, deterministic provider set, matches what `ServiceDefaults` was designed to add.

**Alternatives considered:**
- *Keep the JSON formatter for stdout but wire a second OpenTelemetry logger manually with a specific OTLP endpoint*: more code, no benefit — the Aspire OTLP endpoint is already what `ServiceDefaults` auto-wires.

### D4. Replace Promtail with Grafana Alloy (or otel-collector-contrib) for log ingestion into Loki

Promtail's `/var/lib/docker/containers` dependency is fundamentally incompatible with macOS Docker Desktop. Two choices:

- **D4a.** Keep Promtail. Drop the `/var/lib/docker/containers` mount (it exists for speed — parsing container log files directly — not correctness). Promtail then relies on `docker_sd`'s container-log API via the Docker socket, which works on macOS. Loses some parsing latency optimisations, costs nothing else.
- **D4b.** Replace Promtail with **Grafana Alloy**. Alloy is the successor agent that Grafana Labs positions as promtail's replacement and natively speaks OTLP in + Loki out. This means: the server's OTLP exporter (already configured via `ServiceDefaults`) can push directly to Alloy, which forwards to Loki — one log pipeline instead of two. No docker-label scrape, no `/var/lib/docker/containers` — just OTLP.

Pick **D4b**. Benefits:
- Eliminates the `logging=promtail` label requirement (D4a would still need labels on every Aspire container — D4b makes that moot for server + web, and container stdout can still be scraped via docker_sd in the Alloy config if we want worker stdout too).
- Unifies the server log path: one exporter, two sinks (Aspire dashboard via its own OTLP receiver, Loki via Alloy).
- Matches the direction Grafana Labs is steering users (Promtail LTS, not feature-developed).

Cost: one new container in the AppHost (`grafana/alloy`), one new config file (`config/alloy/config.alloy`). Prod `docker-compose.yml` can stay on Promtail — no requirement for prod to change.

Trap to avoid: Alloy must bind to a port the server can reach. In host mode the server runs on the developer's machine, not in Aspire's container network — so Alloy's OTLP port needs an Aspire-published HTTP endpoint the server can target. Wire the endpoint URL into the server via a new env var `OTEL_EXPORTER_OTLP_LOGS_ENDPOINT` (or similar) while leaving `OTEL_EXPORTER_OTLP_ENDPOINT` pointed at the Aspire dashboard for metrics + traces. Worth verifying Aspire's OpenTelemetry conventions here — may need to use the combined endpoint and split via protocol at the Alloy side.

Actually reconsider: Aspire already owns `OTEL_EXPORTER_OTLP_ENDPOINT` pointing at the Aspire dashboard's OTLP receiver. Pointing it at Alloy instead would lose dashboard logs. Cleanest wiring is a second exporter in `ServiceDefaults` for logs specifically — but `ServiceDefaults` is shared with prod.

Safer path: **fork Aspire's OTLP endpoint to dual-export** via `OTEL_EXPORTER_OTLP_LOGS_ENDPOINT=<alloy-endpoint>` + keep `OTEL_EXPORTER_OTLP_ENDPOINT=<aspire-dashboard>` for traces/metrics. The OTel .NET SDK honours the per-signal env var overrides.

**Alternatives considered:**
- *Drop PLG entirely in dev, just use Aspire dashboard*: the user stated goal is dual-sink observability (Aspire + Grafana). Honour the intent.
- *Run Promtail in the Aspire container network only, scraping the worker container via docker_sd*: works on Linux, still broken on macOS due to `/var/lib/docker/containers`. Rejecting the path that fails on the primary dev OS.

### D5. Apply `logging=promtail` label to every dev container resource (only if D4a is picked)

If Decision D4 lands on D4b (Alloy + OTLP), this decision is moot — OTLP doesn't need the label. Documenting it only as the fallback path.

If D4a: every `AddContainer` / `AddDockerfile` call in `src/Homespun.AppHost/Program.cs` (for the worker resource, plus the `dev-container` server/web Dockerfile builds) gets `WithContainerRuntimeArgs("--label", "logging=promtail")` appended. Verification: the AppHostTests assert every resource of type `Container` in the built model has the annotation.

### D6. AppHost tests assert the invariants

Add to `tests/Homespun.AppHost.Tests/AppHostTests.cs`:
- `AppHost_no_resource_references_ghcr_worker_image()` — iterate `DistributedApplicationModel.Resources`, assert no `ContainerImageAnnotation` (or similar) whose image contains `ghcr.io`.
- `DevLive_server_resource_has_AgentExecution_Docker_WorkerImage_env()` — set `HOMESPUN_AGENT_MODE=Docker` + `HOMESPUN_DEV_HOSTING_MODE=host` and assert the server resource's environment annotations include `AgentExecution__Docker__WorkerImage` pointing at a non-empty, non-GHCR value.
- `DevWindows_worker_resource_is_built_from_dockerfile()` — assert the `worker` resource is a Dockerfile-backed container (`DockerfileBuildAnnotation` present), not a plain `ContainerImageAnnotation`.

Existing `AgentExecutionModeStartupTests` already cover the DI-level Docker/SingleContainer registration; no change there.

### D7. Migration for the server-logging fix: no breaking change in prod

`Program.cs`'s logging rewire must preserve prod stdout format. `JsonConsoleFormatter` stays in place, still formats as the final console provider. Prod docker logs continue to be JSON. Diff boils down to moving one line (`ClearProviders`) from after `AddServiceDefaults()` to before it. The only observable prod change is that OTLP logging (currently also wiped by ClearProviders) is now active — which is a no-op in prod when `OTEL_EXPORTER_OTLP_ENDPOINT` is unset (ServiceDefaults gates OTLP exporter addition on that env var). So prod behaviour is genuinely unchanged.

## Risks / Trade-offs

- **Longer first-boot time for dev-windows / dev-container / dev-live.** [Risk] The worker Dockerfile build adds 30–60s on a cold Docker layer cache; without it, first boot was ~5s (just pull). → [Mitigation] Document in CLAUDE.md; subsequent boots hit the layer cache and are near-instant. Consider pre-pulling base images in `scripts/set-user-secrets.sh` or a similar bootstrap step (out of scope here, but flag).
- **`AddDockerfile` image-tag addressability from DooD.** [Risk] Aspire computes a tag for built images (e.g. `apphost-worker:latest` or a hash-suffixed name). The server's DooD call must be able to `docker run <tag>` that exact string via the host Docker socket. → [Mitigation] Verify empirically as the first task. If Aspire's computed tag is not addressable from a separate Docker client (e.g. it lives in a private Aspire image store), fall back to explicitly tagging the image via `WithImageTag("homespun-worker:dev")` or equivalent. See Open Question O1.
- **Alloy config churn.** [Risk] Introducing Alloy means learning a new config language (River/HCL-ish). Dev stack now has two log shippers if D4b is picked: Alloy in dev, Promtail in prod. → [Mitigation] The configs live side-by-side in `config/`; prod is unaffected. If the Alloy config becomes a maintenance burden, fall back to D4a.
- **`ServiceDefaults` log reorder in Program.cs could surprise.** [Risk] The provider ordering in `Logging` builder is subtle; a future reader may reintroduce `ClearProviders()` after `AddServiceDefaults()` thinking it's harmless. → [Mitigation] Add a code comment in Program.cs explaining the ordering constraint; add an API integration test asserting at least one `OpenTelemetryLoggerProvider` is present on the built host.
- **Test surface drift.** [Risk] Asserting "no resource references GHCR" in AppHostTests is brittle if the docker-compose-authored resources are ever loaded into the AppHost model. → [Mitigation] Scope the assertion to `ContainerResource`/`DockerfileBuildAnnotation` shapes that the AppHost actually produces.

## Migration Plan

1. Land the server `Program.cs` reorder + an API integration test asserting OTLP logger presence. Verify `aspire otel logs server` returns entries post-request.
2. Land the AppHost `AddDockerfile` swap for the SingleContainer branch + the `AgentExecution__Docker__WorkerImage` env injection for the dev-live path. Extend AppHostTests.
3. Spike: start Alloy container + minimal config + verify OTLP-in-Loki-out works; confirm Grafana datasource still resolves and queries return server logs. Decide D4a vs D4b definitively based on the spike.
4. Land whichever D4 branch the spike validates. Remove promtail from AppHost only (leave `docker-compose.yml` promtail untouched).
5. Update `CLAUDE.md` dev-orchestration + observability sections to reflect the new log flow and the local-worker-build behaviour.
6. **Rollback:** revert the single commit. Prod is untouched by construction; reverting restores the pre-change (broken) dev behaviour without risk to production.

## Open Questions

- **O1.** Is the image tag produced by `AddDockerfile` addressable from a sibling DooD `docker run`? If yes → D2 is clean. If no → need to explicitly tag via `WithImageTag` or reverse-engineer Aspire's naming convention. Required resolution: first task of the implementation.
  - *Resolved (2026-04-19):* Aspire (13.2) sets `DockerfileBuildAnnotation.ImageName` to the resource name and `ImageTag` to a computed hash by default (e.g. `worker:191ccd735…`). Calling `.WithImageTag("dev")` on the resource builder overrides both the `ContainerImageAnnotation.Tag` AND `DockerfileBuildAnnotation.ImageTag` to `"dev"`, producing a deterministic `worker:dev` image that Docker Desktop's host daemon stores and that sibling `docker run` calls can reference. The AppHost now pins `WithImageTag("dev")` on every worker `AddDockerfile` call and injects the resulting `worker:dev` tag into `AgentExecution__Docker__WorkerImage`. Verified via `WorkerImageAppHostTests`.
- **O2.** Does Aspire's OpenTelemetry wiring in `ServiceDefaults` let us split OTLP sinks (traces+metrics to Aspire dashboard, logs to Alloy) via per-signal env vars (`OTEL_EXPORTER_OTLP_LOGS_ENDPOINT`)? Verify against the OTel .NET SDK version the repo is on before committing to D4b.
- **O3.** Should the dev `AddDockerfile` build trigger on every AppHost start, or should we cache the tag across runs? Aspire's default is per-session rebuild (respecting Docker cache). Probably fine; revisit if first-boot pain proves material.
- **O4.** `docker-compose.yml` still lists Promtail. Should prod eventually migrate to Alloy for consistency with dev? Out of scope here — decide in a follow-up change if D4b proves itself in dev.
