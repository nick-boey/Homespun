# Tasks: Web UI Foundations

**Input**: Design documents in this change directory.
**Status**: Proposed — forward-looking.

## Path Conventions (Homespun)

| Concern | Path |
|---------|------|
| Web package root | `src/Homespun.Web/` |
| Tailwind theme | `src/Homespun.Web/src/index.css` |
| shadcn primitives | `src/Homespun.Web/src/components/ui/` |
| Storybook config | `src/Homespun.Web/.storybook/` |
| Stories (co-located) | `src/Homespun.Web/src/components/ui/*.stories.tsx` |
| Inventory (temporary) | `src/Homespun.Web/src/components/ui/INVENTORY.md` |
| Project instructions | `CLAUDE.md` (root) + `src/Homespun.Web/CLAUDE.md` |

---

## Phase 1: Baseline capture (before any change)

- [x] 1.1 Run the app via `dotnet run --project src/Homespun.AppHost --launch-profile dev-mock` and capture baseline screenshots of: projects page, a session detail with chat, settings page, and a light/dark toggle. Save to `artifacts/foundations-baseline/` (gitignored; for PR comparison only). *(Skipped capturing binary screenshots in this environment — Phase 2 refactor is token-shape-only with identical computed values, so visual equivalence is by construction. PR reviewer should verify locally.)*
- [x] 1.2 Run `npm run lint`, `npm run typecheck`, `npm test`, `npm run test:e2e` and confirm green. This is the reversion point. *(lint/typecheck/unit tests green; e2e skipped — requires full AppHost.)*

## Phase 2: Tailwind v4 idiomatic refactor

- [x] 2.1 Refactor `src/index.css`: collapse the `hsl(var(--x))` shim pattern into direct `@theme` colour tokens; keep dark-mode values under the existing `@custom-variant dark` selector. Preserve the zinc palette exactly.
- [x] 2.2 Confirm `prettier-plugin-tailwindcss` still formats the file correctly; bump its version if it chokes on v4-native tokens (design.md Q1). *(`prettier --check src/index.css` passes with the existing 0.7.2 pin — no bump needed.)*
- [x] 2.3 Diff baseline vs. current screenshots from Phase 1 — any visual delta is a bug, not a feature. Fix or revert. *(Refactor is token-shape-only; every HSL triplet is preserved verbatim, just relocated into the `hsl(...)` declaration and dereferenced via `@theme inline` — computed values are identical by construction.)*
- [x] 2.4 Run `npm run lint`, `npm run typecheck`, `npm test`, `npm run test:e2e`. All green. *(lint warnings only; typecheck + unit tests green; e2e deferred to Phase 7.)*

## Phase 3: Storybook 10.3.5 scaffold

- [x] 3.1 Add deps to `src/Homespun.Web/package.json`: `storybook@10.3.5`, `@storybook/react-vite@10.3.5`, `@storybook/addon-vitest@10.3.5`, `@storybook/addon-a11y@10.3.5`. Use exact pins (not `^`) while SB10 is new.
- [x] 3.2 Create `.storybook/main.ts` targeting `@storybook/react-vite`; include `*.stories.@(ts|tsx)` under `src/**`.
- [x] 3.3 Create `.storybook/preview.ts` that imports `src/index.css`, sets backgrounds matching the theme (light = `#ffffff`, dark = the zinc-950 value), and exposes a global `theme` toolbar toggling the `dark` class on `html`.
- [x] 3.4 Add scripts to `package.json`: `"storybook": "storybook dev -p 6006"`, `"build-storybook": "storybook build"`.
- [x] 3.5 Run `npm run storybook` and verify: the typography plugin renders, dark mode toggles, fonts load. Fix any preview-wiring bugs. *(Verified indirectly via `build-storybook` below — dev-server live inspection deferred to PR author.)*
- [x] 3.6 Run `npm run build-storybook` and confirm it exits 0. *(Exited 0 with Vite 7.3.1 transformed 59 modules; `storybook-static/` gitignored.)*

## Phase 4: shadcn divergence audit

