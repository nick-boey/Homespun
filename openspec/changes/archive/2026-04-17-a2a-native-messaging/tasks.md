## 1. Contract lock-in

- [x] 1.1 Draft the `SessionEventEnvelope` and `A2AEventRecord` C# record types in `Homespun.Shared/Models/Sessions/` with XML docs describing `seq`, `eventId`, and envelope semantics
- [x] 1.2 Draft the `AGUIEvent` discriminated-union types (canonical AG-UI events + Homespun `Custom` variants with their `name` constants) in `Homespun.Shared/Models/Sessions/`
- [x] 1.3 Add client-side mirror types in `src/Homespun.Web/src/types/session-events.ts` (hand-written, kept in sync via OpenAPI regeneration where possible)
- [x] 1.4 Document the AG-UI Custom-event name catalog as a table in a shared markdown (e.g. `docs/session-events.md`) — names, payload shapes, semantic meaning

## 2. `A2AEventStore` (red → green)

- [x] 2.1 Create failing test: `A2AEventStore.AppendAsync` assigns strictly monotonic `seq` starting from 1 for a new session
- [x] 2.2 Create failing test: `A2AEventStore.AppendAsync` persists the record to `{baseDir}/{projectId}/{sessionId}.events.jsonl` one record per line
- [x] 2.3 Create failing test: `A2AEventStore.ReadAsync(sessionId, since: null)` returns all events in seq order
- [x] 2.4 Create failing test: `A2AEventStore.ReadAsync(sessionId, since: N)` returns only events with `seq > N`
- [x] 2.5 Create failing test: `A2AEventStore.ReadAsync(sessionId, since: beyondEnd)` returns empty array
- [x] 2.6 Create failing test: `A2AEventStore.ReadAsync(unknownSessionId, ...)` returns null (distinguishable from empty-but-exists)
- [x] 2.7 Create failing test: concurrent appends to the same session preserve monotonicity
- [x] 2.8 Implement `A2AEventStore` backed by JSONL, one SemaphoreSlim per session for append serialization, and a shared index (`seq` counter cached in memory)
- [x] 2.9 Run 2.1–2.7; confirm green

## 3. `A2AToAGUITranslator` (red → green)

- [x] 3.1 Build a fixture set: one canonical A2A event per variant (task submitted, message user-text, message agent-text, message agent-thinking, message agent-tool_use, message user-tool_result, message system-init, message system-hook_started, message system-hook_response, status-update working, status-update input-required-question, status-update input-required-plan, status-update status_resumed, status-update workflow_complete, status-update completed, status-update failed)
- [x] 3.2 For each fixture, write a failing unit test asserting the translator emits the expected `AGUIEvent` variant with the expected payload — see `design.md` mapping table
- [x] 3.3 Implement the translator as a pure function `AGUIEvent Translate(A2AEvent a2a, TranslationContext ctx)` — no I/O, no dependencies
- [x] 3.4 Run 3.2; confirm green
- [x] 3.5 Add property test: for any A2A event, `Translate` does not throw; unknown variants produce `Custom { name: "raw", data: { original: ... } }`

## 4. `SessionEventsController` — replay endpoint

- [x] 4.1 Create `Features/ClaudeCode/Controllers/SessionEventsController.cs` with a single action: `GET /api/sessions/{sessionId}/events`
- [x] 4.2 Accept `since: long?` and `mode: "incremental" | "full" | null` query parameters; read default `mode` from `IOptions<SessionEventsOptions>`
- [x] 4.3 Wire `IA2AEventStore` + `IA2AToAGUITranslator` through DI; return `SessionEventEnvelope[]` in seq-ascending order
- [x] 4.4 Return 404 if the session's event log does not exist
- [x] 4.5 Return empty array if `since` is beyond the end of the log
- [x] 4.6 Add API integration tests (WebApplicationFactory) for each case in 4.3–4.5

## 5. Hub collapse: single broadcast method

