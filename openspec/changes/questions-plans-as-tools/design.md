## Context

The worker's Claude Agent SDK emits question/plan requests as "input-required" control events. Today the stack wraps them twice:

```
Claude SDK          Worker A2A                    Server translator         Client reducer
────────────        ──────────                    ─────────────────         ──────────────
question_pending ─▶ StatusUpdate                 ─▶ CustomEvent            ─▶ pendingQuestion
                    { state: input-required,         { name: "question        (ghost state,
                      inputType: question,             .pending",             cleared by
                      question: {…} }                  data: {…} }            status.resumed)

plan_pending     ─▶ StatusUpdate                 ─▶ CustomEvent            ─▶ pendingPlan
                    { state: input-required,         { name: "plan.pending"   (same shape)
                      inputType: plan-approval,        data: {…} }
                      plan: {…} }
```

The user-side reply currently bypasses the AG-UI stream entirely — `SignalR.invoke("AnswerQuestion", sessionId, json)` → server → HTTP POST to worker resolve endpoint. There is no AG-UI envelope representing the answer; the conversation continues when the worker next emits normal `Message` events.

This change reshapes the middle and right halves of that diagram to:

```
Worker A2A                           Server translator               Client Toolkit
──────────                           ─────────────────               ──────────────
StatusUpdate                       ─▶ TOOL_CALL_START              ─▶ render({args, result: undefined})
  { state: input-required,           { toolCallId: T, toolName:      → @tool-ui/question-flow
    inputType: question,                "ask_user_question" }          or @tool-ui/plan
    question: {…} }                  TOOL_CALL_ARGS                 │
                                     { toolCallId: T, args: {…} }   │  user commits
                                     TOOL_CALL_END                  │  ▼
                                                                    addResult(decision)
                                                                    │
                            ┌───────────────────────────────────────┘
                            ▼
                  client → server hub → worker HTTP resolve
                                  │
                                  ▼ worker confirms
                  server appends TOOL_CALL_RESULT
                                  { toolCallId: T,
                                    result: decision }
```

## Goals / Non-Goals

**Goals:**
- Make question/plan state part of the conversation content (tool calls), not ghost state.
- Replay becomes trivial: `TOOL_CALL_START/ARGS/END` + `TOOL_CALL_RESULT` fold deterministically, no special-case fields.
- Adopt Tool UI's purpose-built components (`question-flow`, `plan`, `approval-card`).
- Preserve every user-facing behaviour the current panels have: the plan's `keepContext` toggle, free-text rejection feedback, single and multi-choice question variants.

**Non-Goals:**
- Changing the worker. It continues to emit `question_pending` / `plan_pending` control events.
- Removing the `AnswerQuestion` / `ApprovePlan` hub methods (optional — they can stay as thin shims; see D3).
- Adding "pin the active requires-action tool above the composer" layout behaviour. If product wants it, follow-up.
- Supporting branching multi-turn question flows beyond what `@tool-ui/question-flow` handles out of the box.

## Decisions

### D1: Canonical tool names are `ask_user_question` and `propose_plan`

**Decision:** The translator emits `toolName = "ask_user_question"` for questions and `toolName = "propose_plan"` for plans. Snake_case, stable, namespaced by intent not implementation.

