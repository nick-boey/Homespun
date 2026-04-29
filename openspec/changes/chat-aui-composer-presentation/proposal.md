## Why

The `chat-assistant-ui` change put the AUI runtime in place but kept the composer's controls (model dropdown, mode toggle) and the presentation layer (assistant bubble, custom italic reasoning div, ungrouped tool calls) on the pre-AUI shadcn pattern. The result is an AUI shell wrapped around custom UI — half-migrated. The installed-but-unused `Terminal` tool-ui component is the loudest symptom: the registry-driven tool surface is in the repo, yet `Bash` still renders through a generic `CodeBlock`.

This change finishes the migration by adopting AUI's idiomatic primitives for the composer affordances and the presentation surface, and by wiring the registry components that are already on disk.

## What Changes

- **Composer rebuilt on `ComposerPrimitive.Input` + AUI primitives.**
  - Replace the custom `DropdownMenu` model picker with AUI `ModelSelector`, fed by `useAvailableModels` (the live-Anthropic-catalogue hook). Selector lives as a sibling of the input.
  - Replace the custom `Hammer/Shield` Plan/Build button with AUI `Tabs`. Two tabs: Plan, Build. State still flows through the existing `session-settings-store`.
  - Replace the plain `<textarea>` with `ComposerPrimitive.Input`. Mention semantics (file/PR insertion via `MentionSearchPopup`) move into AUI's `composer-trigger-popover` mechanism for `@`. A `composer-trigger-popover` for `/` is wired with an empty command list (placeholder for a follow-up command catalogue).
  - Submit + cancel + disabled wiring run through `ComposerPrimitive.Send` and the runtime's `isRunning`/`onCancel`. The form-level `requestSubmit` glue goes away.

- **Assistant message bubble removed.**
  - `AssistantMessage` renders text on the page background — no `bg-secondary` wrapper.
  - `UserMessage` keeps its `bg-primary` bubble (right-aligned).
  - `SystemMessage` keeps its centered `bg-muted` chip.
  - Code blocks, tool-call cards, and the (new) collapsible reasoning surface keep their own borders so the lack of a parent bubble doesn't dissolve their boundaries.

- **Reasoning swapped to AUI's `Reasoning` component.**
  - `ReasoningPart` (a 4-line custom italic div) is replaced by AUI's collapsible `Reasoning` primitive: collapsed by default once a real text part arrives; expanded while it's the only thing streaming.

- **Terminal wired for `Bash`.**
  - The `Bash` Toolkit entry in `runtime/toolkit.tsx` swaps its inline `CodeBlock` for the `Terminal` tool-ui component (already installed at `components/tool-ui/terminal/`). Args' `command` becomes the prompt; `result` becomes the output.
  - `Read`, `Grep`, `Write` keep their current renderers — they aren't terminal-shaped (Read = file fetch, Grep = tabular results, Write = success/path confirmation). A future change can decide whether to unify them under a "command-like card" pattern.

- **`ToolGroup` for consecutive tool calls within a turn.**
  - `MessagePrimitive.Parts` receives `tools.Group: ToolGroup` so a run of `[toolUse, toolUse, toolUse, text]` from a single assistant message renders as a grouped card followed by the prose, instead of three separate inline tool cards.
  - Single isolated tool calls render ungrouped (no card wrapper), matching AUI default behavior.

- **CLAUDE.md update.**
  - `src/Homespun.Web/CLAUDE.md`: update the "Chat surface" section to point at the new composer location, the `Reasoning` swap, the Terminal wiring on Bash, and the ToolGroup expectation for new toolkit entries.

Behavior kept identical:
- AG-UI reducer, SignalR contract, replay endpoint — untouched.
- `convertAGUIMessage` mapping — untouched (text/reasoning/tool-call shapes already feed cleanly into AUI's Reasoning + Group + Terminal).
- Interactive tools (`ask_user_question`, `propose_plan`) — untouched. They keep their current `type: "human"` Toolkit entries and `tool-ui` renderers (`question-flow`, `approval-card`).
- Mention-search popup data sources (`useProjectFiles`, `useSearchablePrs`) — untouched. Only the trigger plumbing moves into the AUI popover pattern.
- Live/replay parity — untouched. This change is presentation only; reducer output remains the single source of truth.

## Capabilities

### New Capabilities
*(none)*

### Modified Capabilities
- `chat-assistant-ui`: Tool-call rendering requirement updates from "per-tool inline render in toolkit" to "Bash uses Terminal; multiple consecutive tool calls render under ToolGroup". A new requirement on assistant-message presentation (no bubble) and on composer-affordance idioms (ModelSelector, Tabs, ComposerPrimitive.Input + trigger-popovers) is added. The `Reasoning` requirement is tightened to mandate AUI's collapsible primitive (vs. any rendering of reasoning).

## Impact

- **Frontend**: `src/Homespun.Web/`
  - `features/sessions/components/chat-input.tsx` (~300 lines) — rewritten on `ComposerPrimitive.Input` + AUI primitives. Mention-popup logic preserved through trigger-popover; `/` popover stubbed.
  - `features/sessions/components/assistant-chat/messages.tsx` (~150 lines):
    - `AssistantMessage`: drop `bg-secondary` wrapper, render parts on background.
    - `ReasoningPart`: deleted; replace with AUI `Reasoning` reference in `MessagePrimitive.Parts` mapping.
    - `MessagePrimitive.Parts`: add `tools.Group: ToolGroup`.
  - `features/sessions/runtime/toolkit.tsx`: `Bash` entry uses `Terminal` from `components/tool-ui/terminal/`.
  - `features/sessions/components/assistant-chat/ChatSurface.stories.tsx`: add stories that exercise: bubble-less assistant message, collapsed/expanded reasoning, multi-tool turn under ToolGroup, Terminal-rendered Bash output, model selector + tabs in composer, `@` and `/` popovers.
  - Tests: `chat-input.test.tsx` rewritten against new primitives; `messages` tests updated to assert no `bg-secondary` on assistant content and to drive Reasoning collapse/expand. Mention-popup integration test moves from textarea-driven to popover-driven.
  - `package.json`: no new dependencies. All required AUI primitives + tool-ui components are already installed.
  - `CLAUDE.md`: updated chat-surface section.
- **Backend / Worker / Shared**: unaffected.
- **Risk**: Low–medium. The reducer, runtime hook, and converter — the load-bearing pieces — are not touched. The risk surface is the composer rewrite (mention popup re-plumbing under the trigger-popover pattern is the only non-trivial bit) and the visual delta from removing the assistant bubble.
- **Dependencies**: Hard-depends on the delivered `chat-assistant-ui` work (already in `main`). Soft-touches `model-catalog` (consumes `useAvailableModels`).

## Migration & roll-back

- Atomic swap, no feature flag. Old composer and new composer cannot share a Tab + ModelSelector state surface.
- Roll-back is `git revert`. No reducer / contract / DB change.
- Visual diff is intentional and material (assistant bubble removed). PR description carries before/after screenshots in light + dark.

## Follow-ups (explicitly out of scope)

- Slash-command catalogue. `/` popover ships with no commands; the command set is a separate product decision.
- Terminal-ifying Read/Write/Grep, or introducing a unified "command-like card" primitive shared across Bash/Read/Write.
- `ActionBar` on assistant messages (copy, regenerate). Today the runtime can't regenerate; copy is a fine cheap add but not bundled here.
- `BranchPicker`, attachments, voice — none modeled in our chat.
