## Why

`question.pending` and `plan.pending` are shaped like tool calls: the agent hands the user a structured prompt, the user replies with a structured answer, the agent continues. We modeled them as session-level modal state (`pendingQuestion` / `pendingPlan` in the reducer) because the old chat UI had nowhere cleaner to render them, and because AG-UI's `CustomEvent` escape hatch was the path of least resistance on the server translator.

Two things change that calculus:

1. **Tool UI ships purpose-built components.** `@tool-ui/question-flow`, `@tool-ui/plan`, `@tool-ui/approval-card`, and `@tool-ui/option-list` are the exact UX surfaces our modal panels hand-roll today, with better affordances (branching questions, plan step statuses, receipt states, `choice` prop for post-answer render).
2. **Replay parity gets easier.** A tool call with a submitted result is replay-safe by construction — the reducer is just threading AG-UI events. A `pendingQuestion` field is stateful ghost data that has to be cleared by the *next* status transition, which is exactly the shape of bugs that the `a2a-native-messaging` change set out to prevent.

The Claude Agent SDK already emits these as tool-shaped primitives at the bottom of the stack. Server-side we re-wrap them as `input-required` A2A `StatusUpdate`s, then the translator re-wraps them as `CustomEvent`s, and the client keeps them out of the message stream. This change unwinds two of those wraps.

## What Changes

- **Server translator emits `TOOL_CALL_*` for input-required, not `CustomEvent`.**
  - `A2AToAGUITranslator.BuildInputRequired(...)` currently emits `AGUIEventFactory.CreateQuestionPending(...)` / `CreatePlanPending(...)`, both of which produce `CustomEvent(name = "question.pending" | "plan.pending")`.
  - It SHALL instead emit `TOOL_CALL_START` + `TOOL_CALL_ARGS` + `TOOL_CALL_END` with a canonical `toolName` (`ask_user_question` for questions, `propose_plan` for plans) and the existing payload serialised into the args JSON.
  - No `TOOL_CALL_RESULT` is emitted at this point — the absence of a result is the "requires-action" signal.
- **Answer / approval flow returns a tool result instead of invoking a custom hub method.**
  - When the user submits via Tool UI (`addResult`), the client SHALL send a `TOOL_CALL_RESULT` through the existing `AnswerQuestion` / `ApprovePlan` hub methods **or** through a consolidated `SubmitToolResult(sessionId, toolCallId, resultJson)` hub method (pick during design — both compose with the existing worker HTTP POST paths).
  - The server-side bridge to the worker (`DockerAgentExecutionService.AnswerQuestionAsync` / `ApprovePlanAsync`) stays intact at the worker-HTTP layer; only the SignalR method shape and/or the AG-UI event that represents the answer may change.
  - The server SHALL append a `TOOL_CALL_RESULT` envelope to the session event log when the worker confirms the answer, so live and replay both see the completed tool call.
- **Reducer drops `pendingQuestion` and `pendingPlan` fields.**
  - `AGUISessionState.pendingQuestion` and `.pendingPlan` are removed.
  - The reducer no longer applies `CustomEvent(name = "question.pending" | "plan.pending" | "status.resumed")`; these become `TOOL_CALL_*` events handled by the existing tool-call paths.
  - The `status.resumed` signal that currently clears the pending fields is also redundant — once a `TOOL_CALL_RESULT` arrives, the tool transitions from requires-action to complete naturally.
- **Client adopts Tool UI `question-flow`, `plan`, `approval-card`.**
  - New Toolkit entries in `src/Homespun.Web/src/features/sessions/runtime/toolkit.tsx`:
    - `ask_user_question`: `type: "frontend"`, `render({ args, result, toolCallId, addResult })` → `@tool-ui/question-flow` (or `@tool-ui/option-list` for single-choice cases — decide during implementation based on `args.kind`).
    - `propose_plan`: `type: "frontend"`, `render` → `@tool-ui/plan` with `@tool-ui/approval-card` for the approve/reject action.
  - Both entries include the existing `keepContext` toggle and optional `feedback` text field for plan rejection as action-bar extras, preserving prior UX.
- **Remove `PlanApprovalPanel` and `QuestionPanel` from the session page.**
  - They become dead code once the tool-call renderers own the UX. Delete the components and their tests.
  - Their former position (sibling of the thread, pinned) is no longer a layout concern — the tool call renders inline in the message stream where it semantically belongs. If "pin the active requires-action tool above the composer" proves desirable in review, add it as a thread-level overlay in a follow-up; don't couple the concerns to this change.
