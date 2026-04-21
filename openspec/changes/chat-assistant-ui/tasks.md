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

- [ ] 1.1 Add `@assistant-ui/react` (latest stable), `assistant-stream` to `src/Homespun.Web/package.json`. Pin exactly.
- [ ] 1.2 Create `features/sessions/runtime/` directory with placeholder `index.ts`.
- [ ] 1.3 Confirm the `web-ui-foundations` Storybook harness is on the branch (either merged or rebased). Fixture-driven stories in Phase 5 depend on it.
- [ ] 1.4 `npm install`, then `npm run typecheck` — green before touching anything else.

## Phase 2: TDD — convertAGUIMessage (pure)

- [ ] 2.1 Create `features/sessions/runtime/convertAGUIMessage.ts` with an empty export; add `convertAGUIMessage.test.ts` next to it.
- [ ] 2.2 Write failing tests for: text block → text part; thinking block → reasoning part; toolUse block → tool-call part (with `toolCallId`, `toolName`, `argsText`, `result`); multi-block assistant message preserves order; user and system roles pass through unchanged.
- [ ] 2.3 Implement `convertAGUIMessage` to green the tests. Keep it pure, colocated, ~40 LOC.
- [ ] 2.4 Add tests for edge cases: streaming text block with `isStreaming=true` still renders text; toolUse without a `result` renders with `result: undefined`; zero-content message renders with empty content array.

## Phase 3: TDD — useSessionAssistantRuntime (integration)

- [ ] 3.1 Create `features/sessions/runtime/useSessionAssistantRuntime.ts`. This hook takes the reducer's current `AGUISessionState` plus a `sendMessage`/`cancel` pair and returns an `AssistantRuntime`.
- [ ] 3.2 Write failing integration test in `useSessionAssistantRuntime.test.tsx` (React Testing Library) that: renders a `ThreadPrimitive.Root`, seeds the reducer with a scripted envelope sequence, asserts the rendered messages appear in DOM order.
- [ ] 3.3 Implement the hook using `useExternalStoreRuntime({ messages, convertMessage: convertAGUIMessage, isRunning, onNew, onCancel })`. Green the test.
- [ ] 3.4 Add a test for the replay path: feed a `MESSAGES_SNAPSHOT`-equivalent (a full reducer state) and assert the rendered DOM matches what an incremental feed of the same envelopes produces.
- [ ] 3.5 Add a test for idempotence at the UI boundary: applying the same envelope twice through the reducer and rendering does not duplicate DOM nodes.

## Phase 4: Tool UI registrations

- [ ] 4.1 Create `features/sessions/runtime/tool-ui/` with subfiles: `bash.tsx`, `read.tsx`, `grep.tsx`, `write.tsx`, and an `index.ts` that exports a `toolUIs` array.
- [ ] 4.2 Port JSX 1:1 from `features/sessions/components/tool-results/bash-tool-result.tsx` into a `makeAssistantToolUI({ toolName: "Bash", render: ... })` in `bash.tsx`. Do the same for Read, Grep, Write.
- [ ] 4.3 Register `ToolFallback` (AUI built-in) for unknown tool names.
- [ ] 4.4 Wire the `toolUIs` array through `AssistantRuntimeProvider`'s tool-UI hook at the session page level.
- [ ] 4.5 Delete `features/sessions/components/tool-results/*` and its tests. No rename; the JSX now lives under `runtime/tool-ui/`.

## Phase 5: Replace the session detail render tree

- [ ] 5.1 In `features/sessions/pages/session-detail.tsx` (or wherever `MessageList` is mounted today), replace the `<MessageList />` block with:
      ```tsx
      <AssistantRuntimeProvider runtime={runtime}>
        <ThreadPrimitive.Root>
          <ThreadPrimitive.Viewport>
            <ThreadPrimitive.Messages components={{ UserMessage, AssistantMessage, EditComposer }} />
          </ThreadPrimitive.Viewport>
          <Composer />
        </ThreadPrimitive.Root>
        {pendingPlan && <PlanApprovalPanel pendingPlan={pendingPlan} … />}
        {pendingQuestion && <QuestionPanel pendingQuestion={pendingQuestion} … />}
      </AssistantRuntimeProvider>
      ```
