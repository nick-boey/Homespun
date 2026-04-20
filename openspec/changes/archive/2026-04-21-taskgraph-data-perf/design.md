## Context

`GraphController.GetTaskGraphData` → `GraphService.BuildEnhancedTaskGraphAsync` → `IssueGraphOpenSpecEnricher.EnrichAsync` is the slow path. In production the single ASP.NET request span is the only trace emitted, so the hotspot is invisible. Source inspection shows two concrete amplifiers:

1. `IssueBranchResolverService.ResolveIssueBranchAsync` falls through to `IGitCloneService.ListClonesAsync(project.LocalPath)` per visible node when an issue has no linked PR and no `WorkingBranchId`. Each call enumerates `.clones/*` and spawns ~70 `git` subprocesses (`rev-parse --abbrev-ref HEAD` + `rev-parse HEAD` per clone, plus `git for-each-ref`). With 20–30 visible nodes and ~35 clones this is 1 400–2 100 subprocess launches per request.
2. `ChangeScannerService.GetArtifactStateAsync` spawns `openspec status --change X --json` for every linked change on every `BranchStateCacheService` miss. The cache TTL is 60 s and there is no stampede guard, so concurrent first-hits each pay the full fan-out.

The `BranchStateCacheService` already exists but covers only the per-(project, branch) snapshot. It does not cover `ScanMainOrphansAsync`, the full `TaskGraphResponse`, or the `ListClonesAsync` result. There is no background refresh — every TTL expiry is paid on a user request.

Fleece cache (`EnsureCacheLoadedAsync`) is initialised once and stays warm, so the 500-issue read itself is not in the hot path; the enricher loop already iterates only `response.Nodes` (20–30 entries), not all 500 issues.

## Goals / Non-Goals

**Goals:**

- Bring `GET /api/graph/{projectId}/taskgraph/data` under 3 s P95 for the "500 issues, ~35 clones, 20–30 visible nodes" prod shape.
- Make the endpoint's internal time allocation visible in Seq via named spans.
- Eliminate `git` subprocess fan-out driven by enricher per-node branch resolution.
- Avoid re-invoking `openspec status` when nothing under the change directory changed.
- Keep the response contract unchanged — no client code touches this change.

**Non-Goals:**

- Changing the task-graph layout algorithm or what becomes a visible node.
- Paginating or splitting the response into phases.
- Moving the endpoint to SignalR push or a streaming transport. (Retained as a future option if the snapshot refresher is not enough.)
- Introducing a new persistence layer. Snapshot store is in-memory only.
- Altering Fleece.Core behaviour or upgrading its NuGet.

## Decisions

### 1. `ActivitySource`s + `CommandRunner` span wrap

Create `Homespun.Gitgraph` and `Homespun.OpenSpec` `ActivitySource`s in dedicated static holders, register them on the OTLP tracer provider in `Program.cs` and `MockServiceExtensions`. Wrap:

- `GraphService.BuildEnhancedTaskGraphAsync` → `graph.taskgraph.build`
- `GraphService` sub-phases: `graph.taskgraph.fleece.scan`, `graph.taskgraph.sessions`, `graph.taskgraph.prcache`
- `IssueGraphOpenSpecEnricher.EnrichAsync` → `openspec.enrich` with per-node child `openspec.enrich.node` (tags: `issue.id`, `branch.source`)
- `BranchStateResolverService.GetOrScanAsync` → `openspec.state.resolve` (tag `cache.hit`)
- `ChangeReconciliationService.ReconcileAsync` → `openspec.reconcile`
- `ChangeScannerService.ScanBranchAsync` → `openspec.scan.branch`
- `ChangeScannerService.GetArtifactStateAsync` → `openspec.artifact.state` (wraps subprocess)
- `IssueBranchResolverService.ResolveIssueBranchAsync` → `openspec.branch.resolve`
- `CommandRunner.RunAsync` → `cmd.run` (tags: `cmd.name`, `cmd.exit_code`, `cmd.duration_ms`)

Cardinality-safe tags only: `project.id`, `issue.id`, `change.name`, `cache.hit`, `branch.source`, `phase`. Every new span name lands in `docs/traces/dictionary.md` (existing drift check in the server test suite enforces this).

**Alternative considered:** re-use `Microsoft.AspNetCore.*` auto-instrumentation scopes. Rejected — `ActivitySource.StartActivity` at the service-method boundary gives shape; auto-instrumentation only buckets HTTP frames.

### 2. `BranchResolutionContext` (hoisted lookups)

Introduce a plain record:

```csharp
public sealed record BranchResolutionContext(
    IReadOnlyList<CloneInfo> Clones,
    IReadOnlyDictionary<string, string> PrBranchByIssueId);
```

`GraphService.BuildEnhancedTaskGraphAsync` constructs it once (single `ListClonesAsync` + single `GetPullRequestsByProject` projection) and passes it to a new overload `IssueBranchResolverService.ResolveIssueBranchAsync(projectId, issueId, BranchResolutionContext)`. The existing `IIssueBranchResolverService.ResolveIssueBranchAsync(projectId, issueId)` remains for callers outside the hot path (issue detail, agent start) and internally calls the new overload with a freshly-built context.

**Alternative considered:** cache `ListClonesAsync` result inside `GitCloneService` with a short TTL. Rejected — the cache invalidation story for clones is already handled by explicit add/remove calls; layering another TTL on top invites staleness bugs elsewhere. Hoisting to request scope is simpler and eliminates the fan-out without changing semantics.

