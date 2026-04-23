## Context

The chat UI in `src/Homespun.Web/src/features/sessions/` has two layers today:

1. **State — `agui-reducer.ts`** (504 lines, pure, idempotent). Takes `SessionEventEnvelope`s from both SignalR (live) and `GET /api/sessions/{sessionId}/events` (replay) and folds them into an `AGUISessionState`. Dedup by composite key `${eventId}::${event.type}`; strict `seq` monotonicity. This is the load-bearing piece — it underwrites the `session-messaging` capability's live/replay parity invariant.

2. **Render — `features/sessions/components/*` + `components/ui/{prompt-input,message,scroll-to-bottom,…}`** (~2,000 lines). Handles partitioning messages into chat bubbles + tool-call groups, tool-call-to-result pairing via `toolCallId` lookup, scroll-to-bottom, custom composer with mode/model selectors, markdown/code rendering, skeleton loaders.

The render layer is substantially what `@assistant-ui/react` provides out of the box. The question is not whether Assistant UI is a good fit — it is — but *how* to adopt it without touching the reducer-owned invariants.

We evaluated three integration strategies and picked one.

## Goals / Non-Goals

**Goals:**
- Replace the hand-rolled message-list + tool-execution + composer + markdown/code/shimmer/scroll stack with `@assistant-ui/react` primitives.
- Preserve the AG-UI reducer unchanged; keep live/replay envelope parity as strong as it is today.
- Rewire tool-result rendering through `makeAssistantToolUI` registrations, one per known tool (Bash, Read, Grep, Write) plus `ToolFallback` for the rest.
- Preserve session-level UX: mode switching (Plan/Build), model switching (Opus/Sonnet/Haiku), `@`-mention search, plan approval, pending-question answering.
- Produce a Storybook story surface that drives the chat page end-to-end through a scripted AG-UI envelope fixture.

**Non-Goals:**
- Adopting `@assistant-ui/react-ag-ui`. See D1 for why — its `RunAggregator` is not idempotent-by-eventId and does not fit replay.
- Adopting AI Elements as the primary runtime. It's a skin library, not a runtime; the complexity we want to delete is structural, not cosmetic.
- Changing the AG-UI reducer, the `SessionEventEnvelope` shape, the SignalR hub contract, or the replay endpoint.
- Adding multi-thread UI, attachments, voice, branching, or editing — AUI supports them but we don't have them today.
- Re-theming. `web-ui-foundations` owns the theme; this change consumes it.

## Decisions

### D1: Use `useExternalStoreRuntime` directly, not `@assistant-ui/react-ag-ui`

**Decision:** Build the runtime on Assistant UI's `useExternalStoreRuntime`, with the existing AG-UI reducer producing the message array. Do not adopt `@assistant-ui/react-ag-ui`.

**Rationale:** The `react-ag-ui` package (v0.0.26, marked `@experimental`) is an HTTP-based client: it owns an `HttpAgent` connection, drives events through a `RunAggregator` that maintains `Map<string, ToolCallState>` and text-buffer counters, and assumes a request/response run lifecycle. Three disqualifying facts:

1. **No eventId dedup.** Re-applying the same event double-appends text deltas and duplicates tool-call state. The reducer invariant `appliedEnvelopeKeys.add(\`${eventId}::${event.type}\`)` has no counterpart.
2. **Run-lifecycle assumption.** `runAgent(input, subscriber)` is a single open-close operation. Replay of a historical session doesn't have "a run" — it has a sequence of envelopes to apply in order. Bridging to this shape requires a fake `HttpAgent` whose `runAgent` synthesises events from a store, which defeats the simplification.
3. **Experimental API.** Types declare: *"This API is still under active development and might change without notice."* Taking a pre-1.0 experimental dependency for a load-bearing client invariant is the wrong risk.

