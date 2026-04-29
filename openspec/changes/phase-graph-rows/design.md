## Context

> **Sequencing note (2026-04-29):** this change has been rebased to land **after** `upgrade-fleece-core-v3`. That change migrates the server off `Fleece.Core` 2.1.1's removed `BuildFilteredTaskGraphLayoutAsync` API, pushes semantic edges (`Edge<Issue>` with `EdgeKind` + `SourceAttach` / `TargetAttach`) over the wire as a new `Edges[]` collection on `TaskGraphResponse`, and rewrites `task-graph-layout.ts` as a thin pass-through that drops the entire connector-inference layer. Most of the per-row connector fields that earlier drafts of this design referenced (`drawTopLine`, `drawBottomLine`, `seriesConnectorFromLane`, `isSeriesChild`, `parentLane`, `parentLaneReservations`, `multiParentIndex`, lane-0 handling, hidden-parent flags) are gone after the Fleece migration. `computeLayout` returns `{ lines, edges }` instead of `TaskGraphRenderLine[]`, and `renderGroup` / `renderGroupTreeView` no longer exist. The decisions below have been updated accordingly.

The current `PhaseRollupBadges` component (`src/Homespun.Web/src/features/issues/components/phase-rollup.tsx`) renders OpenSpec change phases as inline pills inside the issue row's content strip:

```
[Type] [Status] [⌥] [Phase A: 3/5] [Phase B: 0/4] [Phase C: 7/7] [Phase D: 1/2]
```

The badges live in a `flex flex-wrap gap-1` container, and the parent row is `style={{ height: ROW_HEIGHT /* 40px */ }}`. When an issue has multiple phases, the badges wrap and visually overflow into adjacent rows, producing the reported "overlapping badges that sit over the UI" bug. Clicking a badge opens a modal dialog (`Dialog` from `@/components/ui/dialog`) listing the phase's leaf tasks.

The `openspec-integration` spec already explicitly requires phases to be rendered as virtual graph nodes ("one virtual sub-node per phase heading") with no dispatch action. The implementation drift is the bug.

The (post-Fleece-v3) task graph layout pipeline (`computeLayout` in `services/task-graph-layout.ts`) builds `{ lines: TaskGraphRenderLine[], edges: TaskGraphEdge[] }` from a `TaskGraphResponse`. Two existing union variants matter for this work: `TaskGraphIssueRenderLine` (an issue row) and `TaskGraphPrRenderLine` (a merged-PR row above the issue list). Render lines and edges are produced once and consumed by `task-graph-view.tsx` (which switches on `line.type` to mount the right row component) plus `task-graph-svg.tsx` (which renders one path per `edge`).

`OpenSpecState.Phases` is already on the wire — `IssueGraphOpenSpecEnricher.ResolveForIssueAsync` populates `response.OpenSpecStates[issueId].Phases` on every graph fetch. The data is just not threaded into the layout function today; the row component reaches for it directly via `taskGraph.openSpecStates?.[id]` at render time.

The `mock-openspec-seeding` change (sister change, must land first) provides realistic phase data to validate this work against. The `upgrade-fleece-core-v3` change (sequencing dependency above) must land before any work in this change begins.

## Goals / Non-Goals

**Goals:**

- Make the implementation match the existing `openspec-integration` spec.
- Eliminate the visual overflow bug by removing inline badges entirely.
- Replace the modal flow with inline expansion so the interaction model is consistent with how issue details already expand.
- Drop the fixed `EXPANDED_DETAIL_HEIGHT = 700` constant — variable-height inline panels become possible without a layout overhaul because rows already use natural flow.
- Keep the existing `squareNode` indicator (issue has a linked change) untouched. The diamond is *additional* visual information, not a replacement.

**Non-Goals:**

- Backend changes. `IssueOpenSpecState.Phases` and the enricher already produce the right shape.
- Re-architecting the row positioning model. Rows already use `style={{ height: ... }}` in normal flow, not absolute positioning.
- Adding new graph behaviours beyond what the spec requires (no drag-and-drop on phase rows, no per-phase dispatch, no per-phase status overrides).
- Changing the way `IssueGraphOpenSpecEnricher` computes `Phases` or how `TasksParser` parses `## Phase` headings.