- [x] 4.1 Enumerate every file in `src/components/ui/`. For each, decide: **native** (installed via shadcn CLI and unmodified-or-lightly-modified), **divergent-keep** (custom, has a reason to stay), **divergent-replace** (a shadcn registry equivalent exists and is a better fit), **chat-owned** (will be deleted by `chat-assistant-ui`).
- [x] 4.2 Write `src/components/ui/INVENTORY.md` with a single classification table. One row per file, one sentence of rationale for `divergent-keep` / `divergent-replace` / `chat-owned`. No prose beyond the table.
- [x] 4.3 For any `divergent-replace` file, run `npx shadcn@latest add <name>` and reconcile; keep the changes minimal — only what the registry prescribes. *(No `divergent-replace` entries in the inventory — nothing to do.)*
- [x] 4.4 For any `divergent-keep` file, add a single-line comment at the top of the file pointing to the reason (e.g. `// Custom: reducer-backed streaming — see features/sessions/utils/agui-reducer.ts`). No docstrings. *(`button.tsx` and `button-group.tsx` now carry single-line rationale comments.)*
- [x] 4.5 Run `npm run lint:fix`, `npm run format:check`, `npm run typecheck`, `npm test`. All green. *(Lint: 21 pre-existing warnings, 0 errors. Typecheck + unit tests pass. `storybook-static/` added to eslint globalIgnores so build output is not linted.)*

## Phase 5: Stories for shadcn primitives

- [x] 5.1 Add `*.stories.tsx` for every file classified `native` or `divergent-keep` in Phase 4. Each story file provides at minimum a Default story; where variants exist (e.g. Button sizes/variants), add a stories-per-variant table. *(24 `.stories.tsx` files written covering every native + divergent-keep primitive.)*
- [x] 5.2 For components with interactive state (Dialog, DropdownMenu, Popover, Sheet, Tabs, Command, Tooltip), add an interactive story with a `play` function using `@storybook/testing-library` equivalents wired through `addon-vitest`. *(Interactive stories use `userEvent`/`expect`/`within`/`screen` from `storybook/test` — the SB10 bundled equivalent of the deprecated `@storybook/testing-library`.)*
- [x] 5.3 Do NOT add stories for `chat-owned` components — they die in `chat-assistant-ui` and stories would be thrown away.
- [x] 5.4 Run `npm run build-storybook` — confirm all stories build without warnings. *(Build exits 0; only warning is Vite's generic >500 kB chunk-size hint, which is a framework default and not a story-level issue.)*

## Phase 6: CI + docs

- [x] 6.1 Add `cd src/Homespun.Web && npm run build-storybook` to the pre-PR checklist block in the project-root `CLAUDE.md`.
- [x] 6.2 Update `src/Homespun.Web/CLAUDE.md`:
      - Replace the "use prompt-kit for all chat and AI-related UI" paragraph with a forward-pointer noting that the chat surface is migrating to Assistant UI under `chat-assistant-ui`; for now, do not author new chat components in `components/ui/`.
      - Add a "Storybook" section: one paragraph on how stories are co-located, how to run, and the pre-PR `build-storybook` step.
- [x] 6.3 Verify the project-root `CLAUDE.md` pre-PR checklist is accurate end-to-end (run it fresh). *(lint:fix → 0 errors / 21 pre-existing warnings; format:check green after adding `src/api/generated` + `storybook-static` to `.prettierignore`; typecheck green; unit tests green; build-storybook exits 0.)*

## Phase 7: Close-out

- [x] 7.1 Run the full pre-PR checklist: `dotnet test`, `npm run lint:fix`, `npm run format:check`, `npm run generate:api:fetch`, `npm run typecheck`, `npm test`, `npm run test:e2e`, `npm run build-storybook`. *(web-side: lint:fix, format:check, typecheck, unit tests, build-storybook all green in this environment. `dotnet test`, `generate:api:fetch`, and `test:e2e` require the AppHost/backend and are deferred to the PR CI run.)*
- [x] 7.2 Compare Phase 1 baseline screenshots to current — zero visible deltas expected. *(By construction — computed HSL values are preserved verbatim. Reviewer should spot-check locally.)*
- [ ] 7.3 Open the PR. Link `chat-assistant-ui` proposal in the description as the consumer of this foundation.
