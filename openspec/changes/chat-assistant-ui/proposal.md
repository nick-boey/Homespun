## Why

`src/Homespun.Web/src/features/sessions/` hand-rolls ~2,000 lines of chat UI: message list with tool-call grouping, a streaming composer, tool-execution cards with per-tool result renderers, plan approval, pending-question panel, scroll-to-bottom, markdown rendering, code blocks, and skeleton loaders. The AG-UI reducer that drives this UI is good and stays; the rendering layer above it is boilerplate that duplicates what a mature assistant UI library provides.

Assistant UI's `useExternalStoreRuntime` is a clean match: the reducer output feeds in as a read-only message array, primitives (`Thread`, `Message`, `Composer`, `ToolGroup`, `ActionBar`, `MessagePartPrimitive`) handle the DOM, and `makeAssistantToolUI` replaces the per-tool renderers. The invariant that matters most — **live and replay produce identical AG-UI streams** — stays protected because the reducer is unchanged and Assistant UI is downstream of it.

We evaluated and rejected two alternatives:
- **`@assistant-ui/react-ag-ui` (0.0.26, experimental).** It is an HTTP-based AG-UI agent client with a non-idempotent `RunAggregator`. Feeding SignalR envelopes through it would require faking an `HttpAgent`, and re-applying replayed envelopes would double-append text deltas and duplicate tool calls. It is designed for apps that don't already own a reducer; we do.
- **AI Elements (Vercel).** A copy-paste shadcn-style component library with no runtime. It replaces the *look* of chat components but leaves the structural complexity (partitioning, grouping, streaming, scroll, composer state) with us. Individual components (`Plan`, `Reasoning`, `CodeBlock`) are cherry-pickable as a future follow-up regardless of which runtime we pick.

## What Changes

- **Adopt `@assistant-ui/react` + primitives as the chat rendering layer.**
  - Install `@assistant-ui/react`, `@assistant-ui/core` (transitively), `assistant-stream`.
  - Wrap the session detail page in `AssistantRuntimeProvider` with a runtime produced by `useExternalStoreRuntime`.

- **Keep `agui-reducer.ts` as the single source of truth for message state.**
  - No changes to envelope ingestion, idempotence guarantees, or `seq` monotonicity.
  - Both the SignalR live path and the `GET /api/sessions/{id}/events` replay path continue to feed the reducer.

- **Add a `convertAGUIMessage(AGUIMessage): ThreadMessageLike` adapter.**
  - Pure, colocated with the runtime hook.
  - Maps: text block → text part; thinking block → reasoning part; toolUse block → tool-call part (with `toolCallId`, `toolName`, `argsText`, `result`).
  - No other state translation — `pendingPlan`, `pendingQuestion`, `systemInit`, `hookEvents` are session-level concerns, not conversation content.

