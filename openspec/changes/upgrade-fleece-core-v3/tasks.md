## 1. Package + Dockerfile bump

- [x] 1.1 Update `src/Homespun.Server/Homespun.Server.csproj` line 21: `<PackageReference Include="Fleece.Core" Version="2.1.1" />` → `Version="3.0.0"`
- [x] 1.2 Update `src/Homespun.Shared/Homespun.Shared.csproj` line 10: same `Fleece.Core` 2.1.1 → 3.0.0
- [x] 1.3 Update `Dockerfile.base` line 62: `RUN dotnet tool install Fleece.Cli --version 2.1.1 …` → `--version 3.0.0`
- [x] 1.4 Run `dotnet restore` and confirm the new packages resolve. Note any new transitive dependencies in the build log
- [x] 1.5 Run `dotnet build src/Homespun.Server/Homespun.Server.csproj` — expect compile errors at every removed-API call site (good — this is the punch list for sections 2–3)

## 2. DI registration + service contract

- [x] 2.1 In `src/Homespun.Server/Program.cs`, register the new layout services as singletons immediately after the existing Fleece DI block:
  ```csharp
  builder.Services.AddSingleton<IGraphLayoutService, GraphLayoutService>();
  builder.Services.AddSingleton<IIssueLayoutService, IssueLayoutService>();
  ```
  (Imports: `Fleece.Core.Services.Interfaces`, `Fleece.Core.Services.GraphLayout`.)
- [x] 2.2 Update `IProjectFleeceService` (`src/Homespun.Server/Features/Fleece/Services/IProjectFleeceService.cs`) lines 90, 102:
  ```diff
  - Task<TaskGraph?> GetTaskGraphAsync(...)
  - Task<TaskGraph?> GetTaskGraphWithAdditionalIssuesAsync(...)
  + Task<GraphLayout<Issue>?> GetTaskGraphAsync(...)
  + Task<GraphLayout<Issue>?> GetTaskGraphWithAdditionalIssuesAsync(...)
  ```
  Add `using Fleece.Core.Models.Graph;` at the top.
- [x] 2.3 Update XML-doc comments on both methods to reference `GraphLayout<Issue>` and `IIssueLayoutService` rather than `TaskGraph` / `TaskGraphService`
- [x] 2.4 Inject `IIssueLayoutService` into `ProjectFleeceService`'s constructor. Save it as a field

## 3. Rewrite `ProjectFleeceService.GetTaskGraphWithAdditionalIssuesAsync`

- [x] 3.1 In `ProjectFleeceService.cs:536-565`, replace the body so the active-status filter stays the same but the layout call moves from `IFleeceService.BuildFilteredTaskGraphLayoutAsync` to the new injected `_issueLayoutService.LayoutForTree`:
  ```csharp
  var includedIssues = cache.Values
      .Where(i => i.Status is IssueStatus.Draft or IssueStatus.Open
                  or IssueStatus.Progress or IssueStatus.Review
               || additionalIds.Contains(i.Id))
      .ToList();
  if (includedIssues.Count == 0) { /* same null-return path */ }
  try
  {
      var layout = _issueLayoutService.LayoutForTree(
          includedIssues, InactiveVisibility.Hide);
      _logger.LogDebug("Built layout: {Nodes}n / {Lanes}l / {Rows}r / {Edges}e for {Path}",
          layout.Nodes.Count, layout.TotalLanes, layout.TotalRows, layout.Edges.Count, projectPath);
      return layout;
  }
  catch (InvalidGraphException ex)
  {
      _logger.LogWarning(ex, "Layout rejected for {Path}: {Msg}", projectPath, ex.Message);
      return null;
  }
  ```
- [x] 3.2 Confirm the cancellation-token path is preserved: `LayoutForTree` is synchronous, so wrap in `Task.FromResult` if needed at the interface boundary. Keep `ct.ThrowIfCancellationRequested()` at the top of the method
- [x] 3.3 Drop the unused `service` local (was the `IFleeceService` instance). Stop calling `GetOrCreateFleeceService(projectPath)` here unless still needed for cache load (it is, for `EnsureCacheLoadedAsync`)
- [x] 3.4 Verify no other call site in `ProjectFleeceService.cs` uses `BuildFilteredTaskGraphLayoutAsync` or `BuildTaskGraphLayoutAsync` (grep)

## 4. Update `GraphService` consumers

