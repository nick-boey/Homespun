## Purpose

Integrate OpenSpec change lifecycle (propose → apply → archive) with Homespun's Fleece-issue-driven agent workflow. Links changes to issues via `.homespun.yaml` sidecars, scans branches for change state, surfaces progress in the issue graph, and adds an OpenSpec tab to the run-agent panel with auto-selected skill dispatch.

## Requirements

### Requirement: Change-to-issue linkage via sidecar

The system SHALL link OpenSpec changes to Fleece issues via a `.homespun.yaml` sidecar file in the change directory.

#### Scenario: Sidecar contains fleece issue ID
- **WHEN** a change at `openspec/changes/<name>/` contains `.homespun.yaml`
- **THEN** it SHALL contain `fleeceId: <issue-id>` and `createdBy: server|agent`
- **AND** the scanner SHALL use this to determine the linked Fleece issue

#### Scenario: Server auto-writes sidecar post-session for orphan changes
- **WHEN** a session ends and the snapshot shows a new change without a sidecar
- **AND** exactly one orphan change exists on the branch
- **THEN** the server SHALL write `.homespun.yaml` with the branch's fleece-id and `createdBy: agent`

#### Scenario: Multiple orphan changes require UI disambiguation
- **WHEN** a session ends and multiple unlinked changes exist on the branch
- **THEN** the server SHALL NOT auto-link
- **AND** the UI SHALL surface each orphan with [link-to-issue] / [create-sub-issue] actions

#### Scenario: Sidecar survives archive
- **WHEN** a change is archived to `openspec/changes/archive/<dated>-<name>/`
- **THEN** the `.homespun.yaml` sidecar SHALL be preserved in the archive directory

### Requirement: Branch scanner service

The system SHALL scan branches for OpenSpec change state and make it available to the issue graph.

#### Scenario: Post-session snapshot
- **WHEN** an agent session ends on a branch
- **THEN** the worker SHALL scan `openspec/changes/` on the branch
- **AND** SHALL POST a snapshot to the server containing `{branch, fleeceId, changes[], phaseState}`
- **AND** the server SHALL cache this keyed by `(projectId, branch)`

#### Scenario: On-demand scan with cache fallback
- **WHEN** the UI requests graph data and no cached snapshot exists (or cache is stale beyond 60s TTL)
- **THEN** the server SHALL perform a live disk scan of the on-disk clone
- **AND** SHALL cache the result

#### Scenario: Inherited changes are filtered out
- **WHEN** a branch contains changes inherited from main (with sidecars pointing to other fleece-ids)
- **THEN** the scanner SHALL exclude them from the branch's linked changes
- **AND** only changes whose `fleeceId` matches the branch's fleece-id suffix SHALL be shown under that issue

#### Scenario: Archived change fallback
- **WHEN** a linked change is no longer in `openspec/changes/` but exists in `openspec/changes/archive/`
- **THEN** the scanner SHALL read the archived sidecar
- **AND** SHALL auto-transition the fleece issue to `complete` status

### Requirement: Issue graph change indicators

The system SHALL display branch and change status indicators on each issue row in the graph. The enrichment SHALL be served from a per-project `TaskGraphResponse` snapshot maintained by a background refresher so that `GET /api/graph/{projectId}/taskgraph/data` returns without running the full reconciliation on every request. Branch resolution SHALL reuse a single per-request `BranchResolutionContext` (clones list + PR-to-branch dictionary) and SHALL NOT invoke `IGitCloneService.ListClonesAsync` more than once per request.

#### Scenario: Branch indicator colours
- **WHEN** an issue has no branch → gray branch symbol
- **WHEN** an issue has a branch but no change → white branch symbol
- **WHEN** an issue has a branch with a change → amber branch symbol

#### Scenario: Change status symbols
- **WHEN** no change exists → no symbol
- **WHEN** change exists, artifacts incomplete → red ◐
- **WHEN** all schema artifacts created → amber ◐
- **WHEN** all tasks checked → green ●
- **WHEN** change archived → blue ✓

#### Scenario: Issue node shape
- **WHEN** an issue has no linked change → round node (○)
- **WHEN** an issue has a linked change → square node (□)

#### Scenario: Snapshot served on repeat calls
- **WHEN** a client calls `GET /api/graph/{projectId}/taskgraph/data` while a fresh snapshot for that `(projectId, maxPastPRs)` exists
- **THEN** the endpoint SHALL return the snapshot without re-running `IssueGraphOpenSpecEnricher.EnrichAsync`
- **AND** the server SHALL record `snapshot.hit=true` on the request span

