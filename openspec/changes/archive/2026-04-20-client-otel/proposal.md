## Why

The React client today ships custom `TelemetryService` + `TelemetryBatcher` (`src/lib/telemetry/`) and a `sessionEventLog` helper (`src/lib/session-event-log.ts`) that POST JSON batches to `/api/client-telemetry` and `/api/log/client`. Both schemas are Homespun-specific, pre-date OTel adoption, and don't carry TraceId/SpanId — so client-originated user actions can't be correlated with server or worker spans in Seq.

Most traces in Homespun begin on the client — "user clicks Start Agent" is the root span for an entire session's worth of server + worker work. Switching the client to standard OpenTelemetry SDKs gives end-to-end distributed tracing for free via W3C `traceparent` propagation on fetches. Bundle cost is ~40 KB gzipped without `zone.js`.

SignalR is the one gap: browsers cannot set per-invoke headers on WebSocket transports. The chosen mechanism (see `session-event-log-to-spans` / exploration) is the **traceparent-as-first-arg convention** for client→server invokes plus a new `Traceparent` field on `SessionEventEnvelope` for server→client broadcasts.

## What Changes

- **Install the browser OTel stack**: `@opentelemetry/api`, `@opentelemetry/api-logs`, `@opentelemetry/sdk-trace-web`, `@opentelemetry/sdk-logs`, `@opentelemetry/core`, `@opentelemetry/resources`, `@opentelemetry/semantic-conventions`, `@opentelemetry/exporter-trace-otlp-http`, `@opentelemetry/exporter-logs-otlp-http`, `@opentelemetry/instrumentation`, `@opentelemetry/instrumentation-fetch`, `@opentelemetry/instrumentation-xml-http-request`. Do NOT install `@opentelemetry/context-zone` — StackContextManager (default) suffices and saves ~80 KB.
- **Add `src/Homespun.Web/src/instrumentation.ts`** — boots `WebTracerProvider` + `LoggerProvider`, points exporters at `/api/otlp/v1/{logs,traces}`, registers `CompositePropagator(W3CTraceContextPropagator + W3CBaggagePropagator)`, enables `FetchInstrumentation` + `XMLHttpRequestInstrumentation` with `ignoreUrls: [/\/api\/otlp\/v1\//]` (avoid tracing our own exporter). Listens for `pagehide` to force-flush both providers.
- **Make `src/main.tsx`'s first import `./instrumentation`** so fetch/XHR patches apply before any other module caches them.
- **Add SignalR traceparent plumbing** via a new `src/lib/signalr/trace.ts`:
  - `injectTraceparent()` → current context → string (first arg for hub invokes).
  - `withExtractedContext(envelope, fn)` → runs `fn` under the envelope's traceparent.
  - `traceInvoke(invoke, name, ...args)` → wraps a typed invoke, starting a client span named `signalr.invoke.<method>`.
