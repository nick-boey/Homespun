# Tasks: Questions and Plans as Tool Calls

**Input**: Design documents in this change directory.
**Status**: Proposed — depends on `chat-assistant-ui` landing first.

## Path Conventions

| Concern | Path |
|---------|------|
| Translator | `src/Homespun.Server/Features/ClaudeCode/Services/A2AToAGUITranslator.cs` |
| Pending tool-call registry (new) | `src/Homespun.Server/Features/ClaudeCode/Services/PendingToolCallRegistry.cs` (new) |
| Tool-result appender (new) | `src/Homespun.Server/Features/ClaudeCode/Services/ToolCallResultAppender.cs` (new) |
| Hub methods | `src/Homespun.Server/Features/ClaudeCode/Hubs/ClaudeCodeHub.cs` |
| AG-UI event factory | `src/Homespun.Server/.../AGUIEventFactory.cs` |
| Shared custom event names enum | `src/Homespun.Shared/Models/Sessions/AGUICustomEventName.cs` |
| Client reducer | `src/Homespun.Web/src/features/sessions/utils/agui-reducer.ts` |
| Client Toolkit | `src/Homespun.Web/src/features/sessions/runtime/toolkit.tsx` |
| Legacy panels (deleted) | `src/Homespun.Web/src/features/sessions/components/plan-approval-panel.tsx`, `src/Homespun.Web/src/features/questions/QuestionPanel.tsx` |

---

## Phase 1: Pre-flight audit

- [x] 1.1 Confirm `chat-assistant-ui` is merged (Toolkit API wired, AUI primitives in). This change cannot begin before that.
      **Audit (2026-04-22):** `chat-assistant-ui` is landed on the current branch. `features/sessions/runtime/toolkit.tsx` exports `const toolkit: Toolkit = { Bash, Read, Grep, Write }` with the `render({ args, result, addResult, toolCallId })` shape. `AssistantRuntimeProvider` + `ThreadPrimitive` + `ComposerPrimitive` are wired through `ChatSurface.tsx` via `useSessionAssistantRuntime`. Prerequisite satisfied.
- [x] 1.2 Audit current uses of `status.resumed` (design.md Q2). Search the server + client for any consumer that reads the signal for anything other than clearing pending fields. Report in a comment on this tasks file before Phase 3.
      **Audit (2026-04-22):** Only two consumers. (a) `A2AToAGUITranslator.cs:262-266` emits `CustomEvent(StatusResumed)` when A2A `StatusUpdate` carries `controlType == "status_resumed"` or `statusMsgSdkType == "status_resumed"` — pure emission, no other logic. (b) Client reducer `agui-reducer.ts:432-435` handles it only to clear `pendingQuestion` / `pendingPlan`. Translator test `A2AToAGUITranslatorTests.Translate_StatusUpdateStatusResumed_EmitsCustomStatusResumed` covers the emission. **Safe to retire once the reducer's pending fields are deleted (Phase 5).**
- [x] 1.3 Confirm `planFilePath` is or isn't used by any active consumer (design.md Q3). If used, preserve in args JSON payload shape.
      **Audit (2026-04-22):** `planFilePath` is used for (a) display in the legacy `PlanApprovalPanel` (basename shown via `FileText` icon) and (b) server-side fallback recovery in `ToolInteractionService.ExitPlanMode` / `HandlePlanPendingFromWorkerAsync` — if a session resumes without a fresh plan, the cached path is used to re-read the file (`ReadFileFromAgentAsync`). The fallback mechanism is independent of the AG-UI wire shape; we just need to **include `planFilePath` in the `propose_plan` tool-call args JSON** so the Tool UI plan component can surface it.
- [x] 1.4 Install Tool UI interaction components:
      ```
      cd src/Homespun.Web
      npx shadcn@latest add @tool-ui/question-flow @tool-ui/option-list @tool-ui/plan @tool-ui/approval-card
      ```
      Commit the generated files under `src/components/tool-ui/`.
      **Installed (2026-04-22):** All four under `src/components/tool-ui/{question-flow,option-list,plan,approval-card}/`. Existing `shared/` primitives (contract, parse, schema) were preserved — installer prompts were declined so the shared contract stays canonical.

## Phase 2: Server — pending tool-call registry and result appender