#### Scenario: Cold project switch fills the snapshot synchronously
- **WHEN** a client calls the endpoint for a project with no tracked snapshot
- **THEN** the server SHALL compute the response synchronously, store it in the snapshot cache, mark the project as tracked for background refresh, and return

#### Scenario: Refresh endpoint invalidates snapshot
- **WHEN** `POST /api/graph/{projectId}/refresh` succeeds
- **THEN** the snapshot for every `(projectId, maxPastPRs)` entry for that project SHALL be invalidated
- **AND** the next call to `/taskgraph/data` SHALL recompute synchronously

#### Scenario: Per-request branch resolution avoids subprocess fan-out
- **WHEN** `IssueGraphOpenSpecEnricher.EnrichAsync` is invoked for a project with N visible nodes
- **THEN** `IGitCloneService.ListClonesAsync(project.LocalPath)` SHALL be called at most once for that request regardless of N
- **AND** `IDataStore.GetPullRequestsByProject` SHALL be called at most once for that request

### Requirement: Virtual sub-issue rendering from tasks.md

The system SHALL parse `tasks.md` from linked changes and render phase-level roll-ups in the issue graph.

#### Scenario: Phase-level roll-up
- **WHEN** tasks.md contains `## N. Phase Name` headings with checkbox tasks
- **THEN** the graph SHALL show one virtual sub-node per phase heading
- **AND** each sub-node SHALL display `done/total` task counts

#### Scenario: Phase detail modal
- **WHEN** the user clicks a phase badge
- **THEN** a modal SHALL display all leaf tasks under that phase with their checkbox state

#### Scenario: Phases are not individually dispatchable
- **WHEN** a virtual phase sub-node is rendered
- **THEN** it SHALL be display-only with no dispatch action

### Requirement: OpenSpec tab in run-agent panel

The system SHALL provide an "OpenSpec" tab in the run-agent panel that replaces the former "Workflow" tab.

#### Scenario: All 8 OpenSpec skills are listed
- **WHEN** the user opens the OpenSpec tab for a change-linked issue
- **THEN** the tab SHALL list all 8 OpenSpec skills
- **AND** each SHALL be selectable for dispatch

#### Scenario: Auto-selection defaults
- **WHEN** no change exists or artifacts are incomplete → default to `openspec-explore`
- **WHEN** all schema artifacts are created → default to `openspec-apply-change`
- **WHEN** all tasks in tasks.md are checked → default to `openspec-archive-change`

#### Scenario: Readiness gating for apply, verify, sync, archive
- **WHEN** the user selects `apply`, `verify`, `sync`, or `archive`
- **AND** their prerequisites are not met
- **THEN** the skill SHALL be visually marked as blocked
- **AND** SHALL NOT be dispatchable
- **AND** `explore`, `propose`, `new-change`, and `continue-change` SHALL always be available

#### Scenario: Schema override injection
- **WHEN** the project uses a non-default schema (per `openspec/config.yaml`)
- **THEN** the dispatch SHALL inject `"use openspec schema '{schema}' for all openspec commands"` into the session's system prompt

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

### Requirement: Multi-change per branch

The system SHALL support multiple changes on a single branch, each linked to its own fleece issue via its own sidecar.

#### Scenario: Sibling changes under same issue
- **WHEN** multiple changes on a branch have sidecars pointing to the same fleece-id
- **THEN** the graph SHALL render each as a sibling node under that issue

#### Scenario: Changes linked to different issues on same branch
- **WHEN** changes on a branch have sidecars pointing to different fleece-ids
- **THEN** each change SHALL appear under its own respective issue in the graph

### Requirement: Artifact-state micro-cache

`ChangeScannerService.GetArtifactStateAsync` SHALL cache parsed `ChangeArtifactState` values keyed on the tuple `(clonePath, changeName, mtimeTuple)` where `mtimeTuple` is derived from the last-write times of `proposal.md`, `tasks.md`, and the `specs/` subtree. The scanner SHALL only invoke the `openspec status` subprocess when no cache entry matches the current mtime tuple.

#### Scenario: Repeated scan with unchanged files skips subprocess
- **WHEN** `GetArtifactStateAsync` is called twice for the same change directory with no file modifications between calls
- **THEN** the second call SHALL return the cached value
- **AND** `ICommandRunner.RunAsync` SHALL NOT be invoked for the second call

#### Scenario: File modification busts cache entry
- **WHEN** `tasks.md` under a cached change directory is modified
- **THEN** the next `GetArtifactStateAsync` call SHALL re-invoke `openspec status` and produce a fresh `ChangeArtifactState`

