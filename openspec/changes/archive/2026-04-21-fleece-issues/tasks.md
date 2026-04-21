---
description: "Retrospective task list for the migrated Fleece Issues feature"
---

# Tasks: Fleece Issues

**Input**: Design documents from `/specs/fleece-issues/`
**Status**: Migrated — all in-scope tasks reflect work that is already complete. Gaps are left **unchecked** and tracked in `follow-up-issues.md`.

> **Migration semantics.** `[x]` marks observed as-built work. Unchecked items are real, remediable gaps — do not delete them. Task groups mirror the user-story structure of `spec.md` so the backlog remains coherent with the SDD workflow going forward.

## Path Conventions (Homespun)

| Concern | Path |
|---------|------|
| Server slice | `src/Homespun.Server/Features/Fleece/...` |
| Shared contracts | `src/Homespun.Shared/{Models/Fleece,Models/Issues,Requests,Hubs}/...` |
| Web issues slice | `src/Homespun.Web/src/features/issues/...` |
| Web issues-agent slice | `src/Homespun.Web/src/features/issues-agent/...` |
| Routes | `src/Homespun.Web/src/routes/projects.$projectId.issues*.tsx` + `sessions.$sessionId.issue-diff.tsx` |
| Server unit tests | `tests/Homespun.Tests/Features/Fleece/...` |
| Server API tests | `tests/Homespun.Api.Tests/Features/{Issues*,FleeceSync*}.cs` |
| Web unit tests | co-located `*.test.ts(x)` next to the source |
| Web e2e tests | `src/Homespun.Web/e2e/...` |

---

## Phase 1: Setup (Slice Scaffolding)

- [x] T001 Server slice scaffolding under `src/Homespun.Server/Features/Fleece/` with `Controllers/` + `Services/` sub-folders.
- [x] T002 Web issues slice scaffolding under `src/Homespun.Web/src/features/issues/` with `components/`, `hooks/`, `services/`, `stores/`, `types.ts`, `index.ts`.
- [x] T003 Web issues-agent slice scaffolding under `src/Homespun.Web/src/features/issues-agent/` with `components/`, `hooks/`, `index.ts`.
- [x] T004 Test projects extended: `tests/Homespun.Tests/Features/Fleece/` + sub-folder `Services/`; `tests/Homespun.Api.Tests/Features/{Issues*,FleeceSync*}`.

---

## Phase 2: Foundational (Blocking Prerequisites)