- [x] 4.1 In `GraphService.cs:251-280` (`BuildTaskGraphAsync`), change the local type from `TaskGraph?` to `GraphLayout<Issue>?`. Update the log line that reads `taskGraph.Nodes.Count` and `taskGraph.TotalLanes` — both fields still exist on `GraphLayout`
- [x] 4.2 In `GraphService.cs:283-290` (`BuildTaskGraphTextAsync`), update the call to `TaskGraphTextRenderer.Render(layout)` to pass the new layout type. Wait for section 6 to land the renderer rewrite
- [x] 4.3 In `GraphService.cs:567-700` (`BuildEnhancedTaskGraphAsync`), update the local `taskGraph` to `GraphLayout<Issue>?`. Replace the existing `Nodes = taskGraph.Nodes.Select(n => new TaskGraphNodeResponse { … }).ToList()` mapper with a mapper over `PositionedNode<Issue>`:
  ```csharp
  Nodes = layout.Nodes.Select(n => new TaskGraphNodeResponse
  {
      Issue = IssueDtoMapper.ToResponse(n.Node),
      Lane = n.Lane,
      Row = n.Row,
      IsActionable = ComputeIsActionable(n.Node, openPrLinkedIssueIds)  // existing helper
  }).ToList()
  ```
- [x] 4.4 In the same method, populate the new edge collection: `response.Edges = layout.Edges.Select(GitgraphApiMapper.MapEdge).ToList(); response.TotalRows = layout.TotalRows;` (mapper added in section 5)
- [x] 4.5 In `GraphService.cs:584-592`, the activity tag block: keep `graph.taskgraph.fleece.scan` span name. Add tags `layout.nodes`, `layout.edges`, `layout.rows`, `layout.lanes` for observability (cardinality-safe ints; not the issue ids)
- [x] 4.6 If `IsActionable` is computed inside this method via a helper, leave the helper signature unchanged; only its input changes from `TaskGraphNode` to `Issue`

## 5. Wire format: `TaskGraphEdgeResponse` + mapper

- [x] 5.1 In `src/Homespun.Shared/Models/Fleece/TaskGraphDto.cs`, add the new DTO:
  ```csharp
  public class TaskGraphEdgeResponse
  {
      public required string From { get; set; }
      public required string To { get; set; }
      public required string Kind { get; set; }
      public required int StartRow { get; set; }
      public required int StartLane { get; set; }
      public required int EndRow { get; set; }
      public required int EndLane { get; set; }
      public int? PivotLane { get; set; }
      public required string SourceAttach { get; set; }
      public required string TargetAttach { get; set; }
  }
  ```
- [x] 5.2 In `TaskGraphResponse`, add:
  ```csharp
  public List<TaskGraphEdgeResponse> Edges { get; set; } = [];
  public int TotalRows { get; set; }
  ```
- [x] 5.3 In `src/Homespun.Server/Features/Gitgraph/Services/GitgraphApiMapper.cs`, add a `MapEdge` static method that takes `Edge<Issue>` and returns `TaskGraphEdgeResponse`. Map enum values to strings via `nameof` or `.ToString()`. Verify that the `From.Id` / `To.Id` chain works — `Issue.Id` is the public identifier
- [x] 5.4 Regenerate the OpenAPI client: `cd src/Homespun.Web && npm run generate:api:fetch`. Verify the new types appear in `src/api/generated/types.gen.ts` (manually updated due to server startup constraint)
- [ ] 5.5 Spot-check the new wire shape via curl against dev-mock: boot `dotnet run --project src/Homespun.AppHost --launch-profile dev-mock`, then `curl -s http://localhost:5101/api/graph/<projectId>/taskgraph/data | jq '.edges[0:3]'` — expect non-empty edge entries with the new fields populated

## 6. Server text renderer rewrite

- [x] 6.1 In `src/Homespun.Server/Features/Gitgraph/Services/TaskGraphTextRenderer.cs`, replace the entire file with an edge-driven implementation. New entry point: `Render(GraphLayout<Issue> layout) → string`. Walk `layout.Occupancy` row × lane, render the cell's node marker (if any) and segment characters from `cell.Edges`. Drop `GroupNodes`, `RenderGroup`, `parentByNode`, `parentRowByChildId` helpers entirely
- [x] 6.2 Preserve the existing character constants (`ActionableMarker = '○'`, `OpenMarker = '◌'`, `CompleteMarker = '●'`, `ClosedMarker = '⊘'`, `Horizontal`, `Vertical`, `TopRight`, `RightTee`, `BottomRight`). Still consumes `Issue.Status` and the Homespun "actionable" projection — pass that classification in alongside `GraphLayout` if needed (signature: `Render(GraphLayout<Issue> layout, IReadOnlySet<string> actionableIds)`)
- [x] 6.3 Run `tests/Homespun.Tests/Features/Gitgraph/TaskGraphTextRendererTests.cs` — verify all existing snapshots still pass byte-for-byte. Any failure is a renderer bug; investigate the divergence and fix the renderer (do **not** accept new snapshots)
- [x] 6.4 Update the renderer's XML doc comment to reflect the v3-driven approach

## 7. Test fixture migration

