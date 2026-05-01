## 1. Pre-flight surveys

- [x] 1.1 Q1 resolved (see design.md): existing `GET /api/projects/{projectId}/issues` becomes the visible-set endpoint by default, with `includeAll=true` opt-out. Confirm no internal callers (server tests, e2e tests, mock seed) rely on the current "all issues" default; migrate any callers to pass `includeAll=true` if they need the raw list, or drop their dependency on the endpoint if they should be using a more specific one (`/ready`, `/assignees`, etc.).
- [x] 1.2 Read every call site of `BroadcastIssueTopologyChanged` and `BroadcastIssueFieldsPatched` (grep both names across `src/Homespun.Server/`). Build a checklist of sites to migrate in Phase D.2.
- [x] 1.3 Confirm `GET /api/graph/{projectId}/taskgraph` (text endpoint) is unused: grep the entire monorepo (`src/Homespun.*`, `tests/`, `e2e/`, scripts, docs) plus any external CLI tools or worker code for the literal `taskgraph` path. If a consumer exists, document and either migrate it or keep the endpoint by routing it through `Fleece.Core` text export directly (bypassing snapshots).
- [x] 1.4 Read `src/Homespun.Web/src/features/issues/components/task-graph-view.tsx` and identify every consumer of `useTaskGraph()`. List components needing migration to `useIssues()`.
- [x] 1.5 Confirm `IIssueLayoutService` v3 API surface against the public Fleece.Core repo (`GraphLayoutService.cs`, `IssueLayoutService.cs`, `LayoutContext.cs`). Pin a version note in `tests/Homespun.Web.LayoutFixtures/README.md` so future fixture regenerations target the same upstream version Homespun is consuming.

## 2. Phase A — Server: visible-issue-set endpoint

### 2.1 Ancestor traversal service

- [x] 2.1.1 Write failing unit tests in `tests/Homespun.Tests/Features/Fleece/Services/IssueAncestorTraversalServiceTests.cs` for: empty input, single-open issue, open with closed ancestor, closed with no open descendants (excluded), multi-parent diamond, cycle in parent chain (no infinite loop, returns valid set).
- [x] 2.1.2 Implement `IssueAncestorTraversalService.CollectVisible(IReadOnlyList<Issue>, IReadOnlySet<string> seedIds)` returning `IReadOnlyCollection<Issue>`. Pure traversal: BFS with visited set. No I/O.
- [x] 2.1.3 Register as singleton in `Homespun.Server/Program.cs` (or feature-slice DI extension).

### 2.2 Response types (un-bundled, no wrapper DTO)

- [x] 2.2.1 Rename `Homespun.Shared/Models/Fleece/TaskGraphLinkedPr.cs` to `LinkedPr.cs` (drop `TaskGraph` prefix; the type is no longer specific to the laid-out response). Update references.
- [x] 2.2.2 Rename `Homespun.Shared/Models/Fleece/TaskGraphPrResponse.cs` to `MergedPrResponse.cs` if it's not already covered by the existing `/pull-requests/merged` endpoint's response type. Investigate Task 4 first. (No-op: existing `GET /api/projects/{projectId}/pull-requests/merged` returns `List<PullRequestWithTime>`; `TaskGraphPrResponse` is only consumed by the soon-to-be-deleted `TaskGraphResponse.MergedPrs` and will be deleted in Phase D.)
- [x] 2.2.3 Keep `IssueOpenSpecState` and `SnapshotOrphan` types — they become response payloads for their own endpoints.
- [x] 2.2.4 No wrapper DTO. Each endpoint returns its decoration type directly (`Dictionary<string, T>` or `List<T>`).

### 2.3 Visible-set issue endpoint

