## Context

The `chat-assistant-ui` change (delivered, archived) put the Assistant UI runtime in place: `useExternalStoreRuntime`, `convertAGUIMessage`, the `Toolkit` API for tool-call rendering, and a thin `assistant-chat/` shell over `ThreadPrimitive`/`MessagePrimitive`. The interactive-tool-calls change (also delivered) collapsed the old `PlanApprovalPanel`/`QuestionPanel` surfaces into Toolkit `type: "human"` entries that render through registry tool-ui components (`approval-card`, `question-flow`).

What didn't change: the composer (`features/sessions/components/chat-input.tsx`) and the assistant-message presentation in `assistant-chat/messages.tsx`. Both still use pre-AUI custom shadcn:

- The composer is a `<form>` with custom `DropdownMenu` (model), custom `Button` toggle (mode), and a plain `<textarea>` driving a manually-positioned `MentionSearchPopup`. `ComposerPrimitive.Input` is intentionally bypassed — a comment in `CLAUDE.md` cites the mention popup's need for programmatic value injection.
- The `AssistantMessage` is a `bg-secondary` rounded card. `ReasoningPart` is a 4-line italic div. There is no `ToolGroup` — consecutive tool calls render inline in the bubble. The `Terminal` tool-ui component is installed at `components/tool-ui/terminal/` but no Toolkit entry uses it; `Bash` renders through a plain `CodeBlock language="bash"`.

This change closes those gaps. The reducer, runtime hook, converter, and Toolkit wiring are not touched. The change is presentation + composer only.

## Goals / Non-Goals

**Goals:**
- Move the composer onto `ComposerPrimitive.Input`, with a model selector and a Plan/Build tabs control as siblings, and `composer-trigger-popover` for both `@` and `/`.
- Drop the assistant-message bubble; render parts on the page background.
- Replace the custom italic reasoning div with a collapsible reasoning surface that auto-collapses once a non-reasoning part appears.
- Wire the installed `Terminal` tool-ui component for the `Bash` Toolkit entry.
- Add `ToolGroup` so a run of consecutive tool calls in one assistant message reads as a unit.
- Keep all live/replay behavior, all hooks, all data flows unchanged.

> **Spike (Section 1) findings — primitive sourcing.** `@assistant-ui/react@0.12.25` (the version pinned in `package.json`) does **not** export a `Tabs`, `ModelSelector`, or built-in collapsible `Reasoning` primitive — it exposes `ComposerPrimitive` (`Input`, `Send`, `Cancel`, `Unstable_TriggerPopoverRoot`, `Unstable_SlashCommandRoot`), and the `ToolGroup` slot lives at the **top level** of `MessagePrimitive.Parts`'s `components` prop (not under `tools.Group` as the proposal initially suggested). For controls that have no AUI counterpart we use the shadcn primitives already on disk in `components/ui/` (`tabs.tsx`, `select.tsx`, `collapsible.tsx`) — keeping the "no new dependencies" constraint. See `D2`, `D4`, and `D6` below for the specifics.

**Non-Goals:**
- Slash-command catalogue. The `/` popover ships with no commands. A separate change owns the catalogue.
- Re-Terminal-ifying Read/Write/Grep, or introducing a unified command-card primitive.
- `ActionBar` features (copy, regenerate, edit, branch).
- Any change to `ask_user_question`/`propose_plan` (interactive-tool-calls capability).
- Any change to the converter, the reducer, or the `useExternalStoreRuntime` wiring.

## Decisions

### D1: Use `ComposerPrimitive.Input`, re-host the mention popup via `composer-trigger-popover`

**Decision:** The composer textarea becomes the one `ComposerPrimitive.Input` produces. The `@`-mention search moves from "manually positioned popup driven by `useMentionTrigger` over local React state" to "popover attached via AUI's `composer-trigger-popover` mechanism, fed by the same `useProjectFiles` and `useSearchablePrs` data hooks". A `/` popover is added on the same mechanism, with an empty command list as content.

