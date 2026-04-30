## Why

`Fleece.Core` v3.0.0 ships a new generic graph-layout engine (`IGraphLayoutService<TNode>`) and a Fleece-specific adapter (`IIssueLayoutService`) that produces a richer `GraphLayout<Issue>` containing positioned nodes, semantic edges (`Edge<Issue>` with `EdgeKind`, `Start`/`End`, `PivotLane`, `SourceAttach`/`TargetAttach`), and a per-cell occupancy matrix. The v2 surface that Homespun is built on (`IFleeceService.BuildFilteredTaskGraphLayoutAsync` returning `TaskGraph` + `TaskGraphNode`) is **removed in v3 with no compatibility shim** — every consumer must migrate to the new types.

Homespun's contact with the removed APIs is a single call site (`ProjectFleeceService.GetTaskGraphWithAdditionalIssuesAsync`), but the surrounding 280-line `TaskGraphTextRenderer` and the 1148-line frontend `task-graph-layout.ts` were written precisely *because* the v2 type lacked semantic edge information. Both modules hand-roll connector geometry by walking `Issue.ParentIssues` and re-deriving series/parallel relationships. With v3's `Edge<Issue>` carrying `EdgeKind` + `SourceAttach`/`TargetAttach` directly, this re-derivation can move out of Homespun entirely. The migration is the natural moment to push that authoritative geometry over the wire and thin both renderers.

A parallel motivation: we are skipping v2.2.0 (removed broken TUI, removed external editor fallback from create/edit — CLI-only changes) and v2.2.1 (CLI DI fix). Neither affects how Homespun consumes `IFleeceService`, so jumping straight to v3.0.0 is safe.

## What Changes

Three deltas, sequenced inside one change so the wire format is migrated atomically with the producers and consumers.

- **Delta 1 — Server migration (foundation).** Bump `Fleece.Core` 2.1.1 → 3.0.0 in `Homespun.Server.csproj` + `Homespun.Shared.csproj`; bump `Fleece.Cli` 2.1.1 → 3.0.0 in `Dockerfile.base`. Register `IGraphLayoutService` + `IIssueLayoutService` in DI. Rewrite `ProjectFleeceService.GetTaskGraphWithAdditionalIssuesAsync` to use `IIssueLayoutService.LayoutForTree` (input: `active ∪ open-PR-linked-terminal` issues, `InactiveVisibility.Hide`) — preserves v2 byte-equivalent behaviour. Update `IProjectFleeceService` return type from `Fleece.Core.Models.TaskGraph?` to `Fleece.Core.Models.Graph.GraphLayout<Issue>?`. Update `GraphService.BuildEnhancedTaskGraphAsync` mapper to consume `PositionedNode<Issue>` + `Edge<Issue>`. Migrate the test fixtures in `tests/Homespun.Tests/Features/Gitgraph/` and `tests/Homespun.Tests/Features/Observability/` that hand-construct `TaskGraph` / `TaskGraphNode`.

- **Delta 2 — Wire format extension.** Extend `TaskGraphResponse` (in `Homespun.Shared/Models/Fleece/TaskGraphDto.cs`) with two new collections — `Edges: TaskGraphEdgeResponse[]` (carrying `From`, `To`, `Kind`, `StartRow`, `StartLane`, `EndRow`, `EndLane`, `PivotLane?`, `SourceAttach`, `TargetAttach`) and `TotalRows: int`. Existing `Nodes` list keeps `Lane` / `Row` / `IsActionable` populated for backwards compatibility; new fields are populated unconditionally. Regenerate the OpenAPI client (`npm run generate:api:fetch`).

- **Delta 3 — Frontend rewrite.** Replace the hand-rolled connector inference in `src/Homespun.Web/src/features/issues/services/task-graph-layout.ts` with a thin adapter that emits one render line per `PositionedNode` and consumes the server's `Edges[]` directly for connector rendering. Drop `drawTopLine`, `drawBottomLine`, `seriesConnectorFromLane`, `isSeriesChild`, `isFirstChild`, `parentLane`, `parentLaneReservations`, `drawLane0Connector`, `isLastLane0Connector`, `drawLane0PassThrough`, `lane0Color`, `hasHiddenParent`, `hiddenParentIsSeriesMode`, `multiParentIndex`, `multiParentTotal`, `isLastChild`, `hasParallelChildren` from `TaskGraphIssueRenderLine`. Rewrite `task-graph-svg.tsx`'s connector layer to switch on `EdgeKind` × `(SourceAttach, TargetAttach)`. Keep PR-row, separator-row, and orphan-aggregation synthesis (Homespun-only concerns) intact. Net change: ~750 LOC removed, ~250 LOC added across three files.

- **Delta 4 — Cleanup.** Rewrite `TaskGraphTextRenderer.cs` (server-side text renderer for `BuildTaskGraphTextAsync`) on top of `GraphLayout.Edges` + `OccupancyCell` matrix. Remove the hand-rolled connected-component BFS in `GroupNodes` and the parent-edge map in `RenderGroup`.

## Capabilities

### New Capabilities

None. This change extends the existing `fleece-issue-tracking` capability with new requirements covering the v3 layout engine integration and the wire-format additions.

### Modified Capabilities

- `fleece-issue-tracking`: adds two requirements covering (a) the use of `Fleece.Core.IIssueLayoutService` as the single source of layout truth and (b) the addition of semantic-edge fields to `TaskGraphResponse` + the obligation that the frontend renders connectors from those server-supplied edges rather than re-deriving them.

## Impact

**Server code:**

