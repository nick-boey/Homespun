---
description: "Consolidate orphan-changes rendering at the bottom of the task graph; add a filterable link-picker dialog."
---

# Tasks: Orphan Changes Link Picker

**Input**: Design documents in `openspec/changes/orphan-changes-link-picker/`
**Target slice**: `src/Homespun.Web/src/features/issues/`
**TDD**: all test tasks (`T0xx` in the "Tests" subsections) land *before* the corresponding implementation tasks. Red → green → refactor.

## Path Conventions

| Concern | Path |
|---------|------|
| Orphan components | `src/Homespun.Web/src/features/issues/components/orphan-*.tsx` |
| Task graph row | `src/Homespun.Web/src/features/issues/components/task-graph-row.tsx` |
| Shared issue row content | `src/Homespun.Web/src/features/issues/components/issue-row-content.tsx` (new) |
| Orphan aggregation | `src/Homespun.Web/src/features/issues/services/orphan-aggregation.ts` (new) |
| Hooks | `src/Homespun.Web/src/features/issues/hooks/use-link-orphan.ts` |
| Dialog primitive | `src/Homespun.Web/src/components/ui/dialog.tsx` (existing shadcn) |
| Web unit tests | co-located `*.test.ts(x)` next to source |
| Web e2e tests | `src/Homespun.Web/e2e/` |

---

## Phase 1: Setup

- [x] T001 Confirm `ChangeSnapshotController.cs:117-177` behavior against a feature-branch orphan: `branch: null` → 404 for a change not on main. Attach the verification snippet to the PR description.
- [x] T002 Audit current test coverage for `orphan-changes.tsx`, `use-link-orphan.ts`, and any e2e spec referencing `[data-testid="branch-orphan"]` / `[data-testid="main-orphan"]`. Enumerate which tests need updates vs. deletion.

---

## Phase 2: Foundational — shared row content + orphan aggregation

### Tests

- [x] T003 [P] `components/issue-row-content.test.tsx` — renders type/status pills, OpenSpec indicators, phase rollup, execution-mode toggle, title, assignee, linked-PR; `editable=false` freezes pill dropdowns.
- [x] T004 [P] `services/orphan-aggregation.test.ts`:
  - [x] T004.1 Empty `mainOrphanChanges` + no branch orphans → empty list.
  - [x] T004.2 Only main orphan → single row with `occurrences: [{ branch: null }]`, `containingIssueIds: []`.
  - [x] T004.3 Only branch orphan under issue X → single row with `occurrences: [{ branch: 'feat/X' }]`, `containingIssueIds: ['X']`.
  - [x] T004.4 Same change name on main + one branch → one deduped row with two occurrences and one containing issue.
  - [x] T004.5 Same change name on two branches → one row with two occurrences and two containing issues; order stable and deterministic.
  - [x] T004.6 Null/empty `orphan.name` → filtered out.

### Implementation

- [x] T005 [P] Extract `IssueRowContent` from `task-graph-row.tsx:165-315` into `components/issue-row-content.tsx`. Props: `line`, `projectId`, `openSpecState`, `searchQuery`, `editable`, type/status/exec-mode change callbacks (all optional when `editable=false`), `showPrStatus` (default true for graph, false for picker).
- [x] T006 [P] Rewrap `TaskGraphIssueRow` in `task-graph-row.tsx` to host its SVG gutter, selection/move styling, `IssueRowActions`, and `IssueRowContent` pass-through. No visual diff vs. main.
- [x] T007 [P] `services/orphan-aggregation.ts` — pure function `aggregateOrphans(taskGraph: TaskGraphResponse): OrphanEntry[]` returning `{ name, occurrences, containingIssueIds }` sorted by `name`.

**Checkpoint**: shared row content lands with tests green; task-graph visual unchanged.

---

## Phase 3: Link-picker dialog

### Tests

- [x] T008 [P] `components/orphan-link-picker.test.tsx`:
  - [x] T008.1 Renders a filter input, a pinned block (when `containingIssueIds.length > 0`), a divider, and the full issue list below.
  - [x] T008.2 Fuzzy filter narrows the lower list; pinned block is unaffected by filter input.
  - [x] T008.3 A highlighted issue appears *both* in the pinned block and in the lower list (duplicated) regardless of filter state.
  - [x] T008.4 Clicking a row invokes `onSelect(issueId)` and closes the dialog.
  - [x] T008.5 Empty-state: zero issues → "No issues" placeholder; with filter that matches nothing → "No matches" placeholder.

### Implementation

- [x] T009 [P] `components/orphan-link-picker.tsx` — Dialog wrapping `DialogContent` + `Input` (filter) + a two-section scrollable list that renders `IssueRowContent` in a minimal picker shell (button-role wrapper, click-to-select, no hover actions, no SVG gutter). Props: `open`, `onOpenChange`, `title`, `issues`, `containingIssueIds`, `onSelect(issueId)`.
- [x] T010 [P] Fuzzy-substring filter utility reused or inlined — case-insensitive, matches on `issue.title`.

---

## Phase 4: Rewire orphan-changes.tsx and hook fan-out

### Tests

- [x] T011 [P] `hooks/use-link-orphan.test.ts`:
  - [x] T011.1 Single-occurrence input emits exactly one `ChangeSnapshot.postApiOpenspecChangesLink` call with the occurrence's `branch`.
  - [x] T011.2 Multi-occurrence input emits N calls (one per occurrence) in parallel; all must resolve before `onSuccess` fires.
  - [x] T011.3 Any call failing rejects the mutation with the first error; subsequent occurrences' state is whatever the parallel calls produced (not rolled back).
  - [x] T011.4 `onSuccess` invalidates `['task-graph', projectId]` exactly once.
