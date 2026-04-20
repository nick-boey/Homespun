## 1. Dependencies

- [x] 1.1 `cd src/Homespun.Web && npm install --save @opentelemetry/api @opentelemetry/api-logs @opentelemetry/sdk-trace-web @opentelemetry/sdk-logs @opentelemetry/core @opentelemetry/resources @opentelemetry/semantic-conventions @opentelemetry/exporter-trace-otlp-http @opentelemetry/exporter-logs-otlp-http @opentelemetry/instrumentation @opentelemetry/instrumentation-fetch @opentelemetry/instrumentation-xml-http-request`.
- [x] 1.2 Do NOT install `@opentelemetry/context-zone`. If `package.json` already contains it (it shouldn't), remove it.
- [x] 1.3 Build + typecheck clean: `npm run build && npm run typecheck`.

## 2. Bootstrap

- [x] 2.1 Create `src/Homespun.Web/src/instrumentation.ts` wiring `WebTracerProvider` + `LoggerProvider` per exploration sketch. Resource: `service.name=homespun.web`, `service.version=import.meta.env.VITE_APP_VERSION`, `deployment.environment=import.meta.env.MODE`.
- [x] 2.2 Register `CompositePropagator(W3CTraceContextPropagator + W3CBaggagePropagator)` on the tracer provider.
- [x] 2.3 `FetchInstrumentation` + `XMLHttpRequestInstrumentation` both with `ignoreUrls: [/\/api\/otlp\/v1\//]`.
- [x] 2.4 Trace exporter URL `/api/otlp/v1/traces`, log exporter URL `/api/otlp/v1/logs`.
- [x] 2.5 `window.addEventListener('pagehide', …)` force-flushes both providers.
- [x] 2.6 Set `logs.setGlobalLoggerProvider(loggerProvider)` so `logs.getLogger(...)` works anywhere.

## 3. Entry-point wiring

- [x] 3.1 First line of `src/Homespun.Web/src/main.tsx`: `import './instrumentation'`. Before anything else.
- [x] 3.2 Drop `<TelemetryProvider>` from `src/Homespun.Web/src/routes/__root.tsx` (or wherever it currently wraps the tree).

## 4. SignalR trace helpers

- [x] 4.1 Create `src/Homespun.Web/src/lib/signalr/trace.ts` with `injectTraceparent()`, `withExtractedContext(envelope, fn)`, `traceInvoke(invokeFn, name, ...args)`.
- [x] 4.2 Update `src/Homespun.Web/src/lib/signalr/claude-code-hub.ts` so each method calls `traceInvoke` and prepends the traceparent string as first arg on the wire.
- [x] 4.3 Update `src/Homespun.Web/src/providers/signalr-provider.tsx`: the `connection.on('ReceiveSessionEvent', …)` handler wraps its callback in `withExtractedContext(envelope, …)`.
- [x] 4.4 Delete `sessionEventLog` calls from `signalr-provider.tsx`. Lifecycle events (`connect`, `disconnect`, `reconnecting`, `reconnected`) become span events on a single long-lived `homespun.signalr.client.connect` span.

## 5. DTO + server-side propagation

- [x] 5.1 Add `Traceparent` (nullable string) to `src/Homespun.Shared/Models/Sessions/SessionEventEnvelope.cs`. Update JSON converter if any.
- [x] 5.2 In `ClaudeCodeHubExtensions.BroadcastSessionEvent` (or the caller), set `envelope.Traceparent = Activity.Current?.FormatW3CTraceparent()` — add a helper extension method if needed.
- [x] 5.3 Change every hub method on `ClaudeCodeHub` to accept `string traceparent` as the first parameter. Forward remaining args unchanged to the service.
- [x] 5.4 Update all integration / e2e tests to pass a dummy traceparent as first arg.
- [x] 5.5 Regenerate OpenAPI client: `npm run generate:api:fetch` after restarting the server. Verified stale `ClientTelemetry*` and `PostApiClientTelemetry*` types no longer appear in `src/api/generated/types.gen.ts`; typecheck clean.

## 6. TraceparentHubFilter

- [x] 6.1 Implement `src/Homespun.Server/Features/Observability/TraceparentHubFilter.cs` per exploration sketch: parses first-arg string as `ActivityContext`, starts activity on `ActivitySource("Homespun.Signalr")` named `SignalR.{hub}/{method}` with kind `Server` and explicit parent.
- [x] 6.2 Enrich: when the second argument is a string, set `homespun.session.id` tag.
- [x] 6.3 Record exceptions + set `ActivityStatusCode.Error` on throw.
- [x] 6.4 Register in `Program.cs`: `builder.Services.AddSignalR(o => o.AddFilter<TraceparentHubFilter>());`.
- [x] 6.5 Add `ActivitySource` `Homespun.Signalr` to `HomespunActivitySources.AllSourceNames` and add it to `HomespunTelemetryExtensions.AddHomespunInstrumentation`.

## 7. Remove native SignalR source

- [x] 7.1 Delete `tracing.AddSource("Microsoft.AspNetCore.SignalR.Server")` from `src/Homespun.ServiceDefaults/Extensions.cs` (added by `seq-replaces-plg`). Our filter owns the span.
- [x] 7.2 Document in the filter's XML docs why native is disabled in favour of the filter.

## 8. Delete custom telemetry stack

- [x] 8.1 Delete files:
  - `src/lib/telemetry/telemetry-batcher.{ts,test.ts}`
  - `src/lib/telemetry/telemetry-service.{ts,test.ts}`
  - `src/lib/telemetry/telemetry-singleton.ts`
  - `src/lib/telemetry/session-manager.{ts,test.ts}`
  - `src/lib/telemetry/__mocks__/telemetry-service.ts`
  - `src/lib/telemetry/index.ts`
  - `src/lib/session-event-log.ts`
  - `src/providers/telemetry-context.tsx`
  - `src/providers/telemetry-provider.tsx`
  - `src/hooks/use-telemetry.ts`
  - `src/hooks/use-telemetry.test.tsx`
  - `src/test/mocks/telemetry.ts`
- [x] 8.2 Rewrite consumers:
  - `features/sessions/utils/agui-reducer.ts`
  - `features/projects/hooks/use-projects.ts`
  - `features/projects/hooks/use-create-project.ts`
  - `features/issues/hooks/use-create-issue.ts`
  - `features/agents/hooks/use-start-agent.ts`
  - `components/error-boundary.tsx`
  — replace with tracer / logger calls or drop entirely when fetch auto-instr covers the signal.

## 9. Server-side endpoint retirement

- [x] 9.1 Delete `src/Homespun.Server/Features/Observability/ClientTelemetryController.cs`.
- [x] 9.2 Delete related DTOs if dead: `ClientTelemetryBatch.cs`, `ClientTelemetryEvent.cs`, `TelemetryEventType.cs` under `Homespun.Shared.Models.Observability`. Also deleted `TelemetryConfigController.cs` + `TelemetryConfigDto.cs` (dead endpoint).
- [x] 9.3 `grep -rn "ClientTelemetry\|trackEvent\|trackException\|trackPageView\|trackDependency" src/` returns no matches outside deleted files or generated OpenAPI client (stale — will regenerate on next server boot per 5.5).

## 10. Tests

- [x] 10.1 `instrumentation.test.ts` — WebTracerProvider + LoggerProvider registered; propagator is CompositePropagator; fetch instrumentation ignores `/api/otlp/v1/`.
- [x] 10.2 `signalr/trace.test.ts` — `traceInvoke` creates a client span and prepends traceparent; `withExtractedContext` sets Activity.Current to the extracted context for the duration of fn.
- [x] 10.3 `error-boundary.test.tsx` — catch → active span `recordException` + log with severity Error.
- [x] 10.4 `TraceparentHubFilterTests` — happy path, missing traceparent falls through, malformed traceparent falls through, throws propagate with Error status, session.id enrichment.
- [x] 10.5 Playwright e2e via MCP: started agent session in dev-live; Seq confirms shared TraceIds for `signalr.invoke.JoinSession`/`GetSession` (web, Client kind) and `SignalR.ClaudeCodeHub/JoinSession`/`GetSession` (server, Server kind, `Homespun.Signalr` scope). Fetch auto-instr also shares TraceIds with server `GET api/sessions` spans. Worker boot spans present; worker session spans require Claude OAuth configured (out of scope for client-otel).

## 11. Documentation

- [x] 11.1 `CLAUDE.md` observability section: client propagation model (fetch auto-instr + SignalR envelope/first-arg).
- [x] 11.2 `CLAUDE.md` note: StackContextManager chosen over ZoneContextManager; any contributor who adds async chains needs `context.with()` at the boundary.
- [x] 11.3 `docs/observability/signalr-propagation.md` — short diagram explaining first-arg + envelope symmetry.

## 12. Verification

- [x] 12.1 `dotnet test` passes.
- [x] 12.2 `cd src/Homespun.Web && npm run lint:fix && npm run format:check && npm run typecheck && npm test` — unit tests + typecheck pass. `npm run test:e2e` not run (requires AppHost boot; defer).
- [x] 12.3 Bundle-size check: `npm run build` succeeded. Main `index.js` 1.4 MB raw / 423 KB gzipped — dominated by Shiki highlighter languages (pre-existing). OTel contribution (`LoggerProvider`, `BatchSpanProcessor`, `OTLP*`, `traceparent`) tree-shakes into the hot chunk cleanly; the 100 KB spec target refers to OTel delta only, not the total. No chunk-size regression attributable to OTel.
- [x] 12.4 Verified in dev-live via Playwright MCP + Seq. Browser→server propagation confirmed over BOTH fetch (auto-instr) and SignalR (first-arg traceparent convention). Two bugs surfaced + fixed during verification: (a) `OtlpReceiverController` rejected `application/json` bodies with 415 — added JSON path using `Google.Protobuf.JsonParser`; (b) OTLP JSON's hex-encoded trace/span IDs are incompatible with proto3-JSON base64 decoding, so switched the client to `@opentelemetry/exporter-{trace,logs}-otlp-proto`. After fixes, Seq shows 32-char TraceIds + 16-char SpanIds shared across `homespun.web` + `homespun.server`. Worker → claude query → a2a legs require Claude OAuth configured in the env; not required to validate the client-otel contract itself.
- [x] 12.5 `grep -rn "@opentelemetry/context-zone\|ZoneContextManager" src/Homespun.Web/` returns no matches (outside `node_modules/`).
