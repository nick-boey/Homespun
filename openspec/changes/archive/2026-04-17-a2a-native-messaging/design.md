## Context

The current Homespun messaging pipeline has drifted from its stated architecture: the worker was introduced as the translation boundary between the Claude Agent SDK and the rest of the system, using the A2A protocol so that any A2A-speaking worker could be swapped in. In practice the server re-parses A2A back into SDK-shaped types (`A2AMessageParser.ConvertToSdkMessage` → `SdkMessage` → `ClaudeMessage`) and downstream consumers (cache, SignalR AG-UI broadcast) see formats that are neither A2A nor SDK. Two independent paths reach the client — SignalR AG-UI events for live, REST `ClaudeMessage` JSON for refresh — and they carry different data with different fidelity. The refresh path loses some assistant-message content due to assembly bugs in `MessageProcessingService`; both paths silently drop hook/system messages.

Relevant current-state pointers:
- `src/Homespun.Worker/src/services/a2a-translator.ts` — SDK → A2A (correct; stays).
- `src/Homespun.Worker/src/services/sse-writer.ts` — emits A2A SSE to the server.
- `src/Homespun.Server/Features/ClaudeCode/Services/A2AMessageParser.cs:101` — `ConvertToSdkMessage`; the offending re-parse.
- `src/Homespun.Server/Features/ClaudeCode/Services/MessageProcessingService.cs:474` — `if (CurrentAssistantMessage.Content.Count == 0 && ...)` guard that blocks second-and-later assistant messages of a turn from being cached.
- `src/Homespun.Server/Features/ClaudeCode/Services/MessageCacheStore.cs` — per-session JSONL of `ClaudeMessage`.
- `src/Homespun.Server/Features/ClaudeCode/Controllers/SessionCacheController.cs` — `GET /api/sessions/{id}/cache/messages` for refresh replay.
- `src/Homespun.Web/src/features/sessions/hooks/use-session-messages.ts` — live AG-UI reducer.
- `src/Homespun.Web/src/features/sessions/hooks/use-historical-session-messages.ts` — refresh REST path (separate shape).

Worker runs with `includePartialMessages: false`, so no token-by-token streaming events reach the server. The server's content-block assembler and stream-event processing paths are effectively dead code; they are deleted rather than carried forward.

## Goals / Non-Goals

**Goals:**
- Worker is the *only* place in the system that speaks the native SDK format.
- Server ingests, stores, and replays A2A events verbatim.
- Server translates A2A → AG-UI once, using one translator for live and replay.
- Client observes one event stream shape (`SessionEventEnvelope { seq, sessionId, eventId, event }`), identical live and on refresh.
- On refresh, client resumes from `lastSeenSeq` and receives only the events it has not already observed; a server or client toggle flips to full replay if incremental replay misbehaves.
- All worker-observable events (assistant text, thinking, tool use/result, hooks, system init, control events, result) survive to the client under perfect fidelity.
- The `ClaudeMessage*` shared types and their downstream consumers are fully deleted.

**Non-Goals:**
- Token-by-token streaming. `includePartialMessages` stays `false`; AG-UI `TextMessageContent` events carry whole text blocks.
- Cross-session event ordering. Sequence numbers are per-session.
- Multi-user or multi-instance cache coordination. That falls to `multi-user-postgres`.
- Swapping SignalR for a different transport. SignalR remains the live channel; AG-UI envelopes flow over it.
- Redesigning the worker's A2A translator (`a2a-translator.ts`). Only minor envelope additions for stable IDs.
- Changing session lifecycle states (`Starting`, `Running`, `WaitingForInput`, etc.) — only the event *content* flowing through the lifecycle changes.

## Decisions

### Worker is the only SDK → A2A boundary

After this change the server does not have any type, method, or field that mentions "SdkMessage", "ClaudeMessage", or anything equivalent. The A2A event shape from the worker is the server's ingestion contract; the AG-UI envelope is the client's contract. Swapping a non-Claude-Code worker is a pure worker-side concern.

**Why:** This is the stated principle behind introducing the A2A protocol layer. Every deviation has produced either dead code (the stream-event path), lossy cache (multi-block turns), or divergence between live and refresh. Reasserting the boundary removes all three in one pass.

**Alternatives considered:**
- *Keep the server-side SDK shape as an internal representation and translate on egress.* Rejected — it's exactly the current design and is the source of the drift and bugs.
- *Translate A2A → AG-UI on the worker and have the server only forward.* Rejected — the server has policy concerns (cache, fan-out, lifecycle) that AG-UI events can't express cleanly, and making the worker aware of "AG-UI vs A2A" couples it to the client UI protocol.

