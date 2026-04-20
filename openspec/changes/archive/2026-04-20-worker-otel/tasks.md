## 1. Dependencies

- [x] 1.1 `cd src/Homespun.Worker && npm install --save @opentelemetry/sdk-node @opentelemetry/api @opentelemetry/api-logs @opentelemetry/auto-instrumentations-node @opentelemetry/exporter-trace-otlp-http @opentelemetry/exporter-logs-otlp-http @opentelemetry/sdk-logs @opentelemetry/resources @opentelemetry/semantic-conventions`.
- [x] 1.2 Confirm `package.json` lists pinned minor versions for all 9 new packages.
- [x] 1.3 Run `npm run build` and confirm tsc still passes with the new deps.

## 2. OTel bootstrap

- [x] 2.1 Create `src/Homespun.Worker/src/instrumentation.ts` that:
  - Imports `NodeSDK`, exporters, resource, auto-instrumentations.
  - Builds a `Resource` with `service.name=homespun.worker`, `service.version`, `deployment.environment`, and per-session env attrs (`homespun.session.id`, `homespun.issue.id`, `homespun.project.name`, `homespun.agent.mode`) where present.
  - Configures `NodeSDK` with `traceExporter: new OTLPTraceExporter({url: `${OTLP_PROXY_URL}/traces`})` and `logRecordProcessors: [new BatchLogRecordProcessor(new OTLPLogExporter({url: `${OTLP_PROXY_URL}/logs`}), {scheduledDelayMillis: 1000})]`.
  - Disables `instrumentation-fs`, `instrumentation-net`, `instrumentation-dns` via `getNodeAutoInstrumentations({'@opentelemetry/instrumentation-fs': {enabled:false}, ...})`.
  - Calls `sdk.start()`.
  - Registers SIGTERM + SIGINT handlers that `await sdk.shutdown().catch(() => {})` then `process.exit(0)`.
- [x] 2.2 Add `import './instrumentation'` as the FIRST non-shebang line of `src/Homespun.Worker/src/index.ts`.
- [x] 2.3 Expose a dev-only `DEBUG_OTEL_CONSOLE=true` flag that attaches a console exporter in parallel for local diagnostics.

## 3. Log/trace call-site migration

