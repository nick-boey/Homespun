## ADDED Requirements

### Requirement: Fleece mutations invalidate the task-graph snapshot before broadcasting

Every Fleece issue mutation path on the server SHALL invalidate the per-project task-graph snapshot **before** broadcasting the `IssuesChanged` SignalR event. The invalidation and broadcast SHALL be performed by a single hub-extension helper so that mutation endpoints cannot broadcast without also invalidating.

Applies to:

- All mutation endpoints on `IssuesController` (create, update, delete, set-parent, remove-parent, remove-all-parents, move-sibling, apply-agent-changes, resolve-conflicts, undo, redo).
- `IssuesAgentController.AcceptChangesAsync` after post-merge processing.
- All three endpoints on `FleeceIssueSyncController` (`Sync`, `Pull`, `DiscardNonFleeceAndPull`) after a successful `ReloadFromDiskAsync`.
- Both side-effects inside `ChangeReconciliationService.ReconcileAsync` (sidecar auto-link, archive auto-transition).

#### Scenario: Client refetch after mutation returns fresh data
- **WHEN** a client sends `PUT /api/issues/{issueId}` and receives `IssuesChanged` over SignalR
- **THEN** the client's immediate refetch of `GET /api/graph/{projectId}/taskgraph/data` SHALL return a response that reflects the mutation (not the pre-mutation snapshot)

#### Scenario: Invalidation is ordered before broadcast
- **WHEN** the hub helper is invoked for a topology-class mutation
- **THEN** `IProjectTaskGraphSnapshotStore.InvalidateProject(projectId)` SHALL be invoked synchronously before any `Clients.*.SendAsync("IssuesChanged", …)` call

#### Scenario: FleeceIssueSyncController broadcasts on repo pull
- **WHEN** `POST /api/fleece-sync/{projectId}/pull` succeeds and calls `ReloadFromDiskAsync`
- **THEN** the task-graph snapshot for that project SHALL be invalidated and `IssuesChanged` SHALL be broadcast to the project group (with `issueId: null` to indicate a bulk change)

#### Scenario: ChangeReconciliationService broadcasts on sidecar auto-link
- **WHEN** `ChangeReconciliationService.ReconcileAsync` writes a sidecar to auto-link a single orphan change
- **THEN** the reconciler SHALL invalidate the project snapshot and broadcast `IssuesChanged` so connected clients refetch the graph

### Requirement: Hub-helper split enforces invalidation at broadcast time

`NotificationHubExtensions` SHALL expose exactly two extension methods for issue-change broadcasts and SHALL NOT expose a public helper that broadcasts `IssuesChanged` without also invalidating or patching the snapshot:

- `BroadcastIssueTopologyChanged(IHubContext<NotificationHub>, string projectId, IssueChangeType changeType, string? issueId)` — invalidates the project snapshot, fires-and-forgets `ITaskGraphSnapshotRefresher.RefreshOnceAsync`, broadcasts `IssuesChanged`.
- `BroadcastIssueFieldsPatched(IHubContext<NotificationHub>, string projectId, string issueId, IssueFieldPatch patch)` — applies the patch to the snapshot in place, broadcasts (see "SignalR patch push" requirement for Delta 3 event choice).

The legacy `BroadcastIssuesChanged` helper SHALL be removed.

#### Scenario: Topology helper invalidates and broadcasts in order
- **WHEN** a controller calls `BroadcastIssueTopologyChanged`
- **THEN** the helper SHALL call `snapshotStore.InvalidateProject(projectId)` before `Clients.All.SendAsync("IssuesChanged", …)` and `Clients.Group(...).SendAsync(...)`

#### Scenario: Topology helper kicks the refresher without blocking
- **WHEN** `BroadcastIssueTopologyChanged` runs
- **THEN** `ITaskGraphSnapshotRefresher.RefreshOnceAsync` SHALL be started fire-and-forget and SHALL NOT be awaited inside the helper so the HTTP response is not delayed

#### Scenario: Snapshot store not registered is tolerated
- **WHEN** `TaskGraphSnapshot:Enabled=false` so `IProjectTaskGraphSnapshotStore` is not registered
- **THEN** the hub helpers SHALL still broadcast `IssuesChanged` without throwing, and SHALL omit the invalidate/patch step

