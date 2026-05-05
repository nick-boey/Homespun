## REMOVED Requirements

### Requirement: Per-project task-graph snapshot store

**Reason:** The snapshot store exists solely to amortise server-side layout cost. Layout moves to the client (see `fleece-issue-tracking` delta). With no laid-out response to cache, `IProjectTaskGraphSnapshotStore` is deleted entirely along with all its scenarios (idle eviction, access-touch, patch-issue-fields, concurrent patch+invalidate).

**Migration:** the new `GET /api/projects/{projectId}/issues` endpoint runs the visible-set filter on every request — no caching layer. The per-request work is bounded by the number of issues × decoration enrichers and is acceptable without a snapshot.

### Requirement: Background task-graph snapshot refresher

**Reason:** The hosted-service refresher exists solely to keep snapshots warm. With no snapshots, there is nothing to refresh. `ITaskGraphSnapshotRefresher` is deleted along with the per-project semaphore registry, the 10-second tick, and `RefreshOnceAsync`.

**Migration:** none required. Mutations no longer kick a refresher because they no longer invalidate a snapshot. The new `IssueChanged` SignalR event flows directly to clients.

### Requirement: Fleece mutations invalidate the task-graph snapshot before broadcasting

**Reason:** The invalidate-before-broadcast contract assumes a snapshot exists. With the snapshot store deleted, every mutation site that previously called `BroadcastIssueTopologyChanged` (or the patched variant `BroadcastIssueFieldsPatched`) now calls a single `BroadcastIssueChanged` extension that emits one SignalR event with no snapshot side-effects. See the `fleece-issue-tracking` delta's "Unified IssueChanged SignalR event" requirement for the new contract.

**Migration:** every call site listed in the original requirement (~12 sites including `IssuesController` mutations, `IssuesAgentController.AcceptChangesAsync`, `FleeceIssueSyncController` endpoints, `ChangeReconciliationService`, agent-start, clone-lifecycle, PR-status-resolver) is migrated to `BroadcastIssueChanged` in this change. The `IssueChanged` event payload includes the canonical `IssueResponse` so clients can apply it directly with no refetch.

### Requirement: Hub-helper split enforces invalidation at broadcast time

**Reason:** The two-method split (`BroadcastIssueTopologyChanged` + `BroadcastIssueFieldsPatched`) exists only to enforce snapshot consistency. With the snapshot deleted, a single `BroadcastIssueChanged` helper is sufficient. The compile-time guard against "broadcasting without invalidating" is no longer needed because there is nothing to invalidate.

**Migration:** `NotificationHubExtensions` exposes a single `BroadcastIssueChanged(IHubContext<NotificationHub>, string projectId, IssueChangeKind kind, string? issueId, IssueResponse? issue)` extension. Both legacy methods are deleted.

### Requirement: In-place field patching for structure-preserving edits

**Reason:** Field patching is an optimisation that exists to update the cached snapshot without a full rebuild. With the snapshot deleted, the patchable/topology distinction collapses: every mutation simply emits `IssueChanged` carrying the canonical issue, and the client applies a replace-by-id merge regardless of which fields changed.

**Migration:** the `PatchableFieldAttribute` is deleted. The whitelist of patchable fields is deleted. `IssuesController.UpdateIssueAsync` no longer inspects the request body for field categorisation — it always emits `IssueChanged({kind: 'updated', ...})`. The client's `apply-patch.ts` helper is deleted; the SignalR handler in `useIssues` does an unconditional replace-by-id on the cache.

### Requirement: SignalR patch-push delivers field updates without refetch

**Reason:** The patch-push event channel (`IssueFieldsPatched`) is part of the same optimisation as field patching — it allows the client to apply a partial update to its cached `TaskGraphResponse` without refetching. With the cache shape changed (issue map, not laid-out response) and the unified `IssueChanged` event carrying the full canonical issue, partial-update plumbing is unnecessary.

**Migration:** the `IssueFieldsPatched` SignalR event is deleted along with its `INotificationHubClient` typed-method declaration and the `TaskGraphSnapshot:PatchPush:Enabled` configuration knob. The client subscribes only to `IssueChanged`.

