# Task-graph snapshot store

`GET /api/graph/{projectId}/taskgraph/data` is served from an in-memory
snapshot maintained by `IProjectTaskGraphSnapshotStore` and refreshed on a
configurable interval by `ITaskGraphSnapshotRefresher` (an `IHostedService`).
First access for a `(projectId, maxPastPRs)` key computes synchronously,
stores the result, and marks the key tracked. Subsequent reads return the
stored snapshot without re-running `IssueGraphOpenSpecEnricher.EnrichAsync`.

The endpoint sets `snapshot.hit=true|false` on the active HTTP server span so
operators can distinguish warm hits from cold fills in Seq / the Aspire
dashboard.

## Options (`TaskGraphSnapshot:*`)

| Option | Default | Purpose |
| --- | --- | --- |
| `Enabled` | `true` | Master switch. When `false`, the controller falls back to the original synchronous compute path and no snapshot store / refresher is registered. |
| `RefreshIntervalSeconds` | `10` | How often the refresher iterates tracked keys and rebuilds each snapshot. |
| `IdleEvictionMinutes` | `5` | Entries whose `LastAccessedAt` is older than this window are evicted from the store (and therefore no longer refreshed). |
| `PatchPush:Enabled` | `true` | When `true`, `BroadcastIssueFieldsPatched` emits the dedicated `IssueFieldsPatched` SignalR event so clients can apply the patch via `queryClient.setQueryData` with no HTTP refetch. When `false`, the helper falls back to emitting `IssuesChanged` (Delta-2 behaviour — clients invalidate + refetch). Snapshot patching on the server happens in both cases. |

Bound from the `TaskGraphSnapshot` section of `appsettings.json` and mirrored
in `appsettings.Mock.json`. Tests can pin the interval high to prevent the
refresher from racing assertions.

## Invalidation triggers

The refresher alone is not enough — snapshots must be busted whenever a
mutation could change the response. The central helper
`NotificationHubExtensions.BroadcastIssueTopologyChanged` is the single
invalidate-and-broadcast entry point: it invalidates the snapshot, fires
a best-effort `RefreshOnceAsync` on the refresher, and then broadcasts
`IssuesChanged` to `Clients.All` and `Clients.Group($"project-{projectId}")`
— in that strict order, so a client refetching in response to the
broadcast cannot observe the pre-mutation snapshot. The following paths
invalidate via this helper (or directly via `InvalidateProject`):

- **Every mutation endpoint on `IssuesController`** (create, update,
  delete, set-parent, remove-parent, remove-all-parents, undo, redo,
  history replay — 11 call sites total). Each endpoint calls
  `BroadcastIssueTopologyChanged(HttpContext.RequestServices, projectId, changeType, issueId)`
  as the final step of a successful request.
- **`IssuesAgentController.CompleteAsync`** — when an agent session
  completes and the resulting issue set is persisted, the controller
  broadcasts so connected clients refetch the graph.
- **`FleeceIssueSyncController`** — on each successful
  `ReloadFromDiskAsync` inside `Sync`, `Pull`, and `DiscardNonFleeceAndPull`,
  the controller broadcasts for the target project. Failed pulls do
  **not** reload the in-memory cache and therefore do not invalidate.
- `POST /api/graph/{projectId}/refresh` — calls
  `IProjectTaskGraphSnapshotStore.InvalidateProject(projectId)` after a
  successful incremental refresh. The next `/taskgraph/data` call for that
  project pays the full compute once.
- **Sidecar auto-link** inside
  `ChangeReconciliationService.ReconcileAsync` — when the scanner
  writes a `.homespun.yaml` sidecar to link a single orphan change, the
  reconciler invalidates and broadcasts on the same tick.
- **Archive auto-transition** inside
  `ChangeReconciliationService.ReconcileAsync` — when a linked change
  archives and the Fleece issue transitions to Complete, the reconciler
  invalidates and broadcasts so the graph reflects the phase change
  immediately.
- `POST /api/openspec/changes/link` (`ChangeSnapshotController.LinkOrphan`)
  — explicit user-driven orphan link invalidates after writing the
  sidecar.