### Requirement: In-place field patching for structure-preserving edits

`IProjectTaskGraphSnapshotStore` SHALL expose `PatchIssueFields(string projectId, string issueId, IssueFieldPatch patch)` that, for every matching snapshot entry keyed by `projectId`, replaces the `TaskGraphResponse.Nodes[i].Issue` for the matching `issueId` with a new `IssueResponse` whose non-null fields from `patch` overlay the existing values. `LastBuiltAt` SHALL be bumped to the current time. Entries for other `(projectId, maxPastPRs)` keys belonging to the same project SHALL all be patched.

The patchable fields SHALL be exactly: `Title`, `Description`, `Priority`, `Tags`, `AssignedTo`, `CreatedBy`, `ExecutionMode`, `LastUpdate`. Any other field on `IssueResponse` SHALL route through `BroadcastIssueTopologyChanged` instead.

`IssuesController.UpdateIssueAsync` SHALL inspect the request body against the whitelist and call `BroadcastIssueFieldsPatched` only when every mutated field is whitelisted. If any non-whitelisted field is included in the same request, the handler SHALL fall through to `BroadcastIssueTopologyChanged`.

Undo/redo (`IssuesController.Undo`/`Redo`) SHALL always invalidate — they replace the full issue list.

#### Scenario: Title-only edit patches in place
- **WHEN** `PUT /api/issues/{id}` is called with only `title` set
- **THEN** the snapshot entry for the project SHALL have the matching node's `Issue.Title` updated and `LastBuiltAt` bumped, without triggering a full rebuild

#### Scenario: Status change invalidates instead of patching
- **WHEN** `PUT /api/issues/{id}` is called with `status` set (with or without other fields)
- **THEN** the handler SHALL call `BroadcastIssueTopologyChanged` and the snapshot entries for the project SHALL be removed

#### Scenario: Multi-field edit with mixed fields invalidates
- **WHEN** `PUT /api/issues/{id}` is called with `title` AND `status` set
- **THEN** the handler SHALL call `BroadcastIssueTopologyChanged` (the presence of a non-whitelisted field forces the topology path)

#### Scenario: Patch applied to a snapshot evicted mid-race is a no-op
- **WHEN** `PatchIssueFields` is invoked between the entry being read and the idle-eviction tick removing it
- **THEN** the patch call SHALL no-op and SHALL NOT re-create the snapshot entry

#### Scenario: Whitelist drift is caught at compile time
- **WHEN** a new property is added to `IssueResponse` without being marked either patchable or topology-only
- **THEN** the server test suite SHALL fail with a clear message identifying the unclassified property

### Requirement: SignalR patch-push delivers field updates without refetch

When `TaskGraphSnapshot:PatchPush:Enabled` is `true` (default), `BroadcastIssueFieldsPatched` SHALL emit a new SignalR event `IssueFieldsPatched(string projectId, string issueId, IssueFieldPatch patch)` on both `Clients.All` and the project group. The event SHALL be typed via `INotificationHubClient`. The patch payload SHALL contain only non-null fields.

Client task-graph views SHALL handle `IssueFieldsPatched` by locating the issue node in the cached `TaskGraphResponse` (`queryKey = ['taskGraph', projectId]`) and applying the patch via `queryClient.setQueryData(...)`. No HTTP refetch SHALL be triggered by this event.

When `TaskGraphSnapshot:PatchPush:Enabled` is `false`, `BroadcastIssueFieldsPatched` SHALL emit `IssuesChanged` (not `IssueFieldsPatched`) so clients fall back to invalidate-and-refetch. The server-side in-place patch SHALL still be applied so the fallback refetch returns fresh data.

#### Scenario: Patch push default delivers instant updates
- **WHEN** a title edit completes on the server with `PatchPush:Enabled=true`
- **THEN** the client SHALL receive `IssueFieldsPatched` and apply it via `queryClient.setQueryData` without firing any `GET /api/graph/{projectId}/taskgraph/data` request

