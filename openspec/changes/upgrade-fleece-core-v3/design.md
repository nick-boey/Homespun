## Context

`Fleece.Core` v3.0.0 is the first version of the library to expose its graph-layout engine as a generic, DI-registered service. The v2 API was a single method on `IFleeceService` (`BuildFilteredTaskGraphLayoutAsync(IReadOnlySet<string>?)`) that returned `TaskGraph` — a flat list of `TaskGraphNode` records carrying `(Issue, Lane, Row, IsActionable)`. Edge information was implicit: consumers had to walk `Issue.ParentIssues` and re-derive series/parallel sibling relationships from `ExecutionMode` to draw connectors.

The v3 API is two interfaces:

```csharp
// src/Fleece.Core/Services/Interfaces/IGraphLayoutService.cs
public interface IGraphLayoutService
{
    GraphLayoutResult<TNode> Layout<TNode>(GraphLayoutRequest<TNode> request)
        where TNode : IGraphNode;
}

// src/Fleece.Core/Services/Interfaces/IIssueLayoutService.cs
public interface IIssueLayoutService
{
    GraphLayout<Issue> LayoutForTree(
        IReadOnlyList<Issue> issues,
        InactiveVisibility visibility = InactiveVisibility.Hide,
        string? assignedTo = null,
        GraphSortConfig? sort = null);

    GraphLayout<Issue> LayoutForNext(
        IReadOnlyList<Issue> issues,
        IReadOnlySet<string>? matchedIds = null,
        InactiveVisibility visibility = InactiveVisibility.Hide,
        string? assignedTo = null,
        GraphSortConfig? sort = null);
}
```

`GraphLayout<Issue>` carries `Nodes: PositionedNode<Issue>[]` (with `Row`, `Lane`, `AppearanceIndex`, `TotalAppearances`), `Edges: Edge<Issue>[]` (with `Kind`, `Start`, `End`, `PivotLane?`, `SourceAttach`, `TargetAttach`), an `Occupancy: OccupancyCell[,]` matrix, and `TotalRows` / `TotalLanes`. Edge kinds are `SeriesSibling`, `SeriesCornerToParent`, `ParallelChildToSpine`. Attach sides are `Top` / `Bottom` / `Left` / `Right`.

Homespun's contact with the v2 API is one method call (`ProjectFleeceService.cs:562`). But the absence of semantic-edge information in v2 motivated two large pieces of code in Homespun:

1. **`TaskGraphTextRenderer.cs` (280 LOC, server)** — renders `TaskGraph` as ASCII for the `BuildTaskGraphTextAsync` endpoint. Hand-rolls connected-component grouping (BFS over parent refs), parent-edge geometry, and per-row character placement. Originally written because v2 didn't expose connector geometry.

2. **`task-graph-layout.ts` (1148 LOC, frontend)** — converts `TaskGraphResponse` into a `TaskGraphRenderLine[]` consumed by `task-graph-svg.tsx`. Computes `drawTopLine` / `drawBottomLine` / `seriesConnectorFromLane` / `isSeriesChild` / `parentLaneReservations` / multi-parent indices / lane-0 cross-issue connectors. Each of those fields is a derivation of "what does the connector geometry look like?" that v3's `Edge<Issue>` answers directly.

The migration is small at the call-site level (one method) but large in *implication* — it lets us delete most of (1) and (2) and move the authoritative geometry to the server, populated by `Fleece.Core` rather than re-derived per-renderer.

Two adjacent considerations:

- **`phase-graph-rows`** is in flight (44 tasks, 0 complete). It modifies `task-graph-layout.ts` heavily — adding a `'phase'` render-line type, threading `openSpecStates` into `computeLayout`, splicing phase lines into `renderGroup` / `renderGroupTreeView`. We need to land Fleece v3 first and let `phase-graph-rows` rebase onto the simpler post-Fleece pipeline.

- **`mock-openspec-seeding`** is also in flight but does not touch the graph pipeline. No coordination needed.

## Goals / Non-Goals

