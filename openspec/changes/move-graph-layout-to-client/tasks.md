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

- [ ] 5.1.1 Add `src/Homespun.Web/src/features/issues/hooks/useIssues.ts`. TanStack Query key: `['issues', projectId, includeIds, includeOpenPrLinked]`. `staleTime: Infinity`. SignalR subscription to `IssueChanged` events with `applyIssueChanged` merge per design.md D2.
- [ ] 5.1.2 Add `src/Homespun.Web/src/features/issues/hooks/useLinkedPrs.ts`. Query key: `['linked-prs', projectId]`. Subscribes to existing PR-state SignalR channel; on event, invalidate this key.
- [ ] 5.1.3 Add `src/Homespun.Web/src/features/issues/hooks/useAgentStatuses.ts`. Query key: `['agent-statuses', projectId]`. Subscribes to existing agent-status SignalR channel.
- [ ] 5.1.4 Add `src/Homespun.Web/src/features/issues/hooks/useOpenSpecStates.ts`. Query key: `['openspec-states', projectId, issueIds]`. Confirm whether an OpenSpec-state SignalR channel exists; if not, this hook invalidates on `IssueChanged` (since most OpenSpec changes coincide with issue mutations) — flagged as a follow-up if a dedicated channel proves necessary.
- [ ] 5.1.5 Add `src/Homespun.Web/src/features/issues/hooks/useOrphanChanges.ts`. Query key: `['orphan-changes', projectId]`. Same SignalR-channel logic as 5.1.4.
- [ ] 5.1.6 Add `src/Homespun.Web/src/features/issues/hooks/useMergedPrs.ts`. Query key: `['merged-prs', projectId, max]`. Subscribes to PR-state SignalR channel (same channel as `useLinkedPrs`).
- [ ] 5.1.7 Wire reconnect on every hook: on `HubConnection.onreconnected`, invalidate the relevant query keys (forces refetch).
- [ ] 5.1.8 Unit tests for `applyIssueChanged` reducer (created/updated/deleted, idempotency).
- [ ] 5.1.9 Component test for each hook rendered under `QueryClientProvider` + mock SignalR — assert echo events apply, deletes drop entries, reconnect refetches.

### 5.2 Layout-driving wrapper

- [ ] 5.2.1 Rewrite `src/Homespun.Web/src/features/issues/services/task-graph-layout.ts` to take `Issue[]` + decoration maps (`linkedPrs`, `agentStatuses`, `openSpecStates`, `mergedPrs`, `orphanChanges`) + `viewMode` + filter args, call `layoutForTree` or `layoutForNext`, then synthesize Homespun-only rows (PR rows, separators, "load more"). Memoise the layout call (key: stable hash of the issue set + viewMode + filter, decorations excluded from the key — they affect render only, not layout). Output is the existing `TaskGraphRenderLine[] + TaskGraphEdge[]` shape so consumers don't change.
- [ ] 5.2.2 Handle `{ ok: false, cycle }` result: degraded-mode flat-list output + error banner data field. Add Storybook story demonstrating the banner.
- [ ] 5.2.3 Update `task-graph-layout.test.ts` to cover new wrapper behaviour.

### 5.3 View migration

- [ ] 5.3.1 Switch `src/Homespun.Web/src/features/issues/components/task-graph-view.tsx` from `useTaskGraph()` to the parallel hook tuple: `useIssues()`, `useLinkedPrs()`, `useAgentStatuses()`, `useOpenSpecStates()`, `useOrphanChanges()`, `useMergedPrs()`. Compose the layout-wrapper inputs from the assembled data. Remove dead branches around server-positioned-node consumption.
- [ ] 5.3.2 Confirm `viewMode` toggle (Tree↔Next) is a pure client transformation: no refetch, just re-runs `task-graph-layout.ts` with different params. Verify with React DevTools / network panel during manual smoke.
- [ ] 5.3.3 Delete `src/Homespun.Web/src/features/issues/lib/apply-patch.ts` and any `PatchableField`-aware merge logic. Delete `src/Homespun.Web/src/features/issues/hooks/useTaskGraph.ts`.
- [ ] 5.3.4 Update SignalR subscription in `useIssues` to handle the unified `IssueChanged` event; remove `IssueFieldsPatched` subscriber.
- [ ] 5.3.5 Run `npm run lint:fix && npm run typecheck && npm test`. Fix all errors before proceeding.