`useExternalStoreRuntime` is the right primitive. It's stable (part of `@assistant-ui/core`), capability-based (handlers you don't provide hide their UI affordances), and — critically — reads `messages` from whatever state you give it. Our reducer is that state.

**Consequence:** We write a tiny pure `convertAGUIMessage` function and a thin `useSessionAssistantRuntime` hook. The reducer is still the single source of truth. Replay parity holds by construction.

### D2: `pendingPlan` and `pendingQuestion` live outside the message stream

**Decision:** `PlanApprovalPanel` and `QuestionPanel` are rendered as siblings of `<ThreadPrimitive.Root>`, reading their state directly from the reducer (via the existing Zustand stores / hooks). They do not participate in Assistant UI's message tree.

**Rationale:** Today they are rendered *inside* `MessageList`, which implies they are conversation content. They are not — they are session-level modal state that gates further interaction. Assistant UI's message model has fixed roles (`user`, `assistant`, `system`, `tool`), and the `data-*` custom part mechanism is technically able to carry them, but doing so would:
- Pretend they are content that could be interleaved with chat, when they cannot (there is only ever zero or one active).
- Complicate the convertMessage logic with a synthetic "pending" message that disappears on approval/answer.
- Tie their presentation to the Thread's scroll behaviour, when they deserve to be pinned.

**Consequence:** The two panels keep their existing logic almost verbatim. Only their position in the JSX tree changes.

### D3: Tool UI `Toolkit` API, not `makeAssistantToolUI`

**Decision:** Use Assistant UI's `Toolkit` API — a single `toolkit` object exporting one `{ type, description?, parameters?, render }` entry per tool, passed through `Tools({ toolkit })` into `useAui(...)` and rendered by AUI's tool-call layer. Do NOT adopt the `makeAssistantToolUI` component-registration pattern.

**Rationale:** Spike B (`spike-findings.md`) confirmed that `Toolkit` and `makeAssistantToolUI` end up writing to the **same** `aui.tools().setToolUI` store — they are two surfaces over one mechanism, not two distinct generations. So the decision is about ergonomics + ecosystem fit, not compatibility:

1. **The `tool-ui` skill and shadcn registry components are written against `Toolkit`.** Every Tool UI component's example uses the `Toolkit` entry shape. Using `makeAssistantToolUI` would mean translating examples at every step.
2. **`Toolkit` carries `type` + `parameters` for frontend tools.** Backend tools (our Bash/Read/Grep/Write — all worker-implemented) only need `render`. But the `questions-plans-as-tools` follow-up adds **frontend** tools (`ask_user_question`, `propose_plan`) where the user commits results via `addResult` — those need `parameters` (zod schema) for model-context registration. `makeAssistantToolUI` has no analogue for this.
3. **Single hop.** Using `Toolkit` here means zero rewriting when `questions-plans-as-tools` lands.

**Consequence:** `features/sessions/runtime/toolkit.tsx` exports a `Toolkit` object. Spike B confirmed the runtime-agnostic render lookup (`MessageParts.js:98 — s.tools.tools[props.toolName] ?? Fallback`), so `useExternalStoreRuntime` composes with `Toolkit` without any special wiring. AUI ships a built-in Fallback at that lookup site — we don't need to register a fallback manually unless we want a Homespun-branded one. Phase 4 task set simplifies accordingly.

### D7: Adopt Tool UI registry display components selectively

**Decision:** When a Tool UI registry component materially improves on our hand-rolled UI component, install it via `npx shadcn@latest add @tool-ui/<id>` and use it. When our component is already fit for purpose, keep ours. Decide per-component, not wholesale.

**Concretely:**