- [ ] 5.2 Implement `UserMessage`, `AssistantMessage` as thin wrappers around `MessagePrimitive` that map `text` / `reasoning` / `tool-call` parts. Use `ChainOfThought` primitive for reasoning if available (design.md Q1).
- [ ] 5.3 Implement `Composer` on `ComposerPrimitive`: preserve the mode dropdown, model dropdown, and `@`-mention trigger. Keep logic; swap the DOM shell.
- [ ] 5.4 Delete `features/sessions/components/message-list.tsx`, `tool-execution-group.tsx`, `tool-execution-row.tsx`, `chat-input.tsx` (old), `features/sessions/utils/agui-to-display-items.ts` and their tests.
- [ ] 5.5 Delete `components/ui/{prompt-input,message,scroll-to-bottom,thinking-bar,text-shimmer,loader,markdown,code-block}.tsx` and their tests. These are the `chat-owned` primitives from `web-ui-foundations`'s `INVENTORY.md`.
- [ ] 5.6 Run `npm run typecheck` — fix import breakage from the deletions.

## Phase 6: Fixture-driven Storybook stories

- [ ] 6.1 Create `features/sessions/fixtures/envelopes.ts` with named fixtures: `simpleTextTurn`, `toolCallLifecycle`, `thinkingBlock`, `multiBlockTurn`, `planPending`, `questionPending`, `runError`, `streamingInterrupted`. Each is a `SessionEventEnvelope[]`.
- [ ] 6.2 Create a `sessions/ChatSurface.stories.tsx` that renders the full session page shell (including runtime + plan/question panels) against a chosen fixture. One story per fixture from 6.1.
- [ ] 6.3 Add interactive stories for the Composer (`play` functions via `addon-vitest`) that type a message, assert submit, and verify the runtime's `onNew` callback fires.
- [ ] 6.4 Run `npm run build-storybook` — zero warnings expected.

## Phase 7: Plan approval and question panel repositioning

- [ ] 7.1 Move `PlanApprovalPanel` out of `MessageList`'s children; render as a pinned sibling of the Thread (likely above the Composer, below the message viewport). Preserve the existing hooks/handlers.
- [ ] 7.2 Same for `QuestionPanel` (which today also renders mid-list in `MessageList`).
- [ ] 7.3 Update the existing `plan-approval-panel.test.tsx` and `session-info-panel/*.test.tsx` tests where assertions referenced mid-list rendering. Visual placement changes; logic does not.
- [ ] 7.4 Add Storybook stories for the panels in their new position (one story per each of: plan shown, question shown, both hidden).

## Phase 8: e2e regression pass

- [ ] 8.1 Run `npm run test:e2e`. Any failures are either real regressions (fix) or selectors that referenced the old DOM (update). Prefer semantic selectors over class-based where possible.
- [ ] 8.2 Exercise the golden paths manually via `dotnet run --project src/Homespun.AppHost --launch-profile dev-mock`: send a message, run a tool call, see a thinking block, get a plan, approve it, get a question, answer it, see a run error, resume a prior session.
- [ ] 8.3 Toggle light/dark mode on the chat page; confirm zero visual regressions against the pre-change screenshot (taken in Phase 1 of `web-ui-foundations`).

## Phase 9: Docs + CLAUDE.md

- [ ] 9.1 Update `src/Homespun.Web/CLAUDE.md`:
      - Delete the "prompt-kit for all chat and AI-related UI" paragraph.
      - Add an "Assistant UI" section: one paragraph on `@assistant-ui/react`, where primitives live, and how to add a new tool UI via `makeAssistantToolUI`.
      - Add a pointer to `features/sessions/runtime/` as the owner of chat rendering.
- [ ] 9.2 Leave the project-root `CLAUDE.md` unchanged — no process changes are needed.
- [ ] 9.3 Delete `src/Homespun.Web/src/components/ui/INVENTORY.md` (introduced by `web-ui-foundations`). Its purpose was transitional; the transition is complete.

## Phase 10: Close-out

- [ ] 10.1 Full pre-PR checklist: `dotnet test`, `cd src/Homespun.Web && npm run lint:fix && npm run format:check && npm run generate:api:fetch && npm run typecheck && npm test && npm run test:e2e && npm run build-storybook`.
- [ ] 10.2 Take new screenshots of the chat page (light + dark); attach to PR. Note any intentional visual deltas explicitly.
- [ ] 10.3 PR description links back to this change proposal and to `web-ui-foundations`. Tag the `session-messaging` capability as reaffirmed-but-unchanged (for auditability).