- [x] T005 [P] `Fleece.Core` 2.1.1 referenced in `src/Homespun.Server/Homespun.Server.csproj` and `src/Homespun.Shared/Homespun.Shared.csproj`.
- [x] T006 [P] `Fleece.Cli` 2.1.1 installed in `Dockerfile.base` (MUST match Core — Constitution §IX).
- [x] T007 [P] Shared `IssueResponse` + `ParentIssueRefResponse` at `src/Homespun.Shared/Models/Fleece/IssueDto.cs` with settable properties so trimmed clients can deserialize (FR-016).
- [x] T008 [P] `IssueDtoMapper.ToResponse` at `src/Homespun.Server/Features/Fleece/IssueDtoMapper.cs` — every controller maps `Issue` → `IssueResponse` before returning.
- [x] T009 [P] Shared request family in `src/Homespun.Shared/Requests/IssueRequests.cs` (Create/Update/SetParent/RemoveParent/RemoveAllParents/MoveSeriesSibling/RunAgent + responses + `MoveDirection` enum + `ResolvedBranchResponse`).
- [x] T010 [P] Shared request family in `src/Homespun.Shared/Requests/IssuesAgentRequests.cs` (Create session / accept / diff / summary).
- [x] T011 [P] Shared models in `src/Homespun.Shared/Models/Fleece/{FleeceIssueSyncResult,IssueChangeType,IssueDiff,IssueHistoryModels,TaskGraphDto}.cs`.
- [x] T012 [P] Shared models in `src/Homespun.Shared/Models/Issues/{IssueDto,ApplyAgentChangesModels}.cs`.
- [x] T013 Service interfaces: `IProjectFleeceService`, `IFleeceIssuesSyncService`, `IFleeceIssueTransitionService`, `IIssueBranchResolverService`, `IIssueHistoryService`, `IIssueSerializationQueue`, `IFleeceIssueDiffService`, `IFleeceChangeDetectionService`, `IFleeceChangeApplicationService`, `IFleeceConflictDetectionService`, `IFleecePostMergeService`.
- [x] T014 Service registration in `src/Homespun.Server/Program.cs:131-144` — singleton for cache/queue/history/sync; scoped for transitions/branches/diff/detection/application/post-merge.
- [x] T015 `INotificationHubClient` contract (`src/Homespun.Shared/Hubs/INotificationHubClient.cs`) — `IssuesChanged`, `BranchIdGenerated/Failed`, `AgentStarting/Failed`.
- [x] T016 Swashbuckle annotations on all three controllers so OpenAPI emits every `Issue*` / `Fleece*` / `*AgentChanges*` DTO.
- [x] T017 Generated OpenAPI client refreshed under `src/Homespun.Web/src/api/generated/` covering `/api/issues*`, `/api/issues-agent/*`, `/api/fleece-sync/*`, `/api/projects/{projectId}/issues*`.
- [x] T018 Rename `FleeceService.cs` → `ProjectFleeceService.cs` and `IFleeceService.cs` → `IProjectFleeceService.cs`.
- [x] T019 Split `FleeceChangeApplicationService.cs` into `IFleeceChangeApplicationService.cs`, `FleeceChangeApplicationService.cs`, `IFleeceConflictDetectionService.cs`, `FleeceConflictDetectionService.cs`.
- [x] T020 Rationalise the two `IssueDto.cs` files across `Shared/Models/{Fleece,Issues}/`.

**Checkpoint**: Foundation landed — per-story work flows from here.

---

## Phase 3: US1 — View and filter a project's issues on the task graph (P1) 🎯 MVP

**Goal**: Task graph renders every open issue; filter queries narrow it; SignalR invalidations refresh it.

### Tests

- [x] T021 [P] [US1] `tests/Homespun.Tests/Features/Fleece/FleeceServiceTests.cs::ListIssuesAsync_*` (filter combinations).
- [x] T022 [P] [US1] `tests/Homespun.Tests/Features/Fleece/FleeceServiceTests.cs::GetReadyIssuesAsync_*` and `GetBlockingIssuesAsync_*`.
- [x] T023 [P] [US1] `tests/Homespun.Api.Tests/Features/IssuesApiTests.cs::GetByProject_*` (8 cases) and `GetReadyIssues_*`.
- [x] T024 [P] [US1] `tests/Homespun.Api.Tests/Features/IssuesApiTests.cs::GetProjectAssignees_*`.
- [x] T025 [P] [US1] Web component tests `features/issues/components/task-graph-konva/{task-graph-konva-view,konva-html-row,konva-nodes,compute-virtual-node}.test.tsx`.
- [x] T026 [P] [US1] Web hook tests `features/issues/hooks/{use-task-graph,use-issue-selection,use-keyboard-navigation,use-toolbar-shortcuts,use-default-filter,use-project-assignees}.test.ts(x)`.
- [x] T027 [P] [US1] `features/issues/services/filter-query-parser.test.ts` (613 cases — covers `status:`, `type:`, `priority:`, `assignee:`, `me`, free-text).
- [x] T028 [P] [US1] `features/issues/services/task-graph-layout.test.ts` (1549 cases — lane assignment, edge routing, virtual nodes).
- [ ] T029 [US1] Refactor `// TODO: Implement load more PRs` at `task-graph-view.tsx:1103` — either wire it to a paging endpoint or remove the row. **DEFERRED → fleece:3dd87s**

### Implementation