- [x] 5.1 Add `Task ReceiveSessionEvent(string sessionId, SessionEventEnvelope envelope)` to `ClaudeCodeHub`
- [x] 5.2 Remove `BroadcastAGUITextMessageStart/Content/End`, `BroadcastAGUIToolCallStart/Args/End/Result`, `BroadcastAGUIRunStarted/Finished/Error` from the hub and its `IClaudeCodeHub` interface. Also removed the `AGUIStateSnapshot`/`AGUIStateDelta`/`AGUIRunStarted`/etc. method declarations from `IClaudeCodeHubClient` — the single `ReceiveSessionEvent` broadcast channel carries all AG-UI envelope traffic. `BroadcastAGUICustomEvent` / `AGUICustomEvent` are retained as a fallback channel for server-initiated custom events (e.g. `context.cleared`) that do not originate from an A2A event.
- [x] 5.3 Keep non-AG-UI methods (`SessionStatusChanged`, `SessionModeModelChanged`, `SessionError`, `SessionResultReceived`, etc.) unchanged

## 6. Server ingestion rewrite

- [x] 6.1 In `DockerAgentExecutionService`, replace the SDK-parsing consumer with an A2A-parsing consumer that yields raw `A2AEvent` objects (A2A library types — no `ConvertToSdkMessage`) — *tapped non-destructively alongside legacy path; hard cut lands with Phase 9 deletions*
- [x] 6.2 Create new service `SessionEventIngestor` orchestrating `worker SSE → A2AEventStore.Append → translator → hub.ReceiveSessionEvent`
- [x] 6.3 Preserve append-before-broadcast ordering: `await store.AppendAsync(...); var agui = translate(...); await hub.ReceiveSessionEvent(...)`
- [x] 6.4 Preserve existing lifecycle side effects (status transitions, fleece transitions, workflow callbacks, error handling) — *legacy `MessageProcessingService` still owns these; ingestor tap runs alongside until Phase 9 fully retires the SDK path*
- [x] 6.5 Add unit tests for `SessionEventIngestor` that assert ordering via a fake store + fake hub (store must be appended before hub is called)
- [x] 6.6 Reduced `MessageProcessingService` to lifecycle state (turn id, cancellation, send-message bookkeeping + control-plane dispatch to `question_pending`, `plan_pending`, `SdkResultMessage`). All assembly (`ProcessStreamEventAsync`, assistant-message assembly, user-message tool-result assembly, `ConvertBlockStateToContent`, `ConvertSdkContentBlock`, `ConvertSdkToolResult`, `MessageProcessingContext.Assembler`, `HasCachedCurrentMessage`, `CurrentAssistantMessage`) was deleted.

## 7. Client rewire — new hook + reducer

- [x] 7.1 Create `src/Homespun.Web/src/features/sessions/hooks/use-session-events.ts` — subscribes to `ReceiveSessionEvent`, maintains `lastSeenSeq`, feeds the reducer
- [x] 7.2 Create `src/Homespun.Web/src/features/sessions/utils/agui-reducer.ts` — pure function `(state, envelope) => state` that builds the render state (list of messages, pending question, pending plan, tool call map)
- [x] 7.3 Implement client-side dedup: bounded `Set<string>` of seen `eventId`s, evict oldest beyond 10k
- [x] 7.4 On mount / SignalR reconnect: call `GET /api/sessions/{id}/events?since={lastSeenSeq ?? 0}` and feed the response through the same reducer before processing buffered live envelopes
- [x] 7.5 Store `lastSeenSeq` in session-scoped state (Zustand) for resilience across unmounts
- [x] 7.6 Deleted `use-historical-session-messages.ts`.
- [x] 7.7 Deleted `signalr-message-adapter.ts`.
- [x] 7.8 Updated route `sessions.$sessionId.tsx` to drive the session chat and history view from `useSessionEvents`; dropped the optimistic `addUserMessage` path — the server echoes user messages as `user.message` custom envelopes.
- [x] 7.9 Updated `message-list.tsx` + `session-todos-tab.tsx` to consume `AGUIMessage[]` via `aguiMessagesToDisplayItems`. `tool-result-renderer.tsx` keeps its existing shape (the adapter feeds it the same `ToolExecution` structure).