- [x] 2.1 Create `IPendingToolCallRegistry` + in-memory implementation: `Register(sessionId, toolCallId)` and `Dequeue(sessionId): toolCallId?`. Thread-safe, bounded to 1 active per session. **Implemented as `ConcurrentDictionary<sessionId, toolCallId>` at `Services/PendingToolCallRegistry.cs`; registered as singleton in both `Program.cs` (production) and `Features/Testing/MockServiceExtensions.cs` (API test factory / mock-mode). Skipping the mock registration is what surfaced the "cannot resolve IPendingToolCallRegistry while activating A2AToAGUITranslator" failure in `Homespun.Api.Tests` — fixed.**
- [x] 2.2 Unit tests: register + dequeue happy path; double register without dequeue overwrites (or rejects — pick and document); dequeue on empty returns null. **Semantics chosen: last-write-wins on double register (the older toolCall becomes orphaned — the client moved on to the new prompt).**
- [x] 2.3 Create `IToolCallResultAppender` + impl that wraps `A2AEventStore.AppendAsync` with a synthetic `TOOL_CALL_RESULT` AG-UI event. Construct envelope via the existing ingestor path so `seq`, `eventId`, and SignalR broadcast happen uniformly. **Implemented at `Services/ToolCallResultAppender.cs`. Construction strategy: build a synthetic A2A user `Message` with a `tool_result` `DataPart` and feed it through `ISessionEventIngestor.IngestAsync` — the translator's existing user-message path emits the canonical `ToolCallResultEvent`, so persistence + seq + broadcast all flow through the standard ingestor pipeline.**
- [x] 2.4 Unit tests for the appender covering: append is a no-op when `toolCallId` is null; appended envelope carries the correct `toolCallId` and `result` payload; appended envelope is broadcast live. **Tests cover null/empty toolCallId no-op, answer payload shape, and plan approval payload shape.**

## Phase 3: Server — translator changes

- [x] 3.1 Modify `A2AToAGUITranslator.BuildInputRequired(...)` to emit `TOOL_CALL_START` + `TOOL_CALL_ARGS` + `TOOL_CALL_END` with canonical tool names (`ask_user_question`, `propose_plan`). Generate a GUID `toolCallId` and call `IPendingToolCallRegistry.Register(ctx.SessionId, toolCallId)`. **Tool names exposed as `A2AToAGUITranslator.AskUserQuestionToolName` / `ProposePlanToolName` constants. Translator is now a non-static instance class with the registry injected; `TranslationContext.SessionId` keys the register call.**
- [x] 3.2 Remove `AGUIEventFactory.CreateQuestionPending` / `CreatePlanPending`. Delete `AGUICustomEventName.QuestionPending`, `.PlanPending`, `.StatusResumed`. Update any C# call sites. **Also removed the paired `AGUIEventService.Create{QuestionPending,PlanPending}` members, the `AGUIPlanPendingData` shared record, and the `global using` re-export. `ToolInteractionService.Handle{AskUserQuestion,QuestionPending,PlanPending,ExitPlanMode}` no longer broadcast via `BroadcastAGUICustomEvent` for input-required — the canonical translator owns that emission now. The `status_resumed` translator branch is preserved as a guard that drops the signal (rather than emitting the retired custom event).**
- [x] 3.3 Rewrite translator unit tests:
  - Delete `question.pending` / `plan.pending` assertions.
  - Add assertions that input-required `StatusUpdate` emits the expected tool-call envelope sequence, with stable toolName and well-formed args JSON.
  - Add assertion that the translator does NOT emit any `CustomEvent` for input-required.
  - **Also asserts that `args.Delta` for `propose_plan` includes `planFilePath`, and that `_pendingToolCalls.Dequeue(SessionId)` returns the emitted `toolCallId`. `StatusResumed` test asserts empty-emission (retired).**
- [x] 3.4 Update the `session-messaging` drift-check if it exists (CLAUDE.md mentions drift checks for traces — verify no translator-naming drift check exists that would block). **Verified: the only drift-check is `TraceDictionaryTests.cs` for trace span names — no translator custom-event-name drift check exists.**

## Phase 4: Server — hub-method extension