### 3. Artifact-state micro-cache (mtime-keyed)

`ChangeScannerService.GetArtifactStateAsync` gains an in-memory cache:

- Key: `(clonePath, changeName, mtimeTuple)` where `mtimeTuple` is the hash of last-write times for `proposal.md`, `tasks.md`, and the `specs/` subtree root.
- Value: the deserialised `ChangeArtifactState`.
- Lifetime: process lifetime, bounded by project count × changes per project. Entries are not explicitly evicted — mtime changes make old entries unreachable by key.

**Alternative considered:** Content-hash key. Rejected — reading files to hash them is O(size) and defeats the purpose.

### 4. Per-project snapshot + background refresher

New `IProjectTaskGraphSnapshotStore` keyed by `(projectId, maxPastPRs)` → `TaskGraphResponse`. New `ITaskGraphSnapshotRefresher` `IHostedService` that:

- Maintains a set of "tracked" projects (added on first taskgraph/data request).
- Refreshes each tracked project's snapshot on a configurable interval (default 10 s) and on explicit invalidation.
- Uses a per-project `SemaphoreSlim` so concurrent refresh requests coalesce.

Endpoint handler changes:

- On first call for a project: compute synchronously (single-thread fills the cache), store the snapshot, mark project tracked, return.
- On subsequent calls: return the stored snapshot, touch `lastAccessedAt`.
- Snapshot older than 2 × refresh interval → treated as cold, recomputed synchronously.
- `POST /api/graph/{projectId}/refresh` triggers immediate snapshot invalidation and forces the next call to rebuild (preserves existing refresh semantics).

Eviction policy: if a project has not been accessed for `N × refresh interval` (default 5 min), drop from the tracked set.

**Alternatives considered:**
- **Response-level TTL cache inside the endpoint**: simpler but pays the slow path on the first request after each TTL expiry — exactly the project-switch case.
- **Two-phase delivery (SignalR push)**: larger surface, new client plumbing. Kept as a fallback if the snapshot approach still misses <3 s on cold project switches in prod.

### 5. Log hygiene on the hot path

`BuildEnhancedTaskGraphAsync` currently emits three `LogInformation` calls inside the request: session count, valid-session count, joined entity-id list. Demote all three to `LogDebug` and drop the `string.Join(", ", sessionsByEntityId.Keys.Select(k => $"'{k}'"))` allocation. Same treatment for the per-node "Matched session for issue" info log.

### 6. Mock wiring parity

Register `ActivitySource`s, artifact-state micro-cache, snapshot store, and the snapshot refresher hosted service in `MockServiceExtensions.AddMockServices` with the same lifetimes as production. The refresher's interval becomes a configurable option (`TaskGraphSnapshot:RefreshIntervalSeconds`) so tests can pin it.

## Risks / Trade-offs

- **Stale data visible to users** → Mitigation: refresh interval defaults to 10 s and any mutation (sidecar write, agent edit landed via git, explicit refresh endpoint) invalidates. Client already tolerates `openSpecStates` being incomplete (see `task-graph-view.tsx` uses `?.[issueId] ?? null`).
- **Memory pressure from per-project snapshots** → Mitigation: eviction after 5 min of no access. Payload is small (20–30 nodes + a handful of PRs + a dictionary of OpenSpec states).
- **Mtime-keyed micro-cache incorrectness if a tool modifies files without updating mtime** → Mitigation: the OpenSpec CLI and editors both touch mtimes. For the one edge case (manual `touch -t` or filesystem clock skew) the only consequence is a stale artifact state; the snapshot refresher re-reconciles on its next tick. Not a correctness risk beyond the existing 60 s `BranchStateCacheService` TTL.
- **New `ActivitySource`s without dictionary updates break CI drift check** → Mitigation: tasks.md includes explicit "update `docs/traces/dictionary.md`" step, and the drift check already exists.
- **Background refresh competing with session-event SignalR broadcasts for CPU** → Mitigation: snapshot refresh is single-threaded per project; configurable interval; `CancellationToken` plumbed through so graceful shutdown drains.
- **`BranchResolutionContext` drifts out of sync if call-site changes** → Mitigation: new overload is the canonical one; existing zero-context method is kept as a thin wrapper that allocates a one-off context. Enforced by tests.

## Migration Plan

1. Land Tier-1 spans alone. Run in prod one day. Read the trace in Seq to confirm the fan-out hypothesis before further changes.
2. Land the hoisted `BranchResolutionContext` + log hygiene together (small, low-risk).
3. Land the artifact-state micro-cache.
4. Land the snapshot refresher. Ship with `TaskGraphSnapshot:Enabled=false` and flip the flag after a soak.
5. Rollback: flip `TaskGraphSnapshot:Enabled=false` (endpoint falls back to synchronous build). Revert the micro-cache by deleting the `ConcurrentDictionary` wrap — no persistent state.

## Open Questions

- Should the snapshot refresher also cover the "cached" and "incremental refresh" variants of the graph endpoint, or only taskgraph/data?
- Is the OpenSpec CLI actually installed inside the production container (`Dockerfile.base` ships `@fission-ai/openspec@latest`, confirmed)? Verify `which openspec` inside the running pod as part of Tier 1 rollout so failure mode is understood.
- Interval tuning — 10 s is a guess. Reconsider once we have prod span data.