### Requirement: Graph endpoint logging is debug-level on the hot path

**Reason:** This requirement constrained `GraphService.BuildEnhancedTaskGraphAsync` (deleted in this change). The new `IssuesController.GetVisibleIssues` action does not have a hot-path-logging concern in the same shape — it does not enrich session data per-node and has no entity-id listings to suppress. If hot-path logging emerges as a concern on the new endpoint, it can be re-introduced under a fresh requirement targeted at the new code.

**Migration:** none. The constraint dies with the code it constrained.

## MODIFIED Requirements

### Requirement: Issue graph change indicators

The system SHALL display branch and change status indicators on each issue row in the graph. The enrichment data SHALL be served from independent endpoints rather than bundled into the issue response: per-issue branch/change state via `GET /api/projects/{projectId}/openspec-states`, linked-PR state via `GET /api/projects/{projectId}/linked-prs`, and the per-issue branch fields embedded in the `IssueResponse` DTO from `GET /api/projects/{projectId}/issues`. Each enricher's branch resolution SHALL reuse a single per-request `BranchResolutionContext` (clones list + PR-to-branch dictionary) and SHALL NOT invoke `IGitCloneService.ListClonesAsync` more than once per request.

The web client assembles the indicator data by running parallel queries against each endpoint and joining at render time. Visual placement happens after the TS layout port runs — the *data* informing each indicator comes from the relevant endpoint above.

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

#### Scenario: Per-request branch resolution avoids subprocess fan-out
- **WHEN** `IIssueGraphOpenSpecEnricher.GetOpenSpecStatesAsync` or `GetMainOrphanChangesAsync` is invoked
- **THEN** `IGitCloneService.ListClonesAsync(project.LocalPath)` SHALL be called at most once for that request regardless of N
- **AND** `IDataStore.GetPullRequestsByProject` SHALL be called at most once for that request

## ADDED Requirements

### Requirement: OpenSpec states endpoint

The system SHALL expose `GET /api/projects/{projectId}/openspec-states?issues=<id>,<id>` returning `IReadOnlyDictionary<string, IssueOpenSpecState>` keyed by Fleece issue id.

The optional `issues=` query param SHALL constrain the per-clone scan to the supplied subset (the frontend supplies the visible-set ids it just fetched). When omitted, the server SHALL scan all visible issues — defined as issues with `Status ∈ { Draft, Open, Progress, Review }` plus their ancestors, mirroring the issue-set endpoint's default filter.

The implementation SHALL refactor the existing `IIssueGraphOpenSpecEnricher` interface so that `GetOpenSpecStatesAsync(projectId, issueIds, BranchResolutionContext)` returns *only* the per-issue state map. The combined `EnrichAsync(response)` shape that previously mutated a `TaskGraphResponse` SHALL be removed; the orphan-changes data is served by a sibling endpoint (see "Orphan changes endpoint" requirement).

The endpoint SHALL be independently testable: a test SHALL not require seeding agent sessions, linked PRs, or graph layout — only on-disk OpenSpec change directories within project clones.

#### Scenario: Issue with linked change returns its state
- **WHEN** an issue has a working branch with a clone containing an OpenSpec change linked via sidecar
- **AND** the client calls `GET /api/projects/{projectId}/openspec-states?issues=<that-issue-id>`
- **THEN** the response SHALL contain a single entry keyed by that issue id with the change's state populated

