## Why

`SessionEventLog` exists (in `Homespun.Server/Features/Observability/SessionEventLog.cs` + mirrors in worker + client) because before OTel was wired end-to-end, the only way to correlate a session's pipeline across tiers was to write JSON log lines with a shared `SessionId` and a `Hop` field naming the stage. It was a poor man's distributed trace.

With `seq-replaces-plg`, `server-otlp-proxy`, `worker-otel`, `client-otel`, and `client-otel`'s traceparent propagation landed, every pipeline hop has real trace context. The `Hop` string is redundant with the span name; `SessionId` lives on the span's resource/attribute set. Retiring the hand-rolled hop logger in favour of spans + span events removes ~600 lines of bespoke plumbing and lets Seq's trace-view render the pipeline visually.

This change is the last scoped migration in the series — it depends on the four sibling changes being landed first.

## What Changes

- **Rewrite `SessionEventIngestor.cs`** to start a `homespun.session.ingest` span on `Homespun.SessionPipeline` ActivitySource (new) per ingested A2A event. Hops become span events on that span:
  - `sse.rx` event when SSE parses the event.
  - `ingest.append` event after `A2AEventStore.AppendAsync`.
  - `signalr.tx` event after broadcast.
  - A child span `homespun.agui.translate` wraps the translator call (real work → real span).
- **Populate `SessionEventEnvelope.Traceparent`** from the ingest span's context before broadcast. (Sibling change `client-otel` added the DTO field; this change sets it.)
- **Rewrite `ClaudeCodeHub.OnConnected` / `OnDisconnected` / `JoinSession` / `LeaveSession`** hops as span events on a `homespun.signalr.connect` span (per connection) or standalone `homespun.signalr.{join,leave}` spans.
- **Rewrite worker `sessionEventLog('worker.a2a.emit', …)` calls** as `tracer.startActiveSpan('homespun.a2a.emit', …)` with the same correlation attrs as the current hop fields.
- **Rewrite client `sessionEventLog` call sites** — client hub-lifecycle hops (`client.signalr.{connect,disconnect,reconnecting,reconnected}`) become span events on a long-lived `homespun.signalr.client.connect` span. `client.envelope.rx` becomes a span under the envelope's `Traceparent`.
- **Delete the entire hop-log infrastructure:**
  - Server: `SessionEventLog.cs`, `SessionEventLogOptions.cs` (retain `ContentPreviewChars` + `Enabled` flags relocated to a new `SessionEventContentOptions.cs`), `SessionEventHops.cs`, `SessionEventLogEntry.cs` (and the shared `ClientLogEntry.cs` was already deleted in `worker-otel`).
  - Worker: `sessionEventLog` function + `SessionEventHop` constants in `src/Homespun.Worker/src/utils/` (worker-otel left placeholder compile; this change removes entirely).
  - Client: `src/Homespun.Web/src/lib/session-event-log.ts` (already deleted in `client-otel`).
- **Move PII content-preview gating** from `SessionEventLog.TruncatePreview` to the span-setting call sites using a new `IContentPreviewGate` service (server) + its mirrors. Config key `SessionEventLog:ContentPreviewChars` renamed to `SessionEventContent:ContentPreviewChars` — provide a transition alias that reads both paths for one release.
- **Update the trace dictionary** (`docs/traces/dictionary.md`) so every new span added by this change has a section.

## Capabilities

### Modified Capabilities
- `observability` — retires the hop dictionary and replaces it with span-based correlation.
- `session-messaging` — the existing spec that references "Hop" fields must be updated to reference span events.

## Impact

- **Files touched:**
  - `src/Homespun.Server/Features/Observability/SessionEventLog.cs` — DELETE.
  - `src/Homespun.Server/Features/Observability/SessionEventLogOptions.cs` — RENAME to `SessionEventContentOptions.cs`; trim to `ContentPreviewChars` + `Enabled`.
  - `src/Homespun.Shared/Models/Observability/SessionEventLogEntry.cs` — DELETE.
  - `src/Homespun.Shared/Models/Observability/SessionEventHops.cs` — DELETE; replace with `SessionEventSpanNames.cs` holding span-name constants.
  - `src/Homespun.Server/Features/ClaudeCode/Services/SessionEventIngestor.cs` — rewrite to spans + events.
  - `src/Homespun.Server/Features/ClaudeCode/Services/A2AEventStore.cs` — remove hop-log calls.
  - `src/Homespun.Server/Features/ClaudeCode/Hubs/ClaudeCodeHub.cs` — hub-lifecycle hops → spans.
  - `src/Homespun.Server/Features/Observability/HomespunActivitySources.cs` — +`SessionPipeline` source.
  - `src/Homespun.Server/appsettings.Development.json` + `appsettings.json` — config key rename with alias.
  - `src/Homespun.Worker/src/utils/logger.ts` — already reduced by `worker-otel`; this change deletes remaining `sessionEventLog` + `SessionEventHop` exports.
  - `src/Homespun.Worker/src/services/session-manager.ts` — `sessionEventLog('worker.a2a.emit')` → `tracer.startActiveSpan('homespun.a2a.emit')`.
  - `src/Homespun.Worker/src/services/sse-writer.ts` — analogously.
  - `src/Homespun.Web/src/providers/signalr-provider.tsx` — hub-lifecycle hops → span events on `homespun.signalr.client.connect`.
  - `src/Homespun.Web/src/features/sessions/utils/agui-reducer.ts` — `client.envelope.rx` hop call site already migrated in `client-otel`; verify no residue.
  - `docs/traces/dictionary.md` — add + enrich sections for all new spans.
  - `tests/Homespun.Tests/Features/Observability/SessionEventLogTests.cs` — DELETE.
  - `tests/Homespun.Tests/Features/ClaudeCode/SessionEventIngestorTests.cs` — rewrite around span emissions.
  - `openspec/specs/session-messaging/spec.md` — update requirements mentioning hops.

- **Dependencies:** none new.

- **Risk surface:**
  - Loss of a grep-friendly log format. Seq's trace view replaces it but engineers must learn the new query shape. Mitigation: doc examples in CLAUDE.md `/logs` skill.
  - `ContentPreviewChars` behaviour must round-trip through the new gate identically — test parity explicitly.
  - Span events have no built-in retention policy beyond span retention. If Seq retains spans for 7 days (free tier), hop history shortens from unlimited (current Loki config) to 7 days. User explicitly accepted the free-tier limits.

- **Rollback:** revert. Whole hop infrastructure returns along with the code paths retired here. No data migration needed — hop logs historically landed in Loki which is already gone.

## Ordering

Depends on (must land first, in any order):
- `seq-replaces-plg`
- `server-otlp-proxy`
- `worker-otel`
- `client-otel`
- `trace-dictionary` (provides the enforcement that catches new span additions)

This change is the last of the six-change stack and closes the migration.

## Trace dictionary

Every new span introduced here (`homespun.session.ingest`,
`homespun.agui.translate`, `homespun.signalr.join`, `homespun.signalr.leave`,
`homespun.a2a.emit`, …) MUST land with a matching H3 entry in
[`docs/traces/dictionary.md`](../../../docs/traces/dictionary.md) under the
originator tier's section. Per-tier drift checks block merge otherwise —
see the `trace-dictionary` change.