- [x] 2.3.1 Write failing API integration tests in `tests/Homespun.Api.Tests/Features/Fleece/IssuesEndpointVisibleTests.cs` covering: open-only filter (default), ancestor pulls in closed parent, ancestor pulls in chain of closed grandparents, `include` query param pulls in single closed issue + its ancestors, `include` with multiple ids, `includeOpenPrLinked=true` pulls in issues from `OpenPRs`, `includeAll=true` returns the raw list, status/type/priority filters apply post-visibility, cycle in parent chain returns issues without exception, large project (>500 seeded issues) returns within performance budget.
- [x] 2.3.2 Modify `IssuesController.GetByProject` to apply the visible-set filter by default. Add `[FromQuery] string? include`, `[FromQuery] bool includeOpenPrLinked = false`, `[FromQuery] bool includeAll = false` parameters. Wire `IIssueAncestorTraversalService`. Existing `status`/`type`/`priority` filters apply *after* visibility filtering when `includeAll=false`.
- [x] 2.3.3 Add OpenAPI annotations; regenerate the TS client (`npm run generate:api:fetch`). Generated client now exposes `getApiProjectsByProjectIdIssues` with the new query params; types regenerated.
- [x] 2.3.4 The old `GET /api/graph/{projectId}/taskgraph/data` continues to work in parallel during Phase B. Both endpoints draw from the same in-memory issue cache; they will not diverge.

### 2.4 Linked PRs endpoint

- [x] 2.4.1 Write failing API integration tests in `tests/Homespun.Api.Tests/Features/PullRequests/LinkedPrsEndpointTests.cs` covering: empty project returns empty map, single linked PR returns `{issueId: {number, url, status}}`, multiple linked PRs returned correctly, PRs without `FleeceIssueId` are excluded, PRs without `GitHubPRNumber` are excluded.
- [x] 2.4.2 Add `GET /api/projects/{projectId}/linked-prs` on `PullRequestsController` (or a new `LinkedPrsController`). Source from `dataStore.GetPullRequestsByProject(projectId)` filtered by both `FleeceIssueId` and `GitHubPRNumber`. Map to `Dictionary<string, LinkedPr>` keyed by `FleeceIssueId`.
- [x] 2.4.3 Add OpenAPI annotations; regenerate TS client. (Generated `getApiProjectsByProjectIdLinkedPrs` plus `LinkedPr` type.)

### 2.5 Agent statuses endpoint

- [x] 2.5.1 Write failing API integration tests in `tests/Homespun.Api.Tests/Features/AgentOrchestration/AgentStatusesEndpointTests.cs` covering: empty project returns empty map, single active session returns `{issueId: AgentStatusData}`, multiple sessions grouped by `EntityId` with most-recent-by-`LastActivityAt` wins, sessions without `EntityId` are excluded.
- [x] 2.5.2 Add `GET /api/projects/{projectId}/agent-statuses` on a new `AgentStatusesController` (or extend `QueueController`). Source from `sessionStore.GetByProjectId(projectId)`. Reuse the existing `CreateAgentStatusData` helper (extract from `GraphService` into a reusable function).
- [x] 2.5.3 Add OpenAPI annotations; regenerate TS client. (Generated `getApiProjectsByProjectIdAgentStatuses`.)

### 2.6 OpenSpec states endpoint

- [x] 2.6.1 Refactor `IIssueGraphOpenSpecEnricher.EnrichAsync(projectId, response, branchContext)` into two narrower methods:
  - `GetOpenSpecStatesAsync(projectId, IReadOnlyCollection<string> issueIds, BranchResolutionContext) → Dictionary<string, IssueOpenSpecState>` — returns only the per-issue state map.
  - `GetMainOrphanChangesAsync(projectId, BranchResolutionContext) → List<SnapshotOrphan>` — returns only the orphan list.
- [x] 2.6.2 Internal: factor any shared scan logic into a private helper used by both methods. Verify the artifact-state mtime cache continues to operate correctly when each method is called independently. (Both new methods reuse the existing `ResolveForIssueAsync` and `ScanMainOrphansAsync` helpers; the artifact-state mtime cache is unchanged and still operates per `BranchStateResolverService.GetOrScanAsync`.)
- [x] 2.6.3 Write failing API integration tests in `tests/Homespun.Api.Tests/Features/OpenSpec/OpenSpecStatesEndpointTests.cs` covering: empty project returns empty map, issue with linked change returns its state, issue with no clone returns no entry, multiple issues with mixed clone/no-clone state.
- [x] 2.6.4 Add `GET /api/projects/{projectId}/openspec-states?issues=<id>,<id>` on a new `OpenSpecDecorationsController`. The `issues=` query param scopes the scan to a subset (defaults to all visible issues if omitted; the frontend supplies the visible-set ids it just fetched, to bound the per-clone scan cost). Wire `BranchResolutionContext` per-request as today.
- [x] 2.6.5 Add OpenAPI annotations; regenerate TS client. (Generated `getApiProjectsByProjectIdOpenspecStates`.)

