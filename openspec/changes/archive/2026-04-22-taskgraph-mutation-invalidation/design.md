## Context

PR #786 shipped `IProjectTaskGraphSnapshotStore` + `TaskGraphSnapshotRefresher` to cut `GET /api/graph/{projectId}/taskgraph/data` P95 from 20–30s to <3s. Reads are warm-snapshot hits; misses pay one ~3s full rebuild that the refresher does in the background on a 10-second tick.

Invalidation triggers today: `POST /api/graph/{projectId}/refresh`, `POST /api/openspec/changes/link`, and two side-effects inside `ChangeReconciliationService.ReconcileAsync` (sidecar auto-link, archive auto-transition).

**Nothing on a Fleece-issue-mutation path invalidates.** Confirmed by code sweep — 11 endpoints on `IssuesController`, one on `IssuesAgentController`, three on `FleeceIssueSyncController`, and the `ChangeReconciliationService` broadcast gap all miss either invalidation, broadcast, or both. The client's TanStack Query handler for `IssuesChanged` refetches immediately and races the mutation — it wins, and reads the pre-mutation snapshot. The refresher tick rebuilds the snapshot 10s later but never re-broadcasts, so the client is stuck on stale data until window focus or another mutation.

Even correcting invalidation leaves a ~3s full-rebuild per mutation (one read amortised against one write). For the common case — a single user editing a single issue field — that's still laggy and wasteful. A structure-preserving field edit doesn't need the graph rebuilt; it only needs the one node's fields updated in the already-valid snapshot.

## Goals / Non-Goals

**Goals:**

- Eliminate the stale-read window on every Fleece-mutation path so the client's post-mutation refetch always returns fresh data.
- Push single-field mutations to <100ms user-perceived latency (down from 10s+).
- Keep the refresher + full-rebuild path as the safe fallback for anything that changes topology or derived data.
- Land as three independent deltas so Delta 1 can ship alone for correctness without waiting for Delta 2/3.

**Non-Goals:**

- Invalidating on clone-lifecycle events (agent session start/stop, PR merge). Tracked separately in Fleece issue `MiTdFL`. These are not Fleece-mutation paths.
- Reducing the cold-rebuild cost of `BuildEnhancedTaskGraphAsync`. If 3s remains unacceptable after this change, that's a separate optimisation on the enricher hot path.
- Replacing TanStack Query with a bespoke real-time sync layer. We stay on `queryClient.invalidateQueries` + `setQueryData`; SignalR carries events only.
- Changing the refresher's 10s interval or the 5-minute idle-eviction default.

## Decisions

### D1 — Invalidate inside the hub helper, not at each call site

**Chosen:** Move `IProjectTaskGraphSnapshotStore.InvalidateProject` inside two new extension methods on `IHubContext<NotificationHub>`:

- `BroadcastIssueTopologyChanged(projectId, changeType, issueId)` — invalidates, then broadcasts `IssuesChanged`.
- `BroadcastIssueFieldsPatched(projectId, issueId, IssueFieldPatch patch)` — patches in place, then broadcasts.

Controllers stop taking `IProjectTaskGraphSnapshotStore` as a DI dependency. The hub helper is the single choke point.

**Why:** A new mutation endpoint added later by anyone on the team cannot forget to invalidate — the broadcast call is the same place they'd reach for `IssuesChanged`, and there is no public helper that broadcasts without invalidating. Forces the author to choose "topology" vs "patch" at the call site, which doubles as a code-review signal about what kind of mutation this is.

**Alternatives considered:**

- **Invalidate-at-call-site (11+ lines changed, no new abstraction):** smallest diff but fails the forgetting test — any future endpoint that adds a broadcast without the invalidate line reintroduces the bug silently.
- **Invalidate inside `BroadcastIssuesChanged` itself (single helper, single method):** loses the patch-vs-topology distinction needed for Delta 2. Either we accept two helpers or we thread a patch parameter through the one helper (worse ergonomics, worse readability).

### D2 — Invalidation precedes broadcast; never the other way

**Chosen:** The helper body runs `snapshotStore.InvalidateProject(projectId)` (or `PatchIssueFields(...)`) **synchronously before** `Clients.*.SendAsync("IssuesChanged", ...)`.

**Why:** SignalR broadcast causes the client to invalidate its TanStack cache and refetch immediately. If the server invalidates after the broadcast, the refetch arrives during a window where the snapshot is still the pre-mutation one — the client reads it, renders stale data, and no second chance fires. Inverting the order forces the refetch to miss the snapshot (or hit a patched one) and always return fresh data.

**Alternatives considered:**

- **Broadcast first, invalidate async:** simpler but reintroduces the bug we're fixing. Rejected.
- **Broadcast-first with a "snapshot-version" header on the response:** client could compare versions and re-refetch if stale. Adds client complexity and doesn't solve the underlying race — rejected.

### D3 — Kick the refresher inline for topology-class invalidations