**Rationale:** These names end up on the wire and in the event log forever. Aligning with Tool UI conventions (`option_list`, `approval_card` are snake_case in the skill's examples) and using intent-shaped names (`propose_plan` rather than `plan_approval`) keeps the door open for future variants (`revise_plan`?) without wire-protocol churn.

**Consequence:** Hard-coded string constants in the translator and the client toolkit. Document in `session-messaging` spec.

### D2: Tool-call id is assigned by the translator and reused for the result

**Decision:** When the translator encounters an `input-required` `StatusUpdate`, it generates a GUID `toolCallId = T` and includes `T` in the emitted `TOOL_CALL_START/ARGS/END`. The server remembers `T` keyed by session id so that when the user's answer is confirmed by the worker, `TOOL_CALL_RESULT{toolCallId: T, …}` can be appended to the event log.

**Rationale:** The `TOOL_CALL_RESULT` has to reference the start's id or the client can't match them. The worker doesn't know the translator-assigned id. Two options:
- (a) Have the worker assign the id and include it in the A2A `StatusUpdate` metadata. Requires a worker change.
- (b) Have the translator assign it and cache it server-side.

Option (b) is server-only. The cache lives in the same process that owns the event log; there is at most one pending requires-action per session (the worker won't issue a second question before the first is resolved). A simple `ConcurrentDictionary<sessionId, Queue<toolCallId>>` keyed by session id with a single active entry suffices.

**Consequence:** The translator is no longer pure — it has side effects on a cache. Design note: isolate the cache behind an `IPendingToolCallRegistry` so the translator's signature becomes `Translate(event, ctx, registry)` and testability is preserved. Existing translator tests continue to work with an in-memory registry instance.

### D3: Keep `AnswerQuestion` / `ApprovePlan` hub methods; add server-side result synthesis

**Decision:** The SignalR hub keeps its `AnswerQuestion(sessionId, answersJson)` and `ApprovePlan(sessionId, approved, keepContext, feedback?)` methods. The handler path is extended so that, after the worker confirms success, the server appends a `TOOL_CALL_RESULT` envelope to the event log keyed by the session's current pending `toolCallId`.

**Alternatives considered:**
- *Single `SubmitToolResult(sessionId, toolCallId, resultJson)` hub method.* More orthogonal, but the client-side Toolkit `render`'s `addResult(value)` already receives typed values (selection string, approval boolean + metadata). Collapsing both through one `SubmitToolResult` requires the client to serialise result envelopes by toolName, which duplicates the dispatching the worker already does.
- *Pure AG-UI submission via a new `TOOL_CALL_RESULT` client→server event.* Cleanest protocol-wise, but our hub is not a two-way AG-UI event channel; building it would constitute protocol work beyond the scope of this change. Reasonable for `ag-ui-runtime-swap`.

**Rationale:** Preserving the hub methods means the worker-side resolution path (`POST /sessions/{id}/questions/{qid}/answer` etc.) is untouched. Only the server's post-worker step grows a single "synthesise result envelope" call. Minimum-surface change.

**Consequence:** Two hub methods instead of one; two places in the server that synthesise `TOOL_CALL_RESULT`. Acceptable — same as the current asymmetry between question and plan answer shapes. Encapsulate behind `IToolCallResultAppender.AppendAsync(sessionId, toolCallId, resultPayload)` to keep the translator + ingestor tidy.

### D4: The reducer stays; it just loses two fields

**Decision:** Delete `pendingQuestion`, `pendingPlan`, and the `appliedCustom` branches for `question.pending` / `plan.pending` / `status.resumed`. The reducer otherwise remains the idempotence + live/replay parity enforcer.

**Rationale:** The live/replay invariant still needs a gatekeeper. Removing the reducer entirely is the job of `ag-ui-runtime-swap`; this change just removes the fields that were ghost-state.

**Consequence:** `appliedEnvelopeKeys`, `lastSeenSeq`, `seq`-monotonicity checks — unchanged. The reducer's line count drops meaningfully (~80 LOC); its contract narrows usefully.

### D5: Tool UI components are the source of truth for the UX surface

**Decision:** The Toolkit `render` functions use `@tool-ui/question-flow`, `@tool-ui/option-list`, `@tool-ui/plan`, and `@tool-ui/approval-card` directly — no wrapper layer. If a component's API doesn't quite match our needs (e.g. plan's `keepContext` toggle isn't a first-class prop), we mount the secondary controls as siblings (`ApprovalCard` supports `onConfirm` / `onCancel`; `keepContext` is a separate `Switch` sibling in the same container).

**Rationale:** Wrappers at this layer tend to re-introduce the exact complexity we're trying to delete. Keep the `render` functions thin so a future Tool UI component upgrade is a re-install, not a wrapper refactor.

**Consequence:** Per-component styling is dictated by Tool UI + our zinc theme. Prototypes in Storybook (fixture-driven stories, extend the `chat-assistant-ui` harness) validate the visual fit before deletion of the old panels.

### D6: Stale browser tabs degrade gracefully, don't block the deploy

**Decision:** The client unconditionally renders an "unsupported tool" fallback for any tool-call `toolName` not in its Toolkit. No feature flag. No gradual rollout.

**Rationale:** A stale tab sees a `TOOL_CALL_START{toolName: "ask_user_question"}` and either:
- The user has refreshed between connect and event → new client renders correctly.
- The user is on a pre-deploy tab → old client's tool-call renderer would see a tool name it's never seen (today the old client doesn't render unknown tool-use blocks — they go into a generic "tool result" placeholder).

Concretely: the old client's `tool-result-renderer.tsx` handles unknown tools via `generic-tool-result.tsx`. So the old client will show the question's args-JSON as a code block instead of an interactive component. Ugly but not broken. Refresh fixes it.

**Consequence:** No phased migration. Ship the change set together (translator + hub-result path + client Toolkit + reducer field removal) in one deploy.

## Risks / Trade-offs

| Risk | Mitigation |
|---|---|
| Pending tool-call id cache leaks if worker never confirms an answer | Bounded per-session (max 1 active). Clear on session-end. Worker is also responsible for confirming; a missed confirm is already an observable bug. |
| Order-of-operations bug: `TOOL_CALL_RESULT` appended before the worker's next `Message` events | Result append is synchronous on the hub invocation's thread; worker events arrive via the ingestor. Both serialize through `A2AEventStore.AppendAsync`, which already enforces monotonic `seq`. Tests cover the happy path. |
| `keepContext` toggle not surfaced inside `@tool-ui/approval-card` props | Render it as a `Switch` sibling of the ApprovalCard inside the same `render` callback. Tool UI components are composable. |
| Free-text rejection feedback doesn't fit ApprovalCard | Same — add a `Textarea` as a sibling. Or pick a Tool UI component that supports it natively (check `@tool-ui/message-draft` for a pre-send review pattern). |
| Test surface changes: translator tests need rewriting | `A2AToAGUITranslator.Tests` adds new cases for `TOOL_CALL_*` emission; existing `question.pending` / `plan.pending` assertions are inverted (now asserting absence). Roughly neutral on line count. |
| Result id drift if worker answer handler is idempotent and called twice | Guard the result-append with a per-session "already answered" check keyed by `toolCallId`. Second invocation is a no-op. |

## Open Questions

- **Q1:** Should `TOOL_CALL_RESULT` carry the full answer object, or a compact summary? Current panel sends full answer dict; recommend full for symmetry with the Claude SDK tool-result protocol.
- **Q2:** Does the `status.resumed` signal do any work beyond clearing `pendingQuestion`/`pendingPlan` today? If anything else depends on it, retiring the event name is a regression. Audit in Phase 1.
- **Q3:** Is the plan's `planFilePath` field still useful once the plan renders inline? The current plan-approval panel surfaces it; Tool UI's `@tool-ui/plan` may or may not. Preserve it in args JSON regardless.