### A2A event envelope with per-session monotonic `seq`

Each event the server receives is wrapped in:

```csharp
public record A2AEventRecord(
    long Seq,           // per-session, 1-based, strictly monotonic
    string SessionId,
    string EventId,     // UUID, stable per event — survives replay
    DateTime ReceivedAt,
    A2AEvent Event);    // the raw A2A payload (Task | Message | StatusUpdate | ArtifactUpdate)
```

Stored one-per-line in JSONL under `{baseDir}/{projectId}/{sessionId}.events.jsonl`. `seq` is the line number + 1 (explicit in the record rather than implicit in file position, so renames/compaction don't break replay semantics).

The wire representation toward the client — AG-UI envelope — is:

```csharp
public record SessionEventEnvelope(
    long Seq,
    string SessionId,
    string EventId,
    AGUIEvent Event);
```

`EventId` is the same UUID across A2A storage and AG-UI broadcast, letting the client deduplicate regardless of whether an event arrived live or via replay.

**Why:** Per-session monotonic `seq` is the simplest possible replay primitive. The client needs only to remember `lastSeenSeq` and request `?since=N`. No cross-session ordering is ever required. UUIDs handle the dedup edge case (e.g., a live event arrives while the client is mid-replay request).

**Alternatives considered:**
- *ULIDs as the sole identifier.* Rejected — would require a range index for replay; `seq` is zero-cost because it's the line number.
- *Client computes seq from wall-clock timestamps.* Rejected — clock skew, out-of-order delivery, gross.

### AG-UI event catalog — canonical + Custom

AG-UI's canonical events cover only some of what needs to flow through. The extension strategy:

| A2A event | AG-UI event | Notes |
|-----------|-------------|-------|
| `Task` (submitted) | `RunStarted` | Start-of-turn marker |
| `Message` user text | `UserMessage` (Custom) or local-only | Client usually renders from its own submission; server still broadcasts for multi-tab consistency |
| `Message` agent text block | `TextMessageStart` + `TextMessageContent` + `TextMessageEnd` | Single `Content` event per text block (no deltas) |
| `Message` agent thinking block | `Custom { name: "thinking", data: { text } }` | Grouped with any sibling text block via `parentMessageId` |
| `Message` agent tool_use block | `ToolCallStart` + `ToolCallArgs` + `ToolCallEnd` | `Args` carries the full input at once (no delta JSON) |
| `Message` user tool_result block | `ToolCallResult` | Correlated by `toolCallId` |
| `Message` system (init) | `Custom { name: "system.init", data: { model, tools, permissionMode } }` | |
| `Message` system (hook_started) | `Custom { name: "hook.started", data: { hookId, hookName, hookEvent } }` | |
| `Message` system (hook_response) | `Custom { name: "hook.response", data: { hookId, hookName, output, exitCode, outcome } }` | |
| `StatusUpdate` working | (suppressed — implied by RunStarted) | |
| `StatusUpdate` input-required (question) | `Custom { name: "question.pending", data: { questions } }` | |
| `StatusUpdate` input-required (plan) | `Custom { name: "plan.pending", data: { plan } }` | |
| `StatusUpdate` status_resumed | `Custom { name: "status.resumed" }` | |
| `StatusUpdate` workflow_complete | `Custom { name: "workflow.complete", data: { status, outputs, artifacts } }` | |
| `StatusUpdate` completed | `RunFinished { result }` | |
| `StatusUpdate` failed | `RunError { message, code }` | |

**Why Custom events for extensions:** AG-UI's `Custom` event is specifically documented for extension use. Keeping Homespun's extensions inside `Custom` preserves AG-UI spec conformance and makes unknown names safely ignorable by any future non-Homespun AG-UI consumer. Promoting any of these to first-class AG-UI events is a separate, upstream conversation.

**Alternatives considered:**
- *First-class Homespun-specific events.* Rejected on spec-conformance grounds.
- *Raw A2A events over the wire.* Rejected — the user's explicit goal is AG-UI to the client; also, AG-UI has strong client-library precedent the reducer can build on.

### Append-to-disk before broadcast

For each A2A event received from the worker, the server:

1. Assigns `seq` (per-session counter, strictly monotonic).
2. Writes the `A2AEventRecord` to the session's JSONL file and flushes.
3. Translates to AG-UI envelope.
4. Broadcasts via SignalR.

A refresh request served while step 3 is in flight is guaranteed to see the event written in step 2, because the JSONL write completes before the broadcast. A refresh that arrives *during* step 2 sees the file up to the previous seq; the client's `lastSeenSeq` advances only once the live envelope arrives, so the next refresh (if any) re-reads from there.

**Why:** The refresh-vs-live-race invariant becomes trivial: every broadcast envelope is in the log *before* any client could have observed it. No compensating logic needed. Disk flush on modern filesystems is sub-millisecond for appends of a few hundred bytes.

**Alternatives considered:**
- *Broadcast first, append async.* Rejected — introduces the exact race we are trying to eliminate, in exchange for unobservable latency savings.
- *Batch writes.* Rejected — breaks the invariant during the batch window.

### Replay endpoint contract

```
GET /api/sessions/{sessionId}/events?since={long?}&mode={incremental|full}
```

- If `since` is omitted or `mode=full`, return *all* envelopes from seq 1.
- If `since` is provided and `mode=incremental` (default), return envelopes with `seq > since`.
- If `since` is beyond the current end, return an empty array (not an error — the client is caught up).
- If the session has no event log (never existed, or was deleted), return 404.
- Response body: `SessionEventEnvelope[]` in ascending `seq` order.
- Server-side config `SessionEvents:ReplayMode` (values `Incremental | Full`) is consulted when `mode` is absent; default is `Incremental`. The client may explicitly pass `mode=full` to override.

**Why:** A single endpoint, two parameters, covers all replay cases. `mode=full` is a kill switch if incremental replay ever produces a gap (e.g. a bug where the server skips a seq); the config variant is the instance-wide override for that same scenario.

**Alternatives considered:**
- *Stream the replay over SignalR.* Rejected — REST is simpler for bounded replays; SignalR is used for the live, unbounded stream.
- *Two endpoints (`/events` and `/events/full`).* Rejected — the `mode` parameter is equally discoverable and fits the "config toggle" intent.

### Client-side dedup by `eventId`

Both live and replay envelopes carry a stable `eventId` (UUID, generated on the server at receive time). The client keeps a `Set<eventId>` per session (bounded, evicted oldest-first) and discards duplicates. This is the safety net that lets the server serve `mode=full` without corrupting client state, and that protects against the rare race where a live envelope and a replay envelope carry the same event.

**Why:** Decouples client correctness from server replay semantics. Even if the server replays a gap too-permissively, the client never double-renders.

**Alternatives considered:**
- *Trust `seq` monotonicity on the client.* Rejected — works for incremental replay only; breaks under `mode=full`.

### Hub surface

Today's `ClaudeCodeHub` exposes roughly ten `BroadcastAGUI*` methods (`BroadcastAGUITextMessageStart`, `BroadcastAGUIToolCallArgs`, etc.). After this change, AG-UI traffic flows through a single method:

```csharp
Task ReceiveSessionEvent(string sessionId, SessionEventEnvelope envelope);
```

Non-AG-UI hub methods (`SessionStatusChanged`, `SessionModeModelChanged`, lifecycle notifications) stay — they are session *control*, not event *content*.

**Why:** The per-type methods existed to carry strongly-typed AG-UI variants; with the envelope acting as a discriminated union (via `envelope.Event.Type`), the client can match on the embedded event and the hub surface collapses.

### `ClaudeMessage` deletion

`ClaudeMessage`, `ClaudeMessageContent`, `ClaudeMessageRole`, and the `ClaudeContentType` enum are deleted from `Homespun.Shared` and all consumers:

- Server: `MessageCacheStore`, `SessionCacheController`, all assembly in `MessageProcessingService`, references in `ToolInteractionService`, `MockClaudeSessionService`, DTOs on session endpoints.
- Client: `use-historical-session-messages`, `signalr-message-adapter`, every render path in `features/sessions/`. Client state shape is derived from AG-UI envelopes directly.
- Shared: `ClaudeMessage.cs`, `ClaudeMessageContent.cs`.

**Why:** `ClaudeMessage` exists only because the server chose to assemble SDK blocks into a coarse record. Eliminating that assembly eliminates the need for the type.

**Alternatives considered:**
- *Keep `ClaudeMessage` as a client-side render convenience.* Rejected — it introduces a second state shape alongside the AG-UI stream and reintroduces the very duplication we're removing.

### Existing cache data — deleted on upgrade

The existing `{baseDir}/{projectId}/*.jsonl` files store `ClaudeMessage` records and cannot be replayed by the new code. On first startup after upgrade, the server deletes the cache directory contents (preserving `{baseDir}` itself) and logs a warning. This is acceptable because Homespun today is single-user / single-developer per instance and the developer can simply restart any sessions they care about.

**Why:** Migration cost outweighs value for dev-tool-scale data. `multi-user-postgres` will introduce persistent storage at a point where a proper migration path matters.

## Risks / Trade-offs

- **[Risk]** Wide blast radius. `ClaudeMessage` is referenced in ~40+ files across client and server. Mitigation: delete the shared type first and follow TypeScript / C# compiler errors to every call site. No "backwards-compatible shim" — the compiler walk *is* the migration.
- **[Risk]** AG-UI `Custom` events with Homespun-specific names could collide with future upstream AG-UI additions. Mitigation: namespace Homespun customs consistently (`thinking`, `hook.started`, `hook.response`, `system.init`, `question.pending`, `plan.pending`, `status.resumed`, `workflow.complete`). If AG-UI later adds an event with one of these names, rename here.
- **[Risk]** Append-before-broadcast adds fs I/O to the hot path. Mitigation: measure; if a single fsync-less append to an open file handle exceeds ~5 ms at P99 in practice, switch to a WAL-per-session (kept append-only, still serial). Not expected.
- **[Risk]** Refresh-mid-turn sees a partial log with no final status-update yet, and live continues to deliver envelopes after the refresh response is sent. Mitigation: client dedup handles it — no special logic.
- **[Risk]** `multi-user-postgres` expected `MessageCacheStore` to be an EF-backed `ClaudeMessage` table. That proposal now needs to target `A2AEventStore` semantics. Mitigation: updated inline as part of this change; see `openspec/changes/multi-user-postgres/` diff.
- **[Trade-off]** Client state is now derived from an append-only event log, which is more work than a flat message list. But it's the same work the live path already does, now unified with refresh. Complexity drops net-net because there's one reducer instead of two.
- **[Trade-off]** `mode=full` replays are O(n) in total session events. For long sessions this can be many KB. Acceptable for a dev tool; a future capability-level spec could add paging if it matters.

## Migration Plan

Single atomic change. No feature flag on the data plane — `ClaudeMessage` is deleted outright and the new `SessionEventEnvelope` replaces it. The server-side `SessionEvents:ReplayMode` config is the only runtime toggle (Incremental default, Full fallback).

Implementation order (see `tasks.md`):

1. Land envelope types and server-side `A2AEventStore` with unit tests.
2. Land `A2AToAGUITranslator` with exhaustive per-event-type tests.
3. Rewire the server ingestion path (`DockerAgentExecutionService` → `A2AEventStore` → translator → `ClaudeCodeHub.ReceiveSessionEvent`). Old path kept temporarily running only inside test harness.
4. Land `GET /api/sessions/{id}/events` and hook it to the store + translator.
5. Rewrite client hook (`use-session-events`) + reducer. Delete `use-historical-session-messages` and `signalr-message-adapter`.
6. Delete `ClaudeMessage*` shared types; walk the compile errors.
7. Delete `A2AMessageParser.ConvertToSdkMessage`, `SdkMessageParser`, `ContentBlockAssembler`, `MessageCacheStore`, `SessionCacheController`, the per-type `BroadcastAGUI*` hub methods, and the assembly logic in `MessageProcessingService`.
8. Add fidelity integration test: drive a canned worker session, collect live envelopes; refresh mid-session and after completion, compare equality.
9. Startup cache-purge step (deletes pre-upgrade `{baseDir}/*/*.jsonl`).
10. Full test sweep; update `claude-agent-sessions` spec requirements via this change's delta.

## Open Questions

- **Should the server broadcast user-sent messages as AG-UI envelopes too?** Today the client adds the user message locally before the server sees it; multi-tab support requires the server to echo it. Current lean: yes, emit a `Custom { name: "user.message", ... }` envelope on receipt. Confirm during implementation.
- **Eviction policy for the client dedup set.** Bounded LRU at N=10,000 per session should be comfortable. Revisit if we ever see a runaway session.
- **Should `A2AEventStore` expose a tail-subscription API for the hub?** The simple implementation just writes-then-broadcasts from the ingestion service. Moving broadcast to a store-observer is cleaner but requires fan-out logic. Deferred to a follow-up if we need to add multiple observers.