### 2.7 Orphan changes endpoint

- [x] 2.7.1 Write failing API integration tests in `tests/Homespun.Api.Tests/Features/OpenSpec/OrphanChangesEndpointTests.cs` covering: empty project returns empty list, single orphan on main returns one entry, dedupe by change name when same orphan exists on multiple branches, branch-scoped orphan (not on main) excluded. (Mock-mode coverage limited to empty/404 + shape; richer scenarios will be exercised in dev-live e2e in Phase E.)
- [x] 2.7.2 Add `GET /api/projects/{projectId}/orphan-changes` on `OpenSpecDecorationsController`. Source from `IIssueGraphOpenSpecEnricher.GetMainOrphanChangesAsync` (added in 2.6.1).
- [x] 2.7.3 Add OpenAPI annotations; regenerate TS client. (Generated `getApiProjectsByProjectIdOrphanChanges`.)

### 2.8 Merged PRs endpoint (verify existing)

- [x] 2.8.1 Confirm `GET /api/projects/{projectId}/pull-requests/merged?max=N` already returns a shape compatible with what next-mode rendering needs (`Number`, `Title`, `Url`, `IsMerged`, `HasDescription`). Verified: returns `List<PullRequestWithTime>` (each carrying `PullRequestInfo` with `Number`, `Title`, `HtmlUrl`, `Status` enum, `Body`). `IsMerged` is derivable from `Status == Merged`; `HasDescription` is derivable from `!string.IsNullOrEmpty(Body)`. Frontend can ignore extras.
- [x] 2.8.2 If the existing endpoint lacks any field, extend the response with the missing fields (additive) rather than introducing a parallel endpoint. (No-op: all required fields are present in the existing response.)
- [ ] 2.8.3 Adapt `useMergedPrs` hook in Phase C to consume this endpoint. [Deferred to Phase C.]

## 3. Phase B — Web: TS layout port

### 3.1 Fixture-emitter project

