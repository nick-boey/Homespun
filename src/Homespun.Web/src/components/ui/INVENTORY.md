# `components/ui/` Divergence Inventory

**Status:** short-lived. This file is owned by the `web-ui-foundations` change and is
expected to be deleted or overwritten by the `chat-assistant-ui` change once the
prompt-kit surface is replaced by Assistant UI primitives.

**Legend:**
- `native` — installed via `npx shadcn@latest add …`, unmodified or only cosmetic edits.
- `divergent-keep` — custom or extended; has a documented reason to stay.
- `divergent-replace` — a shadcn registry equivalent exists and is a better fit; should be replaced by running the CLI.
- `chat-owned` — imported from prompt-kit for the chat surface; will be deleted or relocated by `chat-assistant-ui`. No story is authored by this change.

| File | Classification | Rationale |
|---|---|---|
| `alert-dialog.tsx` | native | shadcn primitive, unmodified. |
| `avatar.tsx` | native | shadcn primitive, unmodified. |
| `badge.tsx` | native | shadcn primitive, unmodified. |
| `button.tsx` | divergent-keep | Extra size variants (`xs`, `touch`, `icon-xs`, `icon-sm`, `icon-lg`, `icon-touch`) beyond the registry default — required by compact toolbars and the 44 px mobile tap-target rule. |
| `button-group.tsx` | divergent-keep | Thin wrapper that fuses adjacent buttons into a pill group; no current shadcn-registry equivalent is a drop-in replacement for the zero-gap, rounded-edges pattern we use. |
| `card.tsx` | native | shadcn primitive, unmodified. |
| `checkbox.tsx` | native | shadcn primitive, unmodified. |
| `code-block.tsx` | chat-owned | prompt-kit primitive; consumed only by the chat/markdown surface. Dies with `chat-assistant-ui`. |
| `collapsible.tsx` | native | shadcn primitive, unmodified. |
| `command.tsx` | native | shadcn primitive, unmodified. |
| `dialog.tsx` | native | shadcn primitive, unmodified. |
| `dropdown-menu.tsx` | native | shadcn primitive, unmodified. |
| `input.tsx` | native | shadcn primitive, unmodified. |
| `label.tsx` | native | shadcn primitive, unmodified. |
| `loader.tsx` | chat-owned | prompt-kit spinner palette; consumed by chat streaming UI. Dies with `chat-assistant-ui`. |
| `markdown.tsx` | chat-owned | prompt-kit markdown renderer with Shiki streaming; replaced by Assistant UI's content primitives in `chat-assistant-ui`. |
| `message.tsx` | chat-owned | prompt-kit chat-message container; replaced by Assistant UI Thread primitives in `chat-assistant-ui`. |
| `popover.tsx` | native | shadcn primitive, unmodified. |
| `prompt-input.tsx` | chat-owned | prompt-kit input composer; replaced by Assistant UI Composer primitives in `chat-assistant-ui`. |
| `scroll-to-bottom.tsx` | chat-owned | prompt-kit floating button used only by the chat thread; replaced by Assistant UI auto-scroll in `chat-assistant-ui`. |
| `select.tsx` | native | shadcn primitive, unmodified. |
| `separator.tsx` | native | shadcn primitive, unmodified. |
| `sheet.tsx` | native | shadcn primitive, unmodified. |
| `skeleton.tsx` | native | shadcn primitive, unmodified. |
| `sonner.tsx` | native | shadcn primitive; inline `--normal-*` style hooks updated to the v4-idiomatic `var(--token)` shape — no behavioural divergence. |
| `switch.tsx` | native | shadcn primitive, unmodified. |
| `table.tsx` | native | shadcn primitive, unmodified. |
| `tabs.tsx` | native | shadcn primitive, unmodified. |
| `text-shimmer.tsx` | chat-owned | prompt-kit shimmering-text used by `thinking-bar` and chat streaming; dies with `chat-assistant-ui`. |
| `textarea.tsx` | native | shadcn primitive, unmodified. |
| `thinking-bar.tsx` | chat-owned | prompt-kit thinking indicator; replaced by Assistant UI's tool-call/reasoning primitives in `chat-assistant-ui`. |
| `tooltip.tsx` | native | shadcn primitive, unmodified. |