**Rationale:** The `CLAUDE.md` note about needing programmatic value injection was true when the popup was a manual cursor-positioning thing; it isn't a constraint of `ComposerPrimitive.Input`. AUI's textarea exposes a ref and standard DOM mutation works; the trigger-popover pattern is documented for exactly this use case (slash menus, mentions, command palettes). Adopting it means: smaller surface area, AUI handles popover positioning, and the textarea participates in `ComposerPrimitive.Send` / disabled / submit-on-enter wiring instead of a custom `<form>` + `requestSubmit` glue.

**Consequence:** `chat-input.tsx` shrinks materially. The `@`-mention insertion logic (file path quoting, `PR #` formatting) ports verbatim — only the trigger detection and popover positioning change. The `/` popover is a stub with an "empty state" body now; adding commands later is a content-only edit.

**Alternatives considered:**
- *Keep the plain `<textarea>` and just swap the controls.* Cheap, but leaves the chat input perpetually off the AUI happy path — every future AUI feature (attachments, voice) would have to be re-implemented manually.
- *Use `ComposerPrimitive.Input` but keep the manually-positioned popup.* Doable, but the popup positioning would drift on resize and the trigger detection would still be ad hoc — the AUI mechanism is strictly better.

### D2: shadcn `Tabs` for Plan/Build, shadcn `Select`-based `ModelSelector` for the model

**Decision:** The Plan/Build toggle becomes shadcn `Tabs` with two triggers ("Plan", "Build") and no `TabsContent` (presentation-only triggers, used as a toggle group). The model picker becomes a small `ModelSelector` wrapper around shadcn `Select`, with options sourced from `useAvailableModels()`. Both render as siblings of `ComposerPrimitive.Input`, above it.

**Rationale:** Both surfaces are mutually-exclusive single-select state — exactly what shadcn `Tabs` and `Select` model. The custom dropdown for model carries no information shadcn `Select` doesn't (icon + label + selected state) and the custom toggle button for mode is one shadcn `Button` doing the work two `Tabs` triggers do natively. Both shadcn primitives are already on disk under `components/ui/`, match the rest of the app's visual language, and stay within "no new dependencies."

> **Spike correction (Q1, Tasks 1.3 + 1.4):** the proposal originally said "AUI `Tabs`" and "AUI `ModelSelector`". Those primitives are not exported from `@assistant-ui/react@0.12.25`. The composer's per-AUI primitives are `ComposerPrimitive.{Input, Send, Cancel}`; the model dropdown and mode toggle are not first-class AUI concerns at this version. shadcn substitutes are the right call.

