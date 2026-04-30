## MODIFIED Requirements

### Requirement: Fleece mutations invalidate the task-graph snapshot before broadcasting

Every Fleece issue mutation path on the server SHALL invalidate the per-project task-graph snapshot **before** broadcasting the `IssuesChanged` SignalR event. The invalidation and broadcast SHALL be performed by a single hub-extension helper so that mutation endpoints cannot broadcast without also invalidating.

The same helper SHALL also be invoked on clone-lifecycle events that change the graph's `OpenSpecStates` output but are not Fleece-mutation paths. These additional triggers are:

- **Clone created**: `AgentStartBackgroundService.StartAgentAsync` (after `CreateCloneAsync` succeeds for a new agent session), `IssuesAgentController.CreateSession` (after `CreateCloneAsync` succeeds for an Issues Agent session), and `ProjectClonesController.Create` (after a manual clone is created via the project clones API).
- **Clone removed**: `ProjectClonesController.Delete`, `ProjectClonesController.BulkDelete`, and `ProjectClonesController.Prune` (each after the underlying removal succeeds).
- **PR transitioned to Merged or Closed**: `PRStatusResolver.ResolveClosedPRStatusesAsync` (once per resolved PR after the graph cache and any linked Fleece issue have been updated).

Applies to:

- All mutation endpoints on `IssuesController` (create, update, delete, set-parent, remove-parent, remove-all-parents, move-sibling, apply-agent-changes, resolve-conflicts, undo, redo).
- `IssuesAgentController.AcceptChangesAsync` after post-merge processing.
- `IssuesAgentController.CreateSession` after the agent's working clone is created.
- `AgentStartBackgroundService.StartAgentAsync` after a new agent worktree is created.
- `ProjectClonesController` create / delete / bulk-delete / prune endpoints.
- `PRStatusResolver.ResolveClosedPRStatusesAsync` for each Merged or Closed transition.
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

#### Scenario: Agent session start on an issue with no prior clone
- **WHEN** an agent session is dispatched for an issue whose branch has no on-disk clone, and `CreateCloneAsync` succeeds
- **THEN** `BroadcastIssueTopologyChanged(projectId, IssueChangeType.Updated, issueId)` SHALL fire with the dispatch's issue id, and the client's next refetch of `/taskgraph/data` SHALL include the issue's `OpenSpecStates` entry within ~1s rather than waiting up to 10s for the refresher tick

#### Scenario: Agent session start that reuses an existing clone
- **WHEN** an agent session is dispatched for an issue whose branch already has a clone (the existing-clone branch in `StartAgentAsync` is taken)
- **THEN** the helper SHALL NOT be called, since clone presence — and therefore `OpenSpecStates` — has not changed

#### Scenario: Manual clone delete via project clones API
- **WHEN** `DELETE /api/projects/{projectId}/clones?clonePath=…` succeeds
- **THEN** the helper SHALL fire with `issueId: null` and the project snapshot SHALL be invalidated before the SignalR broadcast

#### Scenario: PR transition to Merged invalidates with linked issue id
- **WHEN** `PRStatusResolver.ResolveClosedPRStatusesAsync` resolves a PR as `Merged` and the removed PR carries a `FleeceIssueId`
- **THEN** the helper SHALL fire with `IssueChangeType.Updated` and the linked `FleeceIssueId`

#### Scenario: PR transition to Closed without linked issue invalidates with null id
- **WHEN** `PRStatusResolver.ResolveClosedPRStatusesAsync` resolves a PR as `Closed` and the removed PR has no `FleeceIssueId`
- **THEN** the helper SHALL fire with `IssueChangeType.Updated` and `issueId: null` so the client refetches the project's full graph