- [x] T030 [P] [US1] `ProjectFleeceService.ListIssuesAsync` / `GetIssueAsync` / `GetReadyIssuesAsync` / `GetChildrenAsync` / `GetBlockingIssuesAsync` / `GetPriorSiblingAsync` — all keyed on `projectPath`, served from per-project cache.
- [x] T031 [P] [US1] `ProjectFleeceService.GetTaskGraphAsync` + `GetTaskGraphWithAdditionalIssuesAsync` wrapping `Fleece.Core.TaskGraphService`.
- [x] T032 [P] [US1] `IssuesController.GetByProject` / `GetReadyIssues` / `GetProjectAssignees` / `GetById` / `GetResolvedBranch`.
- [x] T033 [P] [US1] Web `useTaskGraph`, `useIssue`, `useProjectAssignees` with `['task-graph', projectId]` / `['issue', issueId]` keys.
- [x] T034 [P] [US1] Web `<TaskGraphView>` (SVG) + `<TaskGraphKonvaView>` (Konva) with layout from `task-graph-layout.ts`.
- [x] T035 [P] [US1] Web `<ProjectToolbar>` with filter input + render-mode toggle + keyboard shortcut help popover.
- [x] T036 [P] [US1] Web `useKeyboardNavigation` + `useToolbarShortcuts` + `useIssueSelection` driving the task-graph selection + keyboard-driven editing.
- [x] T037 [P] [US1] Web `filter-query-parser.ts` supporting `status:`, `type:`, `priority:`, `assignee:`, `me`, free-text (FR-012).
- [x] T038 [US1] Route `routes/projects.$projectId.issues.tsx` (layout `<Outlet />`) + `routes/projects.$projectId.issues.index.tsx` (task-graph view + `<RunAgentDialog>` + `<AssignIssueDialog>`).
- [x] T039 [US1] Wrap `GET /api/projects/{projectId}/issues/assignees` response in a `ProjectAssigneesResponse` DTO and regenerate the OpenAPI client.

**Checkpoint**: US1 shippable independently — in production since before migration.

---

## Phase 4: US2 — Create an issue with hierarchy positioning (P1)

**Goal**: `POST /api/issues` persists, broadcasts `IssuesChanged`, optionally sets parent / sibling positioning / child, and queues branch-id generation.

### Tests

- [x] T040 [P] [US2] `tests/Homespun.Tests/Features/Fleece/FleeceServiceTests.cs::CreateIssueAsync_*` (type/status/priority/assignedTo variants).
- [x] T041 [P] [US2] `tests/Homespun.Api.Tests/Features/IssuesApiTests.cs::Create_*`.
- [x] T042 [P] [US2] `tests/Homespun.Tests/Features/Fleece/FleeceServiceRelationshipTests.cs` — sibling `InsertBefore` / positioning.
- [x] T043 [P] [US2] `features/issues/hooks/use-create-issue.test.ts`.
- [x] T044 [P] [US2] Playwright `src/Homespun.Web/e2e/inline-issue-hierarchy.spec.ts` — TAB (create as parent) + Shift+TAB (create as child).

### Implementation

- [x] T045 [P] [US2] `ProjectFleeceService.CreateIssueAsync` with `projectPath`, `title`, `type`, optional `description`, `priority`, `executionMode`, `status`, `assignedTo`.
- [x] T046 [P] [US2] `IssuesController.Create` — dispatches to `CreateIssueAsync`, then `UpdateIssueAsync` for `workingBranchId`, then `AddParentAsync` for `parentIssueId` (with `siblingIssueId` / `insertBefore`) or `AddParentAsync` for `childIssueId`; broadcasts `IssuesChanged(Created)`; queues branch-id generation.
- [x] T047 [P] [US2] Web `useCreateIssue` — mutation + `['task-graph', projectId]` invalidation.
- [x] T048 [P] [US2] Web `<InlineIssueEditor>` — inline creation inside the task graph with keyboard shortcuts.
- [x] T049 [P] [US2] Auto-assign user email on create: `IssuesController.Create` passes `dataStore.UserEmail` to the service (FR-013 companion).

