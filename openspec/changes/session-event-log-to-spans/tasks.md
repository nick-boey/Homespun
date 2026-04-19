## 1. ActivitySource + gate scaffolding

- [ ] 1.1 Add `Homespun.SessionPipeline` to `src/Homespun.Server/Features/Observability/HomespunActivitySources.cs`.
- [ ] 1.2 Create `src/Homespun.Server/Features/Observability/SessionEventSpanNames.cs` with constants for every span name introduced (`homespun.session.ingest`, `homespun.agui.translate`, `homespun.signalr.connect`, `homespun.signalr.join`, `homespun.signalr.leave`).
- [ ] 1.3 Create `src/Homespun.Server/Features/Observability/SessionEventContentOptions.cs` (renamed from `SessionEventLogOptions`). Keep `ContentPreviewChars` + `Enabled`.
- [ ] 1.4 Create `IContentPreviewGate` + `ContentPreviewGate` — `string? Gate(string? text)` truncates/nulls per options. Unit test parity with the prior `TruncatePreview` behaviour.
- [ ] 1.5 `Program.cs`: bind `SessionEventContentOptions` from section `SessionEventContent` with alias read of `SessionEventLog` for one release.

## 2. Server — migrate SessionEventIngestor

- [ ] 2.1 Rewrite `IngestAsync` (or equivalent) in `SessionEventIngestor.cs`:
  - Start `using var ingestSpan = _activitySource.StartActivity(SessionEventSpanNames.Ingest, ActivityKind.Consumer)`.
  - `ingestSpan?.SetTag("homespun.session.id", sessionId)` + correlation attrs (`homespun.a2a.kind`, `homespun.seq`, `homespun.task.id`, `homespun.message.id`, `homespun.artifact.id`).
  - `ingestSpan?.AddEvent(new ActivityEvent("sse.rx"))` before parse.
  - `await store.AppendAsync(...)`.
  - `ingestSpan?.AddEvent(new ActivityEvent("ingest.append"))`.
  - `using (var translateSpan = _activitySource.StartActivity(SessionEventSpanNames.Translate, ActivityKind.Internal))` — translate inside.
  - `envelope.Traceparent = FormatTraceparent(Activity.Current)` — helper that formats as W3C string.
  - `ingestSpan?.AddEvent(new ActivityEvent("signalr.tx"))` after broadcast.
- [ ] 2.2 Gate `homespun.content.preview` tag via `IContentPreviewGate` before calling `SetTag`.
- [ ] 2.3 Delete every `SessionEventLog.LogA2AHop` / `LogAGUIHop` call in this file.

## 3. Server — migrate ClaudeCodeHub lifecycle

- [ ] 3.1 `OnConnectedAsync`: start a long-lived activity on `Homespun.Signalr` named `homespun.signalr.connect` kept in `Context.Items`. Store its reference so `OnDisconnectedAsync` can stop it. Add span event `connected`.
- [ ] 3.2 `OnDisconnectedAsync`: retrieve the connect activity from `Context.Items`; add span event `disconnected` with `reason` tag when exception present; stop the activity.
- [ ] 3.3 `JoinSession`: start `homespun.signalr.join` activity; tag `homespun.session.id`, `signalr.connection.id`; stop on completion.
- [ ] 3.4 `LeaveSession`: analogously for `homespun.signalr.leave`.
- [ ] 3.5 Delete every `SessionEventLog.LogHubHop` call.

## 4. Server — delete hop infrastructure

- [ ] 4.1 Delete `src/Homespun.Server/Features/Observability/SessionEventLog.cs`.
- [ ] 4.2 Delete `src/Homespun.Shared/Models/Observability/SessionEventLogEntry.cs`.
- [ ] 4.3 Delete `src/Homespun.Shared/Models/Observability/SessionEventHops.cs` (keep only the constants we need in `SessionEventSpanNames`).
- [ ] 4.4 Delete `src/Homespun.Server/Features/Observability/SessionEventLogOptions.cs` (replaced by `SessionEventContentOptions`).
- [ ] 4.5 `grep -rn "SessionEventLog\|SessionEventHops\|SessionEventLogEntry\|SessionEventLogOptions" src/` returns no matches outside the new `SessionEventSpanNames`.

## 5. Worker — migrate hops