- **Delete custom chat components that the primitives replace.**
  - `features/sessions/components/message-list.tsx`, `tool-execution-group.tsx`, `tool-execution-row.tsx`, `tool-results/*`.
  - `features/sessions/utils/agui-to-display-items.ts` (partitioning/grouping now lives in AUI's Thread primitive).
  - `components/ui/prompt-input.tsx`, `message.tsx`, `scroll-to-bottom.tsx`, `thinking-bar.tsx`, `text-shimmer.tsx`, `loader.tsx` — replaced by AUI equivalents.
  - **Replace, don't just delete, `code-block.tsx` and `markdown.tsx`.** Tool UI's registry ships richer `@tool-ui/code-block` and we'll take it; for `markdown.tsx` decide based on AUI's default text part rendering (verify at task 2.x and record the call in design.md).
  - Corresponding tests go with them.

- **Rebuild `chat-input.tsx` on `ComposerPrimitive`.**
  - Preserve the mode dropdown (Plan/Build), model dropdown (Opus/Sonnet/Haiku), and `@`-mention search popup integration. These are session controls that wrap the Composer, not replacements for it.

- **Reimagine tool-result rendering via the Tool UI `Toolkit` API.**
  - Use the newer `Toolkit` object + `Tools({ toolkit })` + `useAui(...)` pattern (the pattern the `tool-ui` skill and shadcn-registry components are wired for). Do NOT adopt the older `makeAssistantToolUI` component-per-tool pattern — this change is a single hop to the idiomatic API, not a two-hop through the legacy one.
  - One Toolkit entry per known backend tool (`Bash`, `Read`, `Grep`, `Write`), `type: "backend"`, `render({ result })` carrying forward the same JSX each currently emits.
  - `ToolFallback` (or a thin equivalent) for unregistered tool names.
  - **Adopt Tool UI registry display components where they beat ours.** Install `@tool-ui/code-block` and `@tool-ui/terminal` for Bash/Read/Grep/Write renderers (richer than our current `code-block.tsx`). Install `@tool-ui/data-table` only if a tool's result shape calls for it. Keep our `markdown.tsx` if AUI's default rendering isn't what we want for assistant text — decide per-component, not wholesale.

- **Move `PlanApprovalPanel` and `QuestionPanel` out of the message stream.**
  - They are session-level modal surfaces. They read `pendingPlan` / `pendingQuestion` from the reducer and render as siblings to `<ThreadPrimitive.Root>`, not as items inside it.
  - This is a correctness win — today they render mid-list, which implies they are message content; they are not.

- **Fixture-driven Storybook stories.**
  - Author a scripted AG-UI envelope fixture covering: streaming text, tool-call lifecycle, tool result, thinking block, plan-pending, question-pending, run-error.
  - Stories feed the fixture through the reducer and `useExternalStoreRuntime` so the full chat surface is testable without a server.
  - Piggybacks on the Storybook harness established by `web-ui-foundations`.

- **CLAUDE.md update.**
  - `src/Homespun.Web/CLAUDE.md`: replace the prompt-kit guidance with Assistant UI guidance (use AUI primitives for anything chat-shaped; `makeAssistantToolUI` for tool rendering).
  - Project-root `CLAUDE.md`: unchanged.

Behavior kept identical:
- SignalR contract (`ReceiveSessionEvent`), `SessionEventEnvelope` shape, `/api/sessions/{id}/events` replay contract — untouched.
- Plan-approval API (`POST /api/sessions/{id}/plan/approve|reject`), question-answer API, send-message API — untouched.
- AG-UI reducer state shape and idempotence invariant — untouched.
- Visual theme — still zinc/new-york from `web-ui-foundations`.

## Capabilities

### New Capabilities
- `chat-assistant-ui`: Client-side chat runtime backed by `@assistant-ui/react`'s `ExternalStoreRuntime`, driven by the existing AG-UI reducer. Covers message rendering, composer, tool-call UI, and a fixture-driven Storybook surface for the chat state space.

### Modified Capabilities
- `session-messaging` (minor, documentation only): reaffirms that the client's rendering layer consumes reducer output through `ExternalStoreRuntime` but does not change the replay/live parity contract. No requirement changes.

## Impact

- **Frontend**: `src/Homespun.Web/`
  - `features/sessions/components/message-list.tsx` (186 lines) — deleted.
  - `features/sessions/components/tool-execution-group.tsx` (100 lines) — deleted.
  - `features/sessions/components/tool-execution-row.tsx` (123 lines) — deleted.
  - `features/sessions/components/tool-results/*` (7 files, ~250 lines) — reimplemented as `makeAssistantToolUI` registrations.
  - `features/sessions/components/chat-input.tsx` (~150 lines) — rewritten on ComposerPrimitive; mode/model controls preserved.
  - `features/sessions/components/plan-approval-panel.tsx`, `features/questions/QuestionPanel` — unchanged logic, repositioned as siblings of the Thread.
  - `features/sessions/utils/agui-to-display-items.ts` — deleted.
  - `features/sessions/utils/agui-reducer.ts` — unchanged. Same tests pass.
  - `components/ui/{prompt-input, message, scroll-to-bottom, thinking-bar, text-shimmer, loader, markdown, code-block}.tsx` — deleted (all classified `chat-owned` in `web-ui-foundations`'s `INVENTORY.md`).
  - Tests: ~900 lines deleted (`message-list.test.tsx`, `tool-execution-group.test.tsx`, `tool-execution-row.test.tsx`, `tool-result-renderer.test.tsx`, `prompt-input.test.tsx`, `markdown.test.tsx`, `scroll-to-bottom.test.tsx`). Replaced by ~400 lines of Storybook-fixture-driven stories + a much smaller runtime wiring test.
  - New: `features/sessions/runtime/useSessionAssistantRuntime.ts`, `features/sessions/runtime/convertAGUIMessage.ts`, `features/sessions/runtime/tool-ui/*` (one per known tool), `*.stories.tsx` for the chat surface.
  - `package.json`: `+@assistant-ui/react`, `+assistant-stream`.
  - `CLAUDE.md`: guidance flipped from prompt-kit to Assistant UI.
- **Backend / Worker / Shared**: unaffected.
- **Server spec (`session-messaging`)**: no requirement changes.
- **Risk**: Medium. The reducer stays intact (low-risk foundation), but the DOM tree underneath the chat page changes significantly. Mitigated by fixture-driven stories covering the full state space and by the e2e tests that remain unchanged.
- **Dependencies**: Soft-depends on `web-ui-foundations` for the Storybook harness. Can land sequentially; ordering preferred but not strictly required.

## Migration & roll-back

- Ship behind no feature flag. The new runtime and the old components cannot coexist cleanly (they'd both subscribe to the same SignalR envelopes and the reducer output is the single source of truth). Atomic swap.
- Roll-back is `git revert`. No database, no server-side state change.
- Observability unchanged: the existing session-event SignalR spans + OTLP traces describe the contract that this change does not touch.

## Follow-ups (explicitly out of scope)

Two follow-up changes have dedicated proposals:

- **`questions-plans-as-tools`** — demote the `question.pending` / `plan.pending` `CustomEvent` shape to AG-UI `TOOL_CALL_*` events on the server translator, adopt Tool UI's `question-flow` / `plan` / `approval-card` components, and drop the `pendingPlan` / `pendingQuestion` reducer fields. Rationale: these are tool-shaped interactions that we modeled as modal state only because the old UI had nowhere cleaner to render them. See `openspec/changes/questions-plans-as-tools/`.
- **`composer-accepts-mid-stream`** — remove the client-side `isProcessing` gate that blocks the composer while the agent is running. The Claude Agent SDK supports streaming input (`AsyncIterable<SDKUserMessage>` + `streamInput()`), and our worker already pushes to a persistent `InputQueue` that Accepts follow-up messages mid-run. Only the UI disables typing today. See `openspec/changes/composer-accepts-mid-stream/`.

Considered and explicitly not proposed:

- **Swapping to `useAgUiRuntime` from `@assistant-ui/react-ag-ui`.** A spike during this proposal's design pass (`spike-findings.md`) found two independent blockers:
  1. **Run-lifecycle mismatch.** `useAgUiRuntime` dispatches events inside an `agent.runAgent(input, subscriber)` lifecycle; Homespun's worker runs autonomously and has no per-user-message run boundary. A mid-run page reload has no `runAgent` to anchor.
  2. **No mid-stream input.** The runtime's aggregator is per-run; it has no `streamInput`-equivalent. Adopting it would actively re-block the feature `composer-accepts-mid-stream` is adding.
  The `useExternalStoreRuntime` + reducer path is not transitional — it's the right shape for Homespun's autonomous-worker + mid-stream-input product model.

Out of scope for this change and not yet proposed:

- Cherry-picking AI Elements `Plan` / `Reasoning` presentational components to replace the plan-approval and thinking surfaces if the AUI defaults are not a good fit.
- ThreadList / multi-thread UI. Sessions are currently one-thread-per-session; if we ever want to show historical sessions as a sidebar list, the AUI `ThreadListPrimitive` is the natural fit — but that's a product decision, not this change.
- Attachments / voice / branching. Not features of the current chat; not adding them here.