---

## Phase 5: US3 — Edit an issue (P1)

**Goal**: Dedicated edit page with dirty-state blocker, Ctrl+Enter save, assignee combobox, branch-id regeneration on title change.

### Tests

- [x] T050 [P] [US3] `tests/Homespun.Tests/Features/Fleece/FleeceServiceTests.cs::UpdateIssueAsync_*`.
- [x] T051 [P] [US3] `tests/Homespun.Api.Tests/Features/IssuesApiTests.cs::Update_*` + `Delete_*`.
- [x] T052 [P] [US3] `tests/Homespun.Tests/Components/IssueEditPageTests.cs`.
- [x] T053 [P] [US3] `tests/Homespun.Tests/Components/IssueDetailPanelRaceConditionTests.cs`.
- [x] T054 [P] [US3] Route test `routes/projects.$projectId.issues.$issueId.edit.test.tsx` (1175 LOC — the largest component test in the repo).
- [x] T055 [P] [US3] `features/issues/hooks/use-update-issue.test.tsx`.
- [x] T056 [P] [US3] Playwright `e2e/issue-edit-ctrl-enter.spec.ts`.

### Implementation

- [x] T057 [P] [US3] `ProjectFleeceService.UpdateIssueAsync` / `DeleteIssueAsync`.
- [x] T058 [P] [US3] `IssuesController.Update` — auto-assign current user when unassigned; queue branch-id regeneration when title changes and branch-id is empty (FR-013).
- [x] T059 [P] [US3] `IssuesController.Delete`.
- [x] T060 [P] [US3] Web `useUpdateIssue` — mutation + `['issue', issueId]` + `['task-graph', projectId]` invalidation.
- [x] T061 [P] [US3] Web `<AssigneeCombobox>` + `<AssignIssuePopover>` — drawing from `useProjectAssignees`.
- [x] T062 [US3] Route `routes/projects.$projectId.issues.$issueId.edit.tsx` — Zod schema, `useBlocker` dirty-state prompt, Ctrl+Enter save, execution-mode toggle, tags input.

---

## Phase 6: US4 — Hierarchy management (P2)

**Goal**: Set/remove parent, remove all parents, move series sibling. Cycle detection returns `400 Bad Request`.

### Tests

- [x] T063 [P] [US4] `tests/Homespun.Tests/Features/Fleece/SetParentTests.cs` (176 cases covering AddToExisting variants).
- [x] T064 [P] [US4] `tests/Homespun.Tests/Features/Fleece/FleeceServiceRelationshipTests.cs` (549 cases — sibling moves, multi-parent edge cases).
- [x] T065 [P] [US4] `tests/Homespun.Api.Tests/Features/IssuesRelationshipApiTests.cs`.

### Implementation

- [x] T066 [P] [US4] `ProjectFleeceService.SetParentAsync` / `AddParentAsync` / `RemoveParentAsync` / `RemoveAllParentsAsync` / `MoveSeriesSiblingAsync` / `WouldCreateCycleAsync` — delegating to `Fleece.Core.DependencyService`.
- [x] T067 [P] [US4] `IssuesController.SetParent` / `RemoveParent` / `RemoveAllParents` / `MoveSeriesSibling` — catch `InvalidOperationException` with "cycle" message → 400.
- [x] T068 [P] [US4] Web `<IssueRowActions>` + drag/drop handlers in the task graph.

---

## Phase 7: US5 — Run an agent on an issue (P2)

**Goal**: 202 Accepted agent run with atomic duplicate prevention; background clone + prompt render + session start.

### Tests

- [x] T069 [P] [US5] `tests/Homespun.Tests/Features/Fleece/IssuesControllerTests.cs::RunAgent_*` (race, model fallback, branch resolution, conflict).
- [x] T070 [P] [US5] `tests/Homespun.Tests/Features/Fleece/IssueBranchResolverServiceTests.cs` (428 cases — branch resolution across clone + PR lookups).
- [x] T071 [P] [US5] Playwright `e2e/agent-and-issue-agent-launching.spec.ts::"selecting an issue and running a task agent with default prompt"`.

