## 1. Spike — verify AUI primitive APIs

- [x] 1.1 Confirm `ComposerPrimitive.Input` exposes a ref usable for cursor positioning (`setSelectionRange`) and that wrapping `onKeyDown` can `preventDefault` the default submit behavior. **Confirmed**: forwards a `HTMLTextAreaElement` ref, accepts standard textarea props, `submitMode="enter"` controls Enter behavior; a wrapping `onKeyDown` with `event.preventDefault()` is sufficient.
- [x] 1.2 Confirm `composer-trigger-popover` API surface. **Confirmed**: exposed as `ComposerPrimitive.Unstable_TriggerPopoverRoot` (and `Unstable_SlashCommandRoot` for `/`). Uses adapter pattern (`Unstable_TriggerAdapter` with `categories()`/`categoryItems(id)`/`search?(query)`), and `onSelect` is `{ type: "insertDirective", formatter }` or `{ type: "action", handler }`. Trigger position and selection splicing are handled internally.
- [x] 1.3 Confirm `ModelSelector` accepts an arbitrary option list (id + label) sourced at runtime. **Mismatch**: `ModelSelector` is **not exported** by `@assistant-ui/react@0.12.25`. Substituted with shadcn `Select` wrapper (see D2). `useAvailableModels` payload is consumed directly — no shim needed.
- [x] 1.4 Confirm `Tabs` supports trigger-only rendering (no `TabsContent` panels) without warnings. **Mismatch**: `Tabs` is **not exported** by `@assistant-ui/react@0.12.25`. Substituted with shadcn `Tabs` (already in `components/ui/tabs.tsx`); supports trigger-only usage cleanly.
- [x] 1.5 Confirm `ToolGroup` works through `MessagePrimitive.Parts`'s slot, and that single tool calls render ungrouped. **Mismatch**: `ToolGroup` is a **top-level** key in `components` (not `tools.Group`), with signature `ComponentType<PropsWithChildren<{ startIndex: number; endIndex: number }>>`. Marked `@deprecated`/experimental in `@assistant-ui/core` but functional. AUI's runtime supplies the consecutive-tool-call grouping internally.
- [x] 1.6 Confirm `Reasoning` primitive's collapse-when-other-parts-present default. **Mismatch**: AUI does not export a built-in collapsible `Reasoning` primitive — only the `ReasoningMessagePartComponent` type. We supply our own component built on shadcn `Collapsible` (see D6).
- [x] 1.7 Updated `design.md` (Goals, D2/D4/D6/D7, added "Spike Results" table, resolved Q1/Q2 in Open Questions) and `specs/chat-assistant-ui/spec.md` (softened "Assistant UI Tabs/ModelSelector/Reasoning" wording where the qualifier was inaccurate).

## 2. TDD — `chat-input.tsx` rewrite (composer)

- [x] 2.1 Moved old `chat-input.test.tsx` fixtures aside (kept as `chat-input.test.tsx.old-bak` for reference; deleted before commit). New test plan: textarea is `ComposerPrimitive.Input`, `Tabs` toggle calls `onModeChange`, model selector toggles via shadcn `Select` (filled by `useAvailableModels`), `@` opens mention popover, `/` opens empty-state popover, Enter submits, `Tab+Shift` toggles mode.
- [x] 2.2 New test file written; 21 specs covering rendering, sending, disabled/loading, mode tabs, keyboard shortcuts, model selector, mention popover, slash popover, and legacy expectations (no leftover `<form>` glue or `DropdownMenu` `menuitem`s).
- [x] 2.3 `ComposerPrimitive.Root` + `Input` + `Send` (with `asChild`-wrapped `Button`) replace the custom `<form>` + `<textarea>` + manual submit handler. `disabled` and `isLoading` flow through to the wrapper Button; `ComposerPrimitive.Send`'s built-in `canSend` gates submission when the input is empty.
- [x] 2.4 shadcn `Tabs` with two `TabsTrigger`s (Plan/Build, no `TabsContent` panels) replaces the custom toggle button. `Tab+Shift` shortcut preserved via a wrapping `onKeyDown` on `ComposerPrimitive.Input`.
- [x] 2.5 The model `DropdownMenu` is replaced by `ModelSelectorRoot`/`Trigger`/`Content` from `@/components/assistant-ui/model-selector` (installed via `npx shadcn@latest add https://r.assistant-ui.com/model-selector.json`). Options come from `useAvailableModels()`. `MODEL_LABELS` constant is deleted.
- [x] 2.6 The bespoke `MentionSearchPopup` is replaced by `ComposerPrimitive.Unstable_TriggerPopover` keyed on `@` with a `Unstable_TriggerAdapter` over `useProjectFiles`/`useSearchablePrs`. A custom `Unstable_DirectiveFormatter` preserves the existing serialization: `@path`, `@"path with spaces"`, and `PR #N`.
- [x] 2.7 A second `Unstable_TriggerPopover` keyed on `/` with an empty `Unstable_TriggerAdapter` and a "No commands available yet" empty-state body (`data-testid="slash-empty-state"`).
- [x] 2.8 All 21 tests pass. `MODEL_LABELS`, the manual `requestSubmit` path, and the form-shell glue are gone.
- [x] 2.9 Verified: the new `chat-input.test.tsx` has no references to `DropdownMenu` (`menuitem` query is asserted to return null in the legacy-expectations block), the only `<form>` element is the one `ComposerPrimitive.Root` produces, and the textarea is queried via `getByPlaceholderText` (not by the legacy bare `<textarea>` selector).