- [x] T012 [P] `components/orphan-changes.test.tsx` (replace / extend existing):
  - [x] T012.1 Renders one row per deduped change name.
  - [x] T012.2 Single-occurrence shows branch name or "main" inline; multi-occurrence shows "on N branches" with tooltip carrying the full list.
  - [x] T012.3 `[🔗 Link to issue]` opens the picker pre-populated with the orphan's `containingIssueIds` as highlights.
  - [x] T012.4 Split button primary `[+ Create issue]` invokes `useCreateIssue` then `useLinkOrphan` with all occurrences; secondary dropdown item `Create as sub-issue under…` opens the picker in "choose parent" mode.

### Implementation

- [x] T013 [P] Update `hooks/use-link-orphan.ts` — input becomes `{ projectId, occurrences: { branch: string | null, changeName: string }[], fleeceId }`; `mutationFn` maps occurrences to parallel POSTs via `Promise.all`; single invalidation on success.
- [x] T014 [P] Rewrite `components/orphan-changes.tsx`:
  - [x] T014.1 Drop `BranchOrphanList` export.
  - [x] T014.2 Rename `MainOrphanList` → `OrphanedChangesList`; accept aggregated rows from `orphan-aggregation.ts`.
  - [x] T014.3 Render deduped rows with the new two-action layout (link button + split-button create).
  - [x] T014.4 Wire the picker dialog open/close state and both modes (link target / parent target).
- [x] T015 [P] Update `components/task-graph-view.tsx`:
  - [x] T015.1 Remove the `<BranchOrphanList>` invocation near line 1079.
  - [x] T015.2 Replace bottom `<MainOrphanList>` invocation (line 1131) with `<OrphanedChangesList>` fed by `aggregateOrphans(taskGraph)`.

**Checkpoint**: full flow works in mock mode; no more inline branch-orphan rows.

---

## Phase 5: E2E coverage

### Tests

- [x] T016 [P] Deferred — filed follow-up Fleece issue `1OIvoz` "E2E: orphan-link-picker Playwright coverage". Mock mode does not yet seed OpenSpec orphans (no `IssueGraphOpenSpecEnricher` hookup in `MockDataSeederService`), so exercising (1)–(4) from Playwright requires backend seed work out of scope for this PR. Unit tests — `orphan-changes.test.tsx` (9), `orphan-link-picker.test.tsx` (6), `use-link-orphan.test.ts` (4), `orphan-aggregation.test.ts` (7), `issue-row-content.test.tsx` (6) — are the primary coverage.
- [x] T017 [P] Update or delete any existing e2e that asserts inline `[data-testid="branch-orphan"]` rows (they no longer render). — audit found zero e2e specs matching `branch-orphan` / `main-orphan` / `orphan-link-to-issue` / `orphan-create-sub-issue` / `orphan-create-issue`, so no changes required.

---

## Phase 6: Cleanup

- [x] T018 Delete `BranchOrphanList` export and its test surface. — done as part of T014 rewrite (single `OrphanedChangesList` export remains); stale `BranchOrphanList` / `MainOrphanList` describe-blocks in `orphan-changes.test.tsx` replaced by `OrphanedChangesList` coverage.
- [ ] T019 Update `openspec/specs/openspec-integration/spec.md` scenarios after archive (handled automatically by `openspec archive`).
- [x] T020 [polish] Follow-up Fleece issue filed — `gxot0L` "Branchless OpenSpec change-link endpoint (auto-discover clones)" tracks the D3 server-side cleanup.

---

## Dependencies & Execution Order

- **Phase 1** (Setup) blocks nothing; can run in parallel with Phase 2 test authoring.
- **Phase 2** (Foundational) must land before Phase 3 and Phase 4 — both depend on `IssueRowContent` and `aggregateOrphans`.
- **Phase 3** (Picker) is independent of the hook-rewire in Phase 4 as a component, but Phase 4 depends on Phase 3 for wiring.
- **Phase 5** (E2E) depends on Phase 4 landing behind the mock-mode surface.
- **Phase 6** (Cleanup) is last.

---

## Parallel Execution Examples

- T003 / T004 / T007 / T005 are [P] — the shared-row extraction, the orphan-aggregation service, and their tests all touch different files.
- T011 / T012 are [P] — hook and component tests live in separate files.
- T013 / T014 are [P] — hook and component implementation land in separate files.
- T016 and T017 are [P] — e2e additions and deletions.

---

## Implementation Strategy

1. **Ship the refactor first.** Land T005–T007 behind the existing feature so the graph visually un-changes. This decouples the renderer split from the UX shift and keeps the diff reviewable.
2. **Add the picker in isolation.** Land T009–T010 with unit tests only; no consumer yet.
3. **Flip the orphan list.** Land T013–T015 as one PR so the inline list goes away when the new bottom list arrives (avoids a "both lists showing" intermediate state).
4. **E2E + cleanup.** Land T016–T020.

---

## Notes

- The split button pattern is not yet in the shadcn library used here. Compose from the existing `Button` + `DropdownMenu`: primary button bound to `Create issue`, adjacent icon-only button opening a `DropdownMenuContent` with the `Create as sub-issue under…` item. A new component `components/ui/split-button.tsx` may be introduced if the pattern gets reused elsewhere — treat as optional polish, not a blocker.
- Do **not** virtualize the picker list; current projects have at most ~hundreds of issues. Revisit if that changes.