### Implementation

- [x] T072 [P] [US5] `IssueBranchResolverService.ResolveIssueBranchAsync` — checks PR link → existing clone → null.
- [x] T073 [P] [US5] `IssuesController.RunAgent` — session dedup (`IClaudeSessionService.GetSessionByEntityId`) + `IAgentStartupTracker.TryMarkAsStarting` atomic gate (FR-010) + queue via `IAgentStartBackgroundService`.
- [x] T074 [P] [US5] Web `useBranchIdGenerationEvents` (SignalR subscription) + `branch-id-generation-store` (Zustand) for optimistic branch-id display.
- [x] T075 [P] [US5] `<RunAgentDialog>` integrated on the issues list route (consolidates agent-launcher + issues-agent paths).

---

## Phase 8: US6 — Issues Agent session (P2)

**Goal**: Create a session seeded with the `IssueAgentSystem` prompt; compute diff; accept/resolve conflicts or cancel.

### Tests

- [x] T076 [P] [US6] `tests/Homespun.Tests/Features/Fleece/IssuesAgentControllerTests.cs` (359 cases).
- [x] T077 [P] [US6] `tests/Homespun.Api.Tests/Features/IssuesAgentApiTests.cs` (201 cases).
- [x] T078 [P] [US6] `tests/Homespun.Tests/Features/Fleece/Services/FleeceIssueDiffServiceTests.cs` (364 cases).
- [x] T079 [P] [US6] `tests/Homespun.Tests/Features/Fleece/Services/FleeceChangeDetectionServiceTests.cs` (780 cases) + `FleeceChangeDetectionIntegrationTests.cs` (418 cases).
- [x] T080 [P] [US6] `tests/Homespun.Tests/Features/Fleece/Services/FleeceChangeApplicationServiceTests.cs` (847 cases).
- [x] T081 [P] [US6] `tests/Homespun.Tests/Features/Fleece/FleecePostMergeServiceTests.cs` (177 cases).
- [x] T082 [P] [US6] Web hook test `features/issues-agent/hooks/use-refresh-issues-diff.test.ts`.
- [x] T083 [P] [US6] Playwright `e2e/agent-and-issue-agent-launching.spec.ts::"selecting an issue and running an issues agent"`.

### Implementation

- [x] T084 [P] [US6] `IssuesAgentController.CreateSession` — pre-pull via `PullFleeceOnlyAsync`; create clone; start session with `SessionType.IssueAgentModification` and `IssueAgentSystem` system prompt.
- [x] T085 [P] [US6] `IssuesAgentController.GetDiff` + `RefreshDiff` — returns `IssueDiffResponse { MainBranchGraph, SessionBranchGraph, Changes, Summary }`.
- [x] T086 [P] [US6] `IssuesAgentController.Accept` — dispatches to `IFleeceChangeApplicationService.ApplyChangesAsync`; broadcasts `IssuesChanged(Updated, null)`.
- [x] T087 [P] [US6] `IssuesAgentController.Cancel` — stops session and cleans up clone.
- [x] T088 [P] [US6] `IssuesController.ApplyAgentChanges` / `ResolveConflicts` — companion endpoints callable from the plain agent-run flow when it also touches issues.
- [x] T089 [P] [US6] `FleeceConflictDetectionService` (currently buried in `FleeceChangeApplicationService.cs` — see GP-2).
- [x] T090 [P] [US6] `FleecePostMergeService.RebuildCacheAsync` — called after every `git merge` operation.
- [x] T091 [P] [US6] Web hooks `useIssuesDiff`, `useRefreshIssuesDiff`, `useAcceptIssues`, `useCancelIssuesSession`, `useCreateIssuesAgentSession`, `useIssueAgentAvailablePrompts`.
- [x] T092 [P] [US6] Web components `<IssueDiffView>` + `<IssueChangeDetailPanel>`.
- [x] T093 [US6] Route `routes/sessions.$sessionId.issue-diff.tsx` — refuses non-IssueAgentModification sessions (FR-017).

