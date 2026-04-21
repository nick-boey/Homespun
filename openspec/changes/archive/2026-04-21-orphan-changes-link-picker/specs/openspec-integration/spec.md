## MODIFIED Requirements

### Requirement: Orphan change handling

The system SHALL surface orphan changes (changes without sidecars) in a single section at the bottom of the task graph, deduplicated by change name, with actions to link an orphan to an existing issue, create a new top-level issue for it, or create a sub-issue under a chosen parent.

Branch-scoped orphans SHALL NOT render inline under their branch's issue row; the task graph body SHALL contain only issues and PRs.

Rationale: the prior two-place rendering (inline per-branch list + bottom section) duplicated information for multi-occurrence orphans, added vertical noise to the graph body, and did not expose linking to non-branch issues. The bottom list is sufficient because the picker conveys branch containment via pinned highlights.

#### Scenario: Orphan on agent branch renders only at the bottom

- **WHEN** an unlinked change exists on a branch associated with a fleece issue
- **THEN** it SHALL NOT render as a virtual child under that issue in the graph
- **AND** it SHALL render as a row in the bottom "Orphaned Changes" section
- **AND** the issue whose branch carries the change SHALL appear in the row's `containingIssueIds` set (used by the link picker for highlighting)

#### Scenario: Orphan on main renders at the bottom

- **WHEN** an unlinked change exists on main
- **THEN** it SHALL render as a row in the bottom "Orphaned Changes" section
- **AND** the row's occurrence list SHALL contain a single entry with `branch = null`

#### Scenario: Same change name on multiple locations deduplicates to one row

- **WHEN** the same unlinked change name exists on two or more locations (e.g. on main *and* on a feature branch, or on two feature branches)
- **THEN** the bottom section SHALL render exactly one row for that change name
- **AND** the row's occurrence list SHALL contain one entry per location
- **AND** the row's containing-issue set SHALL include every issue whose branch carries the change

#### Scenario: Single-occurrence label shows the location directly

- **WHEN** an orphan row has a single occurrence
- **THEN** the row SHALL display either "main" (for `branch = null`) or the branch name, inline and unabridged

#### Scenario: Multi-occurrence label shows count with a tooltip

- **WHEN** an orphan row has two or more occurrences
- **THEN** the row SHALL display an "on N branches" label
- **AND** the label SHALL expose the full list of branch names (including "main" when applicable) via a tooltip

#### Scenario: Orphan row offers two actions

- **WHEN** an orphan row is rendered
- **THEN** it SHALL expose a `[Link to issue]` action
- **AND** it SHALL expose a split-button create action whose primary is `Create issue` and whose secondary dropdown item is `Create as sub-issue under…`

## ADDED Requirements

### Requirement: Link-picker dialog with filter and containment-based highlights

The system SHALL provide a picker dialog that lists every issue in the project and lets the user commit a selection by click (or keyboard). The dialog SHALL support a plain case-insensitive substring filter over issue titles and SHALL visually pin the subset of issues whose branch already carries the orphan change at the top of the list, duplicated above the filtered tail.

The picker SHALL serve both the "link orphan to existing issue" flow and the "create as sub-issue under an existing parent" flow, differing only in dialog title, the commit callback, and whether containment highlights are shown.

Rationale: the picker is the discoverability path for non-obvious link targets (issues that are *not* the branch's own issue); pinning containing-branch issues surfaces the most likely targets without hiding the broader set.

#### Scenario: Dialog opens with all issues and highlighted block at the top

- **WHEN** the user triggers `[Link to issue]` on an orphan row
- **THEN** the dialog SHALL open with a filter input at the top
- **AND** the list body SHALL render a pinned block containing every issue in `containingIssueIds`, in stable order, followed by a divider, followed by the full project issue list

#### Scenario: Pinned issues appear as duplicates in the lower list

- **WHEN** an issue is in the pinned block
- **THEN** the same issue SHALL also appear in its normal sorted position in the lower list
- **AND** this duplication SHALL be independent of any filter input state

#### Scenario: Filter narrows only the lower list

- **WHEN** the user types into the filter input
- **THEN** the lower list SHALL be narrowed to issues whose title contains the filter string, case-insensitive
- **AND** the pinned block SHALL remain unchanged

#### Scenario: Clicking a row commits the selection

- **WHEN** the user clicks any issue row in either the pinned block or the lower list
- **THEN** the dialog SHALL invoke its commit callback with that issue's id
- **AND** the dialog SHALL close

#### Scenario: Picker rows reuse the graph row content without the graph chrome

- **WHEN** the picker renders an issue row
- **THEN** the row SHALL render the same type/status pills, OpenSpec indicators, phase roll-up, execution-mode display, title, assignee, and linked-PR number as the task-graph row
- **AND** the row SHALL NOT render the SVG gutter, multi-parent badge, hover actions, or live PR-status polling
- **AND** type/status/execution-mode controls SHALL render as static pills, not interactive dropdowns

#### Scenario: Picker serves the create-as-sub-issue flow

- **WHEN** the user opens the picker via the split-button secondary action `Create as sub-issue under…`
- **THEN** the picker dialog SHALL open with the same list, filter, and pinned-highlight behavior
- **AND** committing a selection SHALL invoke the create-sub-issue callback (which creates a child issue under the chosen parent and links every occurrence)

### Requirement: Orphan link fans out across all occurrences

The client SHALL treat the link action on a deduplicated orphan row as an operation over every occurrence. For each occurrence, the client SHALL emit one `POST /api/openspec/changes/link` call with the occurrence's `branch` and the target `fleeceId`. All calls SHALL be awaited in parallel before the task-graph cache is invalidated.

A partial failure (one or more calls rejecting) SHALL propagate as a single rejection to the caller; successfully written sidecars SHALL NOT be rolled back.

Rationale: `POST /api/openspec/changes/link` writes a `.homespun.yaml` sidecar into the specific working clone named by `branch`; there is no transactional boundary across clones, and the user's mental model is "link this change to this issue" regardless of how many clones carry the change directory.

#### Scenario: Single-occurrence orphan emits one link call

- **WHEN** the user commits a link selection on an orphan with a single occurrence
- **THEN** the client SHALL emit exactly one `POST /api/openspec/changes/link` with the occurrence's `branch` (which may be `null` for main)

#### Scenario: Multi-occurrence orphan emits one call per occurrence

- **WHEN** the user commits a link selection on an orphan with two or more occurrences
- **THEN** the client SHALL emit one `POST /api/openspec/changes/link` per occurrence in parallel
- **AND** each call SHALL carry that occurrence's `branch` and the same `changeName` + `fleeceId`

#### Scenario: Cache invalidation runs once after all calls resolve

- **WHEN** every fan-out call has resolved (succeeded or rejected)
- **THEN** the client SHALL invalidate the `['task-graph', projectId]` query exactly once

#### Scenario: Partial failure surfaces as a single error

- **WHEN** any fan-out call rejects
- **THEN** the mutation SHALL reject with the first error message
- **AND** already-written sidecars from successful calls SHALL remain in place
- **AND** the next task-graph refresh SHALL reflect the partial state honestly (remaining occurrences still render as orphan rows)
