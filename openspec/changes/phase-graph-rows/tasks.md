> **Sequencing precondition:** all tasks below assume `upgrade-fleece-core-v3` AND `move-graph-layout-to-client` (PR #812) have merged. Before starting, confirm: (a) `Fleece.Core` is at 3.0.0 in both csproj files, (b) `task-graph-layout.ts` no longer contains `renderGroup` / `renderGroupTreeView` / `parentLaneReservations`, (c) `TaskGraphResponse` has an `edges` field, (d) `task-graph-layout.ts` exports both `computeLayout` (legacy/diff) and `computeLayoutFromIssues` (live), (e) `task-graph-view.tsx` consumes `computeLayoutFromIssues` and `static-task-graph-view.tsx` consumes `computeLayout`. If any of these is false, stop and rebase against the current shape before editing tasks.

## 0. Preliminary: thread `openSpecStates` into both layout entry points

> Phase synthesis tasks 1.4–1.5 below assume the layout entry points already see `openSpecStates`. The data is available client-side today (fetched at `task-graph-view.tsx:142` via `useOpenSpecStates`, and present on `TaskGraphResponse.openSpecStates` for diffs) but is not threaded into either layout function. Add the parameter plumbing first so the type for `TaskGraphPhaseRenderLine` (task 1.1) lands without immediate downstream type errors.

- [x] 0.1 Extend `ComputeLayoutInput` (`task-graph-layout.ts:276`) with `openSpecStates?: Record<string, IssueOpenSpecState> | null`. Destructure it in `computeLayoutFromIssues` (line 456 onward) with a `null` default
- [x] 0.2 Add a fourth optional parameter `openSpecStates?: Record<string, IssueOpenSpecState> | null` to the legacy `computeLayout(taskGraph, _maxDepth, viewMode)` signature (`task-graph-layout.ts:164`); default it to `taskGraph?.openSpecStates ?? null` inside the function so existing callers keep working unchanged
- [x] 0.3 Update the live-graph call site (`task-graph-view.tsx:184` inside the `computeLayoutFromIssues({…})` `useMemo`) to pass `openSpecStates: openSpecStatesHook.openSpecStates ?? null`. Add `openSpecStatesHook.openSpecStates` to the `useMemo` dependency tuple (~line 192-198)
- [x] 0.4 Update the diff-view call site (`static-task-graph-view.tsx:62`) — either rely on the `taskGraph?.openSpecStates` default from 0.2, or pass it explicitly for clarity. Pick one and keep it consistent
- [x] 0.5 Add a unit test that `computeLayout` and `computeLayoutFromIssues` both accept and forward `openSpecStates` without throwing when it is `null`, `undefined`, or `{}`

## 1. Layout: add the `phase` render-line type and synthesise rows in both entry points

- [x] 1.1 Add `TaskGraphPhaseRenderLine` to the `TaskGraphRenderLine` union in `services/task-graph-layout.ts` with fields: `type: 'phase'`, `phaseId: string` (composed as `${issueId}::phase::${name}`), `parentIssueId: string`, `lane: number`, `phaseName: string`, `done: number`, `total: number`, `tasks: PhaseTaskSummary[]`. (The connector-style booleans from the pre-Fleece draft — `isFirstChild`, `drawTopLine`, etc. — are deliberately omitted: connector rendering is now driven by the synthetic edges added in 1.5, not by per-line flags.)
- [x] 1.2 Add an `isPhaseRenderLine(line)` type guard
- [x] 1.3 (Signature work is in §0.) Confirm both entry points return `{ lines: TaskGraphRenderLine[], edges: TaskGraphEdge[] }` (`computeLayout` returns `TaskGraphLayoutResult`; `computeLayoutFromIssues` returns `ClientLayoutResult` which is `(TaskGraphLayoutResult & { ok: true }) | { ok: false; cycle; lines; edges }`). The phase splice runs over `result.lines` / `result.edges` in both cases — for the cycle branch, run it over the degraded `lines` / `edges` too so phase rows still appear in the fallback flat-list view
- [x] 1.4 Extract the splice pass as a single helper `synthesisePhaseRows(lines: TaskGraphRenderLine[], edges: TaskGraphEdge[], openSpecStates: Record<string, IssueOpenSpecState> | null): void` (mutates in place, returns void). For each `TaskGraphIssueRenderLine` whose `openSpecStates?.[issueId]?.phases` is non-empty, walk forward and insert one `TaskGraphPhaseRenderLine` per `PhaseSummary` with `lane = issueLine.lane + 1`, `parentIssueId = issueId`, immediately after the issue line (preserving phase document order). Call the helper from BOTH `computeLayout` and `computeLayoutFromIssues` so behaviour stays identical
- [x] 1.5 Inside `synthesisePhaseRows`, append synthetic `TaskGraphEdge` entries to the `edges` collection. For each issue with phases: one edge `{ from: issueId, to: phase[0].phaseId, kind: 'SeriesCornerToParent', sourceAttach: 'Bottom', targetAttach: 'Top', startRow: issueRow, startLane: issueLane, endRow: phase[0].row, endLane: phase[0].lane, pivotLane: issueLane }`, plus N-1 edges between consecutive phases `{ kind: 'SeriesSibling', sourceAttach: 'Bottom', targetAttach: 'Top', pivotLane: null }`. Phase row indexes are computed inline as the splice progresses
- [x] 1.6 Add a unit test in `task-graph-layout.test.ts` covering BOTH entry points with the same fixtures: (a) an issue with 3 phases produces issue-line + 3 phase-lines in order with one `SeriesCornerToParent` + two `SeriesSibling` edges synthesised; (b) an issue with no phases produces no phase lines and no synthetic edges; (c) an issue with empty `openSpecStates` entry produces no phase lines; (d) phase lines for a later issue do not interleave with earlier issues' phases (subtree contiguity); (e) the cycle-degraded branch of `computeLayoutFromIssues` still emits phase rows
- [x] 1.7 Confirm `task-graph-svg.tsx`'s `<TaskGraphEdges>` component renders the synthetic phase edges identically to server-supplied ones — no special-case branch required because the kind / attach geometry matches existing handlers. (The `nodeMap` update needed so phase edges resolve their endpoints is covered by task 4.2a below, not here.) Add a Storybook story exercising a phase-edge case to lock this in

## 2. SVG node and row components

- [x] 2.1 Create `src/Homespun.Web/src/features/issues/components/task-graph-phase-svg.tsx` exporting `TaskGraphPhaseSvg({ line, maxLanes })` that renders a 12px diamond at `(getLaneCenterX(line.lane), getRowCenterY())` with type-color fill at 0.5 opacity and full-opacity stroke; reuse `LaneGuideLines`, `LANE_WIDTH`, `ROW_HEIGHT`, `NODE_RADIUS` from `task-graph-svg.tsx`
- [x] 2.2 ~~Render the series-child top-line~~ **N/A post-Fleece:** connector lines are rendered by the global `<TaskGraphEdges>` component using the synthetic phase edges added in task 1.5. The phase SVG component renders **only** the diamond node itself; do not duplicate connector-drawing logic here
- [x] 2.3 ~~Render the series-child bottom-line~~ **N/A post-Fleece:** same as 2.2 — consecutive phase rows are connected visually by the `SeriesSibling` edges synthesised in task 1.5
- [x] 2.4 Apply a green stroke + fill when `done >= total && total > 0` to mirror the existing complete-phase badge color treatment (falls out of the design's open question on color)
- [x] 2.5 Create `src/Homespun.Web/src/features/issues/components/task-graph-phase-row.tsx` exporting `TaskGraphPhaseRow({ line, maxLanes, isSelected, isExpanded, onToggleExpand })` that renders the SVG + a static label `<span>{phaseName}: {done}/{total}</span>`; no `IssueRowActions`, no editable pills, no execution-mode toggle
- [x] 2.6 Apply the same `style={{ height: ROW_HEIGHT }}`, role="row", `aria-selected`, `aria-expanded`, focus-visible ring, and `bg-muted` selected styling that issue rows use so keyboard navigation looks identical
- [x] 2.7 Add `data-testid="task-graph-phase-row"` and `data-phase-id="${phaseId}"` for test selectors
- [x] 2.8 Create co-located stories: `task-graph-phase-svg.stories.tsx` (default + complete states) and `task-graph-phase-row.stories.tsx` (default + selected + expanded states)

## 3. Inline expansion replaces the modal

- [x] 3.1 Create `src/Homespun.Web/src/features/issues/components/inline-phase-detail-row.tsx` that takes `{ line: TaskGraphPhaseRenderLine, maxLanes: number }`, renders an `<ul>` of `PhaseTaskSummary` items each with a checkbox SVG + description, with `role="region"` and `aria-label="Phase tasks"`
- [x] 3.2 Apply `max-h-[400px] overflow-y-auto` to the `<ul>` so very long task lists stay scrollable without forcing the row arbitrarily tall
- [x] 3.3 Render an SVG-aligned gutter (LaneGuideLines + a vertical line in the parent issue's lane) so the inline panel visually aligns with the graph above
- [x] 3.4 Mount `<InlinePhaseDetailRow>` from `task-graph-view.tsx` whenever `expandedIds.has(line.phaseId)` for a phase line, immediately after the phase row
- [x] 3.5 Add a co-located story for the new component
- [x] 3.6 Add a unit test that mounts an expanded phase with three tasks (two done, one not), asserts checkbox states, and asserts the scroll region's `max-height` style

## 4. Wire phase rows into `task-graph-view.tsx`

- [x] 4.1 (Done as part of §0.3 / §0.4.) Re-verify: `computeLayoutFromIssues({ …, openSpecStates: openSpecStatesHook.openSpecStates ?? null })` is wired at `task-graph-view.tsx:184` and the destructured return is `{ lines, edges }`. `edges` is forwarded to `<TaskGraphEdges>` as before; synthetic phase edges from task 1.5 are already included
- [x] 4.2 Add a third arm to the render-line switch that mounts `<TaskGraphPhaseRow>` for `'phase'` lines and `<InlinePhaseDetailRow>` immediately after when expanded
- [x] 4.2a Update `<TaskGraphEdges>`' `nodeMap` and `totalHeight` `useMemo` blocks (`task-graph-svg.tsx:405-432`) to handle phase lines: add a `'phase'` arm that mirrors the `'issue'` arm — emit a `nodeMap` entry keyed on `line.phaseId`, and treat `expandedIds.has(line.phaseId)` the same way it treats expanded issue rows for Y-offset accumulation. Without this, synthetic phase edges resolve `from`/`to` against an empty `nodeMap` and render nothing, and inline phase expansion misaligns downstream edge endpoints
- [x] 4.2b Add a `task-graph-svg.test.tsx` case covering: an issue with phases + an expanded phase row produces a `nodeMap` containing the phase's `phaseId` at the expected `(x, y)`, and `totalHeight` accounts for the expanded panel
- [x] 4.3 Update the keyboard handler so that when the selected line is a phase: `e` (edit), `r` (run-agent), `m`/`M` (move), and any hierarchy reparent hotkeys silently no-op (`return` early before the existing handler bodies); `j`/`k`/arrow selection, ⏎/double-click expand, and ESC continue to work
- [x] 4.4 Update click-to-select handlers to accept phase lines as valid selection targets (use `phaseId` as the selected id)
- [x] 4.5 Verify ESC behaviour: when a phase row is selected and expanded, ESC collapses it (existing logic already handles this if `expandedIds.has(selectedId)`)
- [x] 4.6 Update `selectedIssueId` state to a more general `selectedRowId` (or document that phase ids are routed through the same state) — both tracking variants are acceptable as long as the contract is clear

## 5. Remove `phase-rollup.tsx` and the inline-badge mount

- [x] 5.1 Remove the `<PhaseRollupBadges>` import and JSX block from `issue-row-content.tsx` (lines around 186-188)
- [x] 5.2 Delete `src/Homespun.Web/src/features/issues/components/phase-rollup.tsx`
- [x] 5.3 Grep the codebase for any remaining references to `PhaseRollupBadges`, `data-testid="phase-rollup-badges"`, `data-testid="phase-badge"`, `data-testid="phase-task-list"`, `data-testid="phase-task"`; update or remove
- [x] 5.4 Delete or update any test files that asserted on the removed badges/modal flow

## 6. Drop `EXPANDED_DETAIL_HEIGHT` and switch to DOM measurement

- [x] 6.1 Grep all callers of `getRowY` (likely `task-graph-svg.tsx`, `task-graph-view.tsx`, scroll-into-view helpers); read each and confirm the only consumer is scroll-restoration / scroll-into-view
- [x] 6.2 Replace each `getRowY(rowIndex, expandedIds, issueLines)` call with `rowRefs.current[rowIndex]?.offsetTop ?? 0` from a per-row ref array maintained on the rendered row components
- [x] 6.3 Delete the `EXPANDED_DETAIL_HEIGHT = 700` constant from `task-graph-svg.tsx`
- [x] 6.4 Delete the `getRowY` function (or leave a small forwarding shim that just returns `offsetTop`, depending on call-site complexity)
- [x] 6.5 Update `inline-issue-detail-row.tsx` to remove any reliance on a fixed outer height; apply `max-h-[400px] overflow-y-auto` to the inner description/content scroll region instead
- [x] 6.6 Add a unit test (or a Storybook visual) showing an expanded issue row whose content is taller than 400px scrolls within the inner region without forcing the row container tall

## 7. Tests and verification

- [x] 7.1 Add unit tests for the new `'phase'` render line covering: emission order under tree view, emission order under next view, no emission when phases is empty/missing, parent-issue lane is preserved
- [x] 7.2 Add e2e test (under `src/Homespun.Web/e2e/`) using the seeded mock data: navigate to the demo project, find ISSUE-006, assert at least one phase row is present, click one, assert the inline panel renders with the right task list
- [x] 7.3 Add e2e test that selects a phase row, presses `e`, and asserts no edit dialog opens; press `r`, assert no agent dialog; press `m`, assert no move state activates
- [x] 7.4 Run `npm run lint:fix && npm run format:check && npm run typecheck && npm test && npm run build-storybook` from `src/Homespun.Web` — all green
- [x] 7.5 Run `dotnet test` — all backend suites green (no backend changes expected to break)
- [ ] 7.6 Boot `dotnet run --project src/Homespun.AppHost --launch-profile dev-mock` and visually confirm the diamond shape, parent connectors, inline expansion, and that no orphan badges remain anywhere on the issue rows
- [ ] 7.7 Visually verify on a 1080p screen that an issue with 6+ phases renders cleanly (no wrap, no overflow) — confirms the original bug is fixed