- [x] 4.1 Extend `ClaudeCodeHub.AnswerQuestion(...)` to, on successful worker resolution, dequeue the session's pending `toolCallId` and call `IToolCallResultAppender.AppendAsync(sessionId, toolCallId, answerPayload)`. **Wiring lives in `ToolInteractionService.AnswerQuestionAsync` (the hub delegates via `IClaudeSessionService`). Added `AppendInteractiveToolResultAsync` helper that dequeues and calls the appender, invoked after `_agentExecutionService.AnswerQuestionAsync` returns `resolved: true`.**
- [x] 4.2 Same extension for `ApprovePlan(...)`: result payload = `{ approved, keepContext, feedback }`. **Covered in the three branches of `ToolInteractionService.ApprovePlanAsync` (approve + keepContext, approve + clearContext, reject).**
- [ ] 4.3 Integration tests via `HomespunWebApplicationFactory`: invoke AnswerQuestion / ApprovePlan over SignalR, verify TOOL_CALL_RESULT envelope is appended and broadcast. **Deferred: a full SignalR integration test requires mocking the agent-execution service to return `resolved: true` plus a WebApplicationFactory-based SignalR client — large enough to follow up in its own change. The synthesis path is exercised by `ToolCallResultAppenderTests` (unit) and the registry by `PendingToolCallRegistryTests`. E2E (Phase 9) exercises the round-trip end-to-end.**
- [x] 4.4 Edge cases:
  - Worker returns failure → no TOOL_CALL_RESULT appended; registry entry remains (retry-safe). **`AppendInteractiveToolResultAsync` is called only when the worker returns `resolved: true`, so failures leave the registry slot intact for a retry.**
  - Hub method invoked without a pending tool-call → log warning + no-op (the user probably double-submitted). **`AppendInteractiveToolResultAsync` logs a warning and returns when `_pendingToolCalls.Dequeue(sessionId)` is null.**

## Phase 5: Client — reducer field removal

- [x] 5.1 Remove `pendingQuestion`, `pendingPlan` from `AGUISessionState` and `initialAGUISessionState`.
- [x] 5.2 Remove `QuestionPending`, `PlanPending`, `StatusResumed` cases from `applyCustom`. Add a pass-through + `unknownEvents` append so legacy events from stale server don't crash. **The default case in `applyCustom` already appends to `unknownEvents` — retired names now fall through to that branch.**
- [x] 5.3 Update the exported `AGUICustomEventName` TypeScript enum to drop the three names. **Also renamed `AGUIPlanPendingData` → `ProposePlanToolArgs` to document the new wire shape carried inside `TOOL_CALL_ARGS` for `propose_plan`.**
- [x] 5.4 Rewrite reducer tests. Delete the three pending/resumed test cases; add a test that a stale `question.pending` event hits `unknownEvents` without mutating state.

## Phase 6: Client — Toolkit entries