| Area | Today | Plan | Why |
|---|---|---|---|
| Code display in Bash/Read/Grep/Write | `components/ui/code-block.tsx` (thin prompt-kit wrapper) | Install `@tool-ui/code-block` | Registry component has richer copy/collapse/line-number affordances and is actively maintained. |
| Shell-style output for Bash stdout | Same `code-block.tsx` | Consider `@tool-ui/terminal` for Bash only | Terminal styling reads as "command output" better. If we keep `code-block` for Bash too, no harm done — defer to implementation. |
| Markdown for assistant text | `components/ui/markdown.tsx` (prompt-kit wrapper) | Decide at task 2.x | AUI's default text-part renderer may or may not want GFM + syntax-highlighted fenced blocks. If the default is weaker, keep our `markdown.tsx` and pass it into AUI's `MessagePrimitive.Parts`; if it's equivalent or stronger, delete ours. |
| Thinking / reasoning | `components/ui/thinking-bar.tsx` + `text-shimmer.tsx` | Use AUI's reasoning part + (optionally) `ChainOfThought` primitive | AUI-native. Thinking-bar's collapse behaviour should be replicable inside a custom renderer wrapper. |
| Scroll-to-bottom, loader, message shell | `components/ui/{scroll-to-bottom, loader, message}.tsx` | Delete; rely on AUI `Thread` + `Message` primitives | No value left in keeping ours. |

**Rationale:** The `web-ui-foundations` INVENTORY classified all of these as "chat-owned", meaning they were expected to be deleted alongside this change. But there are two very different deletion rationales — "replaced by AUI primitive" and "replaced by Tool UI registry component" — and conflating them loses the richer option. This decision names both explicitly.

**Consequence:** Phase 1 grows a small dependency-install sub-step: `npx shadcn@latest add @tool-ui/code-block @tool-ui/terminal`. The delete list in Phase 5 shrinks for code-block; grows nothing. Net line count still down because the registry components live under `src/components/tool-ui/` (shadcn convention) rather than extending `components/ui/`.

### D8: `@assistant-ui/react-ag-ui` re-verification — DONE at design time (spike-findings.md)

**Status:** Completed as part of the `chat-assistant-ui` design pass, not deferred to Phase 1. See `spike-findings.md`.

**Result:** D1 stands, **reinforced by two independent findings**:

1. **Run-lifecycle mismatch (Spike A).** `useAgUiRuntime`'s event dispatch is driven by `agent.runAgent(input, subscriber)` — one request per run, with a per-run aggregator. Homespun's autonomous-worker sessions do not have per-user-message run boundaries. Pageloads mid-run have no `runAgent` to anchor against. `MESSAGES_SNAPSHOT` handling exists but lives inside the run lifecycle.
2. **No mid-stream input (Spike C).** The runtime's aggregator has no `streamInput`-equivalent. The Claude SDK supports `streamInput` natively; our worker already uses streaming input mode via a persistent `InputQueue`. Only a single client-side `isProcessing` check gates mid-stream sends today. Adopting `useAgUiRuntime` would re-block that feature at the runtime level — the follow-up change `composer-accepts-mid-stream` would be in direct tension with the swap.

**Consequence:** `chat-assistant-ui` proceeds on `useExternalStoreRuntime` + reducer. The previously-proposed `ag-ui-runtime-swap` change has been dropped entirely. The spike memo is the written-down record of the analysis; starting over from a fresh proposal is the right move if upstream ever addresses both constraints.

### D4: Sessions page still owns the SignalR hook and the reducer

**Decision:** The `useSessionEvents` hook (or equivalent) that subscribes to SignalR and feeds the reducer stays in `features/sessions/hooks/`. `useSessionAssistantRuntime` consumes the reducer's output state and returns an `AssistantRuntime`. Composition is: session page → `useSessionEvents` → reducer → `useSessionAssistantRuntime` → `AssistantRuntimeProvider`.

**Rationale:** Clean layering. The SignalR/replay concerns are separate from the rendering runtime. Swapping SignalR for another transport (hypothetical) wouldn't touch the AUI wiring.

**Consequence:** The existing tests for `useSessionEvents` and `agui-reducer` stay valid. New tests are scoped to `convertAGUIMessage` (pure, small) and one integration test that asserts the full pipeline renders the expected DOM for a fixed envelope fixture.

