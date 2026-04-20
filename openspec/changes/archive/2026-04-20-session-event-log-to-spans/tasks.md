## 1. ActivitySource + gate scaffolding

- [x] 1.1 Add `Homespun.SessionPipeline` to `src/Homespun.Server/Features/Observability/HomespunActivitySources.cs`.
- [x] 1.2 Create `src/Homespun.Server/Features/Observability/SessionEventSpanNames.cs` with constants for every span name introduced (`homespun.session.ingest`, `homespun.agui.translate`, `homespun.signalr.connect`, `homespun.signalr.join`, `homespun.signalr.leave`).
- [x] 1.3 Create `src/Homespun.Server/Features/Observability/SessionEventContentOptions.cs` (renamed from `SessionEventLogOptions`). Keep `ContentPreviewChars` + `Enabled`.
- [x] 1.4 Create `IContentPreviewGate` + `ContentPreviewGate` — `string? Gate(string? text)` truncates/nulls per options. Unit test parity with the prior `TruncatePreview` behaviour.
- [x] 1.5 `Program.cs`: bind `SessionEventContentOptions` from section `SessionEventContent` with alias read of `SessionEventLog` for one release.

## 2. Server — migrate SessionEventIngestor

- [x] 2.1 Rewrite `IngestAsync` (or equivalent) in `SessionEventIngestor.cs`:
  - Start `using var ingestSpan = _activitySource.StartActivity(SessionEventSpanNames.Ingest, ActivityKind.Consumer)`.
  - `ingestSpan?.SetTag("homespun.session.id", sessionId)` + correlation attrs (`homespun.a2a.kind`, `homespun.seq`, `homespun.task.id`, `homespun.message.id`, `homespun.artifact.id`).
  - `ingestSpan?.AddEvent(new ActivityEvent("sse.rx"))` before parse.
  - `await store.AppendAsync(...)`.
  - `ingestSpan?.AddEvent(new ActivityEvent("ingest.append"))`.
  - `using (var translateSpan = _activitySource.StartActivity(SessionEventSpanNames.Translate, ActivityKind.Internal))` — translate inside.
  - `envelope.Traceparent = FormatTraceparent(Activity.Current)` — helper that formats as W3C string.
  - `ingestSpan?.AddEvent(new ActivityEvent("signalr.tx"))` after broadcast.
- [x] 2.2 Gate `homespun.content.preview` tag via `IContentPreviewGate` before calling `SetTag`.
- [x] 2.3 Delete every `SessionEventLog.LogA2AHop` / `LogAGUIHop` call in this file.

## 3. Server — migrate ClaudeCodeHub lifecycle

- [x] 3.1 `OnConnectedAsync`: start a long-lived activity on `Homespun.Signalr` named `homespun.signalr.connect` kept in `Context.Items`. Store its reference so `OnDisconnectedAsync` can stop it. Add span event `connected`.
- [x] 3.2 `OnDisconnectedAsync`: retrieve the connect activity from `Context.Items`; add span event `disconnected` with `reason` tag when exception present; stop the activity.
- [x] 3.3 `JoinSession`: start `homespun.signalr.join` activity; tag `homespun.session.id`, `signalr.connection.id`; stop on completion.
- [x] 3.4 `LeaveSession`: analogously for `homespun.signalr.leave`.
- [x] 3.5 Delete every `SessionEventLog.LogHubHop` call.

## 4. Server — delete hop infrastructure

- [x] 4.1 Delete `src/Homespun.Server/Features/Observability/SessionEventLog.cs`.
- [x] 4.2 Delete `src/Homespun.Shared/Models/Observability/SessionEventLogEntry.cs`.
- [x] 4.3 Delete `src/Homespun.Shared/Models/Observability/SessionEventHops.cs` (folded into `SessionEventLogEntry.cs` and removed with it; span-name constants live in `SessionEventSpanNames`).
- [x] 4.4 Delete `src/Homespun.Server/Features/Observability/SessionEventLogOptions.cs` (replaced by `SessionEventContentOptions`).
- [x] 4.5 `grep -rn "SessionEventLog\|SessionEventHops\|SessionEventLogEntry\|SessionEventLogOptions" src/` returns no matches outside the new `SessionEventContentOptions` alias constant, the deprecation comment in `Program.cs`, and the worker `SessionEventLogFields` attribute-shape type.

## 5. Worker — migrate hops

- [x] 5.1 Rewrite `src/Homespun.Worker/src/services/sse-writer.ts`: `sessionEventLog(SessionEventHop.WorkerA2AEmit, fields)` → `workerTracer().startSpan('homespun.a2a.emit', { kind: SpanKind.PRODUCER })` with `span.setAttributes(mapFieldsToAttributes(fields))`.
- [x] 5.2 Apply to every call site: `sse-writer.ts` is the only site (grep confirms `session-manager.ts`, `session-inventory.ts`, `openspec-snapshot.ts`, routes have no residue).
- [x] 5.3 Delete `sessionEventLog`, `SessionEventHop` from `src/Homespun.Worker/src/utils/otel-logger.ts`. `SessionEventLogFields`, `extractA2ACorrelation`, `extractMessagePreview` retained as the attribute-shape helper reused by the span call site.
- [x] 5.4 Worker `CONTENT_PREVIEW_CHARS` logic relocated to the in-span helper `gateContentPreview(text)`.

