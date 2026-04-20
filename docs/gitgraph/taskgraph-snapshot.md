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

Bound from the `TaskGraphSnapshot` section of `appsettings.json` and mirrored
in `appsettings.Mock.json`. Tests can pin the interval high to prevent the
refresher from racing assertions.

## Invalidation triggers

The refresher alone is not enough — snapshots must be busted whenever a
mutation could change the response. The following paths invalidate:

- `POST /api/graph/{projectId}/refresh` — calls
  `IProjectTaskGraphSnapshotStore.InvalidateProject(projectId)` after a
  successful incremental refresh. The next `/taskgraph/data` call for that
  project pays the full compute once.
- **Sidecar auto-link** inside
  `ChangeReconciliationService.ReconcileAsync` — when the scanner
  writes a `.homespun.yaml` sidecar to link a single orphan change, the
  reconciler calls `InvalidateProject(projectId)` on the same tick.
- **Archive auto-transition** inside
  `ChangeReconciliationService.ReconcileAsync` — when a linked change
  archives and the Fleece issue transitions to Complete, the reconciler
  invalidates so the graph reflects the phase change immediately.
- `POST /api/openspec/changes/link` (`ChangeSnapshotController.LinkOrphan`)
  — explicit user-driven orphan link invalidates after writing the
  sidecar.

## Rollback

Flip `TaskGraphSnapshot:Enabled=false` in configuration (or override via the
`TaskGraphSnapshot__Enabled=false` environment variable) and restart the
server. The controller immediately falls back to the synchronous path; the
refresher hosted service is not registered so there is no steady-state CPU
cost from snapshot refresh.