### Requirement: Per-project task-graph snapshot store

The server SHALL maintain an in-memory `IProjectTaskGraphSnapshotStore` keyed by `(projectId, maxPastPRs)` whose entries hold the most recent `TaskGraphResponse` and a `lastAccessedAt` timestamp. Snapshots SHALL be evicted after a configurable idle window with default 5 minutes of no read traffic.

#### Scenario: Idle project is evicted
- **WHEN** no request has read a snapshot entry for longer than the configured idle window
- **THEN** the snapshot refresher SHALL remove the entry from the store and stop refreshing it

#### Scenario: Access touches the snapshot
- **WHEN** the endpoint returns a snapshot to a client
- **THEN** the store SHALL update `lastAccessedAt` to the current time

### Requirement: Background task-graph snapshot refresher

A hosted service `ITaskGraphSnapshotRefresher` SHALL periodically recompute snapshots for every tracked `(projectId, maxPastPRs)` on a configurable interval with default 10 seconds. Refreshes SHALL be serialised per project by a `SemaphoreSlim` so that concurrent refresh invocations coalesce.

#### Scenario: Refresh interval elapses
- **WHEN** the configured refresh interval elapses for a tracked entry
- **THEN** the refresher SHALL rebuild the snapshot via `GraphService.BuildEnhancedTaskGraphAsync` and replace the store entry

#### Scenario: Explicit invalidation triggers immediate refresh
- **WHEN** a sidecar mutation, archive transition, or explicit refresh-endpoint call invalidates a tracked entry
- **THEN** the refresher SHALL rebuild the snapshot at the next iteration without waiting for the full interval

#### Scenario: Graceful shutdown drains the refresher
- **WHEN** the host shuts down
- **THEN** any in-progress refresh SHALL honour the cancellation token and complete before the service stops

### Requirement: Task-graph spans cover the enrichment path

`GraphService.BuildEnhancedTaskGraphAsync`, `IssueGraphOpenSpecEnricher.EnrichAsync`, `BranchStateResolverService.GetOrScanAsync`, `ChangeReconciliationService.ReconcileAsync`, `ChangeScannerService.ScanBranchAsync`, `ChangeScannerService.GetArtifactStateAsync`, `IssueBranchResolverService.ResolveIssueBranchAsync`, and `CommandRunner.RunAsync` SHALL each emit an `Activity` under a dedicated `ActivitySource` (`Homespun.Gitgraph` for graph-service work, `Homespun.OpenSpec` for OpenSpec enrichment work, `Homespun.Commands` for the command runner). Each span SHALL carry cardinality-safe tags only: `project.id`, `issue.id`, `change.name`, `cache.hit`, `branch.source`, `phase`, `cmd.name`, `cmd.exit_code`, `cmd.duration_ms`. Every new span name SHALL appear in `docs/traces/dictionary.md`.

#### Scenario: Request span has child spans for enrichment work
- **WHEN** `GET /api/graph/{projectId}/taskgraph/data` is served with a cache miss
- **THEN** the emitted trace SHALL include `graph.taskgraph.build`, `openspec.enrich`, and at least one `openspec.scan.branch` child span

#### Scenario: Command runner span wraps every subprocess
- **WHEN** `CommandRunner.RunAsync` spawns an `openspec` or `git` subprocess
- **THEN** the subprocess invocation SHALL be surrounded by a `cmd.run` span tagged with `cmd.name` and `cmd.exit_code`

#### Scenario: Trace dictionary drift check enforces new span names
- **WHEN** a pull request adds a new span name but does not update `docs/traces/dictionary.md`
- **THEN** the existing drift-check test in the server suite SHALL fail

### Requirement: Graph endpoint logging is debug-level on the hot path

`GraphService.BuildEnhancedTaskGraphAsync` SHALL NOT emit `LogInformation` for session counts, entity-id listings, or per-node session matches during normal request processing. All such logs SHALL be emitted at `LogDebug` level and SHALL NOT allocate formatted strings (e.g. `string.Join(...)`) unless the debug log level is enabled.

#### Scenario: Production log level suppresses hot-path diagnostics
- **WHEN** the configured log level for `Homespun.Features.Gitgraph` is `Information` or higher
- **THEN** no session-summary, entity-id, or session-match log lines SHALL be emitted while handling `GET /api/graph/{projectId}/taskgraph/data`

#### Scenario: Debug log level still reveals diagnostics
- **WHEN** the configured log level for `Homespun.Features.Gitgraph` is `Debug`
- **THEN** the same diagnostics SHALL be emitted so operators can still inspect session matching locally