#### Scenario: Issue without a clone returns no entry
- **WHEN** an issue has no working clone on disk
- **THEN** the response SHALL NOT contain an entry for that issue (the dictionary's absence-of-key signals "no OpenSpec data")

#### Scenario: issues= query param scopes the scan
- **WHEN** the client requests `?issues=a,b,c` on a project with 100 issues
- **THEN** the per-clone scan SHALL execute only for clones containing those three issues (or their ancestors if the rule for "scope" includes ancestor clones — implementation detail)
- **AND** the response SHALL contain at most three entries

#### Scenario: Empty issues= param returns empty map
- **WHEN** the client requests `?issues=` (empty value)
- **THEN** the response SHALL be `{}` and 200

### Requirement: Orphan changes endpoint

The system SHALL expose `GET /api/projects/{projectId}/orphan-changes` returning `IReadOnlyList<SnapshotOrphan>` — the deduplicated list of OpenSpec changes on the project's main branch that have no sidecar linking them to a Fleece issue.

The implementation SHALL be sourced from `IIssueGraphOpenSpecEnricher.GetMainOrphanChangesAsync(projectId, BranchResolutionContext)` (a new method introduced as part of refactoring the combined `EnrichAsync` into two narrower methods).

The endpoint SHALL be independently testable.

#### Scenario: Single orphan on main returns one entry
- **WHEN** the project's main branch contains an OpenSpec change with no sidecar
- **THEN** the response SHALL contain one entry for that change

#### Scenario: Same change on multiple branches deduplicates
- **WHEN** a change with the same name exists on main and on a feature branch with no sidecar in either location
- **THEN** the response SHALL contain a single entry with both occurrences listed in `containingIssueIds` / occurrence list per the existing `SnapshotOrphan` shape

#### Scenario: Branch-scoped orphan (not on main) is excluded
- **WHEN** an unlinked change exists only on a feature branch and not on main
- **THEN** the response SHALL NOT include it (the existing requirement "Orphan change handling" already covers this — the orphan IS surfaced in the bottom section because the picker shows it via the issue graph's branch-state data, but the *main-branch orphan list endpoint* returns only main-branch orphans)

NOTE: behaviour for branch-scoped orphans is preserved by the existing "Orphan change handling" requirement (unchanged). The new endpoint only exposes the main-branch slice.

#### Scenario: Empty project returns empty list
- **WHEN** the project has no orphan changes on main
- **THEN** the response SHALL be `[]` and 200

### Requirement: Task-graph spans cover the enrichment path

`IssueGraphOpenSpecEnricher.EnrichAsync`, `BranchStateResolverService.GetOrScanAsync`, `ChangeReconciliationService.ReconcileAsync`, `ChangeScannerService.ScanBranchAsync`, `ChangeScannerService.GetArtifactStateAsync`, `IssueBranchResolverService.ResolveIssueBranchAsync`, and `CommandRunner.RunAsync` SHALL each emit an `Activity` under a dedicated `ActivitySource` (`Homespun.OpenSpec` for OpenSpec enrichment work, `Homespun.Commands` for the command runner). Each span SHALL carry cardinality-safe tags only: `project.id`, `issue.id`, `change.name`, `cache.hit`, `branch.source`, `phase`, `cmd.name`, `cmd.exit_code`, `cmd.duration_ms`. Every new span name SHALL appear in `docs/traces/dictionary.md`.

The new `IssuesController.GetVisibleIssues` action and the new `IssueAncestorTraversalService.CollectVisible` SHALL each emit a span on `Homespun.Fleece` (or a dedicated `Homespun.Issues` source) tagged with `project.id`, `issue.count`, and `cache.hit=false` (no snapshot exists). New span names SHALL be added to `docs/traces/dictionary.md` in the same change.

The previously-required spans on `GraphService.BuildEnhancedTaskGraphAsync` (`graph.taskgraph.build`) are removed since that method is deleted.

#### Scenario: Visible-issue-set request span has child spans for enrichment work
- **WHEN** `GET /api/projects/{projectId}/issues` is served
- **THEN** the emitted trace SHALL include a top-level span for the controller action and child spans for `openspec.enrich`, ancestor traversal (e.g. `issues.collect_visible`), and at least one `openspec.scan.branch` if any visible issue has a clone

#### Scenario: Command runner span wraps every subprocess
- **WHEN** `CommandRunner.RunAsync` spawns an `openspec` or `git` subprocess
- **THEN** the subprocess invocation SHALL be surrounded by a `cmd.run` span tagged with `cmd.name` and `cmd.exit_code`

#### Scenario: Trace dictionary drift check enforces new span names
- **WHEN** a pull request adds a new span name but does not update `docs/traces/dictionary.md`
- **THEN** the existing drift-check test in the server suite SHALL fail
