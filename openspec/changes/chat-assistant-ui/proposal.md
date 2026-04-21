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
  - `components/ui/prompt-input.tsx`, `message.tsx`, `scroll-to-bottom.tsx`, `thinking-bar.tsx`, `text-shimmer.tsx`, `loader.tsx`, `markdown.tsx`, `code-block.tsx` — replaced by AUI equivalents.
  - Corresponding tests go with them.

- **Rebuild `chat-input.tsx` on `ComposerPrimitive`.**
  - Preserve the mode dropdown (Plan/Build), model dropdown (Opus/Sonnet/Haiku), and `@`-mention search popup integration. These are session controls that wrap the Composer, not replacements for it.

- **Reimagine tool-result rendering via `makeAssistantToolUI`.**
  - One `makeAssistantToolUI({ toolName: "Bash" | "Read" | "Grep" | "Write" })` registration per tool, carrying forward the same JSX each currently emits.
  - Generic fallback via `ToolFallback` for unrecognised tools.

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

- Cherry-picking AI Elements `Plan` / `Reasoning` presentational components to replace the plan-approval and thinking surfaces if the AUI defaults are not a good fit.
- ThreadList / multi-thread UI. Sessions are currently one-thread-per-session; if we ever want to show historical sessions as a sidebar list, the AUI `ThreadListPrimitive` is the natural fit — but that's a product decision, not this change.
- Attachments / voice / branching. Not features of the current chat; not adding them here.