**Consequence:** `MODEL_LABELS` constant goes away (selector reads labels from `useAvailableModels`'s payload). `Hammer`/`Shield` icons move into the tab triggers. The `Tab + Shift` keyboard shortcut for mode toggle is preserved by intercepting the key in a wrapping `onKeyDown` around `ComposerPrimitive.Input` and calling `onModeChange`.

**Alternatives considered:**
- *Tier aliases (Opus/Sonnet/Haiku) only, hardcoded.* Matches the current 3-item dropdown, but the live Anthropic catalogue is already wired in `useAvailableModels` for everywhere else; the composer is the last surface still hardcoding the tier list. Use the live one.
- *Adopt a future AUI version that ships `Tabs`/`ModelSelector`.* No upgrade is in motion in this branch; "no new deps" is a hard constraint. shadcn substitutes match the rest of the app and keep the diff contained.

### D3: Drop the assistant bubble; keep user + system bubbles

**Decision:** `AssistantMessage` removes its `bg-secondary text-secondary-foreground rounded-lg px-4 py-2 max-w-[…]` wrapper. The text parts render on the page background. Tool-call cards, code blocks, and the new `Reasoning` surface keep their own borders/backgrounds (they already do today). `UserMessage` keeps its `bg-primary` right-aligned bubble. `SystemMessage` keeps its centered `bg-muted` chip.

**Rationale:** The bubble made the assistant content visually heavy and limited width to 80–90% even when prose wanted to use the full pane. Modern assistant chat (Claude.ai, ChatGPT) puts user input in a bubble and assistant output on the page — the asymmetry signals "your turn vs. machine's turn" without forcing the assistant text into a narrow column. System remains a chip because it's diagnostic, not conversational.

**Consequence:** The streaming indicator (the pulsing dot in the old bubble) needs a new home. AUI's `MessagePrimitive.If` for the running state can render a subtle indicator at the message level, outside any bubble. Code blocks and tool cards already carry borders, so they stay readable on the bare background. The `data-testid` selectors (`message-${id}`, `message-content-${id}`) remain on the role-aligned outer container — only the bubble class changes.

**Alternatives considered:**
- *Keep all bubbles as-is.* Functional but visually heavy and inconsistent with where the AUI/Anthropic ecosystem has converged.
- *Drop user bubble too.* Loses the right-alignment + colour cue for "what I said," which is the most-scanned signal in a long thread.

### D4: `ToolGroup` for consecutive tool calls; single tool calls ungrouped

**Decision:** `MessagePrimitive.Parts` receives a top-level `ToolGroup` component so consecutive `tool-call` parts within one assistant message render under a single grouping container. A single isolated tool call (one tool-call part adjacent to text/reasoning) renders without the grouping wrapper. Our `ToolGroup` wrapper renders a thin border + `bg-muted/40` header to match the existing per-tool card styling.

**Rationale:** Today, an assistant turn that runs `Read → Bash → Write → Read` shows four separate inline cards stacked in the bubble. With `ToolGroup`, the four cards are clearly "one investigation step", and the prose that follows (the actual answer) reads as the conclusion. AUI's group primitive is opt-in via the parts component map; the runtime supplies `startIndex`/`endIndex` and renders the children inside our wrapper.

> **Spike correction (Task 1.5):** the proposal originally said `tools.Group: ToolGroup`. The actual API places `ToolGroup` at the **top level** of the `components` prop on `MessagePrimitive.Parts` (sibling of `Text`, `Reasoning`, `tools`), with the signature `ComponentType<PropsWithChildren<{ startIndex: number; endIndex: number }>>`. The `ToolGroup` slot is currently marked `@deprecated` (experimental) in `@assistant-ui/core`, but it is the documented mechanism for grouping consecutive tool calls and is acceptable risk for a presentation-layer wrapper.

**Consequence:** No converter or reducer change. `ToolGroup` is a presentation-layer wrapper — the tool-call parts inside are the same content parts as before. Tests need a new fixture: an assistant message with three consecutive tool calls, assert the `ToolGroup` container renders.

**Alternatives considered:**
- *Group all tool calls in a turn regardless of interleaving.* Would force `[tool, text, tool]` into one group, which misrepresents the intent — the text between tool calls is content, not a separator. Default AUI behaviour (group consecutive only) is correct.

### D5: Wire `Terminal` for `Bash`; leave Read/Grep/Write on current renderers

**Decision:** The `Bash` Toolkit entry's `render` function returns the `Terminal` tool-ui component (already installed at `components/tool-ui/terminal/`), with `command` from args mapped to the prompt and `result` mapped to the output. The error case keeps a similar shape but with the existing destructive-border treatment. `Read`, `Grep`, `Write` keep their existing renderers unchanged.

**Rationale:** Terminal is shaped for shell-like I/O — command in, output out. `Bash` is the only tool whose semantics match exactly. `Read` is "fetch a file" (no command), `Grep` is "search results" (tabular/structured), `Write` is "confirm a side effect" (success + path). Forcing those into a Terminal frame would invent fake commands or hide structure. The right move is to ship Terminal where it fits and revisit the others as a separate exercise (possibly via a unified "command-like card" if the team wants visual unity — that's a future product decision, not this change).

**Consequence:** One file edit (`runtime/toolkit.tsx`'s `Bash` entry). The existing `CodeBlock`-based Bash renderer goes away. The Terminal component takes the same `output` data the CodeBlock currently receives, so the data flow doesn't change.

**Alternatives considered:**
- *Wire Terminal everywhere.* See above — UX downgrade for Read/Grep/Write.
- *Don't ship Terminal for anything; defer the whole Terminal question.* Misses the obvious win. Bash → Terminal is a one-edit change with no cost.

### D6: Custom collapsible `Reasoning` (built on shadcn `Collapsible`) replaces the custom italic div

**Decision:** The `Reasoning` slot in `MessagePrimitive.Parts.components` becomes a small `ReasoningPart` component built on shadcn `Collapsible` (`@radix-ui/react-collapsible`, already a dependency). It is collapsed by default once any non-reasoning part has appeared in the same message and stays expanded while reasoning is the only streaming content. The text rendering uses `Markdown` so reasoning blocks read as prose, with the disclosure header showing the title "Thinking…" while streaming and "Thoughts" once a non-reasoning part is present.

**Rationale:** Today's `ReasoningPart` is `<div className="text-muted-foreground my-1 text-sm break-words italic">{text}</div>` — no disclosure, always visible, italics. The intended UX is collapsible with the right defaults: expanded while it's the only thing streaming, collapsed once a real answer arrives. That matches how thinking blocks read in production — verbose mid-stream, summarisable once you have the answer.

> **Spike correction (Task 1.6):** the proposal originally said "Assistant UI's `Reasoning` primitive (drop-in)." `@assistant-ui/react@0.12.25` does not export a built-in collapsible Reasoning primitive — only the type `ReasoningMessagePartComponent`. We supply our own component, using shadcn `Collapsible` (already on disk in `components/ui/collapsible.tsx`) and the `useMessage` hook to read sibling parts and decide the default-open state.

**Consequence:** Stories need to cover both the expanded (mid-stream) and collapsed (post-answer) states. The fixture-driven Storybook surface from `chat-assistant-ui` already covers reasoning blocks; we extend it with a multi-part fixture (reasoning followed by text) to exercise the collapse behavior.

### D7: No new dependencies

**Decision:** Everything this change needs is already on disk:
- `@assistant-ui/react` (0.12.25) provides `ComposerPrimitive.{Input, Send, Cancel, Unstable_TriggerPopoverRoot, Unstable_SlashCommandRoot, Unstable_TriggerPopoverPopover, Unstable_TriggerPopoverItems, Unstable_TriggerPopoverItem}` and the `ToolGroup` slot on `MessagePrimitive.Parts`.
- shadcn primitives in `components/ui/`: `tabs.tsx`, `select.tsx`, `collapsible.tsx`, `popover.tsx` (already present, no `npx shadcn add` needed).
- The `Terminal` tool-ui component is at `components/tool-ui/terminal/`.
- `useAvailableModels` exists at `features/models/hooks/useAvailableModels.ts` (the live Anthropic catalogue hook).

**Consequence:** Zero `package.json` changes. PR diff is contained to `features/sessions/components/chat-input.tsx`, `features/sessions/components/assistant-chat/messages.tsx`, `features/sessions/runtime/toolkit.tsx`, their tests, and the relevant story file.

## Risks / Trade-offs

| Risk | Mitigation |
|---|---|
| Mention popup re-plumb under `composer-trigger-popover` regresses cursor positioning, multi-character insertion, or `PR #` insertion semantics | Existing `MentionSearchPopup` is data-driven; the trigger pattern is the only thing that changes. New popover-driven test mirrors the old trigger test 1:1, plus a Storybook play function that types `@foo` and asserts insertion. Spike the popover wiring first if AUI's API surface differs from what we expect. |
| Removing the assistant bubble harms readability for short answers (single-sentence reply now floats on the bg) | Visual review in light + dark with realistic fixtures. If short answers feel adrift, add a hairline `border-b` separator between consecutive turns rather than re-introducing a bubble. Falls within "tweak to taste" without re-opening this decision. |
| `Tabs` for Plan/Build implies tab-panel content; we don't have panels (the tabs are pure mode toggles) | Use `Tabs` with empty/no panels — AUI supports the trigger-only pattern. If the API forces a panel, render an empty `<TabsContent>`. Worst case, fall back to `ToggleGroup`-shape primitive; structurally equivalent. |
| Live Anthropic catalogue from `useAvailableModels` includes models the user can't actually use (auth/billing) | Existing `useAvailableModels` already filters per the model-catalog spec; consume what it returns. Out of scope to second-guess the filter. |
| `ToolGroup` styles diverge from the rest of the chat (extra padding, different border weight) | Headless primitive; we style it. Match the existing tool-call card border. Storybook story per fixture catches drift. |
| Streaming indicator loses its anchor when the bubble is gone | Move it to a `MessagePrimitive.If isRunning` slot at the message level, rendered as a subtle inline dot near the bottom of the assistant content. Same visual weight as today; just outside any bubble. |
| `ComposerPrimitive.Input`'s submit-on-enter conflicts with the popover's enter-to-select behavior | Today's `handleKeyDown` already handles this via `isSearchOpen`. The same predicate moves to the popover-aware key handler; AUI's primitive supports `event.preventDefault()` from a wrapping `onKeyDown`. Tested by fixture story. |
| `/` popover with empty content reads as broken to users who try it | Show an explicit "No commands available yet" empty state. If even that feels wrong, gate the `/` popover behind a future feature-flag or simply don't ship the popover until commands exist (then this becomes a follow-up). Cheap to flip. |

## Migration Plan

- Atomic swap. The composer rewrite and the message-presentation changes ship together — the composer's keyboard wiring assumes the new primitive surface, and the bubble removal is a pure visual diff that doesn't gate on the composer.
- Roll-back is `git revert`. No reducer, server, or contract change.
- No data migration. No flag.
- Pre-PR: full pre-PR checklist + screenshots in light + dark.

## Open Questions

- **Q1 — RESOLVED.** AUI 0.12.25 does not export a `ModelSelector` primitive. The composer uses a small `ModelSelector` wrapper around shadcn `Select`, fed directly by `useAvailableModels`. Labels come from the hook's `displayName`/`id` payload — no shim needed.
- **Q2 — RESOLVED.** AUI's `Unstable_TriggerPopoverRoot` uses an adapter pattern (`Unstable_TriggerAdapter` exposing `categories()`, `categoryItems(id)`, optional `search(query)`) and an `OnSelectBehavior` (`{ type: "insertDirective", formatter }` or `{ type: "action", handler }`). The trigger character position and the resolved query are tracked internally; selection callbacks splice the directive back into the composer via the formatter — application code does not need to compute insertion offsets manually. The mention insertion logic (file path quoting, `PR #` formatting) ports into a custom `Unstable_DirectiveFormatter`.
- **Q3 — Open.** Whether the `/` popover should render at all when empty, or stay closed until commands exist. Current decision: render with empty-state. If user-testing finds this confusing, hide entirely and treat the popover as a follow-up that ships with the command catalogue.

## Spike Results (Section 1)

| Task | Finding | Action |
|---|---|---|
| 1.1 `ComposerPrimitive.Input` | Forwards a ref to `HTMLTextAreaElement`; accepts standard textarea props (extends `react-textarea-autosize` props); a wrapping `onKeyDown` can `preventDefault` Enter when the popover is active. | Use as-is; keep a wrapper `onKeyDown` for `Tab+Shift` and popover-aware Enter handling. |
| 1.2 `composer-trigger-popover` | Exists as `ComposerPrimitive.Unstable_TriggerPopoverRoot`. Adapter-based (`Unstable_TriggerAdapter`); `onSelect: OnSelectBehavior` is `insertDirective` (with `Unstable_DirectiveFormatter`) or `action`. `Unstable_SlashCommandRoot` is a `/`-preconfigured convenience wrapper. | Wrap the input in two `Unstable_TriggerPopoverRoot`s — one with `trigger="@"` keyed to a `MentionTriggerAdapter` over `useProjectFiles`/`useSearchablePrs`, one with `trigger="/"` over an empty adapter. |
| 1.3 `ModelSelector` | **Not exported** by `@assistant-ui/react@0.12.25`. | Build a thin wrapper around shadcn `Select`, fed by `useAvailableModels()`. |
| 1.4 `Tabs` | **Not exported** by `@assistant-ui/react@0.12.25`. shadcn `Tabs` exists at `components/ui/tabs.tsx`. | Use shadcn `Tabs` with two triggers and no `TabsContent`. |
| 1.5 `ToolGroup` | Exists at the top level of `MessagePrimitive.Parts`'s `components` prop (`ComponentType<PropsWithChildren<{ startIndex: number; endIndex: number }>>`), **not** under `tools.Group`. Marked `@deprecated`/experimental but documented. | Pass `ToolGroup: MyToolGroup` at the top level of `components`. |
| 1.6 `Reasoning` collapsible | **Not exported** by `@assistant-ui/react@0.12.25`. | Build a `ReasoningPart` on shadcn `Collapsible`, using `useMessage` to detect sibling parts and collapse-by-default once a non-reasoning part appears. |