- [x] 7.1 Add a private helper in `tests/Homespun.Tests/Features/Gitgraph/GraphServiceTestHelpers.cs` (or similar):
  ```csharp
  internal static GraphLayout<Issue> BuildLayout(params (Issue issue, int row, int lane)[] nodes)
  {
      var positioned = nodes.Select(n => new PositionedNode<Issue>
          { Node = n.issue, Row = n.row, Lane = n.lane }).ToList();
      var totalRows = nodes.Length == 0 ? 0 : nodes.Max(n => n.row) + 1;
      var totalLanes = nodes.Length == 0 ? 0 : nodes.Max(n => n.lane) + 1;
      return new GraphLayout<Issue>
      {
          Nodes = positioned, Edges = [],
          Occupancy = new OccupancyCell[totalRows, totalLanes],
          TotalRows = totalRows, TotalLanes = totalLanes
      };
  }
  ```
- [x] 7.2 In `GraphServiceOpenPrIssueTests.cs`, replace every `new TaskGraph { Nodes = [new TaskGraphNode { … }] }` (5 occurrences at lines ~121, 151, 179, 213, 249, 279) with the helper. Drop assertions on `IsActionable` from the layout itself — assert on `TaskGraphNodeResponse.IsActionable` after the mapper runs instead
- [x] 7.3 Same migration in `TaskGraphTextRendererTests.cs` (4 occurrences). Note that the test's `BuildTaskGraph` helper at line 12 calls `adapter.BuildTaskGraphLayoutAsync()` — that adapter no longer exists; change to `IIssueLayoutService.LayoutForTree(issues, InactiveVisibility.Hide)`
- [x] 7.4 Same migration in `GraphServiceHotPathLoggingTests.cs:48` and `GraphServiceEnhancedTaskGraphTests.cs:62`
- [x] 7.5 In `tests/Homespun.Tests/Features/Observability/GraphTracingTests.cs:131` and `IssueGraphOpenSpecEnricherNoFanOutTests.cs:54`, the constructions are of `TaskGraphNodeResponse` (the wire DTO, not the Fleece type) — these stay unchanged but verify the new `Edges` / `TotalRows` fields default to empty / 0 and don't break existing assertions
- [x] 7.6 In `tests/Homespun.Tests/Features/Testing/OpenSpecMockSeederBranchScenariosTests.cs:117, 139`, same `TaskGraphNodeResponse` construction — unchanged

## 8. Frontend layout rewrite

- [ ] 8.1 In `src/Homespun.Web/src/features/issues/services/task-graph-layout.ts`, drop the following fields from `TaskGraphIssueRenderLine`: `parentLane`, `isFirstChild`, `isSeriesChild`, `drawTopLine`, `drawBottomLine`, `seriesConnectorFromLane`, `drawLane0Connector`, `isLastLane0Connector`, `drawLane0PassThrough`, `lane0Color`, `hasHiddenParent`, `hiddenParentIsSeriesMode`, `multiParentIndex`, `multiParentTotal`, `isLastChild`, `hasParallelChildren`, `parentLaneReservations`. Add `appearanceIndex: number` and `totalAppearances: number`
- [x] 8.2 Add a new exported type `TaskGraphEdge` mirroring `TaskGraphEdgeResponse` from the generated client (literal `kind` union, literal `sourceAttach` / `targetAttach` unions)
- [x] 8.3 Rewrite `computeLayout` to: (a) iterate `taskGraph.nodes` in row order, emit one `TaskGraphIssueRenderLine` each, (b) emit `pr` / `separator` / `loadMore` lines around the issue list as before from `taskGraph.mergedPrs` / `taskGraph.hasMorePastPrs` / `taskGraph.mainOrphanChanges`, (c) return `{ lines: TaskGraphRenderLine[], edges: TaskGraphEdge[] }` (was: `TaskGraphRenderLine[]`)
- [ ] 8.4 Delete `renderGroup`, `renderGroupTreeView`, the `parentLaneReservations` post-pass (lines ~314-360), the multi-parent index pass, and the lane-0 connector synthesis. Net deletion target: ~700 LOC out of 1148
- [ ] 8.5 Update `task-graph-layout.test.ts` to assert: (a) one render line per `PositionedNode`, (b) `edges` collection is preserved verbatim from the response, (c) PR / separator / orphan synthesis still runs. Delete every assertion targeting the removed fields. Aim for the rewritten test file to be ~150 LOC
- [x] 8.6 In `src/Homespun.Web/src/features/issues/services/index.ts`, re-export `TaskGraphEdge` if external consumers need it

## 9. Frontend SVG renderer rewrite

