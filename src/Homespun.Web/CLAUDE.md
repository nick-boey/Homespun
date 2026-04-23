# React client

Contains instructions on how to work with the Homespun.Web React client.

## UI Components

### shadcn/ui

The project uses shadcn/ui for base components. Components are installed into `src/components/ui/`. Use the default shadcn/ui theme (which will be styled later).

Add new components:

```bash
cd src/Homespun.Web
npx shadcn@latest add button
npx shadcn@latest add input
# etc.
```

**DO NOT create custom components if a shadcn/ui component already exists.**

Configuration is in `components.json`:

- Style: new-york
- Base color: zinc
- CSS Variables: enabled
- Icon library: lucide

### Chat surface

The session chat surface is built on [`@assistant-ui/react`](https://assistant-ui.com/) (`useExternalStoreRuntime` + `ThreadPrimitive` + `MessagePrimitive` + `ComposerPrimitive`). The runtime wiring, pure converter, and tool-call `Toolkit` all live under `features/sessions/runtime/`:

- `runtime/convertAGUIMessage.ts` — pure mapping `AGUIMessage` → `ThreadMessageLike` (text → text, thinking → reasoning, toolUse → tool-call).
- `runtime/useSessionAssistantRuntime.ts` — hook that turns the existing `AGUISessionState` + a `sendMessage` callback into an `AssistantRuntime` via `useExternalStoreRuntime`.
- `runtime/toolkit.tsx` — a single `Toolkit` object with `{ Bash, Read, Grep, Write }` entries (`type: "backend"`, `render({ toolCallId, argsText, result, isError })`). The `@tool-ui/code-block` shadcn-registry component renders tool output. Unknown tools fall back to AUI's built-in tool-call fallback at `MessageParts.js:98`.
- `components/assistant-chat/` — thin primitive wrappers (`ChatSurface`, `UserMessage`, `AssistantMessage`, `SystemMessage`) and the `ChatInput` composer. `ChatInput` keeps its own React-state textarea (not `ComposerPrimitive.Input`) because the `@`-mention popup needs programmatic value injection.

The AG-UI reducer (`features/sessions/utils/agui-reducer.ts`) is still the single source of truth for session state — both SignalR live and `GET /api/sessions/{id}/events` replay feed into it. Assistant UI runs downstream of the reducer; the live/replay parity invariant holds by construction.

**To add a new backend tool renderer**, add an entry to the `toolkit` object in `runtime/toolkit.tsx`. Don't add new files under `components/ui/` for chat-shaped UI — extend the Toolkit or add an assistant-chat primitive wrapper instead.

**Interactive tool calls** (`ask_user_question`, `propose_plan`) are registered in the same `runtime/toolkit.tsx` with `type: "human"`. They are agent-initiated: the server's `A2AToAGUITranslator.BuildInputRequired` emits `TOOL_CALL_START/ARGS/END` with the canonical tool name, and when the user commits via the renderer's `addResult`, the render also dispatches to the corresponding SignalR hub method (`AnswerQuestion` or `ApprovePlan`). The server then synthesises a `TOOL_CALL_RESULT` envelope on the session stream, which puts the renderer into receipt mode for both live and replay. Tool UI components (`@tool-ui/option-list`, `@tool-ui/approval-card`, …) are composable primitives under `components/tool-ui/` — use them from the tool renderers rather than rehydrating the old `PlanApprovalPanel` / `QuestionPanel` pattern (deleted).

**To add a new fixture-driven Storybook story**, put the envelope sequence in `features/sessions/fixtures/envelopes.ts` and add a named story in `components/assistant-chat/ChatSurface.stories.tsx`.

### Storybook

Storybook 10 runs against the real Tailwind v4 theme (`src/index.css`). Stories are co-located with their components as `*.stories.tsx` under `src/**`. Every shadcn-native and `divergent-keep` primitive in `components/ui/` has a story.

```bash
cd src/Homespun.Web

# Dev server
npm run storybook        # http://localhost:6006

# Static build (part of the pre-PR checklist)
npm run build-storybook
```

When adding a new shadcn primitive via `npx shadcn@latest add <name>`, add a co-located `<name>.stories.tsx` with at minimum a Default story, and an interactive `play` story for anything portal-rendered or with toggle state. The `build-storybook` step in the pre-PR checklist in the project-root `CLAUDE.md` catches story drift at author time.