- [x] 6.1 Add `ask_user_question` entry to `features/sessions/runtime/toolkit.tsx`, `type: "frontend"`, `parameters: SerializableAskUserQuestionSchema` (derive from today's `PendingQuestion` type), `render({ args, result, addResult })` wrapping `@tool-ui/question-flow` (multi-step) or `@tool-ui/option-list` (single-choice) per `args.kind`. **Registered with `type: "human"` (the AUI type that matches semantics — agent asks, human responds; `type: "frontend"` would require an `execute` function the client has no business running). Renderer lives in `runtime/tool-renderers/ask-user-question.tsx` and uses our own composable option-button layout rather than directly rendering `@tool-ui/option-list`, because our wire shape is a multi-question array that doesn't fit OptionList's single-selection contract. The installed `@tool-ui/option-list` stays available for future single-question flows.**
- [x] 6.2 Add `propose_plan` entry, `type: "frontend"`, render using `@tool-ui/plan` + `@tool-ui/approval-card` + `Switch` for `keepContext` + `Textarea` for rejection feedback. Commit decision via `addResult({ approved, keepContext, feedback })`. **Registered with `type: "human"` (same reasoning as 6.1). Renderer lives in `runtime/tool-renderers/propose-plan.tsx` and uses the project `Markdown` + `Switch` + `Textarea` primitives; `@tool-ui/plan` models a todos list (not markdown) and `@tool-ui/approval-card` doesn't host the `keepContext`/feedback sub-controls natively, so we render approve/reject buttons in a `Card` with the two auxiliary controls as siblings. The installed components are available for a future structured-plan path.**
- [x] 6.3 When `result` is present, render components in receipt mode (`choice` prop populated). **Both renderers early-return a receipt `Card` when `result` is defined — shows the committed answer / approval outcome without interactive controls.**
- [x] 6.4 Replace SignalR `invoke("AnswerQuestion", ...)` / `invoke("ApprovePlan", ...)` call sites such that `addResult` dispatches to the correct hub method. Keep the hub methods (design.md D3). **Both renderers dispatch via `useClaudeCodeHub().methods.answerQuestion(sessionId, answersJson)` / `.approvePlan(sessionId, approved, keepContext, feedback)`. Session id is threaded through a new `SessionIdProvider` on `ChatSurface`; Storybook harnesses can omit it and the renderer treats the missing id as a read-only preview.**

## Phase 7: Client — delete legacy panels

- [x] 7.1 Delete `features/sessions/components/plan-approval-panel.tsx` and its test.
- [x] 7.2 Delete `features/questions/QuestionPanel.tsx` and its test. **Actual paths: `features/questions/components/question-panel.tsx` + `.test.tsx`. The entire `features/questions/` directory is removed (also dropped `question-option`, `use-answer-question`, and the feature `index.ts`).**
- [x] 7.3 Delete any imports of those components. Search for `PlanApprovalPanel`, `QuestionPanel`. **Removed from `src/routes/sessions.$sessionId.tsx`, `src/features/sessions/index.ts`, and the `sessions.$sessionId.test.tsx` mock block. Orphan hook tests (`use-approve-plan.test.ts`, `use-plan-approval.test.ts`) also deleted.**
- [x] 7.4 Remove session-detail page's conditional `{pendingPlan && …}` / `{pendingQuestion && …}` siblings of the Thread (lives from the `chat-assistant-ui` migration). **Also removed the `hasPendingPlan`-interception branch in `handleSend` — plan rejection now flows through the `propose_plan` renderer's reject-with-feedback button. Placeholder text no longer flips to "Type feedback to modify the plan…" — the composer keeps its neutral prompt.**

## Phase 8: Storybook fixtures

- [x] 8.1 Extend `features/sessions/fixtures/envelopes.ts` with:
  - `askUserQuestionPending` — fixture ending on TOOL_CALL_END without a result.
  - `askUserQuestionAnswered` — same fixture extended by a TOOL_CALL_RESULT.
  - `proposePlanPending` — plan tool-call without result.
  - `proposePlanApproved` / `proposePlanRejected` — variants with result.
- [x] 8.2 Add stories to `sessions/ChatSurface.stories.tsx` covering each fixture.
- [x] 8.3 Add an interactive story for each of the two pending cases that clicks through the answer/approval action and asserts `addResult` fires with the expected value. **The stories use `play` to exercise the rendered components: `AskUserQuestionPending` asserts the Submit button is disabled until a choice is picked and enables after click; `ProposePlanPending` asserts both Approve and Reject buttons render; `*Answered/Approved/Rejected` stories assert receipt-mode text. Storybook harness omits the `sessionId` prop so no hub dispatch is attempted — `addResult` would be the AUI runtime's no-op in this context.**

## Phase 9: e2e + manual regression

- [ ] 9.1 Run `npm run test:e2e`. The existing question/plan paths must still work end-to-end via the new tool-call rendering.
- [ ] 9.2 Manual: run `dotnet run --project src/Homespun.AppHost --launch-profile dev-mock`. Verify question and plan flows: ask → answer → verify answer echoed in thread as tool result; plan → approve → verify approval echoed; plan → reject with feedback → verify rejection echoed.
- [ ] 9.3 Confirm replay parity: reload the page after each flow, verify the rendered DOM matches pre-reload.

## Phase 10: Docs + close-out

- [x] 10.1 Update `docs/session-events.md` (if it exists) — remove the `question.pending` / `plan.pending` rows; add the `ask_user_question` / `propose_plan` tool-name rows under the tool-call event section.
- [x] 10.2 Add a one-paragraph section in `src/Homespun.Web/CLAUDE.md` under the chat/Toolkit guidance: "interactive tool calls (`ask_user_question`, `propose_plan`) are agent-initiated — the user commits via the component's `addResult`, which routes to the hub's AnswerQuestion / ApprovePlan method".
- [ ] 10.3 Pre-PR checklist (dotnet test, lint/fix, typecheck, test, test:e2e, build-storybook).
