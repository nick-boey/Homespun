## Context

`src/Homespun.Web` is a Vite + React 19 + TypeScript SPA styled with Tailwind v4 and shadcn/ui `new-york`/zinc. The chat surface (`features/sessions/`) is stable enough that a runtime migration (`chat-assistant-ui`) is now viable, but the UI foundation beneath it has two problems that raise the cost of that migration:

1. **Tailwind theme carries v3-era syntax.** `src/index.css` defines tokens as `--color-X: hsl(var(--X))` with raw HSL triplets on `:root` — the shim pattern used during the v3→v4 migration. Tailwind v4 prefers direct colour tokens in `@theme` (optionally `@theme inline` where var indirection matters). This is cosmetic but drifts from shadcn's current new-york reference template, which complicates `npx shadcn@latest add`.

2. **No component harness.** Every change to `components/ui/` or the custom prompt-kit primitives is validated only through e2e or manual browser checks. With Assistant UI primitives about to replace the chat surface, the inability to script fixtures through a range of session states is a real gap.

Storybook 10.3.5 shipped on 2025-10 as ESM-only with a 29% lighter bundle and module-level automocking. The project is already ESM (`"type": "module"`), Vite-native, and React 19 — SB10 is the right version, not a guess.

## Goals / Non-Goals

**Goals:**
- Idiomatic Tailwind v4 theme in `src/index.css` with no visual regression.
- Storybook 10.3.5 running against the project's real theme + fonts.
- A classified inventory of `components/ui/` distinguishing shadcn-native, intentionally-custom, and replaceable components.
- Stories for every shadcn-native `components/ui/` component — enough that a reviewer can see the baseline.
- `build-storybook` wired into the pre-PR checklist.

**Non-Goals:**
- Replacing the chat UI (`features/sessions/components/*`). That is `chat-assistant-ui`.
- Introducing AI Elements. Cherry-picking specific components (`Plan`, `Reasoning`) is a post-`chat-assistant-ui` follow-up.
- Visual/design changes. The zinc palette and radius stay.
- Re-theming shadcn to a different base colour.
- Replacing existing prompt-kit primitives that the chat UI still consumes (they die when `chat-assistant-ui` lands, not here).

## Decisions

### D1: Cosmetic v4 refactor, not a re-theme

**Decision:** Change the token declaration shape only. Palette values stay identical; only the wrapping changes.

**Rationale:** Re-theming is a design decision; this change is a hygiene change. Conflating the two makes review harder and adds regression risk for no gain.

**Consequence:** Screenshots taken before and after this change should diff to zero. If they don't, the refactor is wrong.

### D2: Storybook stories for `components/ui/` only

**Decision:** This change adds stories for shadcn primitives in `src/components/ui/`. Feature-level stories (`features/sessions/**/*.stories.tsx`) land in `chat-assistant-ui`.

**Rationale:** Feature stories need fixture data — in particular, scripted AG-UI event envelopes for the chat surface. That fixture infrastructure is owned by the chat migration, not by this change.

### D3: Divergence audit produces an inventory, not a purge

**Decision:** The audit *classifies* every file in `components/ui/` and documents the result. It does not aggressively delete files. Divergent primitives stay unless a shadcn-registry replacement exists today.

**Rationale:** The chat-runtime migration (`chat-assistant-ui`) will delete several of the prompt-kit primitives on its own. Purging here would cause merge churn and risks deleting something the chat migration still needs in transition. The inventory hands the chat migration a clear map of what it inherits.

**Consequence:** `INVENTORY.md` is a decision-capture artefact, not a README. It rots the moment the chat migration lands — at which point that change is responsible for deleting it or updating it.

### D4: Storybook build in pre-PR checklist, not a separate CI gate

**Decision:** `build-storybook` is added to the checklist in the project-root `CLAUDE.md`. No separate CI job is added in this change.

**Rationale:** The project's existing pattern is one CI job per test surface. Adding a dedicated SB job is overhead until there are enough stories to justify it. `build-storybook` as part of the pre-PR checklist catches drift at author time.

**Consequence:** If stories are broken on main, they only surface when the next PR author runs the checklist. That's acceptable at this story count; revisit once `chat-assistant-ui` lands and the story surface is larger.

### D5: `@storybook/addon-vitest`, not the legacy test runner

**Decision:** Use SB10's `addon-vitest` (Vitest 4 compatible, runs stories as component tests).

**Rationale:** The project already uses Vitest 4.0.18. `addon-vitest` aligns story-level interaction tests with the existing test harness. The legacy `@storybook/test-runner` (Playwright-based) would add a parallel test infrastructure we don't want.

## Risks / Trade-offs

| Risk | Mitigation |
|---|---|
| Tailwind v4 token refactor visually regresses the app | D1 — take before/after screenshots of a representative set of pages (project list, session detail with chat, settings); diff at review time. |
| Storybook 10 is four months old; edge-case bugs around Vite 7 / React 19 | Pin the 10.3.5 versions in `package.json` and revisit on the next dependabot cycle. If a real blocker appears, fall back to SB 9.x (last line with wide Vite 7 support). |
| Inventory rot: `INVENTORY.md` goes stale immediately | D3 — it's explicitly short-lived; the chat migration owns deleting it. |
| Divergent primitives that the chat migration deletes get story files written for them now that will also need deletion | D2 — stories only for shadcn-native primitives; prompt-kit primitives (doomed) get no story. |
| Dark mode regression from removing JS toggles | Theme toggle is driven by `next-themes` already; the `@custom-variant dark` CSS variant is the only selector path. Verify by toggling in Storybook against both light and dark backgrounds. |

## Open Questions

- **Q1:** Does the existing `prettier-plugin-tailwindcss` need a version bump to recognise v4-idiomatic syntax? Check at implementation time; defer decision to the PR.
- **Q2:** Should `@storybook/addon-a11y` be strict (fail the build on violations) or advisory? Default advisory; strict mode is an enhancement once the story surface is established.
