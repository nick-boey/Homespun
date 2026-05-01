## Context

PRs #802 and #810 inverted a working architecture. Pre-#802 the frontend owned a 1148-line layout engine (`src/Homespun.Web/src/features/issues/services/task-graph-layout.ts`) that consumed raw issue data, computed lanes/rows/edge geometry, and rendered with arc-cornered orthogonal connectors. Post-#810, layout lives in Fleece.Core's `IIssueLayoutService` on the server; the frontend is a 252-line passthrough that consumes pre-positioned `TaskGraphResponse` data and renders strictly rectilinear edges.

The new architecture's costs have accumulated:

1. **Snapshot/invalidate machinery** (`IProjectTaskGraphSnapshotStore` + `ITaskGraphSnapshotRefresher` + `BroadcastIssueTopologyChanged` invalidation hooks across ~12 server call sites) exists solely to amortise the ~3s Fleece layout cost. PR #793 added the "patch in place vs full invalidate" split (`BroadcastIssueFieldsPatched`) to mitigate the cost for non-topology edits but topology mutations remain on the slow path.
2. **Dynamic insert race**: when a client creates an issue, the server invalidates → kicks fire-and-forget `RefreshOnceAsync` → broadcasts `IssuesChanged`. The client's refetch can hit the server before the rebuild completes and receive stale layout. The user has explicitly called this out as the precipitating concern.
3. **Curved edges regressed**. The pre-#802 renderer drew arc-cornered orthogonal connectors that survive in the user's memory as "nicer". The post-#810 renderer's `buildEdgePath` produces hard-cornered paths.
4. **Two SignalR event types** (`IssuesChanged` topology, `IssueFieldsPatched`) plus the `PatchableFieldAttribute` reflection that decides which one to fire is non-trivial machinery defending against the cost of full invalidation.

The principle behind this design: **the cost being optimised — server-side layout computation — only exists because the server does the layout.** Move layout to the client and the entire optimisation chain collapses.

## Goals / Non-Goals

**Goals:**

- Eliminate the snapshot/invalidate race window that produces stale layouts after issue creation.
- Restore arc-cornered orthogonal edge rendering.
- Collapse two SignalR events + reflection-driven attribute machinery into a single uniform `IssueChanged` event.
- Make tree↔next mode toggle a zero-network-roundtrip client transformation.
- Keep server-side filtering responsibility narrow (active-status + ancestor-of-open + explicit overrides).

**Non-goals:**

- Pagination of issues (deferred — flagged `// TODO: revisit if N > 2000`).
- Server-side rendering of any graph view (text view is deleted, not relocated).
- Replacing Fleece.Core as a dependency. The C# library remains the reference algorithm; the TS port is a *parallel implementation* validated by golden fixtures.
- Reworking the OpenSpec enrichment, branch-status indicators, or PR sync pipelines beyond the minimum needed to migrate them off the snapshot store onto the new endpoint's response.
- Changes to the Fleece-CLI / `.fleece/` storage format or to the issue mutation endpoints' bodies.
- Reworking the `agent-dispatch` or `claude-agent-sessions` SignalR channels — they are separate hubs with their own events.

## Decisions

### D1: Server filter rule — open OR ancestor-of-open OR explicitly-requested

**Rule (precise):**

```
keep(issue) =
    status(issue) ∈ {Draft, Open, Progress, Review}
  ∨ ∃ open descendant of issue (transitively)
  ∨ id(issue) ∈ explicitInclude
  ∨ ∃ descendant of issue with id ∈ explicitInclude
  ∨ (includeOpenPrLinked ∧ id(issue) ∈ openPrLinkedIds)
  ∨ ∃ descendant of issue with id ∈ openPrLinkedIds (when includeOpenPrLinked)
```

Equivalently: start with the union of (a) all open issues, (b) all explicit-include ids, (c) all open-PR-linked ids if that flag is set; then walk every parent chain to root, keeping every node visited; return the visited set.

**Algorithm:**

```
seeds = openIssues ∪ explicitInclude ∪ (includeOpenPrLinked ? openPrLinkedIds : ∅)
visited = ∅
queue = seeds (deduplicated)
while queue not empty:
    issue = queue.pop()
    if issue.id ∈ visited: continue
    visited.add(issue.id)
    for parent in issue.parents:
        if parent ∉ visited:
            queue.push(parent)
return { issues with id ∈ visited }
```