---

## Phase 9: US7 — Sync `.fleece/` with git remote (P3)

**Goal**: Pull, push-and-commit, discard-non-fleece-then-pull, branch-status. All mutations reload the cache.

### Tests

- [x] T094 [P] [US7] `tests/Homespun.Tests/Features/Fleece/FleeceIssuesSyncServiceTests.cs` (1117 cases).
- [x] T095 [P] [US7] `tests/Homespun.Tests/Features/Fleece/FleeceIssueSyncIntegrationTests.cs` (688 cases — real `git` subprocess).
- [x] T096 [P] [US7] `tests/Homespun.Api.Tests/Features/FleeceSyncApiTests.cs` (243 cases).
- [x] T097 [US7] Playwright `e2e/fleece-sync.spec.ts` covering sync happy path, pull-while-behind, discard-and-pull preserves `.fleece/`.

### Implementation

- [x] T098 [P] [US7] `FleeceIssuesSyncService.CheckBranchStatusAsync` / `SyncAsync` / `PullFleeceOnlyAsync` / `PullChangesAsync` / `DiscardChangesAsync` / `DiscardNonFleeceChangesAsync` / `StashChangesAsync`.
- [x] T099 [P] [US7] `FleeceIssueSyncController.GetBranchStatus` / `Sync` / `Pull` / `DiscardNonFleeceAndPull` — reload cache on any success (FR-005).
- [x] T100 [P] [US7] `FleecePostMergeService` — hook point for post-pull cache rebuild.

---

## Phase 10: US8 — Undo/redo issue history (P3)

**Goal**: Ring-buffered history (max 100 entries) with undo/redo via snapshot application.

### Tests

- [x] T101 [P] [US8] `tests/Homespun.Tests/Features/Fleece/Services/IssueHistoryServiceTests.cs` (287 cases).
- [x] T102 [P] [US8] `tests/Homespun.Tests/Features/Fleece/IssueSerializationQueueServiceTests.cs` (329 cases).
- [x] T103 [P] [US8] `features/issues/hooks/use-issue-history.test.ts`.
- [ ] T104 [US8] Playwright `e2e/fleece-history.spec.ts` covering undo after create + redo after undo. **DEFERRED → fleece:3dd87s**

### Implementation

- [x] T105 [P] [US8] `IssueHistoryService` — append/undo/redo with `MaxHistoryEntries = 100` pruning.
- [x] T106 [P] [US8] `IssueSerializationQueueService` — background `BackgroundService` that persists snapshots to disk.
- [x] T107 [P] [US8] `ProjectFleeceService.ApplyHistorySnapshotAsync` — rewrite cache + queue disk write.
- [x] T108 [P] [US8] `IssuesController.GetHistoryState` / `Undo` / `Redo`.
- [x] T109 [P] [US8] Web `useIssueHistory` + toolbar shortcut bindings.
- [x] T110 [US8] Make `MaxHistoryEntries` configurable via `IOptions<FleeceHistoryOptions>`; add a bound-honoured test.

---

## Phase 11: Cross-cutting (polish + governance gaps)

