## MODIFIED Requirements

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

## ADDED Requirements

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