## Decisions

### Phase rows are first-class render lines, not metadata on issue rows

**Decision:** Add a fourth variant `TaskGraphPhaseRenderLine` (alongside `issue`, `pr`, `loadMore`, `separator`) to the `TaskGraphRenderLine` discriminated union. Each phase becomes its own line, emitted by `computeLayout` directly after its parent issue line.

**Why:** Modeling phases as their own render lines lets the existing scroll/keyboard/expand machinery treat them uniformly. The alternative (carry phases as a metadata field on `TaskGraphIssueRenderLine` and let the row component render its own sub-rows) breaks the height assumptions of `getRowY`, complicates keyboard navigation (each row maps 1:1 with a render line today), and forces phase-specific rendering logic into the issue row.

**Alternative considered:** Attach a `phases?: PhaseSummary[]` to the issue line and have `TaskGraphIssueRow` render its own `<PhaseSubRow>` children. Rejected — duplicates the render-line model in two places (top-level union + nested), and selection state would need a parallel `(issueId, phaseIndex)` addressing scheme.

### Phase synthesis is a post-processing pass over `computeLayout`'s output (rebased)

**Decision:** Thread `openSpecStates` into `computeLayout(taskGraph, maxDepth, viewMode, openSpecStates)`. After the function has produced `{ lines, edges }` from the server response, run a single splice pass: for each `TaskGraphIssueRenderLine` whose `openSpecStates[issueId]?.phases` is non-empty, walk forward from its index and insert one `TaskGraphPhaseRenderLine` per `PhaseSummary` (preserving document order). For each new phase line, append a synthetic `TaskGraphEdge` to `edges` — `kind: 'SeriesSibling'` between consecutive phases under the same parent, and `kind: 'SeriesCornerToParent'` from the first phase to its parent issue, both with `sourceAttach: 'Bottom'` and `targetAttach: 'Top'`. Phase row IDs follow `${issueId}::phase::${name}`.

**Why:** Post-Fleece-v3, `computeLayout` no longer iterates the issue tree — it iterates the server-supplied `PositionedNode[]` which already comes pre-positioned. The edge collection is also server-supplied. There is no `renderGroup` / `renderGroupTreeView` loop to splice into anymore; the only sensible insertion point is after the pass-through. The work that earlier drafts wanted to avoid duplicating ("re-derive lane / connector geometry") has moved to the server in `Fleece.Core.IIssueLayoutService` — Homespun no longer derives it at all. Phase synthesis is therefore the only Homespun-side derivation left in the layout pipeline, and a single localised splice is the cleanest place for it.

**Why phases as Edge entries in the same wire-format-derived collection:** unifies how the SVG renderer consumes connectors. `task-graph-svg.tsx`'s `<TaskGraphEdges>` component (added in `upgrade-fleece-core-v3`) renders every edge with a switch on `(kind, sourceAttach, targetAttach)`. Phase edges are just synthetic entries in that same collection — no parallel rendering path, no special-case "phase connector" code in the SVG layer.

**Alternative considered:** Modify the in-Fleece engine to know about phases. Rejected — phases are an OpenSpec concept entirely orthogonal to Fleece issues; they have no `Issue` representation in `.fleece/`.

**Alternative considered:** Synthesise phase edges only inside `task-graph-svg.tsx` rather than threading them through `computeLayout`'s return. Rejected — the edge collection is the single source of connector-geometry truth post-Fleece, and the rendering layer's job is to draw, not to invent. Keeping all edge synthesis in `computeLayout` keeps the layer responsibilities clean.

### Phase rows occupy `parent.lane + 1` and use `Bottom → Top` attach geometry