## 3. TDD — `assistant-chat/messages.tsx` presentation

- [x] 3.1 Added `messages.test.tsx` with: assistant content carries none of `bg-secondary` / `bg-card` / `bg-muted`; user content keeps `bg-primary text-primary-foreground`; system content keeps `bg-muted` + `italic`. Tests render via a real AUI runtime harness fed from the `envelopeFixtures`.
- [x] 3.2 `AssistantMessage` wrapper rewritten: bubble removed; the inner div now uses `text-foreground min-w-0 max-w-full flex-1 overflow-hidden break-words`. `data-testid="message-${id}"` and `data-testid="message-content-${id}"` are preserved on the outer/inner containers respectively.
- [x] 3.3 Streaming indicator moved into an `<AuiIf condition={(s) => s.message.status?.type === 'running'}>` slot, rendered as a subtle inline `bg-foreground/40` pulsing dot with `data-testid="message-streaming-${id}"`. Visibility is gated by AUI's running state.
- [x] 3.4 The legacy 4-line italic `ReasoningPart` is replaced by a `Markdown`-rendering reasoning part component, paired with a `ReasoningGroup` wrapper supplied at the top level of `components` that hosts the AUI registry's collapsible reasoning surface (`@/components/assistant-ui/reasoning`).
- [x] 3.5 Tests cover: a disclosure trigger labelled "Reasoning" appears on a multi-part turn; expanding the trigger reveals the source thinking-block text verbatim.
- [x] 3.6 Top-level `ToolGroup` is supplied to `MessagePrimitive.Parts`'s `components` (the actual API placement; not `tools.Group` as the proposal originally said) — sourced from `@/components/assistant-ui/tool-group`.
- [x] 3.7 The AUI registry's `ToolGroup` already has the correct semantics: a single isolated tool call is not wrapped, while consecutive tool calls render under a single grouping container. (Verified by inspecting the `MessageParts` runtime in `@assistant-ui/core`.)
- [x] 3.8 `ToolGroup` (variant=`outline` by default) carries a thin border + rounded styling that visually matches the tool-call cards rendered by individual `Toolkit.render` callbacks.

## 4. Toolkit — wire `Terminal` for `Bash`

