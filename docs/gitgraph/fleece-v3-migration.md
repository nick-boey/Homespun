# Fleece.Core v3 migration — wire-format additions

`Fleece.Core` 2.1.1 → 3.0.0 swapped the layout engine. Homespun migrated atomically and pushed the v3 semantic-edge geometry over the wire so the frontend stops re-deriving connector geometry. This doc summarises the wire-format additions and the new types, for reviewers + future readers.

## What's new on `TaskGraphResponse`

Two additive collections (`Homespun.Shared.Models.Fleece.TaskGraphDto`):

```csharp
public List<TaskGraphEdgeResponse> Edges { get; set; } = [];
public int TotalRows { get; set; }
```

`TaskGraphNodeResponse` gained two appearance-tracking fields:

```csharp
public int AppearanceIndex { get; set; }    // 1-based, per Fleece v3
public int TotalAppearances { get; set; } = 1;
```

`AppearanceIndex` / `TotalAppearances` come from `Fleece.Core.Models.Graph.PositionedNode<Issue>`. A multi-parent issue appears in the `Nodes` list once per parent chain; `AppearanceIndex` disambiguates the appearance, `TotalAppearances` is the total count for that issue id. Single-appearance nodes are `(1, 1)`.

## `TaskGraphEdgeResponse`

```csharp
public class TaskGraphEdgeResponse
{
    public required string From { get; set; }                // issue id
    public required string To { get; set; }                  // issue id
    public required string Kind { get; set; }                // see below
    public required int StartRow { get; set; }
    public required int StartLane { get; set; }
    public required int EndRow { get; set; }
    public required int EndLane { get; set; }
    public int? PivotLane { get; set; }                      // ParallelChildToSpine only
    public required string SourceAttach { get; set; }        // Top|Bottom|Left|Right
    public required string TargetAttach { get; set; }
}
```

`Kind` is a string union over `Fleece.Core.Models.Graph.EdgeKind`:

| Kind | Geometry | Notes |
|---|---|---|
| `SeriesSibling` | straight line `Start → End` | sibling-to-sibling vertical chain inside a series-mode parent |
| `SeriesCornerToParent` | L-shape: `Start → (Start.lane, End.row) → End` | last series sibling cornering up to its parent |
| `ParallelChildToSpine` | L-shape via `PivotLane`: `Start → (PivotLane, Start.row) → (PivotLane, End.row)` | parallel-mode children fanning into the parent's spine lane |

Attach sides identify which face of the node bounding box the edge terminates at. The frontend's `<TaskGraphEdges>` overlay (`features/issues/components/task-graph-svg.tsx`) consumes these directly.

## What got deleted

- `task-graph-layout.ts` lost ~700 LOC of connector inference (`drawTopLine`, `drawBottomLine`, `seriesConnectorFromLane`, `parentLaneReservations`, `multiParentIndex`/`multiParentTotal` post-passes, lane-0 connector synthesis, `renderGroup` / `renderGroupTreeView`).
- `TaskGraphTextRenderer.cs` rewritten from 280 LOC (hand-rolled BFS over parent refs) to ~80 LOC over `GraphLayout.Edges` + `OccupancyCell`.
- `IFleeceService.BuildFilteredTaskGraphLayoutAsync` (gone in v3) → `IIssueLayoutService.LayoutForTree(includedIssues, InactiveVisibility.Hide)`.

## Backwards compatibility

Wire change is additive. `TaskGraphNodeResponse.Lane` / `Row` / `IsActionable` still populated. Snapshot store entries serialised before the upgrade are shape-compatible — `Edges` deserialises empty until the next refresh. The renderer treats empty edges as "no connectors", which produces a degenerate but non-broken view.

## Version sync

`Fleece.Core` (NuGet) and `Fleece.Cli` (Dockerfile.base) versions must always match. Both are at `3.0.0`. See the project root `CLAUDE.md` "Version Sync Required" note.
