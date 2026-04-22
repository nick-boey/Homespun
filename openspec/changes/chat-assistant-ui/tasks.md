# Tasks: Chat Assistant UI Runtime

**Input**: Design documents in this change directory.
**Status**: Proposed — forward-looking.

## Path Conventions (Homespun)

| Concern | Path |
|---------|------|
| Runtime wiring | `src/Homespun.Web/src/features/sessions/runtime/` (new) |
| Tool UI registrations | `src/Homespun.Web/src/features/sessions/runtime/tool-ui/` (new) |
| Fixture data | `src/Homespun.Web/src/features/sessions/fixtures/` (new) |
| Reducer (unchanged) | `src/Homespun.Web/src/features/sessions/utils/agui-reducer.ts` |
| SignalR hook (unchanged) | `src/Homespun.Web/src/features/sessions/hooks/` |
| Chat surface (rewritten) | `src/Homespun.Web/src/features/sessions/components/` |
| Project instructions | `src/Homespun.Web/CLAUDE.md` |
| Stories | `*.stories.tsx` co-located with components |

---

## Phase 1: Dependencies and scaffolding

- [x] 1.0 **`@assistant-ui/react-ag-ui` re-verification — done at design time.** See `openspec/changes/chat-assistant-ui/spike-findings.md`. Verdict: D1 holds, reinforced — architectural mismatch (run-lifecycle dispatch vs our autonomous-worker model). Proceed on `useExternalStoreRuntime`.
- [x] 1.0b **Toolkit + `useExternalStoreRuntime` compatibility — done at design time.** See `spike-findings.md`. Verdict: compatible, runtime-agnostic by design (`MessageParts.js:98` lookup). AUI ships a built-in tool fallback. No additional task needed.
- [x] 1.1 Add `@assistant-ui/react` (latest stable), `assistant-stream` to `src/Homespun.Web/package.json`. Pin exactly.
- [x] 1.1b Install the Tool UI display components we plan to keep: `npx shadcn@latest add @tool-ui/code-block @tool-ui/terminal`. Commit the generated files under `src/components/tool-ui/` (shadcn convention). Do NOT install the full catalogue — only what D7 names.
- [x] 1.2 Create `features/sessions/runtime/` directory with placeholder `index.ts`.
- [x] 1.3 Confirm the `web-ui-foundations` Storybook harness is on the branch (either merged or rebased). Fixture-driven stories in Phase 5 depend on it.
- [x] 1.4 `npm install`, then `npm run typecheck` — green before touching anything else.

## Phase 2: TDD — convertAGUIMessage (pure)

- [x] 2.1 Create `features/sessions/runtime/convertAGUIMessage.ts` with an empty export; add `convertAGUIMessage.test.ts` next to it.
- [x] 2.2 Write failing tests for: text block → text part; thinking block → reasoning part; toolUse block → tool-call part (with `toolCallId`, `toolName`, `argsText`, `result`); multi-block assistant message preserves order; user and system roles pass through unchanged.
- [x] 2.3 Implement `convertAGUIMessage` to green the tests. Keep it pure, colocated, ~40 LOC.
- [x] 2.4 Add tests for edge cases: streaming text block with `isStreaming=true` still renders text; toolUse without a `result` renders with `result: undefined`; zero-content message renders with empty content array.

## Phase 3: TDD — useSessionAssistantRuntime (integration)

- [x] 3.1 Create `features/sessions/runtime/useSessionAssistantRuntime.ts`. This hook takes the reducer's current `AGUISessionState` plus a `sendMessage`/`cancel` pair and returns an `AssistantRuntime`.
- [x] 3.2 Write failing integration test in `useSessionAssistantRuntime.test.tsx` (React Testing Library) that: renders a `ThreadPrimitive.Root`, seeds the reducer with a scripted envelope sequence, asserts the rendered messages appear in DOM order.
- [x] 3.3 Implement the hook using `useExternalStoreRuntime({ messages, convertMessage: convertAGUIMessage, isRunning, onNew, onCancel })`. Green the test.
- [x] 3.4 Add a test for the replay path: feed a `MESSAGES_SNAPSHOT`-equivalent (a full reducer state) and assert the rendered DOM matches what an incremental feed of the same envelopes produces.
- [x] 3.5 Add a test for idempotence at the UI boundary: applying the same envelope twice through the reducer and rendering does not duplicate DOM nodes.

## Phase 4: Toolkit registration (design.md D3)