- [x] 3.1 Replace `info(msg)` → `logs.getLogger('homespun.worker').emit({severityNumber: SeverityNumber.INFO, body: msg})` across: `services/{sse-writer,session-manager,session-inventory,openspec-snapshot}.ts`, `routes/{test,sessions,mini-prompt}.ts`, `index.ts`, `scripts/spike-idle-tolerance.ts`.
- [x] 3.2 Replace `warn(msg)` and `error(msg, err)` analogously, with `err` attached via `trace.getActiveSpan()?.recordException(err)` and attributes `exception.type`, `exception.message`, `exception.stacktrace`.
- [x] 3.3 Rewrite `diagnostics.ts` to use the OTel logger rather than stdout JSON.
- [x] 3.4 Keep `sessionEventLog(hop, fields)` call sites compiling for now (stub `sessionEventLog` as a TODO that still logs via OTel under a throwaway `'deprecated-hop'` attribute). `session-event-log-to-spans` (#6) converts these to real spans.
- [x] 3.5 Retain `sdkDebug` behaviour (gated by `DEBUG_AGENT_SDK`) via OTel span events attached to the active span: `trace.getActiveSpan()?.addEvent('sdk.' + direction, {payload})`.

## 4. Delete legacy logger

- [x] 4.1 Delete `src/Homespun.Worker/src/utils/logger.ts`.
- [x] 4.2 Delete `src/Homespun.Worker/src/utils/logger.test.ts`.
- [x] 4.3 Verify `grep -rn "sessionEventLog\|extractA2ACorrelation\|extractMessagePreview" src/` returns no references inside worker source beyond the `utils/` directory (which is deleted). — stub lives in `utils/otel-logger.ts` per 3.4.

## 5. Server-side spawn wiring

- [x] 5.1 In `src/Homespun.Server/Features/ClaudeCode/Services/DockerAgentExecutionService.cs`, extend the docker-run args-builder (the block starting `dockerArgs.Append("-e WORKING_DIRECTORY=/workdir ");` near line 1577) to append `-e OTLP_PROXY_URL=<proxy-url>`.
- [x] 5.2 Resolve the proxy URL from a new `DockerAgentExecutionOptions.ServerOtlpProxyUrl` config key. Default in host mode: `http://host.docker.internal:5101/api/otlp/v1`. Default in container mode: `http://server:8080/api/otlp/v1`. AppHost injects via env.
- [x] 5.3 Also append `-e OTEL_SERVICE_NAME=homespun.worker` and `-e HOMESPUN_SESSION_ID=${session.Id}` / `HOMESPUN_ISSUE_ID` / `HOMESPUN_PROJECT_NAME` so the boot-time `Resource` picks them up.
- [x] 5.4 Replace `docker kill` with `docker stop --time 3` in `StopContainerAsync` so workers receive SIGTERM and flush OTel batches.
- [x] 5.5 Add a unit test asserting `OTLP_PROXY_URL` appears in the built docker args for a representative session.

## 6. Delete ClientLogController + related

- [x] 6.1 Confirm no remaining caller in worker or web source POSTs to `/api/log/client`.
- [x] 6.2 Delete `src/Homespun.Server/Features/Observability/ClientLogController.cs`.
- [x] 6.3 Delete `src/Homespun.Server/Features/Observability/ClientLogEntry.cs`.
- [x] 6.4 Leave `SessionEventLogEntry.cs`, `SessionEventLog.cs`, and related server-side hop logging in place — they are retired by `session-event-log-to-spans` (#6) after the client cutover.

## 7. Tests

- [x] 7.1 `src/Homespun.Worker/src/instrumentation.test.ts` — assert that when `OTLP_PROXY_URL` is unset, boot does not throw; when set, exporters are created with the expected URL suffixes. — lives at `tests/Homespun.Worker/utils/instrumentation.test.ts`.
- [x] 7.2 `DockerAgentExecutionServiceTests.Spawned_container_receives_OTLP_PROXY_URL_env()`.
- [x] 7.3 `DockerAgentExecutionServiceTests.StopContainerAsync_uses_docker_stop_not_kill()`.

## 8. Documentation

- [x] 8.1 Update `CLAUDE.md` observability section with the worker's OTLP path.
- [x] 8.2 Add a note under "Do NOT enable" listing `@opentelemetry/instrumentation-fs` and why.

## 9. Verification

- [x] 9.1 `dotnet test` passes.
- [x] 9.2 `cd src/Homespun.Worker && npm run build && npm test` pass.
- [x] 9.3 `dev-live` boot: spawn a session, observe (a) worker logs arrive in Seq with `service.name = homespun.worker` ✓, (b) worker spans arrive in Seq under the same trace ID as the server's inbound HTTP request — **not fully verifiable on Windows Docker Desktop**: server runs on the Windows host and cannot route to the Docker bridge (`172.17.0.0/16`), so no server→worker HTTP call ever lands on the worker to generate the child span. Switch to `dev-windows` (SingleContainer, worker endpoint exposed via Aspire) or `dev-container` for end-to-end trace verification. (c) `docker logs <worker>` shows human-readable text fallback via `StderrTextLogExporter` ✓.
  - Verification uncovered 4 pre-existing bugs (all fixed in this pass):
    - Worker container did not resolve `host.docker.internal` → OTLP proxy unreachable. Fixed by appending `--add-host=host.docker.internal:host-gateway` in `DockerAgentExecutionService.BuildContainerDockerArgs`.
    - `@hono/node-server` bound IPv6 only, breaking IPv4 reach from Windows host / Docker bridge. Fixed by passing `hostname: '0.0.0.0'` to `serve(...)` in `src/index.ts`.
    - OTel logger emitted nothing to stdout/stderr, so `docker logs` only showed the start.sh banner. Fixed by adding an always-on `StderrTextLogExporter` (text-formatted, not JSON) alongside the OTLP batch processor.
    - `@opentelemetry/exporter-*-otlp-http` defaults to JSON, not protobuf as the doc-comment claimed; `OtlpReceiverController` rejects non-protobuf with HTTP 415. Fixed by switching worker to `@opentelemetry/exporter-{logs,trace}-otlp-proto`.
- [x] 9.4 Grep worker source for `console.log\|console.warn\|console.error` — remaining occurrences must be intentional (document each) or removed.