Visited-set membership before parent enqueue gives O(N) worst case and inherent cycle safety.

**Edge cases handled:**

- Cycles in the parent chain: visited set prevents infinite loops.
- Multi-parent issues: every parent chain is walked; the issue is visited once but all ancestors via all chains are pulled in.
- Orphan closed issue (no children, no open status, not in include list): correctly dropped.
- Closed issue between two open issues in a parent chain: kept (it's an ancestor of an open issue).

**Rejected alternatives:**

- *Reuse Fleece.Core's `LayoutForTree(InactiveVisibility.Hide)` and discard the layout, keeping only the visible node set.* Rejected: pulls a heavy algorithm in for a 30-line traversal. Couples server filter behaviour to Fleece's filtering semantics, which include nuances around `InactiveVisibility.Fade` and `Show` modes that the new architecture doesn't need.
- *Dumb server (return all issues, client filters everything).* Rejected: projects with thousands of historical closed issues would over-fetch by orders of magnitude. The active-status filter is cheap and cuts payload meaningfully.
- *Move filtering to the client too, but expose a paginated raw-issues endpoint.* Rejected: pagination is explicitly deferred and the active-status filter is the natural seam.

### D2: Unified `IssueChanged` event with idempotent client merge

**Wire shape:**

```typescript
type IssueChangedEvent = {
  projectId: string
  kind: 'created' | 'updated' | 'deleted'
  issueId: string
  issue?: Issue  // present on 'created' and 'updated'; absent on 'deleted'
}
```

**Server contract:** every issue mutation path (create / update / delete / set-parent / remove-parent / move-sibling / accept-changes / undo / redo / cancel-issues-agent-session / fleece-sync pull/sync / change-reconciliation auto-link/auto-archive / clone create/delete/prune / PR merge/close transition) calls a single `BroadcastIssueChanged(projectId, kind, issueId, issue?)` extension method on `IHubContext<NotificationHub>`. The method serialises the event and emits to all clients on the project group.

**Client contract:** the SignalR handler unconditionally applies the event to the local issue cache:

```typescript
function applyIssueChanged(cache: Map<string, Issue>, event: IssueChangedEvent): Map<string, Issue> {
  const next = new Map(cache)
  if (event.kind === 'deleted') {
    next.delete(event.issueId)
  } else if (event.issue !== undefined) {
    next.set(event.issueId, event.issue)
  }
  return next
}
```

**Echo handling (chosen pattern: idempotent merge):**

When a local mutation completes, two writes hit the cache:

1. The `POST /api/issues` response body, applied directly by the calling component (or by TanStack Query's `onSuccess`).
2. The SignalR `IssueChanged` echo for the same mutation, applied by the global handler.

Replace-by-id is idempotent: applying the same canonical issue twice is a no-op. No request-ID dedup, no echo-suppression. Order doesn't matter — the canonical issue from either path produces the same end state. Counts as one extra render frame in the worst case; not measurable in practice.

**Rejected:** request-ID echo-suppression (extra plumbing, no observable gain) and "wait for echo only" (perceived lag on every local edit).

### D3: Decoration channels stay separate from `IssueChanged`

Agent statuses, linked-PR data, and OpenSpec states have independent update cadences (agent statuses tick every few seconds during a session; PR data refreshes on GitHub webhook; OpenSpec state changes when an agent commits artefacts). Folding them into `IssueChanged` would force a redundant issue payload on every agent-status tick and couple cadences arbitrarily.

**Plan:** the existing per-decoration SignalR channels stay. The decoration types live in sibling caches in the client (one TanStack Query key per decoration), joined to the issue cache at render time. Initial hydration is per-endpoint (D8) — the issue endpoint and each decoration endpoint are independently fetched. Subsequent updates flow through the existing per-decoration SignalR channels.

### D4: TS layout port — module structure and surface

```
src/Homespun.Web/src/features/issues/services/layout/
  index.ts                       // public exports
  graph-layout-service.ts        // GraphLayoutService<TNode> generic
  issue-layout-service.ts        // LayoutForTree, LayoutForNext entry points
  edge-router.ts                 // OccupancyCell, edge walk, pivot routing
  types.ts                       // shared types
  graph-layout-service.test.ts   // algorithmic unit tests (generic)
  issue-layout-service.test.ts   // algorithmic unit tests (issue-aware)
  edge-router.test.ts            // edge geometry tests
  golden-fixtures.test.ts        // cross-stack diff against C# reference
  fixtures/
    *.input.json                 // issue set inputs
    *.expected.json              // C#-emitted reference outputs
```

**Public surface (mirrors Fleece.Core v3):**

```typescript
// types.ts
export type LayoutMode = 'issue-graph' | 'normal-tree'
export type ChildSequencing = 'series' | 'parallel'
export type EdgeKind = 'series-sibling' | 'series-corner-to-parent' | 'parallel-child-to-spine'
export type EdgeAttachSide = 'top' | 'bottom' | 'left' | 'right'

export interface PositionedNode<T> {
  node: T
  row: number
  lane: number
  appearanceIndex: number   // 1-based
  totalAppearances: number
}

export interface Edge<T> {
  fromId: string
  toId: string
  kind: EdgeKind
  startRow: number
  startLane: number
  endRow: number
  endLane: number
  pivotLane: number | null
  sourceAttach: EdgeAttachSide
  targetAttach: EdgeAttachSide
}

export interface GraphLayout<T> {
  nodes: PositionedNode<T>[]
  edges: Edge<T>[]
  totalRows: number
  totalLanes: number
}

export type GraphLayoutResult<T> =
  | { ok: true; layout: GraphLayout<T> }
  | { ok: false; cycle: string[] }

// issue-layout-service.ts
export function layoutForTree(
  issues: Issue[],
  options?: { assigneeFilter?: string; sortConfig?: GraphSortConfig; layoutMode?: LayoutMode }
): GraphLayoutResult<Issue>

export function layoutForNext(
  issues: Issue[],
  matchedIds: ReadonlySet<string>,
  options?: { assigneeFilter?: string; sortConfig?: GraphSortConfig }
): GraphLayoutResult<Issue>
```

**Note on `InactiveVisibility`:** Fleece.Core v3's API takes an `InactiveVisibility` parameter (`Hide` | `Show` | `Fade`). The TS port omits this parameter — the server pre-filter already drops inactive issues that aren't ancestors of open ones, so all issues arriving at the layout *should* be rendered. Closed issues kept as ancestors of open ones are visually faded by the renderer (CSS), not by the layout.

This is a deliberate scope reduction. If a future requirement needs full visibility-mode parity (e.g. "show all closed issues on demand"), the parameter can be reintroduced — the algorithm doesn't change, only the filter.

### D5: Golden fixtures — cross-stack diff strategy

**Why:** the TS port duplicates an algorithm that lives in a separately-versioned external library. Drift detection at upgrade time matters more than internal-refactor protection — algorithmic unit tests catch refactor regressions but not "Fleece v4 changed the lane-assignment tie-breaker".

**How:**

```
tests/Homespun.Web.LayoutFixtures/
  Homespun.Web.LayoutFixtures.csproj  // references Fleece.Core
  EmitFixturesTests.cs                // [Test, Category("Fixtures")]
                                      //   reads fixtures/*.input.json
                                      //   runs IIssueLayoutService.LayoutForTree/LayoutForNext
                                      //   writes fixtures/*.expected.json
  fixtures/
    01-tree-simple.input.json
    01-tree-simple.expected.json
    02-tree-multi-parent.input.json
    02-tree-multi-parent.expected.json
    03-tree-series-parallel.input.json
    ...
    20-next-large.input.json
    20-next-large.expected.json
```

**Workflow:**

1. Author writes a new `*.input.json` covering a graph topology of interest.
2. Run `dotnet test --filter Category=Fixtures /p:UpdateFixtures=true` to emit the corresponding `.expected.json`. Without `UpdateFixtures=true`, the test is read-only and asserts the existing fixture matches the live engine output (catches accidental drift in either direction).
3. The TS test (`golden-fixtures.test.ts`) loads the input, runs the TS port, and structurally diffs against the expected output. Same input → same nodes/edges/dimensions.

**On Fleece.Core upgrade:** run with `UpdateFixtures=true`. If any fixture changes, the TS port must be updated to match before the upgrade can ship. The diff highlights exactly what changed in the algorithm.

**Initial fixture coverage (target ≥10):** simple tree, deep tree, multi-parent diamond, series chain, parallel children, mixed series/parallel siblings, single-leaf cycle (returns `{ok: false}`), empty input, single-node, large (>200 nodes from a real project export). Plus equivalents for `LayoutForNext` covering matched-leaves-pull-ancestors behaviour.

### D6: Edge rendering — arc-cornered orthogonal

**Geometry primitive:** for each edge with start `(sx, sy)`, end `(ex, ey)`, optional pivot lane `pivotX`, the path replaces hard corners with quarter-circle arcs of radius `r = min(6, halfLaneWidth, halfRowHeight)`.

**Three edge kinds:**

```
SeriesSibling (sibling-to-sibling, vertical):
  M sx,sy
  L sx, ey - r            (vertical run, stop r short)
  Q sx,ey  ex,ey          (quarter-arc into target attach)
  (or in pure-vertical case: just M sx,sy L ex,ey)

SeriesCornerToParent (last-child-to-parent, vertical-then-horizontal):
  M sx,sy
  L sx, ey - r
  Q sx,ey  sx + r·sign(ex-sx), ey
  L ex,ey
  (corner radius applied at the bend)

ParallelChildToSpine (horizontal-to-pivot-then-vertical):
  M sx,sy
  L pivotX - r·sign(pivotX-sx), sy
  Q pivotX, sy  pivotX, sy + r·sign(ey-sy)
  L pivotX, ey - r·sign(ey-sy)
  Q pivotX, ey  pivotX + r·sign(ex-pivotX), ey
  L ex,ey
  (two corners: turn into spine, turn off spine)
```

Lane fidelity is preserved — every path stays axis-aligned except at corners. The arcs are small enough (~6px) that the visual character matches the user's recollection of the pre-#802 renderer.

**Storybook coverage:** one story per `EdgeKind` + one story showing corners under different lane/row spacings.

### D8: Un-bundled decoration endpoints

The first draft of this design bundled all decorations into a single `IssueSetResponse` returned by `GET /api/projects/{projectId}/issues`. Reviewer feedback inverted that: each decoration becomes its own endpoint. The frontend assembles the view by issuing parallel calls and joining them at render time.

```
   GET /api/projects/{projectId}/issues
       ?include=...&includeOpenPrLinked=...&includeAll=...
       → Issue[]                                                 ◄ no decorations

   GET /api/projects/{projectId}/linked-prs
       → Dictionary<issueId, LinkedPr>

   GET /api/projects/{projectId}/agent-statuses
       → Dictionary<issueId, AgentStatusData>

   GET /api/projects/{projectId}/openspec-states
       → Dictionary<issueId, IssueOpenSpecState>

   GET /api/projects/{projectId}/orphan-changes
       → List<SnapshotOrphan>

   GET /api/projects/{projectId}/pull-requests/merged?max=N         ◄ already exists, reuse
       → List<MergedPr>
```

**Why un-bundle:**

- **Testability.** Each endpoint is fixture-able in isolation. An API integration test for `/agent-statuses` doesn't need to seed a working tree of OpenSpec changes; an integration test for `/openspec-states` doesn't need agent sessions in flight.
- **Independent cadences.** Agent statuses tick every few seconds during active sessions; PR data refreshes on GitHub webhook; OpenSpec states change when artefacts are written. A single bundled endpoint is computed at the cadence of its slowest input. Per-endpoint fetches let each decoration set its own React Query staleTime.
- **Independent invalidation.** SignalR events that affect only agent statuses can target the `['agent-statuses', projectId]` query key; events affecting only PRs target `['linked-prs', projectId]`. No need for "this event affects which decorations?" routing logic.
- **Smaller payloads.** A view that doesn't need OpenSpec states (e.g. a future "issues only" view) doesn't pay for them.

**Frontend assembly pattern:**

The `task-graph-view.tsx` consumes a tuple of hooks:

```typescript
const { data: issues } = useIssues(projectId, { includeOpenPrLinked: true })
const { data: linkedPrs } = useLinkedPrs(projectId)
const { data: agentStatuses } = useAgentStatuses(projectId)
const { data: openSpecStates } = useOpenSpecStates(projectId)
const { data: orphanChanges } = useOrphanChanges(projectId)
const { data: mergedPrs } = useMergedPrs(projectId, { max: 5 })
// ...all six are independent queries, fetched in parallel
```

Each hook subscribes to its own SignalR channel for live updates. The layout-driving wrapper in `task-graph-layout.ts` runs once `issues` is available; decoration data join at render time and trigger only re-renders, not re-layouts.

**Cost:**

- 6 round-trips at view load instead of 1. HTTP/2 multiplexing makes this cheap. Browser parallelism handles them concurrently.
- The `IIssueGraphOpenSpecEnricher` was a single pass that scanned all clones once and produced both per-issue states and main-branch orphans. Splitting into two endpoints (`/openspec-states` and `/orphan-changes`) means two scans per view load. Mitigation: the existing artifact-state mtime cache amortises repeat scans of unchanged clones — both endpoints hit the cache when the underlying clone state hasn't changed. Worst case (cold cache, large project): ~200ms duplicated work. Acceptable given the testability win.

**SignalR channel mapping (existing channels reused):**

```
   issue mutations (create/update/delete)        → IssueChanged event
                                                  → invalidate ['issues', projectId]

   agent status changes (already exists)         → AgentStatusChanged event
                                                  → invalidate ['agent-statuses', projectId]

   PR sync changes (already exists)              → PullRequestChanged event
                                                  → invalidate ['linked-prs', projectId]
                                                  → invalidate ['merged-prs', projectId]

   OpenSpec artifact changes                     → OpenSpecChanged event (NEW or existing?)
                                                  → invalidate ['openspec-states', projectId]
                                                  → invalidate ['orphan-changes', projectId]
```

The OpenSpec channel may need to be added if it doesn't exist today — current OpenSpec state changes piggyback on `IssuesChanged`-driven graph rebuilds. Verified during Task 5.1.x.

### D7: Migration sequence within the change

Implementation order matters because the change is large. The sequence preserves a working app at each commit boundary.

```
Phase A — Server: add new endpoint alongside the old one
  A.1  IssueAncestorTraversalService unit tests + impl
  A.2  IssueSetResponse DTO
  A.3  IssuesController.GetVisibleIssues + integration tests
  A.4  Decoration joins (agent statuses, PRs, OpenSpec, mergedPrs)
  A.5  Old endpoints still serve the old DTO; both paths work.

Phase B — Web: TS layout port + tests, no UI integration yet
  B.1  Layout fixture-emitter project (.NET)
  B.2  Initial fixtures (≥10) committed
  B.3  TS port modules + algorithmic unit tests
  B.4  Cross-stack golden-fixture test
  B.5  Storybook entries for edge rendering preview

Phase C — Web: useIssues hook + view migration
  C.1  useIssues hook against new endpoint
  C.2  task-graph-layout.ts rewritten over the layout module
  C.3  task-graph-svg.tsx rewritten with arc corners
  C.4  task-graph-view.tsx switches to useIssues
  C.5  apply-patch.ts deleted; SignalR handler simplifies
  C.6  Tree↔Next ViewMode toggle becomes pure client transform

Phase D — Server: collapse SignalR events + delete old surface
  D.1  BroadcastIssueChanged unified extension
  D.2  Migrate every call site (~12 sites)
  D.3  Delete BroadcastIssueTopologyChanged + BroadcastIssueFieldsPatched
  D.4  Delete PatchableFieldAttribute + IssuesController reflection
  D.5  Delete IProjectTaskGraphSnapshotStore + ITaskGraphSnapshotRefresher + DI
  D.6  Delete GraphController endpoints + GraphService.BuildEnhancedTaskGraphAsync
  D.7  Delete ProjectFleeceService.GetTaskGraphWithAdditionalIssuesAsync
  D.8  Delete TaskGraphResponse + related DTOs
  D.9  Delete obsoleted server tests

Phase E — Verification
  E.1  Full test suite passes (dotnet test + npm test + storybook build)
  E.2  E2E playwright: dynamic-insert smoke test
  E.3  Manual smoke: dev-mock + dev-live profiles
  E.4  Docs updated; CLAUDE.md aligned
```

**Single PR:** all phases ship in one merge. Splitting Phase C from Phase D would leave `IssueFieldsPatched` events firing pointlessly at the new client, which is harmless but confusing.

## Risks / Trade-offs

**R1: Fleece.Core upgrade drift.** The TS port goes stale silently if Fleece v4 changes algorithm internals.
*Mitigation:* golden fixtures (D5) — the read-only fixture test fails on upgrade, forcing the port to be updated before the upgrade lands. Document the "regenerate fixtures + update port" workflow in `docs/graph-layout-client-side.md`.

**R2: Layout cost on low-end clients.** A 500-issue project on a phone in next-mode runs the layout algorithm client-side on every render input change.
*Mitigation:* memoise layout output in the `task-graph-layout.ts` wrapper keyed by `(issueSetVersion, viewMode, sortConfig, assigneeFilter)`. Pre-#802 layout engine was 1148 lines and ran fine on the same hardware. Worst-case measured budget: <100ms layout time on a mid-2020 mobile device for 500 issues. Flagged with `// TODO: revisit if N > 2000`.

**R3: Idempotent merge produces an extra render frame.** Local mutation applies POST response, then SignalR echo arrives and applies the same data.
*Mitigation:* React 18's automatic batching collapses adjacent state updates within the same tick. In practice the echo arrives 1–10ms after the response and the second update is a no-op equality check by TanStack Query. Negligible.

**R4: Deleting the text endpoint breaks unknown CLI consumers.** `GET /api/graph/{projectId}/taskgraph` (text) is presumed unused but not formally tracked.
*Mitigation:* Task 1.3 grep the entire codebase, the Worker repo, and any CI scripts before deletion. If a consumer is found, decide whether to keep the endpoint (and have it call Fleece.Core's text export directly, bypassing the now-deleted snapshot infrastructure) or migrate the consumer.

**R5: Big PR review burden.** Phases A–D produce a wide diff (~50 files, ~3000 LOC delta).
*Mitigation:* the phased commit history within the single PR lets reviewers grok one phase at a time. Each phase passes tests independently, providing natural review checkpoints. Reviewer guidance in the PR description maps phases to file paths.

**R6: Cycle UX regression risk.** Today the server returns null on cycle and the UI silently shows nothing. The TS port's `GraphLayoutResult<T>` discriminated union surfaces `{ ok: false, cycle: string[] }`; the renderer must handle this.
*Mitigation:* the layout-driving wrapper in `task-graph-layout.ts` converts `{ ok: false }` into a banner state; render lines come from a degraded-mode fallback (flat list of issues, no edges). Strict improvement over current silent-null behaviour. Covered by an algorithmic unit test and a Storybook entry.

## Open Questions

- **Q1 [RESOLVED]:** `GET /api/projects/{projectId}/issues` already exists on `IssuesController.GetByProject` (line 45). Currently returns the unfiltered list with optional `status` / `type` / `priority` query-param filters. **Resolution:** keep the route, change default behavior to apply visible-set filter (open + ancestors-of-open + explicit-include + open-PR-linked when flagged). Add `includeAll=true` query param as a backwards-compatible escape hatch for any caller that needs the raw list. Existing `status` / `type` / `priority` params apply *after* visibility filtering. The endpoint returns `Issue[]` only — decorations are un-bundled per D8.
- **Q2 [RESOLVED]:** Decorations (`linkedPrs`, `agentStatuses`, `openSpecStates`, `orphanChanges`, `mergedPrs`) move to **independent endpoints** per D8. The frontend assembles via parallel TanStack Query hooks; each decoration has its own SignalR channel for live updates and its own React Query staleTime.
- **Q3:** Does the `useIssues` hook (and each decoration hook) handle SignalR reconnect by refetching? Yes — each hook subscribes to its connection's `onreconnected` event and invalidates its query key. Stale-time on each query is `Infinity` while connected. Implementation in Task 5.1.