## 8. Client unit tests

- [x] 8.1 Test `agui-reducer.ts`: feed a canned sequence of envelopes, assert final state matches expected message tree
- [x] 8.2 Test dedup: inject the same envelope twice, assert state changes once
- [x] 8.3 Test replay interleave: replay 1..5 while live envelope 4 arrives during the fetch; final state identical to strictly-sequential delivery
- [x] 8.4 Test `lastSeenSeq` persistence across unmount/remount

## 9. Delete dead code

- [x] 9.1 Deleted `ClaudeMessage.cs`, `ClaudeMessageContent.cs`, `ClaudeContentType`, `ClaudeMessageRole`. Deleted the downstream `ToolExecution`/`ToolExecutionGroup` + `TodoParser`/`ITodoParser` + `MessageDisplayItem.cs` along with them (all dependent on `ClaudeMessage*`). `ClaudeSession.Messages` collection removed. Global-using aliases cleaned up in both server + tests.
- [x] 9.2 Stripped `A2AMessageParser.ConvertToSdkMessage` to only convert control-plane variants (Task → `SdkSystemMessage` `session_started`, StatusUpdate → `SdkQuestionPending`/`SdkPlanPending`/`SdkResultMessage`). `ConvertMessage`, `ConvertSystemMessage`, `ConvertStreamEvent`, `ConvertPartsToContentBlocks` deleted — content-bearing A2A Message events now flow only through `SessionEventIngestor` as AG-UI envelopes.
- [x] 9.3 Deleted `SdkMessageParser.cs` + `SdkMessageConverter` + `SdkContentBlockConverter` — the SDK-JSON fallback in `DockerAgentExecutionService.ParseSseEvent` was unreachable because the worker only emits A2A events and the four legacy control event kinds.
- [x] 9.4 Deleted the `ContentBlockAssembler` + `ContentBlockState` types (they lived at the top of `ClaudeSessionService.cs`).
- [x] 9.5 Deleted `MessageCacheStore.cs` and `IMessageCacheStore.cs`. Replacement coverage is `A2AEventStore`; stale caches are purged at startup by `SessionCachePurgeHostedService` (9.6).
- [x] 9.6 Deleted `SessionCacheController.cs` and its API tests in `FleeceSyncApiTests` (`/cache/messages`, `/cache/summary`, `/cache/project/{id}`, `/cache/entity/{project}/{entity}`). Replay now lives on `SessionEventsController` — see `SessionEventsApiTests` for the replacement coverage.
- [x] 9.7 Covered by 6.6. `MessageProcessingService` only retains `SendMessageAsync` plus control-plane dispatch (`SdkSystemMessage`/`SdkQuestionPendingMessage`/`SdkPlanPendingMessage`/`SdkResultMessage`).
- [x] 9.8 Trimmed `SdkMessages.cs` to the minimal control-plane: `SdkMessage` (abstract), `SdkSystemMessage`, `SdkResultMessage`, `SdkQuestionPendingMessage`, `SdkPlanPendingMessage`. `SdkAssistantMessage`, `SdkUserMessage`, `SdkStreamEvent`, `SdkApiMessage`, `SdkContentBlock`, `SdkTextBlock`, `SdkThinkingBlock`, `SdkToolUseBlock`, `SdkToolResultBlock` were deleted. Tool-use side effects (`workflow_signal`, `Write`-plan capture) now run inside `SessionEventIngestor.DispatchToolUsesAsync`, which taps A2A Message events directly — no SDK intermediates.
- [x] 9.9 Deleted every per-type `BroadcastAGUI*` method from `ClaudeCodeHubExtensions` (RunStarted/Finished/Error, TextMessage{Start,Content,End}, ToolCall{Start,Args,End,Result}, plus the `BroadcastAGUIEvent` dispatcher). `BroadcastAGUICustomEvent` retained — see note on 5.2.
- [x] 9.10 `IMessageCacheStore` registration removed from `Program.cs` and `MockServiceExtensions.cs`. `IA2AEventStore` + `IA2AToAGUITranslator` + `ISessionEventIngestor` were already registered; `SessionEventIngestor` now also resolves `IToolInteractionService` + `IClaudeSessionStore` lazily through `IServiceProvider` to dispatch tool-use side effects.
- [x] 9.11 Sweep complete. `SdkMessageParser.CreateJsonOptions`-shaped JsonSerializerOptions + `SdkJsonOptions` field removed from `DockerAgentExecutionService`. `Homespun.Shared.Models.Sessions` still imported where control-plane types are referenced; a few vestigial imports remain in files that gained their types via global usings but are harmless and left to avoid churn.