**Decision:** A phase line sits at `lane = parentIssueLine.lane + 1` and `row = parentIssueLine.row + (1-based phase index)`. Subsequent issue lines are shifted down by the number of phase rows synthesised above them — this is the same row-shift bookkeeping the splice pass does naturally as it inserts elements into the array. Synthetic phase edges all use `sourceAttach: 'Bottom'`, `targetAttach: 'Top'` (vertical drop into the diamond).

**Why:** Phases are an ordered checklist conceptually parallel to a series chain, so the "stairstep down + right" geometry of `LayoutMode.IssueGraph` is the wrong fit (it would put each phase at a different lane). Using a single child lane (`parent.lane + 1`) with a vertical chain is closer to `LayoutMode.NormalTree`'s edge geometry and reads correctly visually. The synthetic edges adopt that geometry directly: `Bottom → Top` is "drop straight down into me", which is the simplest visual signal that a phase row is a child of the issue above it.

**Note on row shifts:** since the splice pass inserts entries after the phase's parent issue line, every subsequent issue line's effective render row index increases by N (where N is the number of phases inserted above it). This is purely a visual / scroll-into-view concern — the original `Row` value on `TaskGraphNodeResponse` is preserved on the underlying issue render line, but selection / keyboard navigation operates on the post-splice line array. No edges from the server need adjustment because phase rows are inserted *between* issue rows; they do not displace any existing edge endpoint's `(row, lane)` coordinates within the SVG render coordinate space — the SVG layer maps render-line-index → y-pixel via `rowRefs[rowIndex]?.offsetTop` (per Decision "Drop `EXPANDED_DETAIL_HEIGHT`…"), which sees the post-splice positions naturally.

### Diamond ◆ shape for the phase node

**Decision:** New `TaskGraphPhaseSvg` component that renders a 12px-wide diamond (rotated square via SVG `<rect>` with `transform="rotate(45 cx cy)"` or a `<polygon>`). Same `NODE_RADIUS = 6` envelope as issue nodes so connector geometry lines up.

**Why:** The spec leaves the symbol open. Issue nodes are circles (no linked change) and squares (linked change). A diamond is distinctive at 12px, parents/sibling connectors terminate cleanly into its bounding box, and it visually subordinates to the parent without being noisy. Color follows the parent issue's type color, but with reduced fill opacity (e.g. `fill-opacity="0.5"`) so the row reads as "child of."

**Alternative considered:** Horizontal bar / pill shape — rejected because connectors don't terminate cleanly. Filled rounded rectangle — rejected because it's too similar to the existing square (linked-change indicator) and would confuse the visual language.

### Inline expansion replaces the modal entirely

**Decision:** Delete `phase-rollup.tsx` (component + Dialog). Add a new `InlinePhaseDetailRow` that renders below an expanded phase row, listing each `PhaseTaskSummary` as a checkbox + description line. Reuses the existing `expandedIds: Set<string>` state in `task-graph-view.tsx`.

**Why:** The user requested this. It also unifies the interaction model — issue rows already expand inline; making phase rows behave the same way removes a special case. Modal dismissal interrupts keyboard nav; inline expansion preserves it.

**Key wrinkle:** `expandedIds` is keyed by `issueId` today. Phase render lines need their own stable id. Use `${issueId}::phase::${phaseName}` to avoid collisions with issue ids and to survive re-renders without stale state.

### Drop `EXPANDED_DETAIL_HEIGHT` and switch `getRowY` callers to DOM measurement

**Decision:** Remove the `EXPANDED_DETAIL_HEIGHT = 700` constant. Inline detail panels grow to their natural height; their inner content gets `max-h-[400px] overflow-y-auto` so very long task lists or descriptions stay scrollable without forcing the whole row tall. Replace `getRowY(rowIndex, expandedIds, issueLines)` callers (currently used for keyboard scroll-into-view) with `rowRefs.current[rowIndex]?.offsetTop` from a per-row ref.

**Why:** The fixed height was only there because the legacy implementation positioned things based on summed offsets. Rows actually use normal flow now (`style={{ height: ROW_HEIGHT }}`, not absolute positioning) — the `getRowY` math is vestigial scroll-into-view plumbing. DOM measurement gives accurate offsets for variable-height rows for free.

