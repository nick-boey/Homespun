## Context

The current `PhaseRollupBadges` component (`src/Homespun.Web/src/features/issues/components/phase-rollup.tsx`) renders OpenSpec change phases as inline pills inside the issue row's content strip:

```
[Type] [Status] [⌥] [Phase A: 3/5] [Phase B: 0/4] [Phase C: 7/7] [Phase D: 1/2]
```

The badges live in a `flex flex-wrap gap-1` container, and the parent row is `style={{ height: ROW_HEIGHT /* 40px */ }}`. When an issue has multiple phases, the badges wrap and visually overflow into adjacent rows, producing the reported "overlapping badges that sit over the UI" bug. Clicking a badge opens a modal dialog (`Dialog` from `@/components/ui/dialog`) listing the phase's leaf tasks.

The `openspec-integration` spec already explicitly requires phases to be rendered as virtual graph nodes ("one virtual sub-node per phase heading") with no dispatch action. The implementation drift is the bug.

The task graph layout pipeline (`computeLayout` in `services/task-graph-layout.ts`) builds a list of `TaskGraphRenderLine`s from a `TaskGraphResponse`. Two existing union variants matter for this work: `TaskGraphIssueRenderLine` (an issue row) and `TaskGraphPrRenderLine` (a merged-PR row above the issue list). Render lines are produced once and consumed by `task-graph-view.tsx`, which switches on `line.type` to mount the right row component.

`OpenSpecState.Phases` is already on the wire — `IssueGraphOpenSpecEnricher.ResolveForIssueAsync` populates `response.OpenSpecStates[issueId].Phases` on every graph fetch. The data is just not threaded into the layout function today; the row component reaches for it directly via `taskGraph.openSpecStates?.[id]` at render time.

The `mock-openspec-seeding` change (sister change, must land first) provides realistic phase data to validate this work against.

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

### Phase synthesis happens in `computeLayout`, not as a post-processing pass

**Decision:** Thread `openSpecStates` into `computeLayout(taskGraph, maxDepth, viewMode, openSpecStates)`. Inside `renderGroup` and `renderGroupTreeView`, immediately after pushing each issue line, look up `openSpecStates[issueId]?.phases` and push one `TaskGraphPhaseRenderLine` per `PhaseSummary`.

**Why:** Keeps all hierarchy logic in one place. A post-processing pass would have to re-derive the lane / connector geometry that `renderGroup*` already computes for the parent. By emitting phase lines in the same loop, they pick up the correct lane (parent.lane + 1) and series-child connector geometry naturally.

**Alternative considered:** Splice phases in via a separate post-pass after `computeLayout` returns. Rejected — duplicates connector geometry logic, harder to test in isolation.

### Treat phase rows as series children for connector geometry

**Decision:** Phase render lines set `isSeriesChild: true` and use `lane = parentIssue.lane + 1` regardless of the parent issue's `executionMode`.

**Why:** The visual model "issue ┊ phase ┊ phase ┊ phase" is conceptually serial — phases are an ordered checklist. The series-child connector code (top-line + arc into node) already produces the right shape. Trying to render phases as parallel siblings would put them all at the same lane offset and look noisy.

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

### `multiParentIndex` and `parentLaneReservations` post-processing skips phase lines

**Decision:** The two `computeLayout` post-processing passes that compute multi-parent indices and parent-lane reservations both filter on `isIssueRenderLine(line)`. Phase lines (which are `'phase'`, not `'issue'`) are naturally skipped.

**Why:** Phase lines never have multi-parent semantics or participate in cross-issue lane reservation. The existing type guards already exclude them; no extra code needed beyond confirming this in tests.

## Risks / Trade-offs

- **Replacing the modal removes accessibility-tested dialog patterns** → Mitigation: the inline panel is just a `<ul>` of checkboxes inside a row; existing keyboard patterns (⏎ to expand, ESC to collapse) are already accessible. Add a `role="region"` + `aria-label` to the panel for screen readers.
- **`expandedIds: Set<string>` collisions between issue ids and phase ids** → Mitigation: enforce phase ids contain `::phase::` separator; never overlap with fleece-issue id format.
- **Variable-height expanded rows could break scroll-restore on filter changes** → Mitigation: `task-graph-view.tsx` already collapses all rows on filter changes; verify this still happens after the height change.
- **Tests asserting on `data-testid="phase-rollup-badges"` will break** → Mitigation: grep & update; this is intentional cleanup as the modal/badge flow goes away.
- **Story drift in Storybook** → Mitigation: add a `task-graph-phase-row.stories.tsx` and update any composite stories that snapshot the issue row with phases.

## Migration Plan

This is a UI-only refactor. Deploy:

1. Land `mock-openspec-seeding` first (sister change, dependency for visual validation).
2. Land this change. Phase badges + modal are removed; phase rows appear inline.
3. Rollback: revert this change. The previous badges + modal return. No persistent state to migrate.

## Open Questions

- **Should phase rows participate in fuzzy title search?** Currently `searchQuery` highlights matching issue rows. If a phase name matches the search query, do we highlight the phase row too? Lean yes — falls out naturally from rendering; small spec scenario could codify this. Defer until we hear from QA / users.
- **Should the diamond color reflect phase completion** (e.g. all-done phases render green like the existing badge complete-state)? Lean yes — easy win, mirrors the badge behaviour, and gives at-a-glance scannability for which phases are done. Add as a tasks.md task; codify in spec scenario if it sticks.
- **Storybook coverage for the new components** — confirm the project's `build-storybook` step in the pre-PR checklist will catch missing stories. Add stories for `TaskGraphPhaseRow`, `TaskGraphPhaseSvg`, and `InlinePhaseDetailRow`.