- [x] T111 [P] [polish] `FleeceIssueSeeder` + `tests/mock-data/.fleece/issues_03eaae.jsonl` (20 issues) — Mock Mode seed data.
- [x] T112 [P] [polish] `IssueTreeFormatter` + tests — textual tree output for CLI-style display.
- [x] T113 [P] [polish] `IssueDtoMapperTests` asserts the Issue→IssueResponse mapping preserves every field.
- [x] T114 [P] [polish] `MockFleeceIssuesSyncService` — Mock Mode implementation of the sync service, no real `git` calls.
- [x] T115 [polish] Delete the dead `RunAgentResponse` DTO from `IssueRequests.cs` and regenerate the OpenAPI client.
- [x] T116 [polish] Document or realign the `IssuesAgentController` route pattern (`/api/issues-agent/{sessionId}/*` vs `{projectId}` elsewhere).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup. Blocks every US phase.
- **US1 (Phase 3)**: depends on Foundational; MVP.
- **US2 (Phase 4)**: depends on US1 (UI entry) for inline creation; the API endpoint itself only depends on Foundational.
- **US3 (Phase 5)**: depends on US2 (an issue must exist to edit).
- **US4 (Phase 6)**: depends on US2 (hierarchy requires existing issues).
- **US5 (Phase 7)**: depends on US3 (agent runs from the issue detail); additionally requires the AgentOrchestration slice to be in place.
- **US6 (Phase 8)**: depends on US5 for session infrastructure; additionally requires the Prompts slice for `IssueAgentSystem`.
- **US7 (Phase 9)**: depends on Foundational only at the service layer; UI surface lives in `<PullSyncButton>` (in the Projects slice toolbar — see `specs/projects/spec.md` §A-3).
- **US8 (Phase 10)**: depends on Foundational at the service layer; UI shortcut depends on US1.
- **Cross-cutting (Phase 11)**: depends on its targeted phase.

### User Story Dependencies

- US1 → MVP; blocks every other UI workflow in the slice.
- US2, US3, US4 are mutually dependent (create → edit → hierarchy) but each is API-independent.
- US5, US6 depend on out-of-slice AgentOrchestration + Prompts but are orthogonal to US7 / US8.
- US7, US8 are orthogonal to everything above.

---

## Parallel Execution Examples

Because the slice is already built, the main value of the `[P]` markers is for any **re-run** of TDD on a gap item:

- `fleece:SGhfxQ` (FI-7, e2e coverage for sync + history) is embarrassingly parallel — each acceptance scenario is its own spec.
- `fleece:KSXXVP` (FI-1, file rename) + `fleece:yf2OTa` (FI-2, file split) + `fleece:JIGk7h` (FI-3, DTO rename) are all structural refactors with no runtime impact. Can run concurrently but the three touch different files, so conflicts are unlikely.
- `fleece:vOx09w` (FI-4, dead DTO deletion) + `fleece:Xqg4qH` (FI-6, assignees DTO wrap) both require rerunning `npm run generate:api:fetch` — batch into a single PR to avoid two client regens.
- `fleece:Nee2Cy` (FI-9, configurable history depth) is a single-service change with no cross-slice impact.

---

## Implementation Strategy

### MVP First (already achieved)

1. ✅ Phase 1–2: Setup + Foundational (Fleece.Core wiring, shared contracts, service interfaces, DI registration).
2. ✅ Phase 3: US1 (task graph + filter + selection) — unblocks every other UI workflow.
3. ✅ Phase 4–5: US2 + US3 (create + edit) — the write side of the MVP.
4. ✅ Phase 6: US4 (hierarchy) — enables series execution.
5. ✅ Phase 7–8: US5 + US6 (agent flows) — the AI differentiator.
6. ✅ Phase 9–10: US7 + US8 (sync + history) — multi-contributor + safety nets.

### Incremental Hardening (gaps to close)

1. Close the structural gaps first (`fleece:KSXXVP` / `fleece:yf2OTa` / `fleece:JIGk7h`) — mechanical refactors, unlock downstream readability.
2. Remove the dead DTO and add the assignees wrapper in one client-regen PR (`fleece:vOx09w` + `fleece:Xqg4qH`).
3. Backfill e2e coverage for sync and history (`fleece:SGhfxQ`).
4. Configurable history depth (`fleece:Nee2Cy`).
5. Decide and document the IssuesAgent route pattern (`fleece:0HQOyi`).
6. Wire up or remove the Load More PRs row (`fleece:KuhsyT`).