#### Scenario: Patch push kill switch falls back cleanly
- **WHEN** `TaskGraphSnapshot:PatchPush:Enabled` is set to `false`
- **THEN** field edits SHALL emit `IssuesChanged` and clients SHALL refetch via TanStack invalidation; the server-side snapshot SHALL still be patched so the refetch returns fresh data

#### Scenario: Topology event channel is unaffected by the flag
- **WHEN** `PatchPush:Enabled` is `false`
- **THEN** topology-class mutations SHALL continue to emit `IssuesChanged` exactly as they do with the flag `true` (the flag only affects field-patch paths)

## MODIFIED Requirements

### Requirement: Per-project task-graph snapshot store

The server SHALL maintain an in-memory `IProjectTaskGraphSnapshotStore` keyed by `(projectId, maxPastPRs)` whose entries hold the most recent `TaskGraphResponse` and a `lastAccessedAt` timestamp. Snapshots SHALL be evicted after a configurable idle window with default 5 minutes of no read traffic. The store SHALL expose:

- `TryGet`, `Store`, `InvalidateProject`, `GetTrackedKeys`, `EvictIdle` (pre-existing)
- `PatchIssueFields(string projectId, string issueId, IssueFieldPatch patch)` — mutates the matching node's issue fields in every entry belonging to `projectId`, bumps `LastBuiltAt`, no-op if no entry exists

Patch and invalidate operations SHALL be thread-safe for concurrent invocation: a racing `PatchIssueFields` + `InvalidateProject` pair SHALL produce the same observable outcome as an `InvalidateProject` alone (patch may be wasted but never corrupts state).

#### Scenario: Idle project is evicted
- **WHEN** no request has read a snapshot entry for longer than the configured idle window
- **THEN** the snapshot refresher SHALL remove the entry from the store and stop refreshing it

#### Scenario: Access touches the snapshot
- **WHEN** the endpoint returns a snapshot to a client
- **THEN** the store SHALL update `lastAccessedAt` to the current time

#### Scenario: Patch updates every tracked key for the project
- **WHEN** `PatchIssueFields(projectId, issueId, patch)` is called with two tracked entries `(projectId, maxPastPRs=5)` and `(projectId, maxPastPRs=10)`
- **THEN** both entries SHALL have the matching node's issue fields patched and their `LastBuiltAt` timestamps bumped

#### Scenario: Concurrent patch and invalidate does not corrupt state
- **WHEN** `PatchIssueFields` and `InvalidateProject` for the same project are invoked from different threads in arbitrary order
- **THEN** the resulting store state SHALL match one of: both applied in either order (patched then invalidated leaves no entry; invalidated then patched leaves no entry because the patch is a no-op on a missing entry)

### Requirement: Background task-graph snapshot refresher

A hosted service `ITaskGraphSnapshotRefresher` SHALL periodically recompute snapshots for every tracked `(projectId, maxPastPRs)` on a configurable interval with default 10 seconds. Refreshes SHALL be serialised per project by a `SemaphoreSlim` so that concurrent refresh invocations coalesce. The refresher SHALL expose `RefreshOnceAsync(CancellationToken)` for callers that want to trigger an immediate rebuild off the hot path.

#### Scenario: Refresh interval elapses
- **WHEN** the configured refresh interval elapses for a tracked entry
- **THEN** the refresher SHALL rebuild the snapshot via `GraphService.BuildEnhancedTaskGraphAsync` and replace the store entry

#### Scenario: Explicit invalidation triggers immediate refresh
- **WHEN** a sidecar mutation, archive transition, or explicit refresh-endpoint call invalidates a tracked entry
- **THEN** the refresher SHALL rebuild the snapshot at the next iteration without waiting for the full interval

#### Scenario: Mutation-triggered kick races the client refetch
- **WHEN** `BroadcastIssueTopologyChanged` fires `RefreshOnceAsync` fire-and-forget after invalidating
- **THEN** the refresher SHALL start a rebuild on a background thread without blocking the mutation's HTTP response, and SHALL coalesce with any concurrent tick-triggered rebuild via its per-project `SemaphoreSlim`

#### Scenario: Graceful shutdown drains the refresher
- **WHEN** the host shuts down
- **THEN** any in-progress refresh SHALL honour the cancellation token and complete before the service stops