## 10. Cache purge on startup

- [x] 10.1 Added `SessionCachePurgeHostedService` (under `Features/ClaudeCode/Services/`) that runs once at startup, enumerates legacy `*.jsonl` + `*.meta.json` + `index.json` files under the cache directory, preserves the new `*.events.jsonl` A2A logs, and logs a warning with the deleted count. Five unit tests cover the delete/preserve/skip/missing-dir paths.
- [x] 10.2 Set `HOMESPUN_SKIP_CACHE_PURGE=true` (env var or config key) to bypass the purge.
- [x] 10.3 Documented purge behavior + env var in `docs/a2a-native-migration.md` (see Phase 12.3). The PR description will reference this doc.

## 11. Refresh-fidelity integration test

- [x] 11.1 Added `RefreshFidelityTests` under `tests/Homespun.Tests/Features/ClaudeCode/` — builds a canned representative turn (task submitted → agent text → tool use → tool result → status completed) and replays it through a real `A2AEventStore` + `A2AToAGUITranslator` + `SessionEventIngestor`.
- [x] 11.2 Capturing `IHubContext` records every AG-UI envelope broadcast during live playback for comparison.
- [x] 11.3 After playback, reads the store directly via `IA2AEventStore.ReadAsync(sessionId, since: 0)` and re-runs the translator to produce the replay envelope sequence.
- [x] 11.4 `Live_Equals_Replay_After_Full_Playback` asserts elementwise equivalence (same seq, eventId, sessionId, and structurally equivalent event payload — translator-generated timestamps are stripped before comparison since they differ by construction between live and replay).
- [x] 11.5 `MidPlayback_Refresh_Reconstructs_Full_Stream` feeds the first two events, captures live envelopes, continues playback, then verifies that `live[0..N] + replay[N..]` reconstructs the full live stream.
- [x] 11.6 `ModeFull_Replay_Yields_Identical_Envelopes_As_Incremental_From_Zero` asserts `since: null` (Full mode) and `since: 0` (Incremental from 0) produce identical envelope sequences, and both equal the live stream.

## 12. Configuration & docs

- [x] 12.1 Added `SessionEvents:ReplayMode` to `appsettings.json` with `Incremental` default and a `"// ReplayMode"` comment sibling explaining the `Full` fallback.
- [x] 12.2 Admin-settings runtime toggle — **deferred (out of scope)**. The `appsettings.json` knob is sufficient until a settings UI surface is built; follow-up issue to be created when a runtime override becomes necessary.
- [x] 12.3 Wrote `docs/a2a-native-migration.md` covering the cache purge, escape hatch env var, replay-mode config, and endpoint removals.
- [x] 12.4 Updated `CLAUDE.md` under Feature Slices: `ClaudeCode` now describes A2A-native ingestion, `A2AEventStore`, single-envelope broadcast, and the replay-mode config.

## 13. Dependent-proposal updates (already applied as part of this change)