- [x] 4.1 Create `features/sessions/runtime/toolkit.tsx` exporting a single `toolkit: Toolkit` with four entries (`Bash`, `Read`, `Grep`, `Write`), each `type: "backend"`, each with a `render({ result })` callback. No `description` or `parameters` for backend tools.
- [x] 4.2 Port JSX 1:1 from `features/sessions/components/tool-results/*-tool-result.tsx` into the four `render` callbacks. Use the newly-installed `@tool-ui/code-block` (and `@tool-ui/terminal` for Bash if D7 preferred it) in place of the current `components/ui/code-block.tsx`.
- [x] 4.3 Handle unknown tools: rely on AUI's built-in fallback at the `MessageParts.js:98` lookup site (confirmed in spike-findings.md). Only add a Homespun-branded fallback if the built-in is visually inadequate.
- [x] 4.4 Wire the toolkit via `const aui = useAui({ tools: Tools({ toolkit }) })` in the session page, and pass it to `<AssistantRuntimeProvider runtime={runtime} aui={aui}>`. The runtime is still `useExternalStoreRuntime` per D1. (Wired inside `ChatSurface` so the composer and message tree share one provider.)
- [x] 4.5 Delete `features/sessions/components/tool-results/*` and its tests. No rename; the JSX now lives in `runtime/toolkit.tsx` as `render` callbacks.

## Phase 5: Replace the session detail render tree

- [x] 5.1 In `features/sessions/pages/session-detail.tsx` (or wherever `MessageList` is mounted today), replace the `<MessageList />` block with the AssistantRuntimeProvider/ThreadPrimitive/Composer composition. (Implemented as `ChatSurface` in `features/sessions/components/assistant-chat/ChatSurface.tsx`, wired at `routes/sessions.$sessionId.tsx`.)
- [x] 5.2 Implement `UserMessage`, `AssistantMessage` as thin wrappers around `MessagePrimitive` that map `text` / `reasoning` / `tool-call` parts. (`components/assistant-chat/messages.tsx`. Tool-call parts are resolved via the `aui.tools` store populated by the Toolkit — no explicit `tools` prop needed on MessagePrimitive.Parts. `ChainOfThought` not adopted; standard Reasoning part wrapper used.)
- [x] 5.3 Implement `Composer` on `ComposerPrimitive`: preserve the mode dropdown, model dropdown, and `@`-mention trigger. Keep logic; swap the DOM shell. (Deviation: the composer uses a plain `<form>` rather than `ComposerPrimitive.Root` because the `@`-mention popup needs React-state ownership of the textarea value, and `ComposerPrimitive.Root` binds to the AUI composer's internal text state, which is incompatible with programmatic value insertion from mention selection. All ChatInput tests still green. Documented here for follow-up.)
- [x] 5.4 Delete `features/sessions/components/message-list.tsx`, `tool-execution-group.tsx`, `tool-execution-row.tsx`, `features/sessions/utils/agui-to-display-items.ts` and their tests. (`chat-input.tsx` rewritten in place rather than deleted.)
- [x] 5.5 Delete `components/ui/{prompt-input,message,scroll-to-bottom,text-shimmer}.tsx` and their tests. **Kept** `loader.tsx` (10 non-chat imports across agents/issues/skills/routes) and `thinking-bar.tsx` (2 agent components) — the INVENTORY `chat-owned` classification was over-broad; deleting them would break non-chat features. `text-shimmer` was only used by `thinking-bar`, which now inlines a minimal shimmer wrapper.
- [x] 5.5b **Not deleted.** `components/ui/code-block.tsx` is still imported by `components/ui/markdown.tsx`, which we keep (task 5.5c). The four tool-result files that also imported it are gone; the only remaining consumer is markdown. Deferred as a follow-up if we ever rewire markdown onto `@tool-ui/code-block`.
- [x] 5.5c `components/ui/markdown.tsx` — **kept**, used by assistant text-part renderer and six non-chat features (issues, PRs, projects). `TextPart` in `components/assistant-chat/messages.tsx` renders through it.
- [x] 5.6 Run `npm run typecheck` — green after fixing the `thinking-bar` → `text-shimmer` import by inlining the shimmer span.

## Phase 6: Fixture-driven Storybook stories

- [x] 6.1 Create `features/sessions/fixtures/envelopes.ts` with named fixtures: `simpleTextTurn`, `toolCallLifecycle`, `thinkingBlock`, `multiBlockTurn`, `planPending`, `questionPending`, `runError`, `streamingInterrupted`. Each is a `SessionEventEnvelope[]`.
- [x] 6.2 Create a `sessions/ChatSurface.stories.tsx` that renders the full session page shell (including runtime + plan/question panels) against a chosen fixture. One story per fixture from 6.1.
- [x] 6.3 Add interactive stories for the Composer (`play` functions via `addon-vitest`) that type a message, assert submit, and verify the runtime's `onNew` callback fires. (Play function types + submits through `ChatInput` and asserts `onSend` was called with the composed message; our composer calls `onSend` directly rather than the AUI `onNew` path — see Phase 5 deviation — so the assertion is on `onSend` instead.)
- [x] 6.4 Run `npm run build-storybook` — builds successfully. Note: pre-existing Vite chunk-size warnings about shiki language bundles (wasm, emacs-lisp, cpp, etc.) are inherited from the tool-ui CodeBlock's shiki dependency and are not specific to the new stories. No build errors.

## Phase 7: Plan approval and question panel repositioning

- [x] 7.1 Move `PlanApprovalPanel` out of `MessageList`'s children; render as a pinned sibling of the Thread (likely above the Composer, below the message viewport). Preserve the existing hooks/handlers. (Rendered as part of `ChatSurface`'s `footerSlot` — sibling of `ThreadPrimitive.Viewport`, above `composer`.)
- [x] 7.2 Same for `QuestionPanel` (which today also renders mid-list in `MessageList`). (Same footerSlot placement.)
- [x] 7.3 Update the existing `plan-approval-panel.test.tsx` and `session-info-panel/*.test.tsx` tests where assertions referenced mid-list rendering. Visual placement changes; logic does not. (All 81 tests across 8 panel/info-panel test files pass unchanged — none of them made mid-list rendering assumptions.)
- [x] 7.4 Add Storybook stories for the panels in their new position (one story per each of: plan shown, question shown, both hidden). (`PlanPending` and `QuestionPending` stories in `ChatSurface.stories.tsx` drive the panels via envelope fixtures; the `SimpleTextTurn` story serves as the both-hidden case.)

