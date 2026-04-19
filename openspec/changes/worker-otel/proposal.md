## Why

The worker today logs via `src/Homespun.Worker/src/utils/logger.ts` — a hand-rolled JSON-to-stdout formatter that produces a PLG-compatible schema (`Timestamp`, `Level`, `Message`, `Component`, per-session fields). With PLG retired (`seq-replaces-plg`) and the server's OTLP proxy receiving client/worker telemetry (`server-otlp-proxy`), the worker's ad-hoc logger is obsolete: it emits the wrong shape for the new sink, doesn't carry TraceId/SpanId, and its hand-rolled `sessionEventLog` hops are a parallel implementation of what OTel spans do natively.

Migrate the worker to `@opentelemetry/sdk-node` with auto-instrumentation so inbound HTTP requests from the server carry trace context through to the Claude SDK calls and downstream A2A emits. Trace continuation from server → worker is free via `@opentelemetry/instrumentation-http` extracting `traceparent` on each inbound request — no env shim needed since every worker operation begins with an HTTP request.

## What Changes

- **Install OTel Node SDK stack**: `@opentelemetry/sdk-node`, `@opentelemetry/api`, `@opentelemetry/api-logs`, `@opentelemetry/auto-instrumentations-node`, `@opentelemetry/exporter-trace-otlp-http`, `@opentelemetry/exporter-logs-otlp-http`, `@opentelemetry/sdk-logs`, `@opentelemetry/resources`, `@opentelemetry/semantic-conventions`.
- **Add `src/Homespun.Worker/src/instrumentation.ts`** — boots `NodeSDK` with both exporters pointing at the server's OTLP proxy (`${OTLP_PROXY_URL}/logs`, `${OTLP_PROXY_URL}/traces`). Resource attributes `service.name = homespun.worker`, `service.version`, plus per-container context from env (`homespun.session.id`, `homespun.issue.id`, `homespun.project.name`, `homespun.agent.mode`). Auto-instrumentations on for Hono/http; disabled for `fs`, `net`, `dns` (too chatty). Batch log processor `scheduledDelayMillis = 1000` so sibling-container kills lose at most 1s of logs.
- **Make `src/index.ts` import `./instrumentation` as its FIRST line** so Hono and its fetch/http use patched modules.
- **Wire SIGTERM / SIGINT** handlers that `await sdk.shutdown()` before `process.exit(0)` — required so batched signals flush before the container dies.
- **Delete `src/utils/logger.ts`** (incl. `sessionEventLog`, `sdkDebug`, hop constants). Call sites rewrite to `logs.getLogger('homespun.worker').emit({...})` and `tracer.startActiveSpan(...)`. (Semantic hop→span migration lives in `session-event-log-to-spans`; this change keeps spans minimal: an `http.server.request` span per inbound + one `homespun.worker.session.init` span at session creation. Richer span coverage is #6's scope.)
- **Server docker-run env injection** — `DockerAgentExecutionService` appends `-e OTLP_PROXY_URL=http://{serverHost}:{port}/api/otlp/v1` to every spawned worker's docker args. Host-mode sibling spawns resolve `serverHost` to `host.docker.internal`; container mode resolves to the server's container hostname.
- **Delete `src/Homespun.Server/Features/Observability/ClientLogController.cs` + `ClientLogEntry.cs`** once worker cutover is verified — they were the worker's server-side ingest. Deletion guarded by a flag-off test: no callers remain.

## Capabilities

### Modified Capabilities
- `observability` — worker now emits OpenTelemetry logs + traces via the server OTLP proxy established by `server-otlp-proxy`. Inherits the sink topology from `seq-replaces-plg`.

## Impact

- **Files touched:**
  - `src/Homespun.Worker/package.json` — +9 OTel deps.
  - `src/Homespun.Worker/src/instrumentation.ts` — new.
  - `src/Homespun.Worker/src/index.ts` — first-line import of `./instrumentation`; SIGTERM handler.
  - `src/Homespun.Worker/src/utils/logger.ts` — DELETE.
  - `src/Homespun.Worker/src/utils/logger.test.ts` — DELETE.
  - `src/Homespun.Worker/src/utils/diagnostics.ts` — rewrite to use OTel logger.
  - `src/Homespun.Worker/src/services/{sse-writer,session-manager,session-inventory,openspec-snapshot}.ts` — rewrite log call sites.
  - `src/Homespun.Worker/src/routes/{test,sessions,mini-prompt}.ts` — rewrite log call sites.
  - `src/Homespun.Worker/scripts/spike-idle-tolerance.ts` — rewrite log call sites.
  - `src/Homespun.Worker/Dockerfile` — ensure `NODE_ENV=production` paths honour tree-shaking if the SDK's dev-only code is ever pulled in.
  - `src/Homespun.Server/Features/ClaudeCode/Services/DockerAgentExecutionService.cs` — append `-e OTLP_PROXY_URL=...` in `BuildDockerRunArgs`.
  - `src/Homespun.Server/Features/Observability/ClientLogController.cs` — DELETE after cutover.
  - `src/Homespun.Server/Features/Observability/ClientLogEntry.cs` — DELETE.
  - `src/Homespun.Shared/Models/Observability/SessionEventLogEntry.cs` — retained for server-side use until `session-event-log-to-spans` retires it.
  - `tests/Homespun.Tests/Features/ClaudeCode/DockerAgentExecutionServiceTests.cs` — add assertion on `OTLP_PROXY_URL` env injection.

- **Dependencies:**
  - `@opentelemetry/sdk-node` ^0.56, `@opentelemetry/api` ^1.9, `@opentelemetry/api-logs` ^0.56, `@opentelemetry/auto-instrumentations-node` ^0.56, `@opentelemetry/exporter-trace-otlp-http` ^0.56, `@opentelemetry/exporter-logs-otlp-http` ^0.56, `@opentelemetry/sdk-logs` ^0.56, `@opentelemetry/resources` ^2.0, `@opentelemetry/semantic-conventions` ^1.29.

- **Risk surface:**
  - Sibling containers killed with `docker kill` (SIGKILL) lose their final log batch. Mitigation: 1s batch delay + server spawns with `docker stop --time 3` (not `docker kill`) in `DockerAgentExecutionService.StopContainerAsync`.
  - Cold-start overhead: `sdk.start()` adds ~200ms. Worker already takes seconds to warm; negligible.
  - `@opentelemetry/instrumentation-fs` is excluded by default — if a future contributor enables it, log volume explodes. Document in CLAUDE.md.

- **Rollback:** revert. Worker regains custom logger; server regains `ClientLogController` (also reverted).

## Trace dictionary

Worker spans introduced here (`homespun.worker.session.init`, and the richer
surface added by `session-event-log-to-spans`) land alongside H3 entries
under `## Worker-originated traces` in
[`docs/traces/dictionary.md`](../../../docs/traces/dictionary.md). The
`trace-dictionary` change's drift check enforces the match in every PR that
touches worker spans.
