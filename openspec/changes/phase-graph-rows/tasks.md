## 1. Layout: thread `openSpecStates` and add the `phase` render-line type

- [ ] 1.1 Add `TaskGraphPhaseRenderLine` to the `TaskGraphRenderLine` union in `services/task-graph-layout.ts` with fields: `type: 'phase'`, `phaseId: string` (composed as `${issueId}::phase::${name}`), `parentIssueId: string`, `lane: number`, `phaseName: string`, `done: number`, `total: number`, `tasks: PhaseTaskSummary[]`, `isFirstChild: boolean`, `isLastChild: boolean`, `drawTopLine: boolean`, `drawBottomLine: boolean`, `parentLane: number`
- [ ] 1.2 Add an `isPhaseRenderLine(line)` type guard
- [ ] 1.3 Update the `computeLayout` signature to take `openSpecStates?: Record<string, IssueOpenSpecState>` as its fourth argument
- [ ] 1.4 In `renderGroupTreeView`, immediately after pushing each issue line, look up `openSpecStates?.[issueId]?.phases` and emit one phase render line per `PhaseSummary` with `lane = issueLine.lane + 1`, `parentIssueId = issueId`, treating it geometrically as a series child (top-line + arc to parent)
- [ ] 1.5 Apply the same emit logic in `renderGroup` (next-view) — phase lines appear immediately after their parent issue regardless of view mode
- [ ] 1.6 Add a unit test in `task-graph-layout.test.ts` covering: an issue with 3 phases produces issue-line + 3 phase-lines in order; an issue with no phases produces no phase lines; an issue with empty `openSpecStates` entry produces no phase lines
- [ ] 1.7 Verify existing post-processing passes (`multiParentIndex` assignment, `parentLaneReservations`, lane-0 connector logic) skip phase lines via the existing `isIssueRenderLine` type guards — add a regression test that confirms phase lines never get `multiParentIndex` set

## 2. SVG node and row components

