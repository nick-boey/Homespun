## ADDED Requirements

### Requirement: Tailwind v4 theme uses idiomatic token declarations

The web client SHALL declare design tokens in `src/index.css` using Tailwind v4's direct-token form (`@theme` / `@theme inline`), not the v3-compatibility shim that wraps raw HSL triplets with `hsl(var(--x))`. The shadcn zinc palette SHALL be preserved.

#### Scenario: No `hsl(var(--x))` shim remains
- **WHEN** `src/index.css` is read
- **THEN** no `@theme` token SHALL be declared as `hsl(var(--x))` or equivalent indirection
- **AND** the file SHALL match the shape prescribed by shadcn's current `new-york` reference for Tailwind v4

#### Scenario: Visible theme is unchanged
- **WHEN** the app is rendered before and after the refactor with identical light-mode state
- **THEN** pixel-level screenshots of representative pages (projects list, session detail, settings) SHALL show no visible difference

#### Scenario: Dark mode continues to work
- **WHEN** the `dark` class is applied to `html`
- **THEN** all tokens SHALL resolve to their dark-mode values via the `@custom-variant dark` selector
- **AND** no JavaScript-driven inline style overrides SHALL be required

### Requirement: Storybook 10.3.5 harness runs against the real theme

The web client SHALL provide a Storybook 10.3.5 harness that renders components against the same Tailwind v4 theme, font stack, and dark-mode mechanism used by the application.

#### Scenario: Storybook preview imports the app stylesheet
- **WHEN** `storybook dev` starts
- **THEN** `.storybook/preview.ts` SHALL import `src/index.css`
- **AND** the typography plugin's styles SHALL be present in the rendered stories

#### Scenario: Dark mode toolbar toggles the theme
- **WHEN** a user toggles the theme toolbar in Storybook
- **THEN** the `dark` class SHALL be applied to the document root
- **AND** all tokens SHALL resolve to their dark-mode values

#### Scenario: Storybook build succeeds in the pre-PR checklist
- **WHEN** `npm run build-storybook` is executed against a clean working tree
- **THEN** the command SHALL exit 0
- **AND** the resulting static output SHALL contain at least one story per shadcn-native `components/ui/*` component

### Requirement: `components/ui/` divergences from shadcn registry are classified and documented

The web client SHALL maintain an `INVENTORY.md` under `src/components/ui/` that classifies every file as `native`, `divergent-keep`, `divergent-replace`, or `chat-owned`. Each file classified `divergent-keep` SHALL include a single-line comment at the top of the source file identifying the reason the divergence is intentional.

#### Scenario: Every ui file is classified
- **WHEN** `INVENTORY.md` is read
- **THEN** every file in `components/ui/` ending in `.tsx` SHALL appear in the table with a classification

#### Scenario: Divergent-keep files carry a rationale comment
- **WHEN** a file in `components/ui/` is classified `divergent-keep`
- **THEN** the source file SHALL begin with a one-line comment referencing the reason the divergence exists

#### Scenario: Chat-owned files are flagged as disposable
- **WHEN** a file is classified `chat-owned`
- **THEN** the `INVENTORY.md` row SHALL state that the file is expected to be deleted or relocated by the `chat-assistant-ui` change
- **AND** no Storybook story SHALL be authored for that file by this change

### Requirement: Pre-PR checklist includes a Storybook build step

The project-root `CLAUDE.md` pre-PR checklist SHALL include `npm run build-storybook` after `npm run test:e2e`.

#### Scenario: Checklist step is reachable from the web package
- **WHEN** a contributor runs the pre-PR checklist from the web package directory
- **THEN** the `build-storybook` step SHALL execute without additional configuration