**Goals:**

- Migrate Homespun off `Fleece.Core` 2.1.1's removed `BuildFilteredTaskGraphLayoutAsync` / `TaskGraph` / `TaskGraphNode` / `RenderingParentId` API surface onto v3.0.0's `IIssueLayoutService.LayoutForTree`.
- Preserve the v2 "include this terminal-status issue because it has an open PR" behaviour byte-for-byte; the existing wire shape (and the task-graph snapshot store) stays consumable.
- Push the v3 `Edge<Issue>` geometry over the wire so the frontend can drop hand-rolled parent-connector inference.
- Rewrite `TaskGraphTextRenderer` on top of `GraphLayout.Edges` so the server-side ASCII path also stops re-deriving geometry.
- Bump `Fleece.Cli` in `Dockerfile.base` to match the library version per the repo's existing version-sync rule.

**Non-Goals:**

- Adopting `LayoutMode.NormalTree`. The new mode exists and is reachable via `IGraphLayoutService` directly, but no Homespun UI surface uses it. Adding a "tree view mode" would be a separate change.
- Removing the v2-shape fields (`Lane`, `Row`, `IsActionable`, `TotalLanes`) from `TaskGraphResponse`. Keep them populated for backwards compatibility — the orphan link picker and the issues-agent diff view reference `Issue` and `Lane` from `TaskGraphNodeResponse`.
- Caching `GraphLayout<Issue>` directly in `IProjectTaskGraphSnapshotStore`. The store keeps caching the projected `TaskGraphResponse` so it stays free of Fleece types.
- Bumping `mock-openspec-seeding` or any other in-flight change. Only `phase-graph-rows` has structural overlap and gets explicit rebase guidance.

## Decisions

### Pre-filter the issue list, then call `LayoutForTree` (preserve v2 byte-equivalence)

**Decision:** Keep the existing Homespun-side filter in `ProjectFleeceService.GetTaskGraphWithAdditionalIssuesAsync`:

```csharp
var includedIssues = cache.Values
    .Where(i => i.Status is IssueStatus.Draft or IssueStatus.Open
                or IssueStatus.Progress or IssueStatus.Review
             || additionalIds.Contains(i.Id))
    .ToList();
```

Then call `_issueLayoutService.LayoutForTree(includedIssues, InactiveVisibility.Hide)` on the pre-filtered set.

**Why:** v2's `BuildFilteredTaskGraphLayoutAsync(set)` laid out only the matched issues — no automatic ancestor inclusion. The cleanest v3 equivalent is "pass only the issues you want laid out, then ask for `Hide` visibility." This is byte-equivalent: every issue passed in is in scope, and the engine's filter never excludes any of them because they all already match the active-status criterion or the additional-id override.