- **Clone lifecycle events** — clone presence drives
  `TaskGraphResponse.OpenSpecStates[issueId]` (the enricher reads OpenSpec
  artefacts off the working clone), so clones appearing or disappearing
  also invalidates:
  - `AgentStartBackgroundService.StartAgentAsync` — after a successful
    `CreateCloneAsync` for a new agent session (skipped when an existing
    clone is reused, since presence didn't change).
  - `IssuesAgentController.CreateSession` — after a successful
    `CreateCloneAsync` for an Issues Agent session.
  - `ProjectClonesController` — after a successful `Create`, `Delete`,
    `BulkDelete` (when at least one removal succeeded), or `Prune`. The
    manual clone API doesn't carry an issue id, so the helper is invoked
    with `issueId: null` (bulk change).
  - `PRStatusResolver.ResolveClosedPRStatusesAsync` — after each PR
    transitions to Merged or Closed. The linked Fleece issue's status
    flips (Complete / Closed) and the associated clone may be evicted,
    both of which reshape `OpenSpecStates` for the issue. The helper is
    invoked once per resolved PR with the linked `FleeceIssueId` if
    present, otherwise `null`.

## In-place field patching

For structure-preserving edits, `BroadcastIssueFieldsPatched` sidesteps
the ~3s full rebuild by calling
`IProjectTaskGraphSnapshotStore.PatchIssueFields(projectId, issueId, patch)`.
The store walks every `(projectId, maxPastPRs)` entry for the project,
replaces the matching node's `Issue` with a new `IssueResponse` whose
non-null patch fields overlay the existing values, and bumps
`LastBuiltAt`. The original response object is never mutated — a shallow
clone with the new node substituted keeps concurrent readers safe.

**Whitelist.** Fields eligible for in-place patching live on
`IssueFieldPatch` and are decorated with `[PatchableField]` on
`IssueResponse`:

- `Title`, `Description`, `Priority`, `Tags`, `AssignedTo`, `CreatedBy`,
  `ExecutionMode`, `LastUpdate`.

**Topology fallback.** Fields that drive lane assignment, grouping, or
cross-node derivations are decorated `[TopologyField]` and force a full
invalidation via `BroadcastIssueTopologyChanged`:

- `Status`, `Type`, `ParentIssues`, `LinkedIssues`, `LinkedPRs`,
  `WorkingBranchId`.

`IssuesController.UpdateIssueAsync` inspects the `UpdateIssueRequest`
against the whitelist via `TryBuildFieldPatch`. If every set field is
patchable, the handler calls `BroadcastIssueFieldsPatched`. If any
topology field is set — or a field is set alongside a topology field in
the same request — the handler falls through to
`BroadcastIssueTopologyChanged`.

**Patch does not rebuild.** A successful patch emits the
`graph.snapshot.patch` span and leaves the snapshot entry in the store;
no refresher kick fires, no full recompute runs. `LastBuiltAt` bumps
only if a matching node was found; patches targeting issues absent from
the current node list are a no-op (they never recreate an evicted
entry). A concurrent `InvalidateProject` always wins — the patch may be
wasted but never corrupts state, because the response substitution is a
single `ConcurrentDictionary.AddOrUpdate` on the shared entry.

## SignalR patch push

When `TaskGraphSnapshot:PatchPush:Enabled` is `true` (the default),
`BroadcastIssueFieldsPatched` emits a dedicated
`IssueFieldsPatched(projectId, issueId, IssueFieldPatch)` SignalR event
after the in-place snapshot patch. The React client handler subscribed
in `task-graph-view.tsx` applies the payload via
`queryClient.setQueryData` using the `applyPatch` helper in
`src/features/issues/lib/apply-patch.ts` — a shallow spread-merge that
overlays non-null patch fields onto the matching node's `issue`,
produces a new response object, and leaves every other node untouched.
No HTTP refetch is triggered.

Topology-class events continue on `IssuesChanged`. The client's
existing `IssuesChanged` handler calls
`queryClient.invalidateQueries({ queryKey: taskGraphQueryKey(projectId) })`,
which triggers one `GET /taskgraph/data` that hits the
already-invalidated snapshot and pays at most one recompute.

**Kill switch.** Flipping `TaskGraphSnapshot:PatchPush:Enabled=false`
reverts to Delta-2 behaviour without a redeploy: the helper still
patches the server snapshot in place but emits `IssuesChanged` instead
of `IssueFieldsPatched`, so clients fall back to the refetch path.
Useful if a client-side `applyPatch` regression is suspected in
production — operators can flip the flag and force refetches while the
fix rolls out.

**Ordering.** SignalR over WebSocket preserves per-connection order,
and both the patch-push event and the snapshot patch are dispatched
from the same helper, so a client that receives `IssueFieldsPatched`
is guaranteed to see the server-side snapshot already patched if it
refetches for any unrelated reason. Cross-user reorder races are
benign: the final snapshot state is monotonic, and any re-read
converges on the refresher's next tick.

## Rollback

Flip `TaskGraphSnapshot:Enabled=false` in configuration (or override via the
`TaskGraphSnapshot__Enabled=false` environment variable) and restart the
server. The controller immediately falls back to the synchronous path; the
refresher hosted service is not registered so there is no steady-state CPU
cost from snapshot refresh.