- `Homespun.Server.Features.Fleece.Services.ProjectFleeceService` — rewrite `GetTaskGraphWithAdditionalIssuesAsync` to call `IIssueLayoutService.LayoutForTree` (~30 LOC changed).
- `Homespun.Server.Features.Fleece.Services.IProjectFleeceService` — return type change from `TaskGraph?` to `GraphLayout<Issue>?`.
- `Homespun.Server.Features.Gitgraph.Services.GraphService` — update `BuildTaskGraphAsync` and `BuildEnhancedTaskGraphAsync` to consume `GraphLayout<Issue>` + emit edges into `TaskGraphResponse` (~50 LOC).
- `Homespun.Server.Features.Gitgraph.Services.GitgraphApiMapper` — new mapper `MapEdge(Edge<Issue>) → TaskGraphEdgeResponse` (~30 LOC).
- `Homespun.Server.Features.Gitgraph.Services.TaskGraphTextRenderer` — rewrite from 280 LOC (v2-driven) to ~80 LOC (edge-driven).
- `Homespun.Server.Program` — register `IGraphLayoutService` + `IIssueLayoutService` (~5 LOC).

**Shared contracts:**

- `Homespun.Shared.Models.Fleece.TaskGraphResponse` — add `Edges: List<TaskGraphEdgeResponse>` + `TotalRows: int`.
- New DTO `TaskGraphEdgeResponse` in `Homespun.Shared.Models.Fleece.TaskGraphDto.cs` mirroring `Fleece.Core.Models.Graph.Edge<Issue>` shape.

**Client code:**

- `src/Homespun.Web/src/api/generated/*` — regenerated from OpenAPI.
- `src/Homespun.Web/src/features/issues/services/task-graph-layout.ts` — drop connector-inference fields from `TaskGraphIssueRenderLine`, drop `parentLaneReservations` / `multiParent*` / `lane0*` post-passes, rewrite `computeLayout` as a thin pass-through.
- `src/Homespun.Web/src/features/issues/components/task-graph-svg.tsx` — rewrite connector layer to switch on `kind × (sourceAttach, targetAttach)` for path geometry.
- `src/Homespun.Web/src/features/issues/services/task-graph-layout.test.ts` — rebase tests on the simpler shape; existing render-output assertions hold via the e2e snapshot suite.

**Build / infra:**

- `src/Homespun.Server/Homespun.Server.csproj` — `Fleece.Core` 2.1.1 → 3.0.0.
- `src/Homespun.Shared/Homespun.Shared.csproj` — `Fleece.Core` 2.1.1 → 3.0.0.
- `Dockerfile.base` line 62 — `Fleece.Cli` 2.1.1 → 3.0.0.

**Tests:**

- `tests/Homespun.Tests/Features/Gitgraph/GraphServiceOpenPrIssueTests.cs` — replace `new TaskGraph { Nodes = [new TaskGraphNode { … }] }` with `new GraphLayout<Issue> { Nodes = [new PositionedNode<Issue> { … }], Edges = [], … }` (5 occurrences).
- `tests/Homespun.Tests/Features/Gitgraph/TaskGraphTextRendererTests.cs` — same migration, plus new assertions exercising the edge-driven text output.
- `tests/Homespun.Tests/Features/Gitgraph/GraphServiceHotPathLoggingTests.cs`, `GraphServiceEnhancedTaskGraphTests.cs` — same migration.
- `tests/Homespun.Tests/Features/Observability/GraphTracingTests.cs`, `IssueGraphOpenSpecEnricherTests.cs` (and the `*NoFanOut` sibling) — `TaskGraphNodeResponse` constructor calls are unchanged (DTO not removed); but the wire DTO gets new fields with defaults so existing tests stay green.

**Docs:**

- `docs/traces/dictionary.md` — no new spans (the layout engine is sync and runs inside the existing `graph.taskgraph.fleece.scan` span).
- `CLAUDE.md` — update the existing "Fleece" section bullet noting Fleece.Core / Fleece.Cli version sync rule (mention v3 specifically; the rule itself is unchanged).
- `docs/gitgraph/` — no change to existing snapshot/refresher docs; if a v3 migration note is useful for future readers, add `docs/gitgraph/fleece-v3-migration.md`.

**Wire format compatibility:**

- Additive only. Old fields (`Nodes[].Lane`, `Nodes[].Row`, `Nodes[].IsActionable`, `TotalLanes`) remain populated. New fields (`Edges`, `TotalRows`) populate from the v3 layout. A frontend that ignores `Edges` falls back to the v2-shape rendering (but the renderer is being rewritten in this change, so no fallback path ships).

## Dependencies

- **None upstream.** This change is the foundation for unblocking other graph-related work.
- **Downstream:** `phase-graph-rows` will rebase on top of this change. Phase synthesis becomes a small post-processing pass over the server-supplied `PositionedNode[]` + `Edge[]` rather than a modification to `computeLayout`'s control flow. See `phase-graph-rows`'s updated `proposal.md` and `design.md` for the rebased plan.

## Out of Scope

- **Frontend feature parity for `LayoutMode.NormalTree`.** The new mode is reachable via the engine but Homespun's UI uses `LayoutMode.IssueGraph` exclusively. We may adopt `NormalTree` for a future "tree view" mode, but it is not a goal of this change.
- **Server-side caching of the full `GraphLayout<Issue>`.** The existing `IProjectTaskGraphSnapshotStore` caches the projected `TaskGraphResponse`, which is sufficient. Caching the raw layout would require pinning a Fleece type into the snapshot store, which we want to avoid.
- **Removing the `IsActionable` flag.** The new `IIssueLayoutService` does not produce an "actionable" classification per node; that is a Homespun-specific projection (status ∪ open-PR-linked override). We keep computing it server-side and continue to populate it on `TaskGraphNodeResponse` for backwards compatibility with anything that still reads it (the orphan-link picker, the issues-agent diff view).
