## Why

PR #786 introduced an in-memory task-graph snapshot (`IProjectTaskGraphSnapshotStore`) that serves `GET /api/graph/{projectId}/taskgraph/data` from a 10-second-refreshed cache. Reads are fast, but **Fleece issue mutations do not invalidate the snapshot**, so every issue create/edit/delete/move leaves the server serving stale data to the client. The SignalR `IssuesChanged` broadcast triggers the client to refetch, the refetch wins the race against the mutation, and the client renders the pre-mutation snapshot. The 10s refresher tick eventually rebuilds but never re-broadcasts, so the UI shows stale state until the next unrelated mutation or a window-focus refetch — user-perceived latency of 10s up to indefinite.

Even once invalidation is wired, the first read after each mutation pays a ~3s full recompute. The client-instigated singular-mutation case — the dominant UX path — should be sub-second.

## What Changes

Three deltas, each independently shippable behind a single OpenSpec change:

- **Delta 1 — snapshot invalidation on mutation (correctness):** split `NotificationHubExtensions.BroadcastIssuesChanged` into two intent-bearing helpers, `BroadcastIssueTopologyChanged` (invalidates snapshot + broadcasts) and `BroadcastIssueFieldsPatched` (reserved for Delta 2, behaves identically to topology in Delta 1). Migrate all 11 `IssuesController` call sites + `IssuesAgentController`. Inject `IHubContext<NotificationHub>` + snapshot store into `FleeceIssueSyncController` (3 endpoints currently broadcast nothing and do not invalidate). Inject `IHubContext<NotificationHub>` into `ChangeReconciliationService` (2 invalidation points currently broadcast nothing — client never learns). Snapshot invalidation happens **before** broadcast so client refetch cannot race a stale read.
- **Delta 2 — in-place field patching (latency):** add `IProjectTaskGraphSnapshotStore.PatchIssueFields(projectId, issueId, patch)` that mutates the matching node's issue fields in the existing snapshot entry and bumps `LastBuiltAt` — no recompute. `BroadcastIssueFieldsPatched` patches then broadcasts. Whitelist of structure-preserving fields: Title, Description, Priority, Tags, AssignedTo, CreatedBy, ExecutionMode, LastUpdate. Topology- or derived-data-affecting fields (Status, Type, ParentIssues, LinkedIssues, LinkedPRs, WorkingBranchId) fall through to topology invalidation. Undo/redo and multi-issue batches always invalidate.
- **Delta 3 — SignalR patch push (instant UX, gated):** new SignalR event `IssueFieldsPatched(projectId, issueId, patch)` carries the patch payload to clients, which apply it via TanStack `queryClient.setQueryData` — no HTTP refetch. Gated by `TaskGraphSnapshot:PatchPush:Enabled` (default true, flip off to revert to Delta-2 refetch behavior). Topology-class events continue on `IssuesChanged`.

## Capabilities

### New Capabilities

None. This change extends the existing OpenSpec-integration capability with additional requirements on top of the snapshot store from PR #786.

### Modified Capabilities

- `openspec-integration`: extends the `Per-project task-graph snapshot store` and `Background task-graph snapshot refresher` requirements with new invalidation triggers, adds an in-place patching requirement, and adds an optional SignalR patch-push contract.

## Impact

**Server code:**
- `Homespun.Server.Features.Notifications.NotificationHub` — new extension helpers, deprecate direct `BroadcastIssuesChanged`
- `Homespun.Server.Features.Fleece.Controllers.IssuesController` — 11 call-site migrations + whitelist-driven patch-vs-invalidate selection in `UpdateIssueAsync`
- `Homespun.Server.Features.Fleece.Controllers.IssuesAgentController` — 1 call-site migration
- `Homespun.Server.Features.Fleece.Controllers.FleeceIssueSyncController` — inject deps + add topology broadcast to 3 endpoints (no-op today)
- `Homespun.Server.Features.OpenSpec.Services.ChangeReconciliationService` — inject `IHubContext`, emit broadcast after existing invalidations
- `Homespun.Server.Features.Gitgraph.Snapshots.IProjectTaskGraphSnapshotStore` — new `PatchIssueFields` method + thread-safety considerations
- `Homespun.Server.Features.Gitgraph.Snapshots.TaskGraphSnapshotOptions` — new `PatchPush:Enabled` option

**Shared contracts:**
- `Homespun.Shared.Hubs.INotificationHubClient` — new `IssueFieldsPatched` client method (Delta 3)
- New DTO `IssueFieldPatch` in `Homespun.Shared.Models.Fleece` for the patch payload

**Client code:**
- `src/Homespun.Web/src/features/issues/components/task-graph-view.tsx` — new SignalR handler for `IssueFieldsPatched` (Delta 3)
- New helper `applyPatch(taskGraphResponse, issueId, patch)` with vitest coverage

**Docs:**
- `docs/gitgraph/taskgraph-snapshot.md` — new invalidation triggers + patch-push contract
- `docs/traces/dictionary.md` — new `graph.snapshot.patch` span (if added)
- `openspec/specs/openspec-integration/spec.md` — synced when this change archives

**Observability:** new span `graph.snapshot.patch` (Delta 2) records patch-vs-invalidate decisions and field names for each patched mutation. No new metrics.

**No breaking API changes.** `IssuesChanged` SignalR event preserves signature; new `IssueFieldsPatched` is additive.

**Follow-up captured:** Fleece issue `MiTdFL` tracks clone-lifecycle invalidation (agent session start/stop, PR merge) — out of scope here because those are not Fleece-mutation paths.
