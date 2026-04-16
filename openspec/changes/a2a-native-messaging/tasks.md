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
- [ ] 6.6 Reduce `MessageProcessingService` to just the session lifecycle state it still needs (turn id, cancellation, send-message bookkeeping); delete the assembly code and stream-event/content-block paths *— deferred to Phase 9 (Delete dead code); the ingestor runs in parallel with the current assembly code so the live path is safe*

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

- [ ] 9.1 Delete `Homespun.Shared/Models/Sessions/ClaudeMessage.cs`, `ClaudeMessageContent.cs`, `ClaudeContentType` enum, `ClaudeMessageRole` enum
- [ ] 9.2 Delete `A2AMessageParser.ConvertToSdkMessage` and all the conversion helpers (`ConvertMessage`, `ConvertSystemMessage`, `ConvertResult`, `ConvertPartsToContentBlocks`, etc.) — keep parsing of incoming A2A events only
- [ ] 9.3 Delete `SdkMessageParser.cs` (server-side)
- [ ] 9.4 Delete `ContentBlockAssembler`
- [ ] 9.5 Delete `MessageCacheStore.cs` and `IMessageCacheStore.cs`
- [x] 9.6 Deleted `SessionCacheController.cs` and its API tests in `FleeceSyncApiTests` (`/cache/messages`, `/cache/summary`, `/cache/project/{id}`, `/cache/entity/{project}/{entity}`). Replay now lives on `SessionEventsController` — see `SessionEventsApiTests` for the replacement coverage.
- [ ] 9.7 Delete `ProcessStreamEventAsync`, `ProcessAssistantMessageAsync` (assembly parts), `ProcessUserMessageAsync` (assembly parts), `ConvertBlockStateToContent`, `ConvertSdkContentBlock`, `ConvertSdkToolResult`, `MessageProcessingContext.Assembler`, `HasCachedCurrentMessage`, `CurrentAssistantMessage` from `MessageProcessingService.cs`
- [ ] 9.8 Delete `SdkMessages.cs` DTOs (server-side) — `SdkAssistantMessage`, `SdkUserMessage`, `SdkToolUseBlock`, `SdkToolResultBlock`, `SdkThinkingBlock`, `SdkStreamEvent`, `SdkSystemMessage`, `SdkResultMessage` — wherever server-side code owned them
- [x] 9.9 Deleted every per-type `BroadcastAGUI*` method from `ClaudeCodeHubExtensions` (RunStarted/Finished/Error, TextMessage{Start,Content,End}, ToolCall{Start,Args,End,Result}, plus the `BroadcastAGUIEvent` dispatcher). `BroadcastAGUICustomEvent` retained — see note on 5.2.
- [ ] 9.10 Remove `IMessageCacheStore` registration from DI in `Program.cs`; add `IA2AEventStore`, `IA2AToAGUITranslator`, `SessionEventIngestor`
- [ ] 9.11 Grep-sweep `using` statements that became unused

## 10. Cache purge on startup

- [ ] 10.1 Add a `SessionCachePurgeHostedService` (or equivalent) that runs once at startup, enumerates existing `{baseDir}/**/*.jsonl` files, and deletes them — logging the count with a warning so it's visible in the upgrade log
- [ ] 10.2 Skip purge if env var `HOMESPUN_SKIP_CACHE_PURGE=true` (escape hatch for dev sessions in progress during upgrade)
- [ ] 10.3 Document the purge behavior in the PR description and the project README's upgrade notes section

## 11. Refresh-fidelity integration test

- [ ] 11.1 Build a canned-worker test harness that replays a recorded A2A event sequence (from `.tmp/Worker Logs-*.txt`) into the new ingestor
- [ ] 11.2 Collect the AG-UI envelopes broadcast during "live" playback
- [ ] 11.3 After playback completes, call `GET /events?since=0`; collect the replay envelopes
- [ ] 11.4 Assert `live == replay` elementwise (same `eventId`s in the same order, same payloads)
- [ ] 11.5 Repeat with a mid-playback refresh at seq=N; assert `live[0..N] + replay[N..]` reconstructs the full stream identically
- [ ] 11.6 Repeat with `mode=full`; assert dedup-applied rendering is still pixel-identical

## 12. Configuration & docs

- [ ] 12.1 Add `SessionEvents:ReplayMode` to `appsettings.json` with `Incremental` default and a comment explaining the `Full` fallback
- [ ] 12.2 Add a runtime toggle in admin-settings (optional — nice to have; defer if frontend scope grows)
- [ ] 12.3 Write a short migration note in `docs/`: "On upgrade, session caches are reset. Restart any in-progress agents."
- [ ] 12.4 Update `CLAUDE.md` under Feature Slices: `ClaudeCode` now describes A2A-native ingestion, `A2AEventStore`, and the single AG-UI broadcast channel

## 13. Dependent-proposal updates (already applied as part of this change)

- [ ] 13.1 Verify `openspec/changes/multi-user-postgres/proposal.md` / `design.md` / `tasks.md` reflect the new `A2AEventStore` schema (A2A envelope + seq) rather than `ClaudeMessage`
- [ ] 13.2 Verify `openspec/changes/claude-agent-sessions/proposal.md` notes that this change rewrites the AG-UI streaming and session-resume requirements
- [ ] 13.3 Confirm no update needed to `agent-dispatch` (SignalR usage there is about active-agent counts, not session events)

## 14. Verification

- [ ] 14.1 `dotnet test` — confirm all non-Live tests pass; any deleted tests are accounted for by deletion commits, not test-suite regressions
- [ ] 14.2 `cd src/Homespun.Web && npm run lint:fix && npm run format:check && npm run typecheck && npm test`
- [ ] 14.3 `cd src/Homespun.Web && npm run generate:api:fetch` — regenerate the API client, confirm `SessionEventsController` appears and `SessionCacheController` does not
- [ ] 14.4 Live worker integration test: spin the worker container from this branch, create a session, send a multi-tool turn, refresh the UI, confirm messages match between live and reload — capture Playwright screenshots
- [ ] 14.5 `openspec validate a2a-native-messaging` — confirm the proposal validates
- [ ] 14.6 Code-review pass (use `superpowers:code-reviewer` or `code-review:code-review` skill)

## Dependencies

- **Hard prerequisite**: None — this change stands alone.
- **Blocks**: `multi-user-postgres` (its session-message table must match the `A2AEventRecord` shape defined here). `claude-agent-sessions` spec updates (its requirements need this change's deltas applied).