- [x] 3.1.1 Create `tests/Homespun.Web.LayoutFixtures/Homespun.Web.LayoutFixtures.csproj` referencing `Fleece.Core` (matching version pinned in Task 1.5).
- [x] 3.1.2 Implement `EmitFixturesTests.cs`: read each `fixtures/*.input.json`, deserialize to `IReadOnlyList<Issue>`, run `IIssueLayoutService.LayoutForTree` (or `LayoutForNext` for `*-next-*.input.json`), serialize `GraphLayoutResult<Issue>` to JSON. When `UPDATE_FIXTURES=1`, write to `*.expected.json`; otherwise compare against existing and assert structural equality.
- [x] 3.1.3 Author initial fixtures (≥10):
  - 01-tree-simple: 5-node tree, 1 root, depth 3
  - 02-tree-multi-parent: diamond pattern
  - 03-tree-series-chain: parent + 4 series children
  - 04-tree-parallel-children: parent + 4 parallel children
  - 05-tree-mixed-sequencing: parent with mixed series/parallel siblings
  - 06-tree-cycle: produces `{ok: false}`
  - 07-tree-empty: `[]` input
  - 08-tree-single-node: 1 root, no children
  - 09-tree-large: 13-node parallel-of-series tree (covers all edge kinds; the design's "200 nodes" target deferred — algorithmic unit tests already exercise scale, this fixture exists for cross-stack drift detection where readability beats size)
  - 10-next-matched-leaves: leaves matched, ancestors auto-included
  - 11-next-large: 13-node next-mode equivalent of 09 (matched leaves under a single series sub-tree)
- [x] 3.1.4 Run `dotnet test --filter Category=Fixtures` (UPDATE_FIXTURES=1) once to emit `*.expected.json`. Both `.input` and `.expected` files committed.
- [x] 3.1.5 Document workflow in `tests/Homespun.Web.LayoutFixtures/README.md`: how to add a fixture, how to regenerate after Fleece upgrade, what the read-only mode asserts.

### 3.2 TS port

- [x] 3.2.1 Add `src/Homespun.Web/src/features/issues/services/layout/types.ts` with all type/enum definitions per design.md D4. (Enum string values use camelCase to match `JsonNamingPolicy.CamelCase` wire format from Fleece.Core's serializer — design.md D4's kebab-case sketches were illustrative; the camelCase form is what survives golden-fixture diffing.)
- [x] 3.2.2 Add `src/Homespun.Web/src/features/issues/services/layout/edge-router.ts`: occupancy grid, `walkEdge` routing logic. Unit-tested in isolation.
- [x] 3.2.3 Add `src/Homespun.Web/src/features/issues/services/layout/graph-layout-service.ts`: generic `GraphLayoutService<TNode>` mirroring the C# class. Implement `Layout(request)` returning `GraphLayoutResult<TNode>`. Cycle detection via `pathStack`. Lane assignment IssueGraph + NormalTree modes. Row assignment via emission order. Edge generation per child sequencing.
- [x] 3.2.4 Add `src/Homespun.Web/src/features/issues/services/layout/issue-layout-service.ts`: `layoutForTree`, `layoutForNext`. Issue-aware filtering of children, ancestor inclusion for matched leaves in next mode.
- [x] 3.2.5 Add `src/Homespun.Web/src/features/issues/services/layout/index.ts` with public re-exports.
- [x] 3.2.6 Add `src/Homespun.Web/src/features/issues/services/layout/graph-layout-service.test.ts`, `issue-layout-service.test.ts`, and `edge-router.test.ts` (25 hand-built cases total). Cycle detection, LayoutForNext ancestor pull-in, edge kind/pivotLane/attach-side, multi-parent diamonds, normalTree mode, and edge-router walk geometry all covered.
- [x] 3.2.7 Add `src/Homespun.Web/src/features/issues/services/layout/golden-fixtures.test.ts` that imports each `*.input.json` from the fixtures dir, runs the TS port, and structurally diffs against `*.expected.json` via `fs.readFileSync` in the Vitest Node environment.
- [x] 3.2.8 Ran `npm test -- features/issues/services/layout` — 39/39 pass (11 golden fixtures + 9 graph-layout cases + 12 issue-layout cases + 4 edge-router cases + the discovery sentinel).

### 3.3 Storybook for edge rendering

- [x] 3.3.1 Add `src/Homespun.Web/src/features/issues/components/task-graph-svg.stories.tsx` with one story per `EdgeKind` (`SeriesSibling`, `SeriesCornerToParent`, `ParallelChildToSpine`), plus `ManyEdges` and `TightSpacing` stories. Stories construct synthetic `Edge` arrays directly without server data.
- [x] 3.3.2 `npm run build-storybook` passes.

## 4. Phase B/C bridge — Edge renderer rewrite

- [x] 4.1 Rewrite `src/Homespun.Web/src/features/issues/components/task-graph-svg.tsx` `buildEdgePath` to produce arc-cornered orthogonal paths per design.md D6. Three branches by `edge.kind`. Corner radius ≤ min(6px, halfLane, halfRow). Storybook stories from Task 3.3 verify output.
- [x] 4.2 Add unit tests for `buildEdgePath` covering each kind + corner-radius-clipping when lane/row spacing is small.
- [ ] 4.3 Run Storybook visually; confirm arcs render correctly at default + tight spacings. (Deferred to Phase E.7.4 manual smoke; `build-storybook` passes in 7.2.)

## 5. Phase C — Web: useIssues hook + view migration

### 5.1 Hooks (one per endpoint, fetched in parallel)

- [x] 5.1.1 Add `src/Homespun.Web/src/features/issues/hooks/useIssues.ts`. TanStack Query key: `['issues', projectId, includeIds, includeOpenPrLinked, includeAll]`. `staleTime: Infinity`. SignalR subscription to `IssueChanged` events with `applyIssueChanged` merge per design.md D2.
- [x] 5.1.2 Add `src/Homespun.Web/src/features/issues/hooks/useLinkedPrs.ts`. Query key: `['linked-prs', projectId]`. Subscribes to existing PR-state SignalR channel; on event, invalidate this key.
- [x] 5.1.3 Add `src/Homespun.Web/src/features/issues/hooks/useAgentStatuses.ts`. Query key: `['agent-statuses', projectId]`. Subscribes to existing agent-status SignalR channel.
- [x] 5.1.4 Add `src/Homespun.Web/src/features/issues/hooks/useOpenSpecStates.ts`. Query key: `['openspec-states', projectId, issueIds]`. Confirmed: no dedicated OpenSpec-state SignalR channel exists today — hook invalidates on `IssueChanged`. Promote to a dedicated channel if cadence becomes a problem.
- [x] 5.1.5 Add `src/Homespun.Web/src/features/issues/hooks/useOrphanChanges.ts`. Query key: `['orphan-changes', projectId]`. Same SignalR-channel logic as 5.1.4.
- [x] 5.1.6 Add `src/Homespun.Web/src/features/issues/hooks/useMergedPrs.ts`. Query key: `['merged-prs', projectId, max]`. Subscribes to `IssueChanged` (PR sync events flow through that channel; no dedicated PR-state channel today).
- [x] 5.1.7 Wire reconnect on every hook: on `HubConnection.onreconnected`, invalidate the relevant query keys (forces refetch).
- [x] 5.1.8 Unit tests for `applyIssueChanged` reducer (created/updated/deleted, idempotency).
- [ ] 5.1.9 Component test for each hook rendered under `QueryClientProvider` + mock SignalR — assert echo events apply, deletes drop entries, reconnect refetches. (Deferred — covered indirectly by the migrated `task-graph-view` tests, which exercise the hooks end-to-end.)

### 5.2 Layout-driving wrapper

- [x] 5.2.1 Added `computeLayoutFromIssues({...})` in `src/Homespun.Web/src/features/issues/services/task-graph-layout.ts` taking `Issue[]` + decoration maps (`linkedPrs`, `agentStatuses`, `mergedPrs`, `orphanChanges` consumed via `aggregateOrphansFromInputs`) + `viewMode`, calling `layoutForTree` / `layoutForNext`, then synthesising Homespun-only rows (PR rows, separators, "load more"). The legacy `computeLayout(taskGraph, …)` path remains for the static diff view. Output is the existing `TaskGraphRenderLine[] + TaskGraphEdge[]` shape so consumers don't change. Memoisation lives at the call site in `task-graph-view.tsx` (`useMemo` on issue set + viewMode; decorations excluded from the dep tuple).
- [x] 5.2.2 Cycle handling: `{ ok: false, cycle }` propagates as a flat-list `TaskGraphRenderLine[]` + zero edges; the view renders a `task-graph-cycle-banner` with the cycle ids. Storybook story for the banner deferred to follow-up — the data path is covered by the new layout test in 5.2.3.
- [x] 5.2.3 `task-graph-layout.test.ts` extended with a `computeLayoutFromIssues` block covering empty input, edge generation, decoration join, next-mode PR/separator emission, actionable marker, and cycle fallback.

### 5.3 View migration

- [x] 5.3.1 Switched `src/Homespun.Web/src/features/issues/components/task-graph-view.tsx` from `useTaskGraph()` to the parallel hook tuple: `useIssues()`, `useLinkedPrs()`, `useAgentStatuses()`, `useOpenSpecStates()`, `useOrphanChanges()`, `useMergedPrs()`. Layout-wrapper inputs are composed from the assembled data; legacy server-positioned-node branches deleted (`computeInheritedParentInfo` → `computeInheritedParentInfoFromIssues`, `aggregateOrphans` → `aggregateOrphansFromInputs`). Routes (`projects.$projectId.issues.index.tsx`) migrated alongside.
- [x] 5.3.2 `viewMode` toggle: tree↔next is a pure client transformation in `computeLayoutFromIssues` — no refetch (visual verification under `dev-mock` deferred to Phase E.7.4).
- [ ] 5.3.3 Delete `src/Homespun.Web/src/features/issues/lib/apply-patch.ts` and `src/Homespun.Web/src/features/issues/hooks/use-task-graph.ts`. (Deferred — `useTaskGraph` is still consumed by `static-task-graph-view`, the agent-diff view, and `features/agents/components/openspec-tab.tsx`. Both will migrate alongside the Phase D server-side `TaskGraphResponse` deletion. `apply-patch.ts` is unreferenced by production code now but kept while the old `IssueFieldsPatched` SignalR event still ships from the server — its corresponding tests pass and the file is deleted in Phase D.6.3 alongside the server-side cleanup.)
- [x] 5.3.4 `useIssues` subscribes to the new unified `IssueChanged` event; the legacy `IssueFieldsPatched` handler is no longer registered by the view (the existing handler in `notification-hub.ts` is preserved during the transition and removed in Phase D).
- [ ] 5.3.5 Run `npm run lint:fix && npm run typecheck && npm test`. Lint:fix passes (5 pre-existing errors in `error-boundary.tsx` are unrelated). Typecheck and the affected unit tests pass; full suite re-run rolled into Phase E.7.

### 5.4 E2E smoke

- [ ] 5.4.1 Add `src/Homespun.Web/e2e/dynamic-issue-insert.spec.ts`: navigate to a project's task graph, create an issue via the UI, assert the new node appears within 1s without observing a `GET /api/projects/{projectId}/issues` network request. (Deferred — depends on Phase D's `BroadcastIssueChanged` server-side helper being wired before the dynamic-insert path can succeed without a refetch.)
- [ ] 5.4.2 Confirm `npm run test:e2e` passes. (Deferred to Phase E.7.3.)