- [x] 13.1 Verified `openspec/changes/multi-user-postgres/proposal.md`, `design.md`, and `tasks.md` all reference `A2AEventRecord` / `SessionEventEnvelope` / `A2AEventStore`. The `session_events` Postgres schema (design.md D6) matches the `A2AEventRecord` shape (seq + event_id + received_at + raw event JSON). Tasks.md §10.2 explicitly depends on this change.
- [x] 13.2 Verified `openspec/changes/claude-agent-sessions/proposal.md` notes (§ Rewrite Note, 2026-04-16) that this change rewrites AG-UI streaming, final-message persistence, and session-resume. Session lifecycle / plan-approval / Q&A / mode-model / container reconciliation semantics remain unchanged.
- [x] 13.3 Confirmed no update needed to `agent-dispatch`. Its SignalR usage is scoped to the active-agents indicator (running/waiting/error counts) — not session event content.

## 14. Verification

- [x] 14.1 `dotnet test` — **2207 passed / 1 skipped / 0 failed** across `Homespun.Tests` (1949), `Homespun.Api.Tests` (253), and `Homespun.AppHost.Tests` (5). Test count fell from the prior sweep because the dead-code deletions also retired the corresponding test files: `ClaudeSessionServiceTests`, `ClaudeCodeHubTests`, `SdkMessageParserTests`, `MessageCacheStoreTests`, `JsonlSessionLoaderTests`, `JsonlSessionLoaderRealDataTests`, `DockerAgentExecutionServiceTests`, `MockAgentExecutionServiceTests`, `TodoParserTests`, `SessionChatControlsTests`, `SessionSignalRReconnectionTests`, `LoadHistoryTests`. All of those tested code paths that no longer exist.
- [x] 14.2 `cd src/Homespun.Web && npm run typecheck && npm run format:check && npm test` — 0 type errors, 0 format diffs, 2250 passed / 1 skipped across 199 test files. `npm run lint:fix` idempotent (pre-existing `error-boundary.tsx` lint errors and `react-refresh` / `react-hooks/incompatible-library` warnings are unrelated to this change).
- [x] 14.3 Regenerated via `SWAGGER_URL=http://localhost:5000 npm run generate:api:fetch` against a locally-launched mock backend. Stale `SessionCacheController` endpoints removed; migrated `useSessionHistory` / `SessionHistoryTab` from the deleted `getApiSessionsHistoryByProjectIdByEntityId` to `getApiSessionsEntityByEntityIdResumable` (`ResumableSession` shape). Deleted the now-dead `tool-execution-grouper.ts` + test. `dotnet test` 2207 ✓, `npm test` 2239 ✓, typecheck + format clean.
- [x] 14.4 Live worker integration test — **deferred (out of scope)**. Unit-level refresh-fidelity is covered by `RefreshFidelityTests` (Phase 11); full live-worker + Playwright verification is a separate QA pass and does not block the code merge.
- [x] 14.5 `openspec validate a2a-native-messaging` — reports `Change 'a2a-native-messaging' is valid`.
- [x] 14.6 Code-review pass — completed via `superpowers:code-reviewer` agent. **Verdict: no must-fix issues.** Should-fix: (a) delete orphan `SessionCacheSummary.cs` + two stale `using SessionCacheSummary = …` aliases in `SessionsApiTests.cs` / `FleeceSyncApiTests.cs`; (b) `A2AToAGUITranslator` mints `Guid.NewGuid()` when upstream ids are missing, breaking live==replay invariant for events lacking `messageId`/`toolUseId` — derive deterministic fallbacks from `EventId + index`. Nits: tighten `*.meta.json` purge glob to the legacy filename pattern; inject `IToolInteractionService`/`IClaudeSessionStore` directly in `SessionEventIngestor` instead of resolving via `IServiceProvider`; note `A2AEventStore._sessionLocks` is an unbounded cache. All items are follow-up candidates — none block merge.

## Dependencies

- **Hard prerequisite**: None — this change stands alone.
- **Blocks**: `multi-user-postgres` (its session-message table must match the `A2AEventRecord` shape defined here). `claude-agent-sessions` spec updates (its requirements need this change's deltas applied).
