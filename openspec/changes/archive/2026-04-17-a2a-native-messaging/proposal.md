## Why

The worker → server → client message pipeline today passes through five different formats (native SDK → A2A → SDK-again → `ClaudeMessage` → AG-UI) and has two separate paths reaching the client: SignalR for live streaming, and a REST cache endpoint that returns a *different* shape for refresh. The live path broadcasts per-block AG-UI events; the refresh path returns coarser `ClaudeMessage` records. System/hook messages are lost on both paths, and the server-side assembly in `MessageProcessingService.ProcessAssistantMessageAsync` has ordering bugs that drop content from multi-block turns.

Symptoms the user hits today:
- After refresh, tool invocations persist but the final assistant message is often missing.
- SessionStart hook content, system messages, and other worker-observable events never reach the client.
- Live streaming and refresh rendering produce visibly different message trees.

The stated architecture principle — *"the worker is the translation boundary; the server and downstream must not know about the native SDK format"* — is violated at least twice (server re-parses A2A into SDK, then flattens into `ClaudeMessage`). That also defeats the swap-the-worker goal that A2A was adopted for.

This change makes the worker the *only* translation boundary, stores A2A verbatim on the server, and gives the client a single event stream that is identical whether received live or replayed on refresh.

## What Changes

- **BREAKING**: Worker emits A2A events as the only wire format. No server-side re-parse to SDK shapes.
- **BREAKING**: Server stores raw A2A events as append-only JSONL with a per-session monotonic `seq` number per event. Existing `ClaudeMessage` JSONL cache is deleted on upgrade (no migration — dev tool, single-user).
- **BREAKING**: Server exposes a single replay endpoint `GET /api/sessions/{id}/events?since=N&mode=full|incremental` returning AG-UI events. Existing `GET /api/sessions/{id}/cache/messages` and the `ClaudeMessage` response shape are removed.
- **BREAKING**: Delete `ClaudeMessage`, `ClaudeMessageContent`, `ClaudeMessageRole`, and the signalr-message-adapter from the shared contracts and client. Client state is built directly from AG-UI events.
- Server-side A2A → AG-UI translator converts each stored A2A event to an AG-UI event; same translator is used for live broadcast and replay so live ≡ refresh.
- AG-UI event coverage: use `Custom` events (spec-conformant) for non-native concerns — `thinking`, `hook.started`, `hook.response`, `system.init`, `question.pending`, `plan.pending`, `status.resumed`, `workflow.complete`. Canonical AG-UI events (`TextMessageStart/Content/End`, `ToolCallStart/Args/End/Result`, `RunStarted/Finished/Error`) are used where they fit.
- Every AG-UI event broadcast via SignalR carries a `SessionEventEnvelope { seq, sessionId, eventId, event }` so clients can track `lastSeenSeq` and request replay from that point.
- Server-side config `SessionEvents:ReplayMode = Incremental | Full` is the default replay mode; clients may override per-request via `?mode=`. Client always deduplicates inbound envelopes by `eventId` as a safety net.
- Append-to-disk completes before SignalR broadcast for each event — no refresh ever sees a live event that is not yet in the replay log.
- **BREAKING**: Remove server-side SDK shape ingestion from the live path — delete `A2AMessageParser.ConvertToSdkMessage`, `SdkMessageParser` (server copy), `ContentBlockAssembler`, `ProcessStreamEventAsync`, `ProcessAssistantMessageAsync` assembly logic, and the entire `HasCachedCurrentMessage` / `CurrentAssistantMessage` scheme.
- `includePartialMessages` remains `false` in the worker; the streaming-delta code paths on the server (which never fired in practice) are deleted rather than ported.
- Hub shape: the many typed `BroadcastAGUI*` methods collapse to a single `ReceiveSessionEvent(sessionId, SessionEventEnvelope)` plus a small number of lifecycle methods (`SessionStatusChanged`, etc. stay).

## Capabilities

### New Capabilities
- `session-messaging`: A2A-native event store, replay-from-seq endpoint, live-equals-refresh AG-UI event stream, client-side dedup by eventId, configurable replay mode.

### Modified Capabilities
- `claude-agent-sessions`: Requirements for AG-UI event streaming, session resume, and final-message persistence are rewritten to describe the A2A-native pipeline. Observable session lifecycle states are unchanged; the event *contents* the client observes, and the cache shape underlying resume, are replaced.

## Impact

- **Worker**: `src/services/a2a-translator.ts` unchanged; `sse-writer.ts` gains an explicit envelope (`eventId`, stable IDs) and clarifies ordering (status → message → status on turn). Net: small.
- **Server — new**: `Features/ClaudeCode/Services/A2AEventStore.cs` (append-only JSONL + replay reader), `A2AToAGUITranslator.cs`, `SessionEventsController.cs` (`GET /events`), `ClaudeCodeHub.ReceiveSessionEvent`. New shared types `SessionEventEnvelope`, `AGUIEvent` (wrapping A2A's Custom-event catalog).
- **Server — deleted**: `MessageCacheStore.cs`, `SessionCacheController.cs`, `A2AMessageParser.ConvertToSdkMessage` (keep parsing for receive path), `SdkMessageParser.cs`, `MessageProcessingService.cs` (assembly logic — keeps only turn lifecycle state), `ContentBlockAssembler`, `ClaudeMessage*.cs` in `Homespun.Shared`. Approx 1–2 KLOC net deletion.
- **Client — new**: `features/sessions/hooks/use-session-events.ts` (single reducer for live + replay), `features/sessions/utils/agui-reducer.ts` (AG-UI → render state). `lastSeenSeq` tracked in store.
- **Client — deleted**: `use-historical-session-messages.ts`, `signalr-message-adapter.ts`, all references to `ClaudeMessage` / `ClaudeMessageContent` in the web app.
- **Shared contracts**: `ClaudeMessage*` deleted; `SessionEventEnvelope`, `AGUIEvent*` DTOs added.
- **Deployment**: First run after upgrade deletes `~/.homespun/sessions/*.jsonl` (or wherever the cache lives in docker-compose); documented in the PR description. No schema migration.
- **Dependencies on this change**: `multi-user-postgres` (reshapes the cache table to store A2A envelopes, not `ClaudeMessage`) and the in-flight `claude-agent-sessions` documentation spec (its AG-UI streaming + resume requirements are rewritten here). Both noted in their proposals.
- **Testing**: New unit tests for `A2AToAGUITranslator` (one per A2A event variant → expected AG-UI envelope), `A2AEventStore` (append + replay-from-seq), `/events` endpoint (incremental + full modes, `since` beyond end, `since` of a deleted/never-seen session), and a refresh-fidelity integration test asserting live-captured events ≡ replay-captured events for a canned session. `SdkMessageParser` tests deleted with the code.
- **Risk**: `ClaudeMessage` has wide reach in the client; grep scope is large but shallow. Mitigation: delete it early, let the TypeScript compiler walk us through every consumer.