**Alternative considered:** Use `LayoutForNext(allIssues, matchedIds: activeAndPrLinkedIds, InactiveVisibility.Hide)`. Rejected because `LayoutForNext` automatically pulls in *ancestors* of matched ids; that is a semantic change versus v2 (terminal-status ancestors that aren't PR-linked would now appear in the graph). We don't want to ship a behaviour change in the same PR as the engine swap. A future change can reconsider this once the migration is stable.

**Alternative considered:** Use `InactiveVisibility.IfHasActiveDescendants` and pass `cache.Values` unfiltered. Rejected — almost matches but not quite: it doesn't account for "issue is terminal but has an open PR pointing at it", which is the side-channel the existing code path handles via `additionalIssueIds`.

### Push semantic edges over the wire as a new `Edges[]` field, additive to existing `TaskGraphResponse`

**Decision:** Extend `Homespun.Shared.Models.Fleece.TaskGraphResponse` with two new fields:

```csharp
public List<TaskGraphEdgeResponse> Edges { get; set; } = [];
public int TotalRows { get; set; }
```

Where `TaskGraphEdgeResponse` is:

```csharp
public class TaskGraphEdgeResponse
{
    public required string From { get; set; }    // issue id
    public required string To { get; set; }      // issue id
    public required string Kind { get; set; }    // "SeriesSibling" | "SeriesCornerToParent" | "ParallelChildToSpine"
    public required int StartRow { get; set; }
    public required int StartLane { get; set; }
    public required int EndRow { get; set; }
    public required int EndLane { get; set; }
    public int? PivotLane { get; set; }
    public required string SourceAttach { get; set; }   // "Top" | "Bottom" | "Left" | "Right"
    public required string TargetAttach { get; set; }
}
```

Keep all existing fields populated. `TaskGraphNodeResponse.Lane` / `Row` / `IsActionable` continue to mirror `PositionedNode<Issue>.{Lane, Row}` and the Homespun-derived `IsActionable` projection.

**Why:** Additive wire changes don't break tooling that doesn't know about the new fields (OpenAPI client regen, consumers in the issues-agent flow, snapshot store serialization). The task-graph snapshot store keeps caching `TaskGraphResponse` as-is — nothing about the cache shape changes except the payload grows by one collection.

`Kind` and the attach sides are serialized as strings rather than as numeric enums so the wire stays self-describing across language boundaries. The TypeScript client gets string-literal unions automatically.

**Alternative considered:** Replace the `Nodes` collection with a `Nodes: PositionedNode<Issue>` shape and remove the v2-style fields. Rejected — breaks the orphan link picker, the issues-agent diff view, and any snapshot serialized to disk before the upgrade. The cost of keeping the legacy fields populated is one mapper line in `GraphService.BuildEnhancedTaskGraphAsync`; not worth the breakage.

### Rewrite the frontend layout pipeline as a thin pass-through, with edges driving connector rendering

**Decision:** `task-graph-layout.ts` keeps the `TaskGraphRenderLine` discriminated-union model (`issue` / `pr` / `separator` / future `phase`) but `TaskGraphIssueRenderLine` loses every field that exists to encode connector geometry:

```diff
 export interface TaskGraphIssueRenderLine {
   type: 'issue'
   issueId: string
   title: string
   description: string | null
   branchName: string | null
   lane: number
-  parentLane: number | null
-  isFirstChild: boolean
-  isSeriesChild: boolean
-  drawTopLine: boolean
-  drawBottomLine: boolean
-  seriesConnectorFromLane: number | null
   marker: TaskGraphMarkerType
   issueType: IssueTypeEnum
   status: IssueStatusEnum
   hasDescription: boolean
   linkedPr: TaskGraphLinkedPr | null
   agentStatus: AgentStatusData | null
   assignedTo: string | null
-  drawLane0Connector: boolean
-  isLastLane0Connector: boolean
-  drawLane0PassThrough: boolean
-  lane0Color: string | null
-  hasHiddenParent: boolean
-  hiddenParentIsSeriesMode: boolean
   executionMode: ExecutionModeEnum
   parentIssues: Array<{ parentIssue?: string | null; sortOrder?: string | null }> | null
-  multiParentIndex: number | null
-  multiParentTotal: number | null
-  isLastChild: boolean
-  hasParallelChildren: boolean
   parentIssueId: string | null
-  parentLaneReservations: Array<{ lane: number; issueType: IssueTypeEnum }>
+  appearanceIndex: number  // from PositionedNode.AppearanceIndex (1-based occurrence)
+  totalAppearances: number // from PositionedNode.TotalAppearances
 }
```

`computeLayout` becomes:

1. Translate each `PositionedNode<Issue>` from the response into one `TaskGraphIssueRenderLine` (no inference — direct copy of `Lane`, plus surrounding metadata).
2. Synthesise PR / separator / orphan / loadMore lines from the existing Homespun-only fields on `TaskGraphResponse` (`MergedPrs`, `MainOrphanChanges`, `LinkedPrs`, etc.).
3. Return the line list + the unmodified `Edges[]` from the server response, threaded through to the renderer.

`task-graph-svg.tsx` switches connector rendering on `(edge.kind, edge.sourceAttach, edge.targetAttach)`:

```
SeriesSibling      → vertical segment from (start.row, start.lane) to (end.row, end.lane)
                     (start.lane === end.lane in NormalTree; differs in IssueGraph)
SeriesCornerToParent → L-path: start → (pivot row, pivot lane) → end
ParallelChildToSpine → L-path: start → (start.row, pivot lane) → end
```

Attach sides tell the renderer which face of the node bounding box to terminate at, removing the existing "is this row a series child? if so draw an arc" inference.

**Why:** The connector logic in `task-graph-layout.ts` is the largest piece of code that exists *because* v2 lacked edge information. With v3 emitting authoritative edges, the inference is redundant. Net code reduction is large (~750 LOC removed across `task-graph-layout.ts` + the connector half of `task-graph-svg.tsx`), and the geometry now lives in a single place — Fleece — instead of being re-derived in the server text renderer + the frontend layout pipeline.

**Risks:** The frontend `task-graph-layout.test.ts` has heavy coverage of every connector field. Most of those tests become irrelevant once the fields are gone. New tests assert that (a) every `PositionedNode` produces exactly one render line, (b) `Edges[]` is threaded through unchanged, (c) PR / separator / orphan synthesis still works.

### Rewrite `TaskGraphTextRenderer` on `GraphLayout.Edges` + `OccupancyCell`

**Decision:** Replace the 280-LOC v2-shaped `TaskGraphTextRenderer` with an edge-driven implementation:

```csharp
public static string Render(GraphLayout<Issue> layout)
{
    if (layout.Nodes.Count == 0) return string.Empty;

    var sb = new StringBuilder();
    for (int row = 0; row < layout.TotalRows; row++)
    {
        for (int lane = 0; lane < layout.TotalLanes; lane++)
        {
            var cell = layout.Occupancy[row, lane];
            sb.Append(RenderCell(cell, layout));
        }
        sb.Append('\n');
    }
    return sb.ToString().TrimEnd('\n');
}
```

`RenderCell` reads `cell.Node` (if any) and `cell.Edges` (the segment list passing through this cell from `Fleece.Core`'s `OccupancyCell`) and emits the right box-drawing character for the segments + node marker. No hand-rolled BFS, no parent-edge map, no group ordering — the engine produces the layout in row order already.

**Why:** Same reason as the frontend rewrite — Fleece.Core now owns the geometry. The v2 renderer's `GroupNodes` BFS was specifically to handle "the v2 layout doesn't always produce row-order nodes for disconnected components"; v3's engine emits in layout order with the occupancy matrix as ground truth.

**Risk:** The existing snapshot tests in `TaskGraphTextRendererTests.cs` assert specific ASCII output. v3's engine produces byte-equivalent output for the `IssueGraph` mode (per the v3 spec scenario "Existing Verify snapshots remain green"), so the existing snapshot tests should still pass — *if* the new renderer uses the same characters and spacing. We pin the character set and verify with the existing tests; any divergence is investigated as a renderer bug, not an accepted snapshot update.

### DI registration follows the canonical Fleece-DI pattern

**Decision:** In `Homespun.Server/Program.cs`, register both services as singletons:

```csharp
builder.Services.AddSingleton<IGraphLayoutService, GraphLayoutService>();
builder.Services.AddSingleton<IIssueLayoutService, IssueLayoutService>();
```

Inject `IIssueLayoutService` into `ProjectFleeceService` via the constructor.

**Why:** The services are pure (no I/O, no state) per the v3 spec — singleton is correct. Constructor injection in `ProjectFleeceService` matches the pattern already used for `IFleeceSettingsService` and `IGitConfigService`.

### `IProjectFleeceService` return type changes from `TaskGraph?` to `GraphLayout<Issue>?`

**Decision:** Update both methods on `IProjectFleeceService`:

```diff
- Task<TaskGraph?> GetTaskGraphAsync(string projectPath, CancellationToken ct = default);
- Task<TaskGraph?> GetTaskGraphWithAdditionalIssuesAsync(...);
+ Task<GraphLayout<Issue>?> GetTaskGraphAsync(string projectPath, CancellationToken ct = default);
+ Task<GraphLayout<Issue>?> GetTaskGraphWithAdditionalIssuesAsync(...);
```

**Why:** `TaskGraph` is removed from Fleece.Core; we cannot keep returning it. Wrapping the new shape in a Homespun-owned facade would just shift the type churn one layer down without reducing it. Direct return is cleaner. The two consumers (`GraphService.BuildTaskGraphAsync` and `GraphService.BuildEnhancedTaskGraphAsync`) both already consume the projection internally — no caller cares about the Fleece type after the mapper runs.

### Test fixtures get migrated, not rewritten

**Decision:** In `tests/Homespun.Tests/Features/Gitgraph/*.cs`, every `new TaskGraph { Nodes = [new TaskGraphNode { Issue, Lane, Row, IsActionable }] }` becomes:

```csharp
new GraphLayout<Issue>
{
    Nodes = [new PositionedNode<Issue> { Node = issue, Row = 0, Lane = 0 }],
    Edges = [],
    Occupancy = new OccupancyCell[1, 1],  // sized [TotalRows, TotalLanes]
    TotalRows = 1,
    TotalLanes = 1
}
```

`IsActionable` was a Homespun projection on `TaskGraphNode`; in v3 it does not exist on `PositionedNode`. The test fixtures that asserted on `IsActionable` are asserting on Homespun's actionability classification, which now lives on `TaskGraphNodeResponse` (the wire DTO), not on the layout type. Tests that need to set up "this node is actionable" should mock the projection at the `IGraphService` boundary, not at the Fleece type.

**Why:** The test churn is mechanical (5–10 files, 20–30 occurrences). Rewriting tests from scratch would lose coverage of edge cases the existing tests already encode (open-PR-linked terminal issues, missing-issue handling, multi-parent layouts). Mechanical migration preserves intent.

## Risks / Trade-offs

- **Skipping v2.2.x.** v2.2.0 removed the broken TUI and the external editor fallback from `create` / `edit` (CLI-only); v2.2.1 fixed CLI DI registration broken by the TUI removal. Neither affects how Homespun consumes `IFleeceService`. Verified by grep: Homespun never invokes `create` / `edit` via the CLI editor flow — all writes go through `IFleeceService.AddAsync` / `UpdateAsync`. Risk: low.

- **`Fleece.Cli` version drift.** Per `CLAUDE.md`, the `Fleece.Cli` version in `Dockerfile.base` must match the `Fleece.Core` package version. We bump both atomically in this change. Risk: low if the change is reviewed end-to-end; high if someone splits the version bump across two PRs.

- **`TaskGraphTextRenderer` ASCII output divergence.** v3's spec asserts that `LayoutMode.IssueGraph` produces byte-equivalent layouts to v2 for the same inputs, but our renderer is being rewritten on top of a different scaffolding (occupancy matrix vs. hand-rolled parent walk). If the rewrite produces slightly-different ASCII, the existing `Verify`-style snapshot tests will catch it. Mitigation: keep the existing snapshots, run them after the rewrite, treat any drift as a bug to fix in the renderer.

- **Frontend test churn.** `task-graph-layout.test.ts` will lose ~80% of its current assertions because most of them target connector-inference fields that are gone. The remaining assertions (per-render-line per `PositionedNode`, edge pass-through, PR/separator/orphan synthesis) are simpler but provide narrower coverage. Mitigation: lean on the existing e2e suite (`src/Homespun.Web/e2e/`) to catch visual regressions — it already exercises the rendered graph via Playwright snapshots.

- **Wire-format snapshot store warm-up.** The `IProjectTaskGraphSnapshotStore` caches `TaskGraphResponse`. When this change deploys, every existing cache entry is shape-compatible (only new fields are added) but its `Edges` collection is empty until the next refresh. Mitigation: the per-project background refresher rebuilds within ~10s by default; clients fetching during the gap render with `Edges = []` (the renderer handles empty edges as "no connectors", which produces a degenerate but not-broken view).

- **`phase-graph-rows` rebase cost.** The change has 0/44 tasks complete and is in active development. Sequencing this Fleece change first means whoever is implementing `phase-graph-rows` waits or rebases. Mitigation: this change is small at the call-site level (one method to rewrite); we should aim to land it within a single PR so the rebase window is short. The phase-graph-rows artifacts are updated in this same change to reflect the new shape so the rebase is mechanical, not exploratory.

- **`GraphSortConfig` is a new concept.** v3 introduces `GraphSortConfig` (passed optionally into `LayoutForTree`). Homespun has no equivalent today — issues are ordered by parent-first DFS plus stable creation time. Mitigation: pass `GraphSortConfig.Default` explicitly (or `null`, which the engine treats as default). If we need custom ordering later, that's a follow-up change.

- **`InvalidGraphException` propagation.** v3's `IssueLayoutService` throws `InvalidGraphException` if the issue set contains a parent cycle. v2 silently produced an empty layout. Mitigation: catch `InvalidGraphException` in `ProjectFleeceService.GetTaskGraphWithAdditionalIssuesAsync`, log a warning, return `null` (matching the v2 "no graph" path). Cycle detection upstream (in `WouldCreateCycleAsync`) prevents this from occurring on user-driven mutations, so the catch is defence-in-depth for race conditions or external file edits.

## Migration Plan

1. **Branch + bump.** New branch `task/upgrade-fleece-core-v3`. Edit both csproj files and `Dockerfile.base` in the same commit.
2. **Server migration (Delta 1).** Rewrite `ProjectFleeceService.GetTaskGraphWithAdditionalIssuesAsync`, update `IProjectFleeceService` signatures, register the new services in DI, fix `GraphService` mapper. Run `dotnet build` and the unit-test suite — fix mock fixtures as compilation fails.
3. **Wire format extension (Delta 2).** Add `TaskGraphEdgeResponse` to `TaskGraphDto.cs`, populate `Edges` + `TotalRows` from the server mapper, regenerate the OpenAPI client (`npm run generate:api:fetch`). Verify the wire shape via a hand-crafted curl against the dev-mock profile.
4. **Frontend rewrite (Delta 3).** Update `task-graph-layout.ts` and `task-graph-svg.tsx` together; rebase `task-graph-layout.test.ts`. Run `npm run lint:fix && npm run typecheck && npm test && npm run build-storybook`. Boot dev-mock and visually validate against several seeded issue trees (series chain, parallel children, multi-parent fan-in, orphan PR).
5. **Server text renderer rewrite (Delta 4).** Rewrite `TaskGraphTextRenderer` against `GraphLayout.Edges` + `OccupancyCell`. Run the existing snapshot tests; investigate any output drift as a renderer bug.
6. **Phase-graph-rows rebase prep.** With this change merged, hand off to whoever is implementing `phase-graph-rows`; their tasks 1.x and 4.x rebase against the new `computeLayout` shape per the updated guidance in their `design.md`.
7. **Rollback plan.** Single commit on the package versions plus a code change. Revert reverts everything atomically. The wire format addition is forward-compatible (no client requires the new fields), so a partial rollout (server reverted, client still expecting `Edges`) is graceful — the client falls back to empty connectors but doesn't crash.

## Open Questions

- **Should we use `LayoutMode.NormalTree` for any current Homespun surface?** The picker dialog (link-picker, sub-issue picker) renders issue rows in a flat list with no connectors. NormalTree's parent-first row order is closer to a "tree view" and could replace some of the picker's layout guesswork. Defer until we have a concrete picker UX problem the new mode would solve.
- **Should `GraphSortConfig` be exposed as an API parameter?** Future work might let users sort by created date, priority, or assignee. Out of scope here; capture as a follow-up issue if there's a UX request.
- **Is the snapshot store's serializer happy with `TaskGraphEdgeResponse`?** The store uses System.Text.Json; required-init records serialize fine. Verify in Delta 1's build step.