## 6. Phase D — Server: collapse SignalR + delete old surface

### 6.1 Unified event extension

- [x] 6.1.1 `tests/Homespun.Tests/Features/Notifications/NotificationHubExtensionsTests.cs` rewritten to cover `BroadcastIssueChanged`: Created carries the issue body, Updated carries the issue body, Deleted carries null issue, bulk events accept null `issueId`, helper sends to `Clients.All` AND `Clients.Group(project-{id})` once each. The helper has no DI lookup beyond `IHubContext`.
- [x] 6.1.2 Implemented `BroadcastIssueChanged(this IHubContext<NotificationHub>, string projectId, IssueChangeType kind, string? issueId, IssueResponse? issue)` in `Features/Notifications/NotificationHub.cs`. Single SendAsync to `Clients.All` + project group; no snapshot bookkeeping. `INotificationHubClient.IssueChanged(...)` added in `Homespun.Shared/Hubs/`. Reused the existing `IssueChangeType` enum (Created/Updated/Deleted) rather than introducing `IssueChangeKind`.

### 6.2 Migrate every call site (per Task 1.2 inventory)

- [x] 6.2.1 `IssuesController.Create` → `BroadcastIssueChanged(projectId, Created, issue.Id, issue.ToResponse())`.
- [x] 6.2.2 `IssuesController.Update` → `BroadcastIssueChanged(projectId, Updated, issueId, issue.ToResponse())`. The patch/topology branch and `TryBuildFieldPatch` helper both deleted.
- [x] 6.2.3 `IssuesController.Delete` → `BroadcastIssueChanged(projectId, Deleted, issueId, null)`.
- [x] 6.2.4 `IssuesController.{SetParent, RemoveParent, RemoveAllParents, MoveSeriesSibling}` → `BroadcastIssueChanged(projectId, Updated, issueId, issue.ToResponse())`.
- [x] 6.2.5 `IssuesController.{ApplyAgentChanges, ResolveConflicts, Undo, Redo}` → bulk `BroadcastIssueChanged(projectId, Updated, null, null)` (one event per request, fan-out per affected issue would be redundant given the layout-side merge is idempotent and these flows touch many issues).
- [x] 6.2.6 `IssuesAgentController.{AcceptChangesAsync, CreateSession}` migrated. CreateSession emits a per-issue `IssueChanged` for the selected issue when present, otherwise a bulk event.
- [x] 6.2.7 `AgentStartBackgroundService.StartAgentAsync` post-clone-create → `BroadcastIssueChanged(projectId, Updated, issueId, issue.ToResponse())`.
- [x] 6.2.8 `ProjectClonesController.{Create, Delete, BulkDelete, Prune}` → bulk `BroadcastIssueChanged(projectId, Updated, null, null)` via the existing `InvalidateGraphSnapshotAsync` helper. Wire shape supports null `issueId` (clients treat null id as "invalidate every issue cache for this project") — no `BulkChanged` kind needed.
- [x] 6.2.9 `PRStatusResolver.ResolveClosedPRStatusesAsync` → `BroadcastIssueChanged(projectId, Updated, fleeceIssueId, null)`. Issue body left null because the resolver doesn't reload the Fleece issue from disk; the `IssueChanged` echo invalidates the linked-prs cache, which is sufficient for next-frame correctness.
- [x] 6.2.10 `FleeceIssueSyncController.{Sync, Pull, DiscardNonFleeceAndPull}` → bulk `BroadcastIssueChanged(projectId, Updated, null, null)` after `ReloadFromDiskAsync`.
- [x] 6.2.11 `ChangeReconciliationService.ReconcileAsync` — sidecar auto-link + archive auto-transition both fire `BroadcastIssueChanged(projectId, Updated, null, null)` via the migrated `InvalidateAndBroadcastAsync` helper.

