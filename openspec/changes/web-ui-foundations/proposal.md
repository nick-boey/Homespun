## Why

`src/Homespun.Web` is already on Tailwind v4.2.1, shadcn/ui `new-york`/zinc, and React 19, but the setup carries v3-era conventions that predate the v4 upgrade — the `@theme` block wraps raw HSL vars with `hsl(var(--x))` shims, and `components/ui/` mixes genuine shadcn primitives with hand-rolled prompt-kit components (`prompt-input`, `message`, `scroll-to-bottom`, `thinking-bar`, `text-shimmer`, `loader`, `markdown`, `code-block`) that were imported before the chat surface stabilised. There is no component harness: every UI regression is caught via e2e or not at all, which makes the upcoming Assistant UI migration (`chat-assistant-ui`) riskier than it needs to be.

This change cleans up the foundation so the chat-runtime migration can land against a stable, tested, scriptable UI baseline — and so the shadcn CLI add flow works cleanly against an idiomatic v4 theme.

## What Changes

- **Tailwind v4 idiomatic refactor**
  - Replace the `hsl(var(--x))`-wrapped variables in `@theme` with direct color tokens per Tailwind v4's recommended shape; use `@theme inline` for tokens that must resolve at CSS-var time.
  - Preserve the current colour palette (zinc / shadcn `new-york`) — no visual regression.
  - Consolidate dark-mode handling to the `@custom-variant dark` already defined; remove any legacy JS dark-mode toggles that duplicate CSS.

- **Storybook 10.3.5 scaffold**
  - Add `@storybook/react-vite` (SB 10.3.5), `@storybook/addon-vitest`, `@storybook/addon-a11y`. ESM-only SB10 fits the existing `"type": "module"` package.
  - Wire Storybook to import `src/index.css` in `preview.ts` so stories render under the real Tailwind v4 theme.
  - `.storybook/` config at the web package root; stories co-located `*.stories.tsx` next to components.
  - Add `npm run storybook` / `npm run build-storybook`; include `build-storybook` in the pre-PR checklist so SB drift is caught in CI.

- **shadcn divergence audit**
  - Classify every file in `src/components/ui/` as (a) shadcn-native registry component, (b) intentional custom primitive with a documented reason, or (c) replaceable by a registry component.
  - Replace (c) via `npx shadcn@latest add …` where an idiomatic equivalent exists.
  - For (b), add a short header comment (single line) pointing to the reason (e.g. `// Custom: AG-UI reducer-backed streaming`). No chatty docs.
  - Document the audit result in `src/Homespun.Web/src/components/ui/INVENTORY.md`.

- **Component-level stories for shadcn primitives**
  - Add a default and interactive story for every component in `components/ui/` so the baseline is visible in Storybook. Tool-specific and feature stories land in `chat-assistant-ui`.

- **CLAUDE.md update**
  - Replace the "prompt-kit for all chat components" line with a pointer forward to `chat-assistant-ui` (the chat-runtime migration). Add Storybook to the authoring workflow.

Behavior kept identical in this change:
- Visual theme (zinc, radius, typography plugin).
- shadcn CLI contract — `components.json` shape stays backward-compatible with `npx shadcn@latest add`.
- Chat UI — not touched here. That's `chat-assistant-ui`.

## Capabilities

### New Capabilities
- `web-ui-foundations`: Idiomatic Tailwind v4 theme, Storybook 10 harness, and a documented audit of `components/ui/` divergences from the shadcn registry.

### Modified Capabilities
<!-- None — this is net-new tooling + a cosmetic theme refactor. No backend or server spec changes. -->

## Impact

- **Frontend**: `src/Homespun.Web/`
  - `src/index.css` — v4 idiomatic theme refactor.
  - `src/components/ui/*` — possible replacements for divergent primitives; new `INVENTORY.md`.
  - `.storybook/` — new directory with `main.ts`, `preview.ts`.
  - `*.stories.tsx` — new files, one per `components/ui/*` component.
  - `package.json` — SB10 deps + `storybook`, `build-storybook` scripts.
  - `CLAUDE.md` — guidance update.
- **CI**: `build-storybook` added to the pre-PR checklist in the project root `CLAUDE.md`.
- **Backend / Worker / Shared / Server specs**: unaffected.
- **Risk**: Low. No production behaviour changes; cosmetic + tooling. The shadcn audit may churn a handful of files but the visible UI is preserved.
- **Dependencies**: None. The Assistant UI change (`chat-assistant-ui`) consumes this foundation but can technically ship first; sequencing is a preference, not a hard gate.

## Follow-ups (explicitly out of scope)

- Replacing the chat surface (`features/sessions/components/*`) — `chat-assistant-ui`.
- Cherry-picking AI Elements components (`Plan`, `Reasoning`, `CodeBlock`) — tracked as a follow-up once Assistant UI lands and the surfaces they'd serve are clear.