- [x] 9.1 In `src/Homespun.Web/src/features/issues/components/task-graph-svg.tsx`, add a `<TaskGraphEdges edges={edges} />` component that takes `edges: TaskGraphEdge[]` and emits one `<path>` per edge. Path geometry switches on `edge.kind`:
  - `SeriesSibling`: vertical line `M ${startX} ${startY} L ${endX} ${endY}` (start.lane and end.lane may differ in IssueGraph mode)
  - `SeriesCornerToParent`: L-shape `M ${startX} ${startY} L ${pivotX} ${startY} L ${pivotX} ${endY} L ${endX} ${endY}` (or its mirror, depending on attach sides)
  - `ParallelChildToSpine`: L-shape via `pivotLane`, with attach side determining which face of the parent the path terminates at
- [x] 9.2 Replace the existing per-row connector inference (the `drawTopLine` / `drawBottomLine` / `seriesConnectorFromLane` consumers) with the new `<TaskGraphEdges>` mount. The connector layer renders **once** per graph, not per row
- [x] 9.3 Use `edge.sourceAttach` / `edge.targetAttach` (`Top` / `Bottom` / `Left` / `Right`) to pick the exact (x, y) coordinates inside the node bounding box: top = (centerX, top), bottom = (centerX, bottom), left = (left, centerY), right = (right, centerY)
- [x] 9.4 Add a colour map: edge stroke matches the `From` issue's type colour at full opacity. Reuse `getIssueTypeColor` already imported elsewhere
- [ ] 9.5 Drop dead helpers in `task-graph-svg.tsx` that only existed to draw inferred connectors per row
- [ ] 9.6 Add a Storybook story `task-graph-edges.stories.tsx` covering: (a) a series chain of 3 issues, (b) a parallel-children parent with 3 leaves, (c) a multi-parent fan-in (issue with 2 parents)

## 10. Frontend integration + manual verification

- [x] 10.1 Update `task-graph-view.tsx` to pass `edges` from `computeLayout`'s new return shape into the new `<TaskGraphEdges>` mount
- [ ] 10.2 Run from `src/Homespun.Web`: `npm run lint:fix && npm run format:check && npm run typecheck && npm test && npm run build-storybook` — all green
- [ ] 10.3 Boot `dotnet run --project src/Homespun.AppHost --launch-profile dev-mock`. Visually verify against seeded mock issues: (a) a parent with 3 series children renders the right-step staircase, (b) a parallel parent renders the spine + left-tees, (c) a multi-parent fan-in renders both edges to the child, (d) orphan-changes section still renders at the bottom, (e) PR rows still appear above the issue list
- [ ] 10.4 Boot `dev-live` (if you have a Claude OAuth token) and verify against a real Fleece project — at minimum, this repo's own `.fleece/` issues
- [ ] 10.5 Run the full e2e suite: `cd src/Homespun.Web && npm run test:e2e`. Investigate any visual diff failures (Playwright screenshot snapshots) — accept new snapshots **only** if the new render is visually correct on inspection

## 11. CLAUDE.md + docs

- [x] 11.1 Update the "Fleece" feature-slice bullet in the project root `CLAUDE.md` (search for "Version Sync Required") to mention that the Fleece.Core and Fleece.Cli versions are now 3.0.0 and the sync rule still applies
- [ ] 11.2 Optional: add `docs/gitgraph/fleece-v3-migration.md` summarising the wire-format additions (`Edges`, `TotalRows`) and the new `TaskGraphEdgeResponse` shape, for future reviewers
- [x] 11.3 Verify `docs/traces/dictionary.md` does not need updates — no new spans were added (existing `graph.taskgraph.build` and `graph.taskgraph.fleece.scan` cover the layout call). If you added new tags in step 4.5, document them in the dictionary entry for `graph.taskgraph.fleece.scan`

## 12. Pre-PR checklist

- [ ] 12.1 `dotnet test` — every backend suite green
- [ ] 12.2 `cd src/Homespun.Web && npm run lint:fix && npm run format:check && npm run generate:api:fetch && npm run typecheck && npm test && npm test:e2e && npm run build-storybook` — all green
- [ ] 12.3 `openspec validate upgrade-fleece-core-v3 --strict` — green
- [ ] 12.4 Read the diff one more time for any stray `using Fleece.Core.Models;` import that should now be `using Fleece.Core.Models.Graph;`
- [ ] 12.5 Confirm `Fleece.Core` 3.0.0 + `Fleece.Cli` 3.0.0 appear together in the diff (per the version-sync rule)
- [ ] 12.6 Hand off to the `phase-graph-rows` owner with a pointer to the rebased shape: `computeLayout` now returns `{ lines, edges }`; phase-line synthesis becomes a post-pass over `lines` (insert phase lines after their parent issue line, synthesise pseudo-edges with `kind: 'SeriesSibling'` and `sourceAttach: 'Bottom'` / `targetAttach: 'Top'`)