- **Extend `SessionEventEnvelope` DTO** (`src/Homespun.Shared/Models/Observability/SessionEventEnvelope.cs`) with `string? Traceparent`. Server populates it from `Activity.Current.Context` before `BroadcastSessionEvent`. Client `withExtractedContext` extracts it before dispatching to `agui-reducer`.
- **Change every hub method signature** on `ClaudeCodeHub` (and any other hub clients call) to accept `string traceparent` as the first parameter. Register a new `TraceparentHubFilter` in `Program.cs` that extracts traceparent, starts an Activity on `Homespun.Signalr` ActivitySource, enriches with `homespun.session.id` when the second arg is a sessionId string.
- **Delete the native SignalR ActivitySource** from the tracer registration: `AddSource("Microsoft.AspNetCore.SignalR.Server")` is removed. Our filter owns the SignalR span tree. Cross-references with `seq-replaces-plg`: this change REVERTS the `AddSource` call added there. (The filter-owned span is richer and, crucially, actually gets our client's traceparent as its parent.)
- **Delete the custom telemetry stack**: `src/lib/telemetry/*`, `src/lib/session-event-log.ts`, `src/providers/telemetry-{context,provider}.tsx`, `src/hooks/use-telemetry.{ts,test.tsx}`, `src/test/mocks/telemetry.ts`. Call sites rewrite to tracer / logger APIs.
- **Delete `/api/client-telemetry` (ClientTelemetryController)** — no remaining callers.

## Capabilities

### Modified Capabilities
- `observability` — adds browser-originated tracing + SignalR propagation to the sink topology.

## Impact

- **Files touched:**
  - `src/Homespun.Web/package.json` — +12 OTel deps.
  - `src/Homespun.Web/src/instrumentation.ts` — new.
  - `src/Homespun.Web/src/main.tsx` — first-line import.
  - `src/Homespun.Web/src/lib/signalr/trace.ts` — new.
  - `src/Homespun.Web/src/providers/signalr-provider.tsx` — wrap invoke + on with trace helpers.
  - `src/Homespun.Web/src/lib/telemetry/**` — DELETE (6 files).
  - `src/Homespun.Web/src/lib/session-event-log.ts` — DELETE.
  - `src/Homespun.Web/src/providers/telemetry-{context,provider}.tsx` — DELETE.
  - `src/Homespun.Web/src/hooks/use-telemetry.{ts,test.tsx}` — DELETE.
  - `src/Homespun.Web/src/test/mocks/telemetry.ts` — DELETE.
  - `src/Homespun.Web/src/routes/__root.tsx` — drop TelemetryProvider wrapping.
  - `src/Homespun.Web/src/components/error-boundary.tsx` — rewrite to use OTel API.
  - `src/Homespun.Web/src/features/sessions/utils/agui-reducer.ts` — drop sessionEventLog callsite.
  - `src/Homespun.Web/src/features/projects/hooks/use-projects.ts`, `.../use-create-project.ts`, `features/issues/hooks/use-create-issue.ts`, `features/agents/hooks/use-start-agent.ts` — drop telemetry calls; rely on fetch auto-instr.
  - `src/Homespun.Shared/Models/Observability/SessionEventEnvelope.cs` — add `Traceparent`.
  - `src/Homespun.Server/Features/ClaudeCode/Hubs/ClaudeCodeHub.cs` — every hub method signature gains `string traceparent` first arg.
  - `src/Homespun.Server/Features/ClaudeCode/Hubs/ClaudeCodeHubExtensions.cs` (or wherever broadcast happens) — populate `envelope.Traceparent` from `Activity.Current`.
  - `src/Homespun.Server/Features/Observability/TraceparentHubFilter.cs` — new.
  - `src/Homespun.Server/Program.cs` — register the filter in `AddSignalR`.
  - `src/Homespun.ServiceDefaults/Extensions.cs` — REMOVE `AddSource("Microsoft.AspNetCore.SignalR.Server")` added by `seq-replaces-plg`.
  - `src/Homespun.Server/Features/Observability/ClientTelemetryController.cs` — DELETE.
  - `src/Homespun.Shared/Models/Observability/ClientTelemetryBatch.cs` + related DTOs — DELETE.
  - OpenAPI regen via `npm run generate:api:fetch` across affected API types.
  - `src/Homespun.Web/e2e/**` — verify Playwright tests still pass with new DTO.

- **Dependencies:** 12 new npm packages (~40 KB gzipped). No new NuGet.

- **Risk surface:**
  - Hub-signature breakage: every hub method gains a new first param. Any existing integration calling `JoinSession(sessionId)` directly (tests, tooling) must be updated. Greppable signature change.
  - `SessionEventEnvelope` gains an optional field — existing consumers unaffected (DTO is additive), but OpenAPI regen must run.
  - StackContextManager caveat: Promise chains across React state boundaries don't auto-propagate context. Tests must cover a fetch-inside-useEffect case to verify the traceparent flows correctly.
  - Deleting `TelemetryProvider` breaks anything importing it — greppable.

- **Rollback:** revert. Custom telemetry stack returns; hub signatures revert; `ClientTelemetryController` restored.

## Trace dictionary

This change introduces new client spans (`homespun.signalr.client.connect`,
`signalr.invoke.<method>`, `homespun.envelope.rx`,
`homespun.client.reducer.apply`). Every new span name MUST land with an H3
entry under `## Client-originated traces` in
[`docs/traces/dictionary.md`](../../../docs/traces/dictionary.md) in the same
PR — the `trace-dictionary` change's drift check refuses to merge otherwise.