**Risk:** Code that relied on `getRowY` for layout (rather than scroll-into-view) would silently break. Mitigation: grep + read every caller before deletion. Likely scope is just one or two `scrollIntoView` calls.

### Keyboard handler short-circuits action keys when a phase is selected

**Decision:** In `task-graph-view.tsx`'s keyboard handler, after resolving the selected render line, check `selected.type === 'phase'` and `return` early for hotkeys `e` (edit), `r` (run agent), `m`/`M` (move), and any hierarchy reparenting hotkeys. Selection (`j`/`k`/arrow), expand (`⏎`/double-click), and ESC (collapse) continue to work.

**Why:** Silent no-op was the agreed UX. Toasts on every disallowed key would be noisy; visually disabling buttons is moot because phase rows don't render those buttons at all. The keyboard short-circuit is the only place the disabled-action behaviour needs explicit enforcement.

### `multiParentIndex` and `parentLaneReservations` post-processing is gone (rebased)

**Decision:** N/A after `upgrade-fleece-core-v3`. Both post-passes are deleted as part of that change because the connector geometry they computed now arrives as `Edges[]` from the server. There is no need for a `phase`-aware filter — the passes do not exist.

**Note:** if a future change re-introduces a `multiParentIndex`-style post-pass for some other purpose (e.g. rendering a "this issue appears N times" badge), it must guard on `isIssueRenderLine(line)` so it skips phase lines. Phase lines never have multi-parent semantics.

## Risks / Trade-offs

- **Replacing the modal removes accessibility-tested dialog patterns** → Mitigation: the inline panel is just a `<ul>` of checkboxes inside a row; existing keyboard patterns (⏎ to expand, ESC to collapse) are already accessible. Add a `role="region"` + `aria-label` to the panel for screen readers.
- **`expandedIds: Set<string>` collisions between issue ids and phase ids** → Mitigation: enforce phase ids contain `::phase::` separator; never overlap with fleece-issue id format.
- **Variable-height expanded rows could break scroll-restore on filter changes** → Mitigation: `task-graph-view.tsx` already collapses all rows on filter changes; verify this still happens after the height change.
- **Tests asserting on `data-testid="phase-rollup-badges"` will break** → Mitigation: grep & update; this is intentional cleanup as the modal/badge flow goes away.
- **Story drift in Storybook** → Mitigation: add a `task-graph-phase-row.stories.tsx` and update any composite stories that snapshot the issue row with phases.

## Migration Plan

This is a UI-only refactor (within Homespun) but it depends on a backend wire-format change that has to land first. Sequence:

1. Land `mock-openspec-seeding` (sister change, dependency for visual validation).
2. Land `upgrade-fleece-core-v3` (foundation: Fleece 3.0.0 migration + wire-format `Edges[]` collection + frontend `task-graph-layout.ts` rewrite that drops every connector-inference field this change used to splice into). All implementation tasks below assume the post-Fleece shape.
3. Land this change. Phase badges + modal are removed; phase rows appear inline as a localised splice over `computeLayout`'s `{ lines, edges }` output.
4. Rollback: revert this change. The previous badges + modal return; the post-Fleece pipeline keeps working without phase rows. No persistent state to migrate.

## Open Questions

- **Should phase rows participate in fuzzy title search?** Currently `searchQuery` highlights matching issue rows. If a phase name matches the search query, do we highlight the phase row too? Lean yes — falls out naturally from rendering; small spec scenario could codify this. Defer until we hear from QA / users.
- **Should the diamond color reflect phase completion** (e.g. all-done phases render green like the existing badge complete-state)? Lean yes — easy win, mirrors the badge behaviour, and gives at-a-glance scannability for which phases are done. Add as a tasks.md task; codify in spec scenario if it sticks.
- **Storybook coverage for the new components** — confirm the project's `build-storybook` step in the pre-PR checklist will catch missing stories. Add stories for `TaskGraphPhaseRow`, `TaskGraphPhaseSvg`, and `InlinePhaseDetailRow`.
