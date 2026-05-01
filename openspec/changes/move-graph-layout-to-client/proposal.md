## Why

The Fleece.Core v3 migration (PR #802 + completion in PR #810) moved all task-graph layout — lane assignment, row emission, edge geometry — to the server, leaving the frontend as a thin renderer of pre-positioned nodes. This bought a single source of truth for layout but introduced two visible regressions and one persistent class of bug:

1. **Dynamic node insertion is hard.** When a client creates an issue, the server must invalidate its in-memory `ProjectTaskGraphSnapshotStore`, kick a fire-and-forget background rebuild (`ITaskGraphSnapshotRefresher`), and broadcast `IssuesChanged`. The client refetches the whole graph. There is a race window where the client's refetch can hit the server before the rebuild completes and receive stale layout. The split between `BroadcastIssueTopologyChanged` (full invalidate) and `BroadcastIssueFieldsPatched` (in-place snapshot patch) was added to amortise this for non-topology edits, but topology mutations remain laggy.
2. **Curved edges regressed.** Pre-#802, the frontend rendered orthogonal connectors with arc-rounded corners. The post-#810 SVG renderer draws strictly rectilinear paths from server-supplied geometry. Reintroducing curves on the current architecture would require either re-encoding curve metadata in `TaskGraphEdgeResponse` or adding a curve-decoration pass in the renderer — extra coupling for a purely visual concern.
3. **Snapshot/invalidate machinery is heavy.** `IProjectTaskGraphSnapshotStore`, `ITaskGraphSnapshotRefresher`, the patch/topology event split, the `PatchableFieldAttribute` reflection on issue DTOs, and the per-project semaphore-serialised refresh — all of it exists only because the server caches a *positioned* response. If the server stops positioning, all of it goes away.

Inverting the split — server filters issues, client owns layout — eliminates the race window (no laid-out cache to be stale), restores the frontend's freedom to pick its own edge geometry, and collapses the SignalR event surface to a single `IssueChanged` event with idempotent client-side merging. The pre-#802 frontend already proved a TypeScript layout port is feasible (~1148 lines for an earlier algorithm). Fleece.Core v3's `GraphLayoutService` + `IssueLayoutService` are well-factored and ready to be ported.

## What Changes

**Server (Homespun.Server):**

- **Modify existing** `GET /api/projects/{projectId}/issues` (currently returns the unfiltered issue list filtered by optional `status`/`type`/`priority` query params) so its default behavior returns the *visible* issue set:
  - All issues with `Status ∈ { Draft, Open, Progress, Review }`.
  - Plus every ancestor of any open issue (a closed/complete/archived issue is kept iff at least one of its descendants is open).
  - Plus every issue id in `include=<id>,<id>` (with their ancestors).
  - Plus, when `includeOpenPrLinked=true`, every issue linked to an open PR (with ancestors).
  - **Issues only.** No decorations bundled into this response.
  - Existing `status`/`type`/`priority` query params apply *after* visibility filtering.
  - New `includeAll=true` query param bypasses visibility filtering for any internal caller that needs the raw list. Backwards-compatible escape hatch.
- **New endpoint** `GET /api/projects/{projectId}/linked-prs` → `Dictionary<string, LinkedPr>` keyed by issue id. Independent fetch for the linked-PR decoration map. Source: `dataStore.GetPullRequestsByProject(projectId)` filtered by `FleeceIssueId` + `GitHubPRNumber`.
- **New endpoint** `GET /api/projects/{projectId}/agent-statuses` → `Dictionary<string, AgentStatusData>` keyed by issue id. Source: `sessionStore.GetByProjectId(projectId)` grouped by `EntityId`, mapped via the existing `CreateAgentStatusData` helper.
- **New endpoint** `GET /api/projects/{projectId}/openspec-states` → `Dictionary<string, IssueOpenSpecState>` keyed by issue id. Source: `IIssueGraphOpenSpecEnricher` (extracted into a per-issue-state-only method). Decoupled from orphan changes.
- **New endpoint** `GET /api/projects/{projectId}/orphan-changes` → `List<SnapshotOrphan>`. Source: same enricher, but only the main-branch orphan section. Decoupled from per-issue OpenSpec states.
- **Reuse existing** `GET /api/projects/{projectId}/pull-requests/merged?max=N` for the merged-PR history that next-mode renders. Confirm response shape covers `Number`, `Title`, `Url`, `IsMerged`, `HasDescription`; if not, add adaptor mapping or extend the response.
- **Delete** `GET /api/graph/{projectId}/taskgraph/data` (laid-out JSON), `GET /api/graph/{projectId}/taskgraph` (text), `POST /api/graph/{projectId}/refresh`, `GET /api/graph/{projectId}/cached`.
- **Delete** `IProjectTaskGraphSnapshotStore`, `ITaskGraphSnapshotRefresher`, all snapshot-related types, the per-project semaphore registry, and the `5-minute idle eviction` / `10-second refresh tick` timing knobs.
- **Delete** `BroadcastIssueTopologyChanged` and `BroadcastIssueFieldsPatched` extension methods. Replace with a single `BroadcastIssueChanged(projectId, kind, issueId, issue?)` extension that emits one SignalR event.
- **Delete** `PatchableFieldAttribute` and the reflection-driven patch-field detection in `IssuesController`.
- **Delete** `ProjectFleeceService.GetTaskGraphWithAdditionalIssuesAsync` and the `IIssueLayoutService` injection on the server. Drop the DI registration.
- **Keep** Fleece.Core's `IIssueLayoutService` available as a transitive dependency only — server code stops calling it.

**Web (Homespun.Web):**

- **New module** `src/features/issues/services/layout/` — TypeScript port of Fleece.Core v3's `GraphLayoutService<TNode>` and `IssueLayoutService` (LayoutForTree + LayoutForNext entry points, occupancy-grid edge routing, cycle detection result type, `EdgeKind` / `EdgeAttachSide` enums).
- **New test harness** `tests/Homespun.Web.LayoutFixtures/` — a small .NET project that loads JSON issue sets, runs the live `IIssueLayoutService.LayoutForTree` / `LayoutForNext` from Fleece.Core, and writes `GraphLayout<Issue>` results to JSON fixtures. The TS port's tests load the same JSON inputs, run the TS layout, and diff against the fixture outputs. Run on every Fleece.Core upgrade to detect drift.
- **Replace** `task-graph-layout.ts` (current 252-line thin pass-through) with a layout-driving wrapper that composes the new module's outputs with Homespun-only rows (PR rows, separators, "load more" affordances).
- **Replace** `task-graph-svg.tsx` edge rendering — strictly orthogonal-with-arc-corners. Each edge corner SHALL render as a small arc (default radius ~6px) instead of a hard right angle. No bezier curves; lane fidelity is preserved.
- **Replace** `useTaskGraph` hook with `useIssues(projectId)` — initial GET against the new endpoint, SignalR subscription to `IssueChanged`, idempotent merge into a TanStack Query cache keyed by `[issues, projectId]`. Reconnect → refetch.
- **Delete** `apply-patch.ts` and the `PatchableField` machinery on the client side; `useIssues` performs whole-issue replace-by-id on every event regardless of "what changed."
- **Tree ↔ Next ViewMode toggle becomes a pure client transformation** — re-runs the layout against the cached issue set with different params; no refetch.

**Tests:**

- API integration tests for the modified `GET /api/projects/{projectId}/issues` endpoint covering: open-only filter, ancestor-of-open inclusion, `include` override (with ancestor pull-in), `includeOpenPrLinked` override, `includeAll=true` opt-out preserves raw list, cycle in parent chain (returns issues, no exception), empty project, large project (>500 issues).
- API integration tests for each new decoration endpoint independently: `/linked-prs`, `/agent-statuses`, `/openspec-states`, `/orphan-changes`. Each test fixture sets up only the data relevant to that decoration, exercising the endpoint in isolation.
- Server unit tests for the ancestor-of-open traversal (visited-set cycle safety, multi-parent handling).
- TS unit tests for `graph-layout-service.ts` and `issue-layout-service.ts` covering: lane assignment, row assignment, multi-parent appearance counts, series vs parallel sequencing, cycle detection result, empty-input case, `LayoutForNext` ancestor inclusion.
- Cross-stack golden-fixture tests: ≥10 fixture inputs covering the same scenarios, diffed structurally between C# reference and TS port.
- Storybook entries for the arc-cornered edge renderer demonstrating each `EdgeKind`.
- Playwright E2E test: create issue mid-session, assert it appears in the graph within 1s without a network refetch.

## Capabilities

### New Capabilities

None. All work modifies existing capabilities.

### Modified Capabilities

- **`fleece-issue-tracking`**: REMOVES the server-side layout requirement and the laid-out `TaskGraphResponse` semantic-edges requirement. ADDS the visible-issue-set endpoint contract, the client-side TS layout requirement, the arc-cornered edge rendering requirement, the unified `IssueChanged` event requirement, and the idempotent client-merge semantics.
- **`openspec-integration`**: REMOVES the per-project task-graph snapshot store, the background snapshot refresher, and the "Fleece mutations invalidate the task-graph snapshot before broadcasting" requirement (the entire invalidation contract collapses because there is no snapshot to invalidate). MODIFIES the "branch and change status indicators" requirement so that decorations are served as a sibling map on the issue-set response rather than via a per-project snapshot.

## Impact

**Server code (delete):**

- `Homespun.Server.Features.Gitgraph.Snapshots.ProjectTaskGraphSnapshotStore` and `IProjectTaskGraphSnapshotStore`
- `Homespun.Server.Features.Gitgraph.Snapshots.TaskGraphSnapshotRefresher` and `ITaskGraphSnapshotRefresher`
- `Homespun.Server.Features.Gitgraph.Controllers.GraphController` (most endpoints; the controller may survive as an empty file pending deletion or be removed entirely)
- `Homespun.Server.Features.Gitgraph.Services.GraphService.BuildEnhancedTaskGraphAsync`
- `Homespun.Server.Features.Fleece.Services.ProjectFleeceService.GetTaskGraphWithAdditionalIssuesAsync` and the `IIssueLayoutService` injection
- `Homespun.Server.Features.Notifications.NotificationHubExtensions.BroadcastIssueTopologyChanged` and `BroadcastIssueFieldsPatched`
- `Homespun.Shared.Models.Fleece.PatchableFieldAttribute`
- `Homespun.Shared.Models.Fleece.TaskGraphResponse`, `TaskGraphNodeResponse`, `TaskGraphEdgeResponse` (laid-out wrapper types). `TaskGraphPrResponse`, `TaskGraphLinkedPr`, `IssueOpenSpecState`, and `SnapshotOrphan` are *retained* and become the response types of the per-decoration endpoints (renamed to drop the `TaskGraph` prefix where appropriate, e.g. `LinkedPr`).
- The reflection-driven patch-field collector inside `IssuesController` (~80 lines)

**Server code (add / modify):**

- Modify `IssuesController.GetByProject` (existing `GET /api/projects/{projectId}/issues`) — apply the visible-set filter by default; add `include`, `includeOpenPrLinked`, `includeAll` query params.
- New `IssueAncestorTraversalService` (or method on `ProjectFleeceService`) — pure parent-chain walk with visited-set cycle safety.
- New decoration endpoints on `IssuesController` (or per-feature controllers — `LinkedPrsController`, `AgentStatusesController`, `OpenSpecDecorationsController`): `/linked-prs`, `/agent-statuses`, `/openspec-states`, `/orphan-changes`.
- Refactor `IIssueGraphOpenSpecEnricher` into two narrower methods: `GetOpenSpecStatesAsync(projectId, issueIds, branchContext) → Dictionary<issueId, IssueOpenSpecState>` and `GetMainOrphanChangesAsync(projectId, branchContext) → List<SnapshotOrphan>`. Drop the combined `EnrichAsync(response)` shape.
- New `BroadcastIssueChanged` extension method — single replacement for the two old methods.
- Modify all existing `IssuesController` mutation endpoints to call the unified helper.
- Modify `IssuesAgentController.AcceptChangesAsync`, `ChangeReconciliationService.ReconcileAsync`, `FleeceIssueSyncController.{Sync,Pull,DiscardNonFleeceAndPull}`, `AgentStartBackgroundService.StartAgentAsync`, `IssuesAgentController.CreateSession`, `ProjectClonesController.{Create,Delete,BulkDelete,Prune}`, `PRStatusResolver.ResolveClosedPRStatusesAsync` — they all currently call `BroadcastIssueTopologyChanged`. Each becomes a call to `BroadcastIssueChanged` with the appropriate kind.

**Web code (add):**

- `src/features/issues/services/layout/graph-layout-service.ts` (~400 lines)
- `src/features/issues/services/layout/issue-layout-service.ts` (~250 lines)
- `src/features/issues/services/layout/edge-router.ts` (~150 lines, occupancy walk + pivot routing)
- `src/features/issues/services/layout/types.ts` (PositionedNode, Edge, EdgeKind, GraphLayout, GraphSortConfig, LayoutResult discriminated union)
- `src/features/issues/services/layout/index.ts` (public exports)
- `src/features/issues/services/layout/*.test.ts` (algorithmic unit tests)
- `src/features/issues/services/layout/golden-fixtures.test.ts` (cross-stack diff)
- `tests/Homespun.Web.LayoutFixtures/` — .NET fixture-emitter project (regenerable via `dotnet test --filter Category=Fixtures` with `UPDATE_FIXTURES=1`).
- New `useIssues(projectId)` hook in `src/features/issues/hooks/`.

**Web code (modify):**

- `src/features/issues/services/task-graph-layout.ts` — rewrites against the new layout module; produces render lines from layout output rather than thin-passthrough server data.
- `src/features/issues/components/task-graph-view.tsx` — switches data source from `useTaskGraph` to `useIssues`; reads laid-out output from `task-graph-layout.ts`.
- `src/features/issues/components/task-graph-svg.tsx` — `buildEdgePath` rewritten for arc-cornered orthogonal paths.
- `src/features/issues/lib/apply-patch.ts` — deleted; replaced by uniform replace-by-id in the SignalR handler.
- `src/features/issues/hooks/useTaskGraph.ts` — deleted (or kept as a thin re-export alias for one cycle, see design.md).

**Tests (delete):**

- `tests/Homespun.Tests/Features/Gitgraph/Snapshots/*` — the entire snapshot test surface goes away.
- `tests/Homespun.Tests/Features/Notifications/HubHelperInvalidationOrderTests.cs` — invalidation no longer exists.
- API tests targeting `/api/graph/{projectId}/taskgraph*` endpoints.
- Tests for `BroadcastIssueTopologyChanged` / `BroadcastIssueFieldsPatched` order.
- Tests for `PatchableFieldAttribute` detection.

**Tests (add):**

- API integration tests for `GET /api/projects/{projectId}/issues` (see "Tests" above in What Changes).
- Server unit tests for ancestor traversal.
- TS layout unit tests.
- Cross-stack golden-fixture diff tests.
- Playwright E2E for dynamic-insert UX.

**Docs:**

- Delete `docs/gitgraph/taskgraph-snapshot.md`.
- Add `docs/graph-layout-client-side.md` covering: the algorithm port's structure, the golden-fixture workflow, how to regenerate fixtures on a Fleece.Core upgrade, the edge-rendering semantics, and the unified `IssueChanged` event contract.
- Update `CLAUDE.md` "Feature Slices → Fleece" section to reflect that the server no longer runs layout for the JSON path.

**Breaking API changes:**

- `GET /api/graph/{projectId}/taskgraph/data` is deleted. No external consumer exists outside the web client.
- `GET /api/graph/{projectId}/taskgraph` (text) is deleted. No external consumer exists outside CLI tooling — confirmed unused before deletion (Task 1.3).
- The `TaskGraphResponse` DTO is deleted; replaced by a new `IssueSetResponse` DTO on the new endpoint.
- The `IssuesChanged` SignalR event signature changes: payload becomes `{ kind: 'created' | 'updated' | 'deleted', projectId: string, issueId: string, issue?: Issue }` instead of the current positional `(projectId, IssueChangeType, issueId)` plus the parallel `IssueFieldsPatched` event.

**Performance:**

- Server CPU per request drops (no layout pass).
- Network payload per request: comparable in size — issue list with decorations vs nodes-with-coords.
- Client CPU per render rises modestly; the pre-#802 layout engine handled this without complaint and the v3 algorithm is comparable in cost. Worst case (~500 issues in next-mode on a phone): expected <100ms layout time. Not pre-optimized; flagged with a `// TODO: revisit if N > 2000` comment in the layout module.

**Migration sequence:** within the single change, implement in the order documented in `tasks.md` so the server endpoint and TS port land before the old endpoint is deleted. The branch is held against `main` and merged in one PR to avoid a half-migrated mid-state.