### D5: Storybook stories carry the regression surface

**Decision:** The chat surface's regression surface moves from component-level DOM tests (`message-list.test.tsx` etc.) to Storybook stories driven by scripted AG-UI envelope fixtures.

**Rationale:** The component-level tests today are testing our partitioning/grouping code. Once AUI owns that code, those tests are testing AUI, which is their job. What we want to test is *our contract with AUI* — that a given envelope fixture produces a given user-visible outcome. Stories with `play` functions (via `addon-vitest`) express that contract at the right level.

**Consequence:** Net test surface shrinks in line count but gains fixture coverage that the current tests lack (e.g. a scripted "tool call that errors mid-stream").

### D6: Atomic swap, no feature flag

**Decision:** The old components and the new runtime cannot coexist. They both attempt to own rendering of the session event stream. We swap in a single PR.

**Rationale:** A flagged coexistence would require a second reducer consumer and parallel DOM trees — more risk than an atomic swap, not less. Revert is `git revert`.

**Consequence:** Reviewers and QA see the full change at once. The PR is larger than ideal; to mitigate, we land `web-ui-foundations` first so the Storybook harness + classification inventory reduce review surface area.

## Risks / Trade-offs

| Risk | Mitigation |
|---|---|
| Assistant UI primitives styled differently from current shadcn look | AUI primitives are headless (unstyled). We style them with the same zinc theme. Visual diff is expected to be small; any material delta is a bug. |
| Tool-call result ordering differs from current grouping | AUI groups tool-calls inside the assistant message by `toolCallId`. Our reducer already emits them inside the parent message, so ordering matches. Tested by fixture story. |
| Streaming text animation feel differs | We run `includePartialMessages=false`, so we already commit whole blocks, not tokens. AUI handles both shapes; visual difference should be negligible. |
| Composer loses mode/model selectors | We rebuild them as Composer siblings (the mode and model are not message content). Existing dropdown logic ports directly. |
| `@`-mention search integration breaks | The `MentionSearchPopup` is a Portal overlay triggered by keypresses. It is transport-agnostic — it can attach to the Composer textarea the same way it attaches today. |
| Plan approval / question panel repositioning looks wrong | D2 — they move out of the scroll list, pinned as siblings. This is also a correctness improvement. Validated in Storybook. |
| `makeAssistantToolUI` can't represent an expanding/collapsing detail view | AUI supports custom JSX arbitrarily inside a tool UI — the collapse/expand state is local React state; no AUI constraint. |
| AG-UI reducer's `unknownEvents` / `hookEvents` have no rendering path in AUI | They don't render today either (diagnostic/observability). No change. |
| Experimental-labelled APIs inside `@assistant-ui/react` (MessagePartPrimitive, ChainOfThought) | Use the stable exports where possible; isolate experimental usage in a single file so a future bump is localised. |
| Bundle size | AUI adds ~40KB gzipped at current tree-shake quality. Deletions we get back are comparable (prompt-kit primitives + our DOM code). Net ≈ neutral. |

## Open Questions

- **Q1:** Does AUI's `MessagePrimitive` expose a way to render `reasoning` parts collapsed-by-default, or do we need a custom wrapper? `ChainOfThought` primitive exists and is the right answer; verify at implementation time.
- **Q2:** The current plan-approval UI supports `keepContext` toggle. That's session control, not AUI — stays as a panel prop. Confirm during task 4.x.
- **Q3:** Our `TEXT_MESSAGE_CHUNK` / `TOOL_CALL_CHUNK` paths (from the AG-UI spec) are not emitted by the worker today (we use the explicit START/CONTENT/END triple). If the worker ever starts emitting chunks, does the reducer cover them? Currently it does not — they'd fall into `unknownEvents`. Out of scope for this change but worth an issue.