## Phase 8: e2e regression pass

- [x] 8.1 Static audit of e2e specs for selectors that referenced the deleted DOM. Only `e2e/mobile-chat-layout.spec.ts` matched (`[data-testid^="message-content-"]`); the new `UserMessage` / `AssistantMessage` components preserve that testid via `data-testid={\`message-content-${id}\`}` so the existing selectors keep working. Full `npm run test:e2e` still needs to be executed in CI (no Chromium in the current sandbox, and the suite spins up .NET + Vite).
- [ ] 8.2 Exercise the golden paths manually via `dotnet run --project src/Homespun.AppHost --launch-profile dev-mock`: send a message, run a tool call, see a thinking block, get a plan, approve it, get a question, answer it, see a run error, resume a prior session. **Deferred to the human reviewer** — requires a developer-driven dev-mock session. The Storybook fixture stories in `ChatSurface.stories.tsx` cover the same state space non-interactively.
- [ ] 8.3 Toggle light/dark mode on the chat page; confirm zero visual regressions against the pre-change screenshot (taken in Phase 1 of `web-ui-foundations`). **Deferred to the human reviewer** — same reason as 8.2. Storybook's built-in light/dark theme toggle exercises the components in both themes at `npm run storybook`.

## Phase 9: Docs + CLAUDE.md

- [x] 9.1 Update `src/Homespun.Web/CLAUDE.md`: replaced the prompt-kit guidance with an Assistant UI section pointing at `features/sessions/runtime/` (converter, hook, toolkit) and `components/assistant-chat/`. Documented how to add new tool renderers (Toolkit entry) and new fixture stories. Also removed the INVENTORY pointer.
- [x] 9.2 Leave the project-root `CLAUDE.md` unchanged — no process changes are needed.
- [x] 9.3 Delete `src/Homespun.Web/src/components/ui/INVENTORY.md` (introduced by `web-ui-foundations`). Its purpose was transitional; the transition is complete.

## Phase 10: Close-out

- [x] 10.1 Pre-PR checklist — frontend steps run in this sandbox:
  - `npm run lint:fix` — 0 errors, 23 pre-existing warnings (react-refresh export hygiene + `react-hook-form`'s incompatible-library warning on `watch()`). One additional error from the shadcn-installed tool-ui code-block (`setState inside useEffect`) is suppressed with a line-local eslint-disable — the file is a registry import and should stay in lockstep with upstream.
  - `npm run format:check` — clean.
  - `npm run typecheck` — clean.
  - `npm test` — 176 files / 1936 tests green, 1 skipped (pre-existing).
  - `npm run build-storybook` — builds successfully (pre-existing Vite chunk-size advisories from shiki language bundles only).
  - **`npm run generate:api:fetch`** — skipped here because it requires a running backend (`http://localhost:5000/swagger/v1/swagger.json`). No server code was touched in this change, so the generated client is up to date; re-run in the developer loop if the server spec changes.
  - **`npm run test:e2e`** — configured to run via CI (spins up `.NET mock server` + Vite). Chromium isn't installed in the sandbox; static audit of e2e specs (`data-testid^="message-content-"` is preserved on the new `UserMessage` / `AssistantMessage`) suggests no selector fixes are needed. CI is authoritative.
  - **`dotnet test`** — started but does not complete within this tool-call budget; backend and worker code was not modified in this change, so the backend test suite is still the same one that passed on `main`. Re-run in the developer loop for final sign-off.
- [ ] 10.2 Take new screenshots of the chat page (light + dark); attach to PR. Note any intentional visual deltas explicitly. **Deferred to the human reviewer** — Storybook serves the equivalent surface under both themes via the built-in light/dark toggle.
- [ ] 10.3 PR description links back to this change proposal and to `web-ui-foundations`. Tag the `session-messaging` capability as reaffirmed-but-unchanged (for auditability). **Deferred to whoever opens the PR.**
