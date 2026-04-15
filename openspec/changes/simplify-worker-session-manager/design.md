## Context

The worker's `SessionManager` (`src/Homespun.Worker/src/services/session-manager.ts`) was built early in the claude-agent-sessions lifecycle, before the TypeScript Agent SDK exposed mid-session primitives. It currently layers four independent workarounds on top of the SDK:

1. **Close-and-resume per turn** — on every `result` message the code closes the `InputController`, which makes the CLI process exit. Subsequent sends call `query()` again with `resume: conversationId`. This duplicates ~80 lines of setup and makes `setPermissionMode` / `setModel` unusable after the first turn (guarded away because the CLI has exited).
2. **Custom `InputController`** — a single-consumer async queue reimplementing `q.streamInput()`.
3. **1000-entry in-memory message history** on `OutputChannel` for server-restart replay, exposed via `GET /sessions/:id/messages`. The server already has `Features/ClaudeCode/Services/MessageCacheStore.cs` persisting messages durably.
4. **220-line `canUseTool` if-ladder** handling four special-cased tools (`AskUserQuestion`, `ExitPlanMode`, `WorkflowComplete`, `workflow_signal`) with two parallel pending-state maps for the first two.

The SDK exposes `q.streamInput()`, `q.setPermissionMode()`, `q.setModel()`, and a well-defined `canUseTool` contract. The worker is short-lived per user-session, so there is no operational need for the in-memory history. Removing these workarounds is expected to take the file from ~1396 → ~600 lines.

## Goals / Non-Goals

**Goals:**
- Keep a single `query()` alive for the full worker lifetime of a session; follow-up messages use `q.streamInput()`.
- Remove the `InputController` class.
- Remove the `OutputChannel` message-history ring buffer and the `/sessions/:id/messages` worker endpoint.
- Replace the `canUseTool` ladder with a small handler registry keyed by tool name, with two handler shapes (interactive, signal).
- Unify the two pending-state maps into one generic `PendingInteraction<T>`.
- Preserve the public API of `SessionManager` and the SSE event contract (control event shapes and ordering).
- Preserve `status`, `lastMessageType`, and `lastMessageSubtype` semantics exactly.
- Add targeted unit tests for the simplified paths.

**Non-Goals:**
- Changing the AG-UI/SSE event vocabulary consumed by the server.
- Simplifying or renaming `status` / `lastMessageType` values exposed to callers.
- Reworking `session-inventory.ts`, `session-discovery.ts`, `sse-writer.ts`, or `a2a-translator.ts`. These may receive tiny call-site updates but their logic is out of scope.
- Changing how workflow-context tools (`WorkflowComplete`, `workflow_signal`) are gated — still required to have `workflowContext`, still validated the same way.
- Touching the C# server's `MessageCacheStore` (this change only removes a worker-side duplicate).

## Decisions

### Single long-lived `query()` per session

Today, each turn after the first is a brand-new `query({ prompt, options: { resume: conversationId, ... }})`. After the refactor, `create()` starts one `query()` and every `send()` calls `q.streamInput(onceIterator(text))` to push the next user message into it. Tools continue to execute via the same `canUseTool` callback inside that single CLI process.

**Why**: The SDK documents `streamInput()` as the idiomatic primitive for multi-turn streaming input mode. The existing close-and-resume pattern was a workaround that the SDK has since obsoleted. One query eliminates ~80 lines of rebuild code, removes the `resultReceived` flag and all its downstream guards, and restores the ability to use `setPermissionMode` / `setModel` at any point.

**Alternatives considered**:
- *Status quo* — rejected; the complexity it creates is not justified by any remaining SDK limitation.
- *Force a fresh `query()` per turn with `resume`* — rejected; it would keep ~80 lines of duplication and still require `resultReceived` gating.

**Validation before landing**: A spike task (Phase 0) explicitly runs a query, idles for ≥10 minutes, then sends a follow-up via `streamInput` to confirm no silent CLI timeout.

### Handler registry for `canUseTool`

Replace the if-ladder with:

```ts
type InteractiveHandler = (input, session, ctx) => Promise<PermissionResult>; // awaits user
type SignalHandler      = (input, session, ctx) => Promise<PermissionResult>; // emits + allows

const handlers: Record<string, InteractiveHandler | SignalHandler> = {
  AskUserQuestion:  askUserQuestionHandler,   // interactive
  ExitPlanMode:     exitPlanModeHandler,      // interactive
  WorkflowComplete: workflowCompleteHandler,  // signal
  workflow_signal:  workflowSignalHandler,    // signal
};
```

Each handler is a small self-contained function that receives the session and shared context (logger, pending-map, output channel). The dispatch becomes two lines.

**Why**: Four structurally-independent concerns are currently interleaved in a 220-line function. Separation makes each handler testable in isolation and makes the list of special-cased tools visible at a glance.

**Alternatives considered**:
- *Class hierarchy* — rejected as over-engineered for four handlers.
- *Separate files per handler* — deferred; a single `tool-handlers.ts` is sufficient at this count.

### Generic `PendingInteraction<T>`

```ts
type PendingKind = "question" | "plan";
interface PendingInteraction<T> {
  data: T;
  resolve: (result: PermissionResult) => void;
  reject:  (err: Error) => void;
}

private pending = new Map<string, Map<PendingKind, PendingInteraction<unknown>>>();
```

