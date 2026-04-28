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
- `runtime/toolkit.tsx` — a single `Toolkit` object with `{ Bash, Read, Grep, Write }` entries (`type: "backend"`, `render({ toolCallId, argsText, result, isError })`). The `Bash` entry renders through the `Terminal` tool-ui component (`@/components/tool-ui/terminal/`) — `args.command` becomes the prompt and `result` becomes the output. `Read`, `Grep`, `Write` keep using `@tool-ui/code-block`. Unknown tools fall back to AUI's built-in tool-call fallback.
- `components/assistant-chat/` — thin primitive wrappers (`ChatSurface`, `UserMessage`, `AssistantMessage`, `SystemMessage`).
- `components/chat-input.tsx` — composer built on `ComposerPrimitive.Root` + `Input` + `Send`. Mode toggle is shadcn `Tabs` (Plan/Build, no `TabsContent`); model selector is the AUI registry `ModelSelector` (`@/components/assistant-ui/model-selector`) fed by `useAvailableModels()`. `@`-mentions and `/`-commands are wired through `ComposerPrimitive.Unstable_TriggerPopoverRoot` / `Unstable_TriggerPopover`: `@` uses an `Unstable_TriggerAdapter` over `useProjectFiles`/`useSearchablePrs` plus a custom `Unstable_DirectiveFormatter` that serializes selections as `@path` / `@"path with spaces"` / `PR #N`; `/` ships with an empty adapter and an "No commands available yet" empty-state body. The composer hosts its own minimal AUI runtime (empty messages, `onNew` → consumer's `onSend`) so it can render anywhere on the page.

**Assistant message presentation:**
- `AssistantMessage` is bubble-less — text renders directly on the page background (no `bg-secondary` / `bg-card` wrapper). Tool-call cards, code blocks, and the reasoning surface keep their own borders/backgrounds.
- `UserMessage` keeps its `bg-primary` right-aligned bubble; `SystemMessage` keeps its centered `bg-muted` italic chip.
- The streaming-state indicator is gated by `<AuiIf condition={(s) => s.message.status?.type === 'running'}>` and renders as a subtle pulsing dot (`data-testid="message-streaming-${id}"`).
- `Reasoning` parts render through the AUI registry's collapsible `Reasoning` primitive: collapsed by default once a non-reasoning part appears, expanded while it's the only/last streaming part. Inner text uses our `Markdown` component.
- Consecutive tool-call parts in one assistant message render under a single `ToolGroup` (top-level slot in `MessagePrimitive.Parts`'s `components`, not `tools.Group`). Single isolated tool calls render ungrouped. The `ToolGroup` component is the AUI registry one at `@/components/assistant-ui/tool-group`.

The AG-UI reducer (`features/sessions/utils/agui-reducer.ts`) is still the single source of truth for session state — both SignalR live and `GET /api/sessions/{id}/events` replay feed into it. Assistant UI runs downstream of the reducer; the live/replay parity invariant holds by construction.

**To add a new backend tool renderer**, add an entry to the `toolkit` object in `runtime/toolkit.tsx`. Prefer the AUI registry tool-ui components (`@/components/tool-ui/...` and `@/components/assistant-ui/...`) — install via `npx shadcn@latest add https://r.assistant-ui.com/<name>.json` rather than building bespoke shadcn primitives.

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