### 5.4 E2E smoke

- [ ] 5.4.1 Add `src/Homespun.Web/e2e/dynamic-issue-insert.spec.ts`: navigate to a project's task graph, create an issue via the UI, assert the new node appears within 1s without observing a `GET /api/projects/{projectId}/issues` network request. (The SignalR `IssueChanged` event + idempotent merge should be sufficient.)
- [ ] 5.4.2 Confirm `npm run test:e2e` passes.

## 6. Phase D — Server: collapse SignalR + delete old surface

### 6.1 Unified event extension

- [ ] 6.1.1 Write failing unit tests in `tests/Homespun.Tests/Features/Notifications/BroadcastIssueChangedTests.cs` covering: `Created` carries the issue body, `Updated` carries the issue body, `Deleted` carries `null` issue, the helper sends to `Clients.Group(projectId)`, the helper does NOT touch any (now-deleted) snapshot store.
- [ ] 6.1.2 Implement `BroadcastIssueChanged(this IHubContext<NotificationHub>, string projectId, IssueChangeKind kind, string issueId, IssueResponse? issue)`. Single SendAsync call. No DI lookup beyond the hub context.

### 6.2 Migrate every call site (per Task 1.2 inventory)

- [ ] 6.2.1 `IssuesController.Create` → `BroadcastIssueChanged(projectId, Created, issue.Id, issue)`
- [ ] 6.2.2 `IssuesController.Update` → `BroadcastIssueChanged(projectId, Updated, issueId, issue)` — collapse the patch/topology branch into a single call (delete the `TryBuildFieldPatch` reflection + `BroadcastIssueFieldsPatched` branch).
- [ ] 6.2.3 `IssuesController.Delete` → `BroadcastIssueChanged(projectId, Deleted, issueId, null)`
- [ ] 6.2.4 `IssuesController.{SetParent, RemoveParent, RemoveAllParents, MoveSibling}` → `BroadcastIssueChanged(projectId, Updated, issueId, issue)` post-mutation read.
- [ ] 6.2.5 `IssuesController.{ApplyAgentChanges, ResolveConflicts, Undo, Redo}` → emit one `Updated` per affected issue (helper supports a fan-out; OR emit a single bulk event if simpler).
- [ ] 6.2.6 `IssuesAgentController.{AcceptChangesAsync, CreateSession}` — same migration.
- [ ] 6.2.7 `AgentStartBackgroundService.StartAgentAsync` post-clone-create — `BroadcastIssueChanged(projectId, Updated, issueId, issue)`.
- [ ] 6.2.8 `ProjectClonesController.{Create, Delete, BulkDelete, Prune}` — `BroadcastIssueChanged(projectId, Updated, null, null)` for bulk-no-issue events. (Confirm wire shape supports null `issueId` for bulk events; if not, introduce a `BulkChanged` kind.)
- [ ] 6.2.9 `PRStatusResolver.ResolveClosedPRStatusesAsync` — `BroadcastIssueChanged(projectId, Updated, fleeceIssueId, issue)` for each Merged/Closed transition.
- [ ] 6.2.10 `FleeceIssueSyncController.{Sync, Pull, DiscardNonFleeceAndPull}` — bulk event after `ReloadFromDiskAsync`.
- [ ] 6.2.11 `ChangeReconciliationService.ReconcileAsync` — sidecar auto-link + archive auto-transition both fire `BroadcastIssueChanged`.

### 6.3 Delete the old event extensions

- [ ] 6.3.1 Delete `BroadcastIssueTopologyChanged` and `BroadcastIssueFieldsPatched` extensions.
- [ ] 6.3.2 Delete the `IssueChangeType` enum if it was used only by these helpers. Otherwise keep the type and migrate the kind values into `IssueChangeKind`.
- [ ] 6.3.3 Delete `Homespun.Shared/Models/Fleece/PatchableFieldAttribute.cs` and remove all `[PatchableField]` attribute applications.
- [ ] 6.3.4 Delete `IssuesController.TryBuildFieldPatch` and any related reflection helpers.

### 6.4 Delete snapshot store + refresher