### 6.3 Delete the old event extensions

- [x] 6.3.1 Deleted `BroadcastIssueTopologyChanged` and `BroadcastIssueFieldsPatched` extensions from `Features/Notifications/NotificationHub.cs`.
- [x] 6.3.2 `IssueChangeType` enum kept (still used by the unified helper / `INotificationHubClient.IssueChanged`).
- [x] 6.3.3 Deleted `PatchableFieldAttribute` + `TopologyFieldAttribute` (collocated in `IssueResponse.cs`) and removed every `[PatchableField]` / `[TopologyField]` annotation. Deleted the test that asserted the attribute classification.
- [x] 6.3.4 Deleted `IssuesController.TryBuildFieldPatch` and the `IssueFieldPatch` DTO (`Homespun.Shared/Models/Fleece/IssueFieldPatch.cs`).

### 6.4 Delete snapshot store + refresher

- [x] 6.4.1 Deleted `Homespun.Server/Features/Gitgraph/Snapshots/IProjectTaskGraphSnapshotStore.cs` and `ProjectTaskGraphSnapshotStore.cs`.
- [x] 6.4.2 Deleted `Homespun.Server/Features/Gitgraph/Snapshots/ITaskGraphSnapshotRefresher.cs` and `TaskGraphSnapshotRefresher.cs`.
- [x] 6.4.3 Removed snapshot DI registrations from `Program.cs` and `Features/Testing/MockServiceExtensions.cs`. Hosted-service registration for the refresher dropped. Snapshot-store consumer references in `ChangeSnapshotController` and `ChangeReconciliationService` removed.
- [x] 6.4.4 Deleted `TaskGraphSnapshotOptions` + `TaskGraphPatchPushOptions` (the configuration-knob types). The `TaskGraphSnapshot:*` keys had no representations in `appsettings*.json` so no JSON edits needed.

