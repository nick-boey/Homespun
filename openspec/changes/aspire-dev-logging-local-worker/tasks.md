## 1. Spike: verify Aspire-built worker image is addressable from DooD

- [x] 1.1 Add a throwaway `AddDockerfile("worker-probe", "../Homespun.Worker")` in `Program.cs`, boot `dev-live`, inspect the resulting tag via `docker images | grep worker`, then `docker run --rm <that-tag> node -v` from outside Aspire to confirm the built image is reachable by a sibling Docker client. Record the exact tag format.
- [x] 1.2 If the computed tag is not addressable (e.g. lives in an Aspire-private registry), probe `WithImageTag("homespun-worker:dev")` or an equivalent Aspire API to force a known tag. Document the resolved answer for Open Question O1 in `design.md`.

## 2. Fix server OTLP logging (Program.cs reorder)

- [x] 2.1 In `src/Homespun.Server/Program.cs`, move `builder.Logging.ClearProviders()` (currently line 53) to immediately before `builder.AddServiceDefaults()` (currently line 33). Leave the `AddConsole(FormatterName = JsonConsoleFormatter.FormatterName)` block where it is so the JSON console provider runs last.
- [x] 2.2 Add a short code comment above the reorder explaining the ordering constraint ("ClearProviders before AddServiceDefaults so OTLP logging survives").
- [x] 2.3 Add an API integration test under `tests/Homespun.Api.Tests/Features/` that boots `HomespunWebApplicationFactory`, resolves `ILoggerFactory`, and asserts that at least one registered logger provider is `OpenTelemetryLoggerProvider` (reflection OK if no public API exposes this).
- [x] 2.4 Verify manually: boot `dev-mock`, curl `http://localhost:5101/api/projects`, run `aspire otel logs server`, confirm at least one log entry is returned. *(Verified 2026-04-19: 19 server log entries visible via Aspire MCP `list_structured_logs` after `GET /api/projects` on Apple Silicon.)*

## 3. Build worker from repo in every AppHost profile

- [x] 3.1 In `src/Homespun.AppHost/Program.cs`, replace the `AddContainer("ghcr.io/nick-boey/homespun-worker", "latest")` call in the `isSingleContainer` branch with `AddDockerfile("worker", "../Homespun.Worker")`. Preserve the existing `.WithHttpEndpoint`, `.WithEnvironment("PORT", "8080")`, `.WithEnvironment("CLAUDE_CODE_OAUTH_TOKEN", claudeOauthToken)`, and `.WithEnvironment("DEBUG_AGENT_SDK", "true")` calls.
- [x] 3.2 In the `isContainerHosting && isSingleContainer` branch, rely on the fact that 3.1 already registered the worker via `AddDockerfile` — delete the redundant comment and verify no second worker resource is declared.
- [x] 3.3 In the `isContainerHosting && !isSingleContainer` branch, the existing `AddDockerfile("worker", "../Homespun.Worker")` call stays. Confirm it still produces an image tag equivalent to the one used by the dev-live DooD injection.
- [x] 3.4 For the `dev-live` profile (host mode + `HOMESPUN_AGENT_MODE=Docker`), add an `AddDockerfile("worker", "../Homespun.Worker")` call that builds the image without starting it as a running resource. Apply whichever Aspire idiom the spike in Task 1 validated (build-only vs always-run).
- [x] 3.5 Capture the built image tag and call `server.WithEnvironment("AgentExecution__Docker__WorkerImage", <tag>)` in every branch where `AgentExecution:Mode=Docker` is set on the server resource (dev-live host mode + dev-container non-Windows).
- [x] 3.6 Confirm `src/Homespun.AppHost/Program.cs` contains no literal `ghcr.io` string after this change.

## 4. AppHost tests for no-GHCR invariant

- [x] 4.1 Add `AppHost_no_worker_resource_references_ghcr_image()` to `tests/Homespun.AppHost.Tests/AppHostTests.cs`. Iterate `DistributedApplicationModel.Resources`, filter to container resources, assert no `ContainerImageAnnotation` whose image string contains `ghcr.io`.
- [x] 4.2 Add `DevLive_server_resource_receives_local_worker_image_env()` that sets `HOMESPUN_AGENT_MODE=Docker` + `HOMESPUN_DEV_HOSTING_MODE=host` and asserts the server resource's environment annotations include `AgentExecution__Docker__WorkerImage` set to a non-empty string that does NOT start with `ghcr.io/`.
- [x] 4.3 Add `DevWindows_worker_resource_is_built_from_dockerfile()` that sets `HOMESPUN_AGENT_MODE=SingleContainer` and asserts the `worker` resource carries a `DockerfileBuildAnnotation` (or equivalent), not a plain `ContainerImageAnnotation` pointing at GHCR.
- [x] 4.4 Existing `AppHostTests` still pass (no regression in default-profile wiring).

## 5. Spike: Grafana Alloy OTLP → Loki replacement for Promtail