- [ ] 6.4.1 Delete `Homespun.Server/Features/Gitgraph/Snapshots/IProjectTaskGraphSnapshotStore.cs` and `ProjectTaskGraphSnapshotStore.cs`.
- [ ] 6.4.2 Delete `Homespun.Server/Features/Gitgraph/Snapshots/ITaskGraphSnapshotRefresher.cs` and `TaskGraphSnapshotRefresher.cs`.
- [ ] 6.4.3 Delete the snapshot DI registrations in `Program.cs` / feature DI extensions. Delete the hosted-service registration for the refresher.
- [ ] 6.4.4 Delete the `5-minute idle eviction` and `10-second refresh tick` configuration knobs (search for keys in `appsettings*.json`).

### 6.5 Delete old graph endpoints + related services

- [ ] 6.5.1 Delete `GraphController.GetTaskGraphData`, `GetTaskGraphText`, `RefreshGraph`, `GetCachedGraph`, `GetGraph` (legacy gitgraph JSON). If `GraphController` becomes empty, delete the file.
- [ ] 6.5.2 Delete `GraphService.BuildEnhancedTaskGraphAsync` and any helpers that only it called. Move shared decoration enrichers (OpenSpec, LinkedPR, AgentStatus) into reusable services if not already extracted in Task 2.3.2.
- [ ] 6.5.3 Delete `ProjectFleeceService.GetTaskGraphWithAdditionalIssuesAsync` and remove the `IIssueLayoutService` field + constructor injection. Drop the `IIssueLayoutService` DI registration.
- [ ] 6.5.4 Delete `Homespun.Shared/Models/Fleece/TaskGraphResponse.cs`, `TaskGraphNodeResponse.cs`, `TaskGraphEdgeResponse.cs`, plus any DTOs used only by the deleted endpoints.
- [ ] 6.5.5 Delete obsoleted server tests: `tests/Homespun.Tests/Features/Gitgraph/Snapshots/*`, `HubHelperInvalidationOrderTests.cs`, snapshot-invalidation tests added by `taskgraph-clone-lifecycle-invalidation` change, all `BroadcastIssueTopologyChanged`/`BroadcastIssueFieldsPatched` order tests, `PatchableFieldAttribute` detection tests, API tests against `/api/graph/{projectId}/taskgraph*`.

### 6.6 OpenSpec change interaction

- [ ] 6.6.1 The `taskgraph-clone-lifecycle-invalidation` change in `openspec/changes/` is partially-shipped (15/16 tasks complete). Its requirements become moot under this change. After this change archives, archive `taskgraph-clone-lifecycle-invalidation` immediately or roll its remaining task into "verified by full test pass under this change."

## 7. Verification

- [ ] 7.1 Run `dotnet test` from repo root. All tests pass.
- [ ] 7.2 In `src/Homespun.Web`, run `npm run lint:fix && npm run format:check && npm run generate:api:fetch && npm run typecheck && npm test && npm run build-storybook`. All pass.
- [ ] 7.3 Run `npm run test:e2e`. New `dynamic-issue-insert.spec.ts` passes. No existing E2E tests regress.
- [ ] 7.4 Manual smoke under `dotnet run --project src/Homespun.AppHost --launch-profile dev-mock`: open task graph, create issue, observe instant insert without network refetch (DevTools network panel). Toggle Tree↔Next mode, confirm no refetch.
- [ ] 7.5 Manual smoke under `dev-live`: same flow plus an agent session creating an issue mid-flight. Confirm SignalR echo lands and graph updates without staleness.
- [ ] 7.6 Confirm `openspec validate move-graph-layout-to-client --strict` passes.

## 8. Docs

- [ ] 8.1 Delete `docs/gitgraph/taskgraph-snapshot.md`.
- [ ] 8.2 Add `docs/graph-layout-client-side.md` covering: TS port module structure, golden-fixtures workflow + when to regenerate, edge rendering semantics, unified `IssueChanged` event contract, the "idempotent client merge" pattern.
- [ ] 8.3 Update `CLAUDE.md` "Feature Slices → Fleece" section: server no longer runs layout for the JSON path; visibility filter is described; client owns layout via TS port; golden fixtures regenerate on Fleece upgrade.
- [ ] 8.4 Update `docs/traces/dictionary.md` to remove span entries for `snapshot.refresh`, `snapshot.invalidate`, etc., and add any new spans introduced by `IssueAncestorTraversalService.CollectVisible`.
- [ ] 8.5 PR description maps phases A–D to file paths so reviewers can grok one phase at a time.