- **Worker unchanged.**
  - The worker continues to emit `question_pending` / `plan_pending` control events that flow into A2A `StatusUpdate{state: InputRequired}`. The translation to AG-UI tool calls is purely a server concern.

## Capabilities

### New Capabilities
- `interactive-tool-calls`: Client-side Toolkit entries that render agent-initiated structured interactions (questions, plan approvals) as AG-UI tool calls, including the user-side `addResult` submission path.

### Modified Capabilities
- `session-messaging`: The translator's mapping for A2A `StatusUpdate{state: InputRequired}` changes from `CustomEvent(question.pending | plan.pending)` to `TOOL_CALL_*` events. Live/replay parity invariant is unchanged. The `question.pending` / `plan.pending` / `status.resumed` Custom event names are retired from the public translator contract.

## Impact

- **Server**: `src/Homespun.Server/Features/ClaudeCode/Services/A2AToAGUITranslator.cs`
  - `BuildInputRequired` rewritten to emit `TOOL_CALL_START/ARGS/END` with canonical tool names.
  - `ExtractPendingQuestion`, `ExtractPlanContent` helpers repurposed to build args JSON rather than custom-event payloads.
  - `AGUIEventFactory.CreateQuestionPending` / `CreatePlanPending` helpers deleted (or kept behind a `[Obsolete]` shim for one release if any external consumer exists — likely none).
  - The `TOOL_CALL_RESULT` path on answer/approval is new on the server: `AnswerQuestionAsync` and `ApprovePlanAsync` SHALL, on worker success, append a synthesised `TOOL_CALL_RESULT` envelope to the session event log keyed by the tool-call id generated at the `BuildInputRequired` time. The tool-call id must therefore be stable across the input-required → answered transition — design.md pins this.
- **Shared contracts**: `AGUICustomEventName.QuestionPending`, `.PlanPending`, `.StatusResumed` removed from the enum. Bump `Homespun.Shared` accordingly.
- **Client**: `src/Homespun.Web/`
  - `features/sessions/utils/agui-reducer.ts`: delete `pendingQuestion`, `pendingPlan`, related apply functions, and `appliedCustom` cases for the three retired names.
  - `features/sessions/components/plan-approval-panel.tsx`, `features/questions/QuestionPanel.tsx` and their tests — deleted.
  - `features/sessions/runtime/toolkit.tsx` gains two frontend-tool entries per the spec.
  - Hub client (`@/providers/signalr-provider`): `AnswerQuestion` / `ApprovePlan` invocations replaced (or wrapped) by a single tool-result submission path.
- **Worker**: unchanged.
- **Tests**:
  - Translator tests (`A2AToAGUITranslator.Tests`) gain coverage for the new tool-call mapping; existing `question.pending` / `plan.pending` tests rewritten, not merely deleted — behaviour moves, doesn't disappear.
  - Reducer tests shrink (no more pending-field cases).
  - New fixture-driven stories in the `chat-assistant-ui` Storybook covering `askUserQuestionToolCall`, `proposePlanToolCall` (extend `features/sessions/fixtures/envelopes.ts`).
- **Session-messaging spec**: requirement amended to reflect new translator mapping.
- **Dependencies**: Hard-depends on `chat-assistant-ui` (Toolkit plumbing is a prerequisite). Cannot merge before it.
- **Risk**: Medium. Two risks dominate:
  1. **Tool-call id stability across worker answer round-trip.** Current answer path bypasses the A2A log — the worker receives an HTTP POST, resolves the question, then emits new A2A events that continue the conversation. The `TOOL_CALL_RESULT` must be synthesised server-side with the same toolCallId as the original `TOOL_CALL_START`. Addressed in design.
  2. **Loss of the "pinned above composer" affordance.** Today the panel is always visible once pending. Inline tool-call rendering scrolls with the message list. If product wants pinning, add it later as a thread overlay — do not block this change on it.

## Migration & roll-back

- **Roll-back:** `git revert` the server translator change and restore the three Custom event names. Re-restore the panels. Client-side is revert-safe because the Toolkit entries are additive until the panels are deleted.
- **Wire protocol impact:** Active connections during deploy see both old-shape (pre-deploy server) and new-shape (post-deploy) events. Since reducer and translator changes land together, there is no intermediate state where the client receives tool-call events it doesn't understand — the client update ships simultaneously. However, a stale browser tab connected pre-deploy would receive tool-call events post-deploy and fail to render them. Mitigation: the client SHALL render unknown tool calls via the fallback (benign degradation), not throw.
- **Feature flag:** Not needed. Atomic deploy, revert is safe.
