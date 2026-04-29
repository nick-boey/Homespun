## ADDED Requirements

### Requirement: Task graph layout via Fleece.Core IIssueLayoutService

The server SHALL produce task-graph layouts via `Fleece.Core.Services.Interfaces.IIssueLayoutService.LayoutForTree` and SHALL NOT re-derive node positions, lane assignments, or connector geometry locally. `IIssueLayoutService` and the underlying `IGraphLayoutService` SHALL be registered in the DI container as singletons (both are pure with no I/O or mutable state).

`ProjectFleeceService.GetTaskGraphWithAdditionalIssuesAsync` SHALL pre-filter the issue cache by `IssueStatus ∈ { Draft, Open, Progress, Review }` unioned with caller-supplied `additionalIssueIds`, then SHALL invoke `LayoutForTree(includedIssues, InactiveVisibility.Hide)`. The pre-filter preserves the v2 contract that "issues linked to open PRs render even if their status is terminal" — those ids are passed in `additionalIssueIds` by `GraphService.BuildEnhancedTaskGraphAsync`. The method SHALL NOT call `LayoutForNext` or pass any non-`Hide` visibility unless a future change adds a configuration surface for it.

If `IIssueLayoutService` throws `InvalidGraphException` (e.g. the issue set contains a parent cycle that escaped upstream cycle detection), the service SHALL log a warning carrying the exception message and SHALL return `null`, mirroring the v2 behaviour for "no graph available."

#### Scenario: Layout uses the injected service, not a local implementation
- **WHEN** the server builds a task graph for any project
- **THEN** the layout call SHALL go through `IIssueLayoutService.LayoutForTree`
- **AND** the call SHALL be made on the engine instance registered in DI (singleton)
- **AND** no Homespun code SHALL re-implement node positioning, lane advancement, or edge geometry

#### Scenario: Pre-filter unions active statuses with additional ids
- **WHEN** `GetTaskGraphWithAdditionalIssuesAsync(projectPath, additionalIssueIds: ["issue-42"])` is called
- **AND** `issue-42` exists in the cache with `Status = Complete`
- **THEN** the issue list passed to `LayoutForTree` SHALL include `issue-42`
- **AND** SHALL also include every issue with `Status ∈ { Draft, Open, Progress, Review }` from the cache
- **AND** SHALL exclude every other terminal-status issue not in `additionalIssueIds`

#### Scenario: Cycle in input set is logged and produces null
- **WHEN** `LayoutForTree` throws `InvalidGraphException` because the supplied issues contain a parent cycle
- **THEN** the wrapper SHALL log a warning including the cycle path from the exception
- **AND** SHALL return `null` without rethrowing
- **AND** the caller (`GraphService`) SHALL treat `null` as "no graph available" exactly as it does for the empty-input case

#### Scenario: Empty issue set returns null without invoking the engine
- **WHEN** the active-status filter union with additional ids produces zero issues
- **THEN** the wrapper SHALL log a debug message and return `null`
- **AND** SHALL NOT invoke `IIssueLayoutService.LayoutForTree` at all

### Requirement: TaskGraphResponse exposes semantic edges and dimension counts

`Homespun.Shared.Models.Fleece.TaskGraphResponse` SHALL carry the layout's semantic edges and grid dimensions in addition to the existing per-node placement information. The wire shape SHALL include:

- `Edges: List<TaskGraphEdgeResponse>` — populated from `GraphLayout<Issue>.Edges`. Each entry SHALL carry `From: string` (issue id), `To: string` (issue id), `Kind: string` (one of `"SeriesSibling"`, `"SeriesCornerToParent"`, `"ParallelChildToSpine"`), `StartRow: int`, `StartLane: int`, `EndRow: int`, `EndLane: int`, `PivotLane: int?`, `SourceAttach: string` (one of `"Top"`, `"Bottom"`, `"Left"`, `"Right"`), and `TargetAttach: string` (same enum domain).
- `TotalRows: int` — populated from `GraphLayout<Issue>.TotalRows`.

The pre-existing fields on `TaskGraphResponse` and `TaskGraphNodeResponse` (`Nodes[].Lane`, `Nodes[].Row`, `Nodes[].IsActionable`, `TotalLanes`, `MergedPrs`, `LinkedPrs`, `OpenSpecStates`, `MainOrphanChanges`, `AgentStatuses`, `HasMorePastPrs`, `TotalPastPrsShown`) SHALL continue to be populated — the wire change is additive only.

`Edges[].Kind`, `SourceAttach`, and `TargetAttach` SHALL be serialized as their string names (e.g. `"SeriesSibling"`) rather than as numeric enum ordinals so the wire stays self-describing for the TypeScript client and any third-party consumer.

The frontend SHALL render parent-child connectors by reading `Edges[]` from the response and SHALL NOT re-derive connector geometry, parent-lane reservations, multi-parent indices, or lane-0 cross-issue lines from `Issue.ParentIssues` walks.

#### Scenario: Edge collection is populated for every parent-child relationship in the layout
- **WHEN** the server returns a `TaskGraphResponse` for a project with a series-mode parent and three series-mode children
- **THEN** `response.Edges` SHALL contain at least three entries: two `SeriesSibling` edges (child-to-child) and one `SeriesCornerToParent` edge (last-child-to-parent)
- **AND** every `Edges[i].From` and `Edges[i].To` SHALL refer to issue ids present in `response.Nodes`

#### Scenario: Existing fields stay populated for backwards compatibility
- **WHEN** the server returns a `TaskGraphResponse`
- **THEN** every entry in `response.Nodes` SHALL have `Lane`, `Row`, and `IsActionable` populated as before
- **AND** `response.TotalLanes` SHALL be populated as before
- **AND** consumers that ignore `Edges` SHALL still see a usable graph payload (without connector information)

#### Scenario: Frontend renders connectors from the wire edge list
- **WHEN** the frontend receives a `TaskGraphResponse`
- **THEN** `task-graph-svg.tsx` SHALL render one connector path per `response.edges[i]`, with geometry chosen by `edge.kind` and end-side by `edge.sourceAttach` / `edge.targetAttach`
- **AND** the frontend SHALL NOT call any `parentLaneReservations`, `multiParentIndex`, `drawTopLine`, `drawBottomLine`, `seriesConnectorFromLane`, `drawLane0Connector`, or `hasHiddenParent` derivation; those fields SHALL NOT exist on `TaskGraphIssueRenderLine`

#### Scenario: TotalRows reflects the laid-out grid height
- **WHEN** `GraphLayout<Issue>.TotalRows` is `N` for a given project
- **THEN** `response.TotalRows` SHALL equal `N`
- **AND** every `response.Nodes[i].Row` and every `response.Edges[j].StartRow` / `EndRow` SHALL satisfy `0 ≤ row < N`

#### Scenario: Empty layout serializes with empty collections
- **WHEN** `IIssueLayoutService` returns a layout with zero nodes (no issues match the filter)
- **THEN** `response.Nodes` SHALL be an empty array
- **AND** `response.Edges` SHALL be an empty array
- **AND** `response.TotalRows` and `response.TotalLanes` SHALL both be `0`