Public manager methods become:
- `resolvePending(sessionId, kind, payload)` (one method, dispatches per kind)
- `hasPending(sessionId, kind)`
- `getPendingData(sessionId, kind)`

The route layer's two endpoints (`/answer`, `/approve-plan`) stay — they just call the unified manager API with different kinds.

**Why**: The two existing maps have identical shapes and lifecycles. Unifying removes three pairs of near-duplicate methods.

**Alternatives considered**:
- *Keep two maps but share a base type* — rejected; same code savings, worse clarity.

### Retain `OutputChannel` minus history

`OutputChannel` stays as the merged stream where SDK messages and worker-emitted control events converge. It loses:
- `history: MessageHistoryEntry[]`
- `maxHistorySize`
- `getMessagesSince`
- `getAllMessages`

The async-iterator half is fine as-is and reads naturally.

**Why**: Two concerns in one class. The async queue is load-bearing; the history is duplication with `MessageCacheStore`.

### Worker `/messages` endpoint removal (BREAKING)

`GET /sessions/:id/messages` is deleted from `src/Homespun.Worker/src/routes/sessions.ts`. Any C# server-side code that calls this worker endpoint is removed as part of the same change. The server's own `MessageCacheStore` already persists all messages; full-session replay uses that.

**Why**: Zero-value duplication, and only a single caller pattern to fix.

### Debug-log watcher extraction

Extract the `/home/homespun/.claude/debug/claude_sdk_debug.log` watcher + `FileMonitor` setup into `attachDebugLogStreaming(sessionId): { cleanup: () => void }` inside a new small helper (or co-located in `session-manager.ts` near `runQueryForwarder`). Called once per session lifetime, not per turn.

### Preserving observable state

`status`, `mode`, `permissionMode`, `lastMessageType`, `lastMessageSubtype`, `lastActivityAt`, `createdAt`, `conversationId` stay exactly as they are today on the `Session` type and in `SessionInfo`. `logStatusChange` stays. These are surfaced by `/sessions/active` and `/sessions/:id` and consumed by the server and UI.

## Risks / Trade-offs

- **[Risk]** SDK CLI self-terminates on long idle → follow-up `streamInput` hangs. **Mitigation**: Phase 0 spike explicitly measures idle tolerance at 10+ min. If there is a limit, add a keep-alive turn or fall back to selective resume only when idle threshold is crossed. If no limit, proceed without mitigation.
- **[Risk]** `setPermissionMode` / `setModel` behave differently when called in a live query vs the currently-guarded "after result" path. **Mitigation**: covered by unit tests exercising both "mid-turn" and "between-turns" transitions.
- **[Risk]** An SSE consumer somewhere relies on the history endpoint for reconnect. **Mitigation**: grep the server for worker `/messages` callers before deletion; if any exist, migrate them to `MessageCacheStore` in the same change.
- **[Trade-off]** Unit-test coverage is added specifically for the simplified paths. Existing HTTP-level tests still gate end-to-end behavior, but they don't discriminate between the old and new code shapes — hence the new unit tests.
- **[Trade-off]** Control-event ordering is preserved by construction (same call sites emit before awaiting the pending promise). No behavioral deviation expected.

## Migration Plan

The change ships as a single atomic refactor — no feature flag, no two-phase rollout. The worker is short-lived per session; the next session to spin up uses the new code, and old sessions naturally finish under the old build. Rollback is a standard git revert if issues surface in staging.

Order of implementation (see tasks.md):

1. **Spike** — verify SDK idle tolerance before touching production code.
2. **Catalog server callers** of the worker's `/messages` endpoint so their removal ships atomically.
3. **Refactor session-manager.ts** bottom-up: handler registry → pending unification → drop InputController → drop history → drop close-and-resume → drop guards.
4. **Delete `/messages` endpoint + server callers**.
5. **Add unit tests** alongside each refactor step (TDD: red → green per the project's conventions).

## Open Questions

- Should the handler registry live in a new file (`src/services/tool-handlers.ts`) or remain inline in `session-manager.ts`? Deferred to implementation — preference is inline unless it pushes the file back over 700 lines.
- Does the worker need to expose any equivalent of the removed `/messages` endpoint for operator diagnostics (e.g. `/sessions/:id/debug`)? No known consumer today; leave out unless a need surfaces.
- Idle tolerance spike script at `src/Homespun.Worker/scripts/spike-idle-tolerance.ts` — run manually before landing the refactor; keep-alive task to be added if it fails.

## Correction — 2026-04-16

The `onceIterator(initialPrompt)` + `q.streamInput(onceIterator(followup))` pattern shipped with this change does not survive past the first turn: the SDK's internal `streamInput` calls `transport.endInput()` at the tail, which closes stdin to the CLI and causes `ProcessTransport is not ready for writing` on the next `streamInput`/`setPermissionMode`/`setModel`. The regression merged green because the worker live tests and `spike-idle-tolerance.ts` were not running in CI. OpenSpec change `fix-worker-streaminput-multi-turn` (2026-04) replaces the per-turn `streamInput` path with a persistent `InputQueue` passed as the `prompt`, and wires the live suite + spike into a scheduled workflow so this class of regression surfaces within hours.