## 6. Client — migrate hub-lifecycle hops

- [x] 6.1 `src/Homespun.Web/src/providers/signalr-provider.tsx`: already uses a long-lived `homespun.signalr.client.connect` span with lifecycle `signalr.<status>` events — installed by sibling `client-otel` change.
- [x] 6.2 No residual `sessionEventLog` imports / calls in `src/Homespun.Web` (verified via grep).
- [x] 6.3 `src/Homespun.Web/src/lib/session-event-log.ts` already deleted by `client-otel`.

## 7. Config migration

- [x] 7.1 `appsettings.Development.json` renamed section to `SessionEventContent`. `Program.cs` reads both `SessionEventContent` (primary) and `SessionEventLog` (legacy fallback) with a deprecation warning at startup when only the legacy section is present.
- [x] 7.2 `docs/observability/otlp-proxy.md` + `docs/session-events.md`: `SessionEventLog:ContentPreviewChars` → `SessionEventContent:ContentPreviewChars`. Old key works for one release.

## 8. Dictionary update

- [x] 8.1 `docs/traces/dictionary.md` entries added for `homespun.session.ingest` (+ events `sse.rx`, `ingest.append`, `signalr.tx`), `homespun.agui.translate`, `homespun.signalr.connect` (+ `connected` / `disconnected`), `homespun.signalr.join`, `homespun.signalr.leave`, worker `homespun.a2a.emit`. Client `homespun.signalr.client.connect` was already documented by `client-otel`.
- [x] 8.2 Drift check passes (`TraceDictionaryTests`).

## 9. Tests

- [x] 9.1 Rewrote `tests/Homespun.Tests/Features/ClaudeCode/SessionEventIngestorTests.cs` around span emissions. An `ActivityListener` on `Homespun.SessionPipeline` captures every span. Asserts: one `homespun.session.ingest` span per ingested A2A event with three span events in order (`sse.rx`, `ingest.append`, `signalr.tx`), and one child `homespun.agui.translate` span per event.
- [x] 9.2 Added `ContentPreviewGateTests` with parity cases against the old `TruncatePreview`.
- [x] 9.3 Deleted `tests/Homespun.Tests/Features/Observability/SessionEventLogTests.cs`.
- [x] 9.4 Worker: `tests/Homespun.Worker/services/sse-writer.test.ts` asserts `homespun.a2a.emit` span emission with attribute shape matching the old `sessionEventLog` fields.
- [x] 9.5 Client: `src/Homespun.Web/src/providers/signalr-provider.test.tsx` asserts connect/disconnect produces exactly one long-lived span with the expected events.

## 10. Spec cleanup

- [x] 10.1 Updated `openspec/specs/session-messaging/spec.md` and `openspec/specs/observability/spec.md` to reference span events / the new config key instead of hop-log entries wherever hops are mentioned.

## 11. Verification

- [x] 11.1 `dotnet test` — Homespun.Tests 1734 passed, Homespun.Api.Tests 212 passed, Homespun.AppHost.Tests 12 passed. One pre-existing failure in `DockerAgentExecutionServiceLiveTests` (missing `homespun-worker:local` image) unrelated to this change.
- [x] 11.2 `npm run typecheck` + `npm run lint:fix` + `npm test` in `src/Homespun.Web` — 1919 tests passed.
- [x] 11.3 `npm run build` + `npm test` in `src/Homespun.Worker` — 237 passed. Two pre-existing `session-inventory` Windows-path failures untouched by this change.
- [x] 11.4 Booted `dev-windows` (SingleContainer), drove a session end-to-end through `/sessions/{id}` with a `say hello` prompt. Seq confirmed a single connected trace `f21fbc258fb340dc02c3a869aa852e9d` containing: SignalR.ClaudeCodeHub/SendMessage (server) → N × `homespun.session.ingest` (server, Consumer, `Homespun.SessionPipeline`) with ordered span events `sse.rx` / `ingest.append` / `signalr.tx` → N × `homespun.agui.translate` (server, Internal, child of ingest) → N × `homespun.envelope.rx` (client, Consumer, `homespun.web.session-events`, parented to ingest via envelope traceparent) → N × `homespun.client.reducer.apply` (client). Separate `homespun.signalr.connect` (Server, long-lived) + `homespun.signalr.join` (Server) spans emit on `Homespun.Signalr`. Worker `homespun.a2a.emit` spans not exported in `dev-windows` because the pre-run `SingleContainer` worker container isn't handed `OTLP_PROXY_URL` (pre-existing AppHost limitation, not this change); attribute shape covered by worker tests.
- [x] 11.5 `grep -rn "SessionEventLog\|sessionEventLog\|SessionEventHop\|Hop" src/ openspec/specs/` returns only the fallback alias constant, deprecation comments, and the reused `SessionEventLogFields` attribute-shape type in the worker — no live hop-log code paths remain.
