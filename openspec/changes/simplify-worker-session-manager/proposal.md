## Why

`src/Homespun.Worker/src/services/session-manager.ts` has grown to ~1400 lines and is difficult to reason about. Much of the complexity is historical workarounds layered on top of the Claude Agent SDK — most notably rebuilding a fresh `query()` with `resume: conversationId` after every turn, a custom `InputController` that reimplements `q.streamInput()`, an in-memory message-history ring buffer that duplicates the server's `MessageCacheStore`, and a 220-line `canUseTool` branch ladder. The SDK now provides idiomatic primitives (`streamInput`, `setPermissionMode`, `setModel`) that make these workarounds unnecessary. Simplifying now pays off before `aci-agent-execution` and `worker-clone-and-push` add more surface area on top.

## What Changes

- Replace per-turn `query()` + `resume` rebuild with a single long-lived `query()` per session; follow-up messages go through `q.streamInput()`.
- Remove the `InputController` class entirely.
- Remove the 1000-entry `MessageHistoryEntry` ring buffer on `OutputChannel`; rely on the server-side `MessageCacheStore` for full-session replay.
- **BREAKING**: Remove the worker's `GET /sessions/:id/messages` endpoint (`getMessageHistory`) and any server-side callers of it.
- Replace the `canUseTool` if-ladder with a small handler registry splitting tool handlers into two classes: **interactive** (`AskUserQuestion`, `ExitPlanMode` — emit control event, await human) and **signal** (`WorkflowComplete`, `workflow_signal` — emit event, allow).
- Unify `pendingQuestions` and `pendingPlanApprovals` into a single `Map<sessionId, Map<PendingKind, PendingInteraction>>` with a generic resolver.
- Extract the debug-log file watcher setup into a helper so it is not duplicated across `create()` and the `send()` resume branch (which is itself being removed).
- Drop the `resultReceived` flag and the `if (!ws.resultReceived && ws.query.setPermissionMode)` guards in `send()` / `setMode()` — no longer needed when the CLI stays alive across turns.
- Add unit tests covering `send`/`streamInput`, `canUseTool` dispatch for all four special-cased tools, pending resolution lifecycle, and `setMode`/`setModel` pass-through.
- Add a small spike task to verify the SDK CLI does not self-terminate during long idle periods (≥10 min) between turns.

Behavior kept identical in this change:
- Public API of `SessionManager` (method names, signatures, semantics from the route layer's perspective).
- SSE event stream contents (control events `question_pending`, `plan_pending`, `status_resumed`, `workflow_complete`, `workflow_signal`).
- `status` field values and `lastMessageType`/`lastMessageSubtype` tracking exposed via `/sessions/active` and `/sessions/:id`.
- Plan-mode tool allow-list.

## Capabilities

### New Capabilities
<!-- None — this is internal refactoring of an existing capability. -->

### Modified Capabilities
- `claude-agent-sessions`: Worker internal session runtime is simplified. No externally observable requirement changes except the removal of the `/sessions/:id/messages` worker endpoint (message replay now comes exclusively from the server-side cache).

## Impact

- **Worker**: `src/Homespun.Worker/src/services/session-manager.ts` — target reduction ~1396 → ~600 lines. `src/Homespun.Worker/src/routes/sessions.ts` — remove the `/messages` handler.
- **Server**: any C# callers of the worker's `/sessions/:id/messages` endpoint in `Features/ClaudeCode/` (to be located during the first task). Server-side replay continues via `MessageCacheStore`.
- **Tests**: new `session-manager.test.ts` (unit). Existing HTTP-level tests should continue to pass unchanged.
- **Risk**: SDK idle-timeout behavior is unverified — addressed by a dedicated spike task before the refactor lands.
- **Dependencies**: none. Changes are self-contained in the worker plus one small server cleanup.