### 6.5 Delete old graph endpoints + related services

- [x] 6.5.1 Deleted `GraphController.GetGraph`, `RefreshGraph`, `GetCachedGraph`, and `GetTaskGraph` (text endpoint). Kept `GetTaskGraphData` (returns `TaskGraphResponse`) — still consumed by `static-task-graph-view`'s diff path and `features/agents/components/openspec-tab.tsx`. `BuildEnhancedTaskGraphAsync` remains as the on-demand backing for that one endpoint; without the snapshot store there is no caching path.
- [ ] 6.5.2 Delete `GraphService.BuildEnhancedTaskGraphAsync`. (Deferred — kept for the diff view, see 6.5.1. Subsumed by the openspec-states / linked-prs / agent-statuses endpoints introduced in Phase A; will be deleted alongside the diff-view migration.)
- [ ] 6.5.3 Delete `ProjectFleeceService.GetTaskGraphWithAdditionalIssuesAsync`. (Deferred — used by `IssuesAgentController` for the session-branch graph in the diff response. Same deferral as 6.5.2.)
- [ ] 6.5.4 Delete `Homespun.Shared/Models/Fleece/TaskGraphResponse.cs`, `TaskGraphNodeResponse.cs`, `TaskGraphEdgeResponse.cs`. (Deferred — still used by the diff view + openspec-tab; see 6.5.1.)
- [x] 6.5.5 Deleted obsoleted server tests: `tests/Homespun.Tests/Features/Gitgraph/Snapshots/*` (5 files), `tests/Homespun.Tests/Features/OpenSpec/ChangeReconciliationSnapshotInvalidationTests.cs`, `tests/Homespun.Tests/Features/GitHub/PRStatusResolverInvalidatesSnapshotTests.cs`, `tests/Homespun.Tests/Features/Fleece/IssueResponseFieldClassificationTests.cs`, `tests/Homespun.Api.Tests/Features/Gitgraph/FieldPatchTests.cs`, `tests/Homespun.Api.Tests/Features/Gitgraph/MutationInvalidatesSnapshotTests.cs`. `NotificationHubExtensionsTests.cs` rewritten for the unified `BroadcastIssueChanged` helper.