- [ ] 5.1 Rewrite `src/Homespun.Worker/src/services/session-manager.ts`: `sessionEventLog(SessionEventHop.WorkerA2AEmit, fields)` → `tracer.startActiveSpan('homespun.a2a.emit', {kind: SpanKind.PRODUCER}, span => { span.setAttributes(mapFields(fields)); span.end() })`.
- [ ] 5.2 Apply to every call site: `sse-writer.ts`, `session-inventory.ts`, `openspec-snapshot.ts` (if any residue), route handlers.
- [ ] 5.3 Delete `sessionEventLog`, `SessionEventHop`, `extractA2ACorrelation`, `extractMessagePreview`, `SessionEventLogEntry`, `SessionEventLogFields` types + interfaces from `src/Homespun.Worker/src/utils/logger.ts`. That file should now only export `info`, `warn`, `error` (already migrated by worker-otel to OTel emit) OR be empty — delete in that case.
- [ ] 5.4 Worker `CONTENT_PREVIEW_CHARS` logic relocates to an in-span helper `gateContentPreview(text)`.

## 6. Client — migrate hub-lifecycle hops

- [ ] 6.1 `src/Homespun.Web/src/providers/signalr-provider.tsx`: replace `logClaudeCodeStatus(...)` `sessionEventLog` calls with span events on a long-lived `homespun.signalr.client.connect` span started on connect and ended on disconnect.
- [ ] 6.2 Any residual `sessionEventLog` imports / calls — delete.
- [ ] 6.3 Verify `src/Homespun.Web/src/lib/session-event-log.ts` file is already deleted by `client-otel`; nothing to do if so.

## 7. Config migration

- [ ] 7.1 `appsettings.json` + `appsettings.Development.json`: add `SessionEventContent` section matching the prior `SessionEventLog` shape. Retain the old section reading as fallback in `Program.cs` with a deprecation warning log on boot when present.
- [ ] 7.2 `CLAUDE.md` notes: `SessionEventLog:ContentPreviewChars` → `SessionEventContent:ContentPreviewChars`. Old key works for one release.

## 8. Dictionary update

- [ ] 8.1 Update `docs/traces/dictionary.md` entries for every new / renamed span: `homespun.session.ingest` (+ events), `homespun.agui.translate`, `homespun.signalr.connect` (+ events), `homespun.signalr.join`, `homespun.signalr.leave`, client `homespun.signalr.client.connect`, worker `homespun.a2a.emit`.
- [ ] 8.2 Drift check passes.

## 9. Tests

- [ ] 9.1 Rewrite `tests/Homespun.Tests/Features/ClaudeCode/SessionEventIngestorTests.cs` around span emissions using an `InMemoryActivityProcessor` or equivalent. Assert: one `homespun.session.ingest` span per ingested A2A event, three span events (`sse.rx`, `ingest.append`, `signalr.tx`), one child `homespun.agui.translate` span per event.
- [ ] 9.2 Add `ContentPreviewGateTests` with parity cases against the old `TruncatePreview`.
- [ ] 9.3 Delete `tests/Homespun.Tests/Features/Observability/SessionEventLogTests.cs`.
- [ ] 9.4 Worker: `tests/session-manager.test.ts` asserts `homespun.a2a.emit` span emission with attribute shape matching the old `sessionEventLog` fields.
- [ ] 9.5 Client: `tests/signalr-provider.test.tsx` asserts connect/disconnect produces exactly one long-lived span with the expected events.

## 10. Spec cleanup

- [ ] 10.1 Update `openspec/specs/session-messaging/spec.md` to reference span events instead of hop-log entries wherever hops are mentioned.

## 11. Verification

- [ ] 11.1 `dotnet test` passes.
- [ ] 11.2 `cd src/Homespun.Web && npm run lint:fix && npm run typecheck && npm test && npm run test:e2e` pass.
- [ ] 11.3 `cd src/Homespun.Worker && npm run build && npm test` pass.
- [ ] 11.4 Boot dev-live, drive a session end-to-end. Seq trace view shows one connected trace per user action containing: client user span → http.server → SignalR.ClaudeCodeHub/… → homespun.session.start → docker.spawn → worker session.init → claude.query → N × a2a.emit (parent: query) → N × homespun.session.ingest (parent: inbound SSE) → N × envelope.rx (client, parent: ingest).
- [ ] 11.5 `grep -rn "SessionEventLog\|sessionEventLog\|SessionEventHop\|Hop" src/ openspec/specs/` returns no matches in active source / specs (archived OpenSpec content excluded).
