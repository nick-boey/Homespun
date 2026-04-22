## Why

The `openspec-integration` spec at `Requirement: Virtual sub-issue rendering from tasks.md` already mandates that the graph SHALL show "one virtual sub-node per phase heading" and that phase nodes SHALL be display-only. The current implementation took a shortcut and rendered phases as inline badges (`PhaseRollupBadges` in a `flex flex-wrap` container) inside the issue row's content strip. The row has a fixed 40px height, so when an issue has multiple phases the badges wrap and visually overflow into adjacent rows ("overlapping badges that sit over the UI"). This is both a visual bug and a spec/implementation drift.

## What Changes

- Add a new `'phase'` render-line type to the task graph layout pipeline. Phase lines are full participants in the render result (selection, expand, scroll-into-view, vertical layout) but are rendered with a distinct visual treatment.
- Render phase lines with a diamond ◆ symbol (`TaskGraphPhaseSvg`) parented to the issue node above, using the existing series-child connector geometry.
- Replace the modal-based `PhaseRollupBadges` flow with inline expansion: double-click or ⏎ on a phase row toggles an `InlinePhaseDetailRow` showing the checkbox task list directly under the phase row.
- Remove `phase-rollup.tsx` entirely and the modal it renders.
- Block edit / run-agent / move / hierarchy hotkeys when a phase row is the selected row (selection itself works; the actions silently no-op).
- Phase rows render no editable pills (no type/status dropdowns), no execution-mode toggle, no `IssueRowActions`, no multi-parent badge.
- **BREAKING (UI-only):** the existing phase-rollup badges and modal are removed; any tests asserting on `data-testid="phase-rollup-badges"` or `data-testid="phase-badge"` need updating to the new row testids.
- Drop the `EXPANDED_DETAIL_HEIGHT = 700` fixed-height constant in `task-graph-svg.tsx`. Inline detail content gets `max-h-[400px] overflow-y-auto` on its inner scroll region instead of a fixed outer height. Replace `getRowY` callers (scroll-into-view) with `element.offsetTop` measurement.
- Thread `openSpecStates` into `computeLayout` so phase-row synthesis happens in one place.

## Capabilities

### New Capabilities
<!-- None — the spec already covers virtual phase rendering. -->

### Modified Capabilities
- `openspec-integration`: the existing `Virtual sub-issue rendering from tasks.md` requirement is modified to (a) drop the modal-based phase detail flow in favour of inline expansion, (b) explicitly state that phase rows are full graph participants for selection and expand but block dispatch/edit/move/hierarchy actions, and (c) require the diamond shape for phase nodes so the visual distinction from issue nodes (round/square) is unambiguous.

## Impact

- **Removed code:**
  - `src/Homespun.Web/src/features/issues/components/phase-rollup.tsx` (component + modal)
  - The `<PhaseRollupBadges>` mount in `issue-row-content.tsx`
  - `EXPANDED_DETAIL_HEIGHT` constant in `task-graph-svg.tsx`
- **New code:**
  - `src/Homespun.Web/src/features/issues/components/task-graph-phase-svg.tsx` — diamond SVG node
  - `src/Homespun.Web/src/features/issues/components/task-graph-phase-row.tsx` — phase row component
  - `src/Homespun.Web/src/features/issues/components/inline-phase-detail-row.tsx` — inline expanded panel showing task checkboxes
- **Modified code:**
  - `src/Homespun.Web/src/features/issues/services/task-graph-layout.ts` — new `TaskGraphPhaseRenderLine` type, `computeLayout` accepts `openSpecStates`, synthesises phase lines
  - `src/Homespun.Web/src/features/issues/components/task-graph-view.tsx` — third arm in render-line switch, keyboard handler short-circuits action keys when selected line is a phase
  - `src/Homespun.Web/src/features/issues/components/task-graph-svg.tsx` — drop `EXPANDED_DETAIL_HEIGHT` and rework `getRowY` callers to use DOM measurement
  - `src/Homespun.Web/src/features/issues/components/issue-row-content.tsx` — remove `PhaseRollupBadges` import and mount
  - `src/Homespun.Web/src/features/issues/components/inline-issue-detail-row.tsx` — replace fixed-height container with `max-h` + scroll on content
- **No backend changes.** `IssueOpenSpecState.Phases` already on the wire.
- **No API or wire-format changes.**
- **Tests:**
  - New unit tests for `computeLayout` covering phase-line synthesis, ordering, and skipped synthesis when `phases` is empty
  - New unit tests for `TaskGraphPhaseRow` covering disabled action surfaces and inline expansion
  - Update tests that asserted on the old badge/modal flow
  - New e2e test covering the keyboard-selection + expand interaction on a phase row in the seeded mock data
- **Depends on `mock-openspec-seeding`** for visual validation.