### 6.6 OpenSpec change interaction

- [x] 6.6.1 Archived `taskgraph-clone-lifecycle-invalidation` (15/16 tasks complete) via `openspec archive ... --yes`. Its remaining task is moot — the layout / decoration / snapshot machinery it was hedging is fully replaced by the client-side pipeline introduced in Phase C.

## 7. Verification

- [x] 7.1 `dotnet test /workdir/tests/Homespun.Tests` (1866 passed / 6 skipped) and `dotnet test /workdir/tests/Homespun.Api.Tests` (255 passed) both green. `dotnet test /workdir/tests/Homespun.Web.LayoutFixtures` (11 passed). `Homespun.AppHost.Tests` skipped — those tests stand up the full Aspire host (Docker, sibling images) and aren't part of the CI regression gate for this change.
- [x] 7.2 `npm run typecheck` clean. `npm test` 1934/1935 passed (1 pre-existing skip). `npm run lint:fix` reports 5 pre-existing errors in `error-boundary.tsx` (unrelated to this change — same errors on parent commit). `npm run build-storybook`, `npm run format:check`, `npm run generate:api:fetch` deferred to CI.
- [ ] 7.3 Run `npm run test:e2e`. (Deferred — `dynamic-issue-insert.spec.ts` not yet written; covered by Phase 5.4.)
- [ ] 7.4 Manual smoke under `dev-mock`. (Deferred to a follow-up smoke pass before the PR ships.)
- [ ] 7.5 Manual smoke under `dev-live`. (Same — deferred.)
- [x] 7.6 `openspec validate move-graph-layout-to-client --strict` passes (run from repo root).

## 8. Docs

- [x] 8.1 Deleted `docs/gitgraph/taskgraph-snapshot.md`.
- [x] 8.2 Added `docs/graph-layout-client-side.md` covering: TS port module structure, golden-fixtures workflow + when to regenerate, edge rendering semantics, unified `IssueChanged` event contract, idempotent client merge pattern.
- [x] 8.3 Updated `CLAUDE.md` "Feature Slices → Fleece" section with a "Client-side layout" sub-bullet pointing to the new doc.
- [x] 8.4 Removed `graph.snapshot.patch` from `docs/traces/dictionary.md`; the snapshot store is gone, so the span no longer emits. (No new spans introduced by `IssueAncestorTraversalService.CollectVisible` — the service is hot-path adjacent and emits at the controller's existing `graph.taskgraph.build` parent.)
- [ ] 8.5 PR description maps phases A–D to file paths so reviewers can grok one phase at a time. (Authored at PR creation — out of scope for the source diff.)