**Chosen:** After `InvalidateProject`, `BroadcastIssueTopologyChanged` fires-and-forgets `ITaskGraphSnapshotRefresher.RefreshOnceAsync(CancellationToken.None)`. No `await`; no blocking the HTTP response.

**Why:** `RefreshOnceAsync` already exists (wired up during PR #786). The existing per-project `SemaphoreSlim` coalesces races — if the refresher tick and the mutation-triggered kick fire back-to-back, only one rebuild runs. The fire-and-forget gives the client's refetch a ~50ms head start on the rebuild; in practice the refetch hits a warm snapshot roughly half the time, a full recompute the other half. Either way, no stale read.

**Alternatives considered:**

- **Await the refresh inside the mutation request:** predictable but blocks the mutation response for ~3s. User writes feel slow. Rejected.
- **Rely solely on the 10s tick:** the client still reads a stale snapshot in the intervening window. Rejected.

### D4 — Field-patch whitelist, topology always invalidates

**Chosen:** Only these fields may be patched in place: `Title`, `Description`, `Priority`, `Tags`, `AssignedTo`, `CreatedBy`, `ExecutionMode`, `LastUpdate`. Every other field (`Status`, `Type`, `ParentIssues`, `LinkedIssues`, `LinkedPRs`, `WorkingBranchId`) forces `BroadcastIssueTopologyChanged`.

**Why:**

- `Status` → drives `IsActionable` in the Fleece task graph, which determines lane assignment (actionable → lane 0). Patching `Status` without recomputing lanes produces a desynchronised snapshot.
- `Type` → `verify`-type has special grouping in the tree-view layout.
- `ParentIssues` / `LinkedIssues` → graph edges; patching breaks topology.
- `LinkedPRs` → projected into `TaskGraphResponse.LinkedPrs` dict; dependent data.
- `WorkingBranchId` → feeds `IssueBranchResolverService` priority-2 fallback which resolves the branch name used to locate OpenSpec artifacts. On its own, a `WorkingBranchId` change only affects `OpenSpecStates` when a clone exists — but verifying "no clone exists for this issue" at mutation time is a directory scan we don't want on the write path. Safer to invalidate.

A multi-field update (`PUT /issues/{id}` touching both Title *and* Status) must fall through to topology invalidation. The `UpdateIssueAsync` handler evaluates all fields set in the request against the whitelist and chooses one helper.

**Alternatives considered:**

- **Patch all fields, recompute derivations in place:** reimplementing the Fleece `BuildFilteredTaskGraphLayoutAsync` in the snapshot store is substantial, fragile, and duplicates logic owned by `Fleece.Core`. Rejected.
- **Invalidate on every mutation (skip patching entirely):** works for correctness but loses the instant-UX goal. Ship this as Delta 1 and stop? — only acceptable if 3s rebuild feels instant enough, which it won't on a field edit.

### D5 — Patch payload carries only the changed fields

**Chosen:** `IssueFieldPatch` is a DTO with nullable fields; null means "unchanged." The client applies non-null fields via shallow merge into the cached `IssueResponse` for the matching node.

**Why:** Keeps the SignalR payload small (typical edits change 1–3 fields out of ~15), removes ambiguity about "null means clear" vs "null means unchanged" — explicit sentinel patterns would require a separate `Cleared` set, which no current field needs.

**Alternatives considered:**

- **Full `IssueResponse` on the wire:** trivial to implement client-side but ~10× the payload for a title edit. Rejected.
- **JSON Patch RFC 6902:** overkill for the shallow-field case and requires a client-side patch library. Rejected.

### D6 — `PatchPush:Enabled` is default-on with an explicit kill switch

**Chosen:** New option `TaskGraphSnapshot:PatchPush:Enabled` (default `true`). When `false`, `BroadcastIssueFieldsPatched` still patches the snapshot but emits `IssuesChanged` (not `IssueFieldsPatched`), so clients fall back to the refetch path.

**Why:** Delta 3 changes the SignalR contract; if a client bug sneaks in that applies patches incorrectly, operators need to flip a switch without a redeploy. Defaulting to on prevents Delta 3 from silently staying off forever.

**Alternatives considered:**

- **Flag-off default for a week of soak:** feature stays inert in prod until someone remembers to flip it. Against our usual "merge → on" bias. Rejected.
- **Two separate events for topology vs field with unified dispatch:** more complex to disable selectively. The single-flag kill switch is simpler.

### D7 — `PatchIssueFields` is atomic with `InvalidateProject` via the store's own locking

**Chosen:** `ProjectTaskGraphSnapshotStore` already uses `ConcurrentDictionary` for the entries map. Add `PatchIssueFields` as a method that looks up the entry, and if present, replaces its `Response` with a new `TaskGraphResponse` holding the patched node. The whole operation is a `_entries.TryGetValue` + construct-new + `_entries.AddOrUpdate` under the dictionary's per-key lock. Racing `InvalidateProject` calls safely remove the entry; a racing patch + invalidate is equivalent to "patch then invalidate" — the patch is wasted but the invalidate wins, which is the correct outcome.

**Why:** Avoids introducing a separate `SemaphoreSlim` or readers-writer lock. Response objects are cheap to reconstruct (shallow copy + one node substitution).

**Alternatives considered:**

- **Mutate `Response.Nodes` in place:** `TaskGraphResponse` is consumed by clients mid-serialisation from concurrent reads. In-place mutation means a `TryGet` can observe a half-patched object. Rejected.

## Risks / Trade-offs

**[R1] Field-patch whitelist drift.** If someone adds a new field to `IssueResponse` or changes what `Status` drives in the graph layout, the whitelist can silently become wrong. → **Mitigation:** the whitelist lives next to the `IssueResponse` DTO in `Homespun.Shared` with an `[PatchableFields]` marker attribute, and a compile-time test asserts every property of `IssueResponse` is either on the whitelist or flagged `[TopologyField]`. Adding a new property without classifying it fails the test.

**[R2] Patch applied to evicted snapshot.** Between the patch call and its persistence the idle-eviction tick could remove the entry. → **Mitigation:** `PatchIssueFields` is a no-op when the entry is absent. The client's refetch, if it hits after eviction, computes fresh. Benign.

**[R3] Order-of-broadcasts with multiple mutations in quick succession.** Two near-simultaneous edits from different users hit patch N and patch N+1 back-to-back; clients may apply N+1 before N arrives if SignalR delivery reorders. → **Mitigation:** SignalR over WebSocket preserves order per-connection, and both clients are in the same project group receiving the same ordered stream. Same-user is single-client so per-message ordering is preserved. We accept the theoretical cross-user reorder risk as benign because the snapshot's `LastBuiltAt` bump is monotonic — even if patches are received out of order the final state on the server is consistent once the refresher ticks.

**[R4] SignalR payload size explosion on large patches.** Editing a description with several kilobytes of markdown ships it to every project-group subscriber. → **Mitigation:** description is already on the wire in `IssueResponse` on every refetch, so payload size is bounded by what we already accept today. Max description length is an orthogonal limit.

**[R5] Delta 2 migration ordering.** `IssuesController.UpdateIssueAsync` accepts many optional fields in one request; the decision logic for "patchable?" must be evaluated before the request is persisted. → **Mitigation:** decision is a pure function of the request DTO — evaluate once, stash in a local, route to the correct helper after the persist. Tests cover the "some whitelisted + some not" case explicitly.

**[R6] Snapshot store not registered when `TaskGraphSnapshot:Enabled=false`.** The existing kill switch leaves `IProjectTaskGraphSnapshotStore` unregistered. The hub helper depends on it. → **Mitigation:** hub helper takes `IProjectTaskGraphSnapshotStore?` (nullable) and no-ops the invalidate/patch call when null. Broadcasts still fire. Fallback path is the pre-PR-#786 synchronous compute, which remains correct.

## Migration Plan

Three PRs, each gated on the previous merging:

1. **Delta 1 PR — `taskgraph-mutation-invalidation/delta-1`:** hub helper split, call-site migrations, `FleeceIssueSyncController` + `ChangeReconciliationService` fixes, all tests. Rollout: merge → deploy → observe Seq for `snapshot.hit=false` rate (should rise from ~0 to whatever mutation rate is; confirms invalidations are firing). Rollback: revert; no schema changes.
2. **Delta 2 PR — `taskgraph-mutation-invalidation/delta-2`:** `PatchIssueFields` method, whitelist attribute + test, `UpdateIssueAsync` patch-vs-topology decision. Rollout: merge → deploy → observe `graph.snapshot.patch` span rate (should dominate for edits). Rollback: revert the whitelist-driven branching and route all edits through topology again.
3. **Delta 3 PR — `taskgraph-mutation-invalidation/delta-3`:** `IssueFieldsPatched` event, client handler, `applyPatch` helper, option binding, vitest. Rollout: flag default-on; monitor client error rate for any patch-application failures; flip flag off to fall back instantly. Rollback: either revert the PR or leave the flag off.

## Open Questions

- **Q1: Does `FleeceIssueSyncController` mutate issues even on failed pulls?** The current code only calls `ReloadFromDiskAsync` on success, but I haven't verified whether `PullFleeceOnlyAsync` can partially mutate the in-memory model on a mid-operation failure. If yes, we need invalidate-on-failure too. *Investigate during Delta 1.*
- **Q2: Does `ChangeReconciliationService` have any other caller that we haven't swept?** It's used during branch-state resolution, which fires during graph build — circular dependency if that path also invalidates during a read. *Confirm during Delta 1 before wiring the broadcast.*
- **Q3: Is there a client-side race where `setQueryData` from a Delta-3 patch lands while a TanStack refetch triggered by a concurrent `IssuesChanged` is in flight?** If the refetch completes after the patch, the refetch result overwrites the patched cache — desync. *Verify during Delta 3 implementation; may need a version check in the patch handler.*