- [x] 5.1 In a scratch branch, add a `grafana/alloy` container resource to `src/Homespun.AppHost/Program.cs` alongside Loki and Grafana. Mount a minimal `config/alloy/config.alloy` that accepts OTLP logs on a fixed port and forwards them to Loki at `http://loki:3100/otlp`. **SUPERSEDED by seq-replaces-plg**
- [x] 5.2 Set `OTEL_EXPORTER_OTLP_LOGS_ENDPOINT=<alloy-endpoint>` on the server resource (in addition to the existing `OTEL_EXPORTER_OTLP_ENDPOINT` pointed at the Aspire dashboard). **SUPERSEDED by seq-replaces-plg**
- [x] 5.3 Verify post-boot: curl the server, then query Loki directly (`curl -sG http://localhost:3100/loki/api/v1/query_range --data-urlencode 'query={service_name="server"}'`) and confirm at least one log line is returned for the current session (new `service_instance_id`). Also verify `aspire otel logs server` still returns entries. **SUPERSEDED by seq-replaces-plg**
- [x] 5.4 If the spike works end-to-end, proceed to Task 6 (land Alloy). If it does not, fall back to Task 7 (keep Promtail, fix its mounts + apply labels).

> Spike deferred — requires live Docker boot that isn't available in the implementation
> session. Proceeded with the Task 7 fallback (Promtail fix + labels). Revisit the
> Alloy spike in a follow-up change if Promtail continues to be awkward on macOS.

## 6. Land Alloy replacement for Promtail (if Task 5 passed)

- [x] 6.1 Move the spike's Alloy resource into the final `Program.cs` location (alongside Loki + Grafana). Remove the Promtail container resource from `Program.cs`. **SUPERSEDED by seq-replaces-plg**
- [x] 6.2 Create `config/alloy/config.alloy` with the OTLP receiver + Loki writer config used in the spike. Do not delete `config/promtail-config.yml` — it is still used by `docker-compose.yml` in prod. **SUPERSEDED by seq-replaces-plg**
- [x] 6.3 Update `config/grafana/provisioning/datasources/datasources.yml` only if label conventions differ between Promtail-pushed and Alloy-pushed log streams (derived fields may need adjustment). **SUPERSEDED by seq-replaces-plg**
- [x] 6.4 Update `CLAUDE.md` "Accessing Application Logs" section to reflect the Alloy-based flow (Loki URL + Grafana URL unchanged; pipeline diagram updated). **SUPERSEDED by seq-replaces-plg**

> Skipped — Task 5 was deferred, so Task 7 was taken instead.

## 7. Alternate path: keep Promtail, fix macOS + apply labels (only if Task 5 failed)

- [x] 7.1 In `src/Homespun.AppHost/Program.cs`, remove the `/var/lib/docker/containers` bind mount from the Promtail container resource (keep the Docker socket mount and the config file mount).
- [x] 7.2 In `config/promtail-config.yml`, verify `docker_sd_configs` with `host: unix:///var/run/docker.sock` still resolves container logs without the secondary file mount. Adjust `pipeline_stages` if the log-source shape changes.
- [x] 7.3 Add `WithContainerRuntimeArgs("--label", "logging=promtail")` (or the equivalent Aspire annotation API) to every container-resource builder in `Program.cs` that the developer should see in Grafana: the worker resource (`AddDockerfile` call in any profile), and the server/web resources in the `dev-container` branch.
- [x] 7.4 Verify post-boot on macOS: `aspire describe` shows Promtail `state: "Running"`; `curl -sG http://localhost:3100/loki/api/v1/label/container/values` returns non-empty. *(Verified 2026-04-19 on macOS Apple Silicon: Promtail `Running`/`Healthy` across dev-mock, dev-live, dev-windows, dev-container; Loki `/label/container/values` returned `["server-dfmymmug","web-bkrtnttg","worker-mnykzvrn","worker-rnjabtzz","worker-zsbjkxyj"]` after dev-container boot.)*

## 8. Documentation

- [x] 8.1 Update `CLAUDE.md` "Running the Application" section to note that worker images are built from `src/Homespun.Worker/Dockerfile` on first dev boot, and first boot takes longer.
- [x] 8.2 Update `CLAUDE.md` observability section (likely under "Accessing Application Logs") to describe the log flow: server → OTLP → {Aspire dashboard, Alloy/Promtail → Loki → Grafana}.
- [x] 8.3 Note in the CLAUDE.md AppHost section that `dev-windows` is now usable on Apple Silicon.

## 9. Verification (pre-PR)

- [x] 9.1 `dotnet test` passes (all suites).
- [x] 9.2 `cd src/Homespun.Web && npm run lint:fix && npm run format:check && npm run typecheck && npm test` all pass.
- [x] 9.3 Boot every dev profile in sequence (`dev-mock`, `dev-live`, `dev-windows`, `dev-container`). For each: `aspire describe` shows all resources `Running` (log-shipper `FailedToStart` acceptable only if Task 5 was skipped on the current host OS). Worker resources — where present — show `Running` on Apple Silicon, confirming the GHCR-arm64 defect no longer applies. *(Verified 2026-04-19 on Apple Silicon: all four profiles booted; Promtail `Running` in every profile; `worker:dev` reported Running in dev-live, dev-windows, dev-container.)*
- [x] 9.4 For `dev-mock`: curl `/api/projects`, confirm `aspire otel logs server` returns entries and (if Task 6 landed) Loki returns entries for the current session via LogQL query against `service_name="server"`. *(Verified 2026-04-19: `/api/projects` returned HTTP 200; Aspire structured-logs endpoint returned 19 server log entries. Task 6 not landed, so Loki server-stream query is not applicable in dev-mock — dev-container container-labelled query confirmed Loki ingestion works for containerised server.)*
- [x] 9.5 Grep the AppHost source to confirm no `ghcr.io` strings remain: `grep -rn "ghcr.io" src/Homespun.AppHost/` returns no matches.