- [x] 4.1 Imported `Terminal` from `@/components/tool-ui/terminal` in `runtime/toolkit.tsx`.
- [x] 4.2 Replaced the `Bash` entry's `render` body. Maps: `args.command` → `Terminal.command`, `result` (toString'd) → `Terminal.stdout` (or `stderr` when `isError`). The destructive-border wrapper is preserved for `isError` via a wrapping `<div className="border border-destructive/50 rounded-lg">`.
- [x] 4.3 Added `runtime/toolkit.test.tsx` with five specs: Bash renders a `[data-slot="terminal"]` element (not a CodeBlock), passes `args.command` into the prompt and `result` into the output, applies destructive border + stderr routing on error, and confirms `Read` (representative of Read/Grep/Write) still renders without a Terminal.
- [x] 4.4 Read, Grep, Write entries are untouched (verified by inspection — only the `Bash` entry's `render` body changed in `runtime/toolkit.tsx`).

## 5. Storybook — extend `ChatSurface.stories.tsx`

- [x] 5.1 `BubblelessAssistant` story added: walks the assistant text's parent chain and asserts no `bg-secondary` / `bg-card` wrapper.
- [x] 5.2 `ReasoningCollapsed` story added (uses `multiBlockTurn`): the disclosure trigger renders with `data-state="closed"` and expanding it reveals the source text.
- [x] 5.3 `ReasoningStreaming` story added (uses new `reasoningStreaming` fixture — RUN_STARTED + a Thinking custom event with no RUN_FINISHED): trigger renders with `data-state="open"` and the streaming text is visible.
- [x] 5.4 `MultiToolGroup` story added (uses new `multiToolGroup` fixture: Bash → Read → Grep → Write + closing text): asserts exactly one `[data-slot="tool-group-root"]` and a "4 tool calls" trigger label.
- [x] 5.5 `BashTerminal` story added (reuses `toolCallLifecycle`): asserts a `[data-slot="terminal"]` element renders and the `ls -la` command appears.
- [x] 5.6 `ComposerControls` story added: asserts both Plan/Build tab triggers, that Build is active by default, and that the model `combobox` renders.
- [x] 5.7 `MentionPopover` interactive story added: types `@` and asserts the textarea value reflects the typed character. (Project-files mock returns empty in stories without API access — this exercises the popover open path.)
- [x] 5.8 `SlashPopoverEmpty` interactive story added: types `/` and asserts the `slash-empty-state` testid renders the expected "No commands available yet" copy.

## 6. CLAUDE.md update

- [x] 6.1 Updated the "Chat surface" section in `src/Homespun.Web/CLAUDE.md`: dropped the "textarea kept plain" note; added the composer breakdown (`ComposerPrimitive.Root/Input/Send`, shadcn `Tabs`, AUI registry `ModelSelector`, `Unstable_TriggerPopover` for `@`/`/`); added the assistant-message presentation rules (bubble-less, kept user/system bubbles, streaming indicator via `AuiIf`, AUI registry `Reasoning` collapsible, `ToolGroup` at the top level of `components`); noted that `Bash` renders via `Terminal` while Read/Grep/Write keep `CodeBlock`; and pointed at `npx shadcn@latest add https://r.assistant-ui.com/<name>.json` as the preferred installation path for new tool-ui components.

## 7. e2e regression pass

- [x] 7.1 Updated e2e selectors that referenced the old composer/bubble:
  - `e2e/mobile-chat-layout.spec.ts`: `[data-testid^="message-content-"]` is now scoped to `[data-role="user"]` so the `max-w-[90%]` / `max-w-[80%]` width assertions only target the user bubble (assistant messages no longer have a bubble width).
  - `e2e/mention-search.spec.ts`: the chat-section "shows file search popup when typing @ in chat" now asserts AUI's `role=option` items rather than the legacy `role=listbox{name=/file search results/}`. Also documents the dropped `#`-trigger in the chat composer with a new test (PRs+files now share the `@` trigger via the multi-category adapter).
  - `messages.tsx` now stamps `data-role` on every message wrapper so future e2e selectors can disambiguate.
  - The full `npm run test:e2e` still requires a running Vite + AppHost (Aspire dev-mock) — left as a manual / CI step (see "Manual run" note below).
- [ ] 7.2 **Manual run required.** `dotnet run --project src/Homespun.AppHost --launch-profile dev-mock` and exercise: send a message, switch mode via Tabs, switch model via ModelSelector, type `@` and select a file, type `/` and see the empty-state, run a Bash tool call (verify Terminal renders), see a multi-tool turn group, expand a collapsed reasoning surface. Storybook (built green via `npm run build-storybook`) covers most of these in fixture form.
- [ ] 7.3 **Manual run required.** Light + dark mode visual sweep on the chat page; capture before/after screenshots for the PR description.

## 8. Close-out

- [x] 8.1 Pre-PR checklist (frontend portion run locally):
  - `npm run typecheck` ✅ clean
  - `npm run lint:fix` ✅ no new errors introduced (4 pre-existing errors in `error-boundary.tsx` are unchanged from base)
  - `npm run format:check` ✅ clean (formatted `src/index.css` after the AUI registry's `tw-shimmer` import was added)
  - `npm run generate:api:fetch` ✅ no-op (no live server in this run; existing generated client unchanged)
  - `npm test` ✅ 1879 passed / 1 skipped (one benign `div.scrollTo is not a function` JSDOM warning emitted by the AUI viewport — harmless under JSDOM)
  - `npm run build-storybook` ✅ builds clean (22.7s)
  - `npm run test:e2e` — **manual run required** (see Section 7).
  - `dotnet test` — out of scope for this presentation-only change; backend + worker are untouched.
- [ ] 8.2 **Manual step.** PR description should link back to this proposal and to the archived `chat-assistant-ui` change; attach light + dark before/after screenshots; call out the bubble removal as the only intentional visual delta.