- [ ] 2.1 Create `src/Homespun.Web/src/features/issues/components/task-graph-phase-svg.tsx` exporting `TaskGraphPhaseSvg({ line, maxLanes })` that renders a 12px diamond at `(getLaneCenterX(line.lane), getRowCenterY())` with type-color fill at 0.5 opacity and full-opacity stroke; reuse `LaneGuideLines`, `LANE_WIDTH`, `ROW_HEIGHT`, `NODE_RADIUS` from `task-graph-svg.tsx`
- [ ] 2.2 Render the series-child top-line (vertical from row top down to diamond top + arc into diamond) using the same connector geometry as existing series children
- [ ] 2.3 Render the series-child bottom-line (vertical from diamond bottom to row bottom) when `drawBottomLine` is true, so consecutive phase rows under the same parent connect visually
- [ ] 2.4 Apply a green stroke + fill when `done >= total && total > 0` to mirror the existing complete-phase badge color treatment (falls out of the design's open question on color)
- [ ] 2.5 Create `src/Homespun.Web/src/features/issues/components/task-graph-phase-row.tsx` exporting `TaskGraphPhaseRow({ line, maxLanes, isSelected, isExpanded, onToggleExpand })` that renders the SVG + a static label `<span>{phaseName}: {done}/{total}</span>`; no `IssueRowActions`, no editable pills, no execution-mode toggle
- [ ] 2.6 Apply the same `style={{ height: ROW_HEIGHT }}`, role="row", `aria-selected`, `aria-expanded`, focus-visible ring, and `bg-muted` selected styling that issue rows use so keyboard navigation looks identical
- [ ] 2.7 Add `data-testid="task-graph-phase-row"` and `data-phase-id="${phaseId}"` for test selectors
- [ ] 2.8 Create co-located stories: `task-graph-phase-svg.stories.tsx` (default + complete states) and `task-graph-phase-row.stories.tsx` (default + selected + expanded states)

## 3. Inline expansion replaces the modal

- [ ] 3.1 Create `src/Homespun.Web/src/features/issues/components/inline-phase-detail-row.tsx` that takes `{ line: TaskGraphPhaseRenderLine, maxLanes: number }`, renders an `<ul>` of `PhaseTaskSummary` items each with a checkbox SVG + description, with `role="region"` and `aria-label="Phase tasks"`
- [ ] 3.2 Apply `max-h-[400px] overflow-y-auto` to the `<ul>` so very long task lists stay scrollable without forcing the row arbitrarily tall
- [ ] 3.3 Render an SVG-aligned gutter (LaneGuideLines + a vertical line in the parent issue's lane) so the inline panel visually aligns with the graph above
- [ ] 3.4 Mount `<InlinePhaseDetailRow>` from `task-graph-view.tsx` whenever `expandedIds.has(line.phaseId)` for a phase line, immediately after the phase row
- [ ] 3.5 Add a co-located story for the new component
- [ ] 3.6 Add a unit test that mounts an expanded phase with three tasks (two done, one not), asserts checkbox states, and asserts the scroll region's `max-height` style

## 4. Wire phase rows into `task-graph-view.tsx`

- [ ] 4.1 Pass `taskGraph.openSpecStates ?? {}` as the fourth argument to `computeLayout(...)`
- [ ] 4.2 Add a third arm to the render-line switch that mounts `<TaskGraphPhaseRow>` for `'phase'` lines and `<InlinePhaseDetailRow>` immediately after when expanded
- [ ] 4.3 Update the keyboard handler so that when the selected line is a phase: `e` (edit), `r` (run-agent), `m`/`M` (move), and any hierarchy reparent hotkeys silently no-op (`return` early before the existing handler bodies); `j`/`k`/arrow selection, ⏎/double-click expand, and ESC continue to work
- [ ] 4.4 Update click-to-select handlers to accept phase lines as valid selection targets (use `phaseId` as the selected id)
- [ ] 4.5 Verify ESC behaviour: when a phase row is selected and expanded, ESC collapses it (existing logic already handles this if `expandedIds.has(selectedId)`)
- [ ] 4.6 Update `selectedIssueId` state to a more general `selectedRowId` (or document that phase ids are routed through the same state) — both tracking variants are acceptable as long as the contract is clear

## 5. Remove `phase-rollup.tsx` and the inline-badge mount

- [ ] 5.1 Remove the `<PhaseRollupBadges>` import and JSX block from `issue-row-content.tsx` (lines around 186-188)
- [ ] 5.2 Delete `src/Homespun.Web/src/features/issues/components/phase-rollup.tsx`
- [ ] 5.3 Grep the codebase for any remaining references to `PhaseRollupBadges`, `data-testid="phase-rollup-badges"`, `data-testid="phase-badge"`, `data-testid="phase-task-list"`, `data-testid="phase-task"`; update or remove
- [ ] 5.4 Delete or update any test files that asserted on the removed badges/modal flow

## 6. Drop `EXPANDED_DETAIL_HEIGHT` and switch to DOM measurement

- [ ] 6.1 Grep all callers of `getRowY` (likely `task-graph-svg.tsx`, `task-graph-view.tsx`, scroll-into-view helpers); read each and confirm the only consumer is scroll-restoration / scroll-into-view
- [ ] 6.2 Replace each `getRowY(rowIndex, expandedIds, issueLines)` call with `rowRefs.current[rowIndex]?.offsetTop ?? 0` from a per-row ref array maintained on the rendered row components
- [ ] 6.3 Delete the `EXPANDED_DETAIL_HEIGHT = 700` constant from `task-graph-svg.tsx`
- [ ] 6.4 Delete the `getRowY` function (or leave a small forwarding shim that just returns `offsetTop`, depending on call-site complexity)
- [ ] 6.5 Update `inline-issue-detail-row.tsx` to remove any reliance on a fixed outer height; apply `max-h-[400px] overflow-y-auto` to the inner description/content scroll region instead
- [ ] 6.6 Add a unit test (or a Storybook visual) showing an expanded issue row whose content is taller than 400px scrolls within the inner region without forcing the row container tall

## 7. Tests and verification

- [ ] 7.1 Add unit tests for the new `'phase'` render line covering: emission order under tree view, emission order under next view, no emission when phases is empty/missing, parent-issue lane is preserved
- [ ] 7.2 Add e2e test (under `src/Homespun.Web/e2e/`) using the seeded mock data: navigate to the demo project, find ISSUE-006, assert at least one phase row is present, click one, assert the inline panel renders with the right task list
- [ ] 7.3 Add e2e test that selects a phase row, presses `e`, and asserts no edit dialog opens; press `r`, assert no agent dialog; press `m`, assert no move state activates
- [ ] 7.4 Run `npm run lint:fix && npm run format:check && npm run typecheck && npm test && npm run build-storybook` from `src/Homespun.Web` — all green
- [ ] 7.5 Run `dotnet test` — all backend suites green (no backend changes expected to break)
- [ ] 7.6 Boot `dotnet run --project src/Homespun.AppHost --launch-profile dev-mock` and visually confirm the diamond shape, parent connectors, inline expansion, and that no orphan badges remain anywhere on the issue rows
- [ ] 7.7 Visually verify on a 1080p screen that an issue with 6+ phases renders cleanly (no wrap, no overflow) — confirms the original bug is fixed
