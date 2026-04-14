# Implementation Plan: Fleece Issues

**Branch**: n/a (pre-spec-kit; built on `main` over many PRs)  |  **Date**: 2026-04-14 (migrated)  |  **Spec**: [`./spec.md`](./spec.md)
**Status**: Migrated — describes the as-built implementation, not a future design.

## Summary

Integrate the external [Fleece](https://www.nuget.org/packages/Fleece.Core) library for local, file-based issue tracking. `Fleece.Core` provides the JSONL storage model, the `Issue` / `TaskGraph` / `DependencyService` types, and cycle detection. Homespun adds:

1. **An HTTP API** over three controllers (`IssuesController`, `IssuesAgentController`, `FleeceIssueSyncController`) — 29 endpoints total.
2. **A project-aware cache** (`IProjectFleeceService`) that keys by project path, writes through to disk via a background serialization queue, and broadcasts `IssuesChanged` over SignalR to invalidate clients.
3. **A git-backed sync layer** (`IFleeceIssuesSyncService`) that commits `.fleece/` JSONL paths and pushes / pulls them against the project's remote, plus a post-merge service that resolves conflicts at the field level.
4. **A specialised "Issues Agent" session flow** — Claude sessions whose job is to modify `.fleece/` files rather than code, reviewed via a diff UI before being applied back to main.
5. **An interactive task-graph UI** — two renderers (SVG + Konva), a rich filter query language, inline editing, and keyboard navigation.
6. **An undo/redo history layer** — ring-buffered snapshots (100 entries) applied through the serialization queue.

The slice is deliberately a *wrapper* around `Fleece.Core` — Homespun contributes the project-awareness, the sync+agent machinery, and the UI, but never re-implements the JSONL schema or the dependency graph.

## Technical Context

**Language/Version**: C# / .NET 10 (server, shared), TypeScript 5.9 + React 19 (web)

**Primary Dependencies**:
- Server: ASP.NET Core, `Fleece.Core` 2.1.1 (NuGet — `src/Homespun.Server/Homespun.Server.csproj` + `src/Homespun.Shared/Homespun.Shared.csproj`), `IDataStore`, `IGitCloneService`, `ICommandRunner` (shell `git`), `IClaudeSessionService`, `IAgentStartBackgroundService`, `IAgentStartupTracker`, `IBranchIdBackgroundService`, `IHubContext<NotificationHub>`, `IAgentPromptService`.
- Web: TanStack Query (`@tanstack/react-query`), TanStack Router file routes, `react-hook-form` + `zod`, shadcn/ui (`Card`, `AlertDialog`, `Button`, `Input`, `Textarea`, `Tabs`, `Label`, `Select`, `Skeleton`, `Popover`, `DropdownMenu`, `Combobox`), Konva + `react-konva` (task graph canvas), `lucide-react` icons, `sonner` toasts, Zustand stores, generated OpenAPI client under `src/api/generated/`.
- Toolchain: `Fleece.Cli` 2.1.1 installed in `Dockerfile.base` (MUST match `Fleece.Core` — Constitution §IX).

**Storage**:
- `.fleece/*.jsonl` at the project's `LocalPath` root — owned by `Fleece.Core`, read/written by Homespun via `IProjectFleeceService`'s cache and `IIssueSerializationQueue`.
- `.fleece/history/*.jsonl` — `IIssueHistoryService` ring buffer (100 entries, per spec FR-006).
- In-memory cache: one dictionary per project path, invalidated by `ReloadFromDiskAsync` after any sync/pull/agent-change operation.
- Mock seed: `tests/mock-data/.fleece/issues_03eaae.jsonl` (20 issues covering every status/type/priority combination) loaded by `FleeceIssueSeeder` when Mock Mode is active.

**Testing**: NUnit + Moq for the service layer; WebApplicationFactory for API tests (`IssuesApiTests`, `IssuesAgentApiTests`, `IssuesRelationshipApiTests`, `FleeceSyncApiTests`); Vitest + RTL for web components/hooks (co-located `*.test.tsx`); Playwright for e2e (`inline-issue-hierarchy.spec.ts`, `issue-edit-ctrl-enter.spec.ts`, `agent-and-issue-agent-launching.spec.ts`).

**Target Platform**: Linux containers in production (Azure Container Apps); Windows/macOS/Linux locally via `dotnet run` and `./scripts/mock.{sh,ps1}`. `Fleece.Cli` is consumed by the *user / CLI workflow* (not by Homespun at runtime — Homespun uses `Fleece.Core` in-process).

**Project Type**: Multi-module monorepo — ASP.NET API + React SPA + shared contracts + worker.

**Performance Goals**:
- `ListIssuesAsync` is O(n) over the in-memory cache; typical n is 10–500 per project, served from memory.
- Task-graph layout (`task-graph-layout.ts`, 1056 LOC) precomputes lane assignments and is re-run only when the graph structure changes (memoized with React selectors).
- Konva task graph targets 60fps for ≥500 issues (see spec SC-003).
- Sync/pull/push wall time is dominated by `git` subprocess latency — no Homespun-side parallelism.

**Constraints**:
- Cross-process DTOs MUST originate in `src/Homespun.Shared` (Constitution §III). Every `Issue*Request`, `*Response`, and shared Fleece model lives there; controllers do not redeclare.
- `Fleece.Core` NuGet version MUST match `Fleece.Cli` version in `Dockerfile.base` (Constitution §IX). Both are at 2.1.1.
- The OpenAPI client is regenerated from the server spec; never hand-edited.
- `Issue` from `Fleece.Core` has `required init` properties that break trimmed Blazor WASM deserialization — every controller MUST map to `IssueResponse` via `IssueDtoMapper.ToResponse` before returning.

**Scale/Scope**:
- Server slice: 23 files under `Features/Fleece/` (~6,088 LOC).
- Shared contracts: 8 files (~800 LOC).
- Web issues slice: ~70 files under `features/issues/` (~23,000 LOC incl. tests); includes Konva sub-slice with its own layout, camera, edge-paths hooks.
- Web issues-agent slice: 10 files (~400 LOC).
- Routes: 4 files (`projects.$projectId.issues.tsx`, `.issues.index.tsx`, `.issues.$issueId.edit.tsx`, `sessions.$sessionId.issue-diff.tsx`).
- Backend tests: 22 files (~10,757 LOC), which is roughly 1.7× the production LOC for this slice.

## Constitution Check

*Retrospective check for the as-built feature. Any box unchecked is called out under **Complexity Tracking** with a remediation note.*

| # | Gate | Pass? | Notes |
|---|------|-------|-------|
| I    | Test-First — failing tests written before production code | [~] | Dense backend coverage (service, controller, integration, API layers). Frontend unit tests on every hook and nearly every component. E2E coverage touches inline-create, edit ctrl+enter, and agent launching but not sync or undo/redo (gap GP-7). Retrospective TDD cannot be reconstructed; forward work on this slice MUST follow the rule. |
| II   | Vertical Slice Architecture — change scoped to identified slice(s) | [x] | Server code under `Features/Fleece/`, web code split between `features/issues/` and `features/issues-agent/` (a deliberate split — the issues-agent flow operates on sessions, not issues, so it has its own hooks + components). The task-graph sub-slice under `features/issues/components/task-graph-konva/` is intentional structural decomposition, not a boundary violation. |
| III  | Shared Contract Discipline — DTOs in `Homespun.Shared`; OpenAPI client regenerated, not hand-edited | [~] | All DTOs live in `Homespun.Shared.Models.{Fleece,Issues}` + `Homespun.Shared.Requests.Issue*`. **But**: two `IssueDto.cs` files across sibling namespaces confuse readers (gap GP-3); one unused DTO (`RunAgentResponse`) has drifted (gap GP-4); assignees endpoint lacks a DTO wrapper (gap GP-6). |
| IV   | Pre-PR Quality Gate — `dotnet test`, `npm run lint:fix`, `npm run format:check`, `npm run generate:api:fetch`, `npm run typecheck`, `npm test`, `npm run test:e2e` all pass | [~] | Historic PRs run the gate; test suites pass today. `npm run test:e2e` passes but does not exercise the sync or history surfaces (gap GP-7). |
| V    | Coverage — delta ≥ 80% on changed lines AND no regression vs `main`; on track for 60%/2026-06-30 and 80%/2026-09-30 | [~] | Test-to-production LOC is ~1.7:1. Every controller has a matching test file; every service has targeted service-level tests + integration tests. `task-graph-view.tsx` has a TODO-backed unreachable branch (gap GP-5). |
| VI   | Fleece-Driven Workflow — issue exists, status transitions, `.fleece/` committed | [n/a] | The slice predates the current workflow. Follow-up issues are drafted in `follow-up-issues.md` and created in Fleece. |
| VII  | Conventional Commits + PR suffix; allowed branch prefix | [x] | Historic commits follow the convention. No branches currently outstanding for this slice. |
| VIII | Naming — PascalCase (C#) / kebab-case (web feature folder) / co-located tests | [~] | `Features/Fleece/`, `features/issues/`, `features/issues-agent/` all conform. **But**: `FleeceService.cs` contains `ProjectFleeceService` and `IFleeceService.cs` contains `IProjectFleeceService` — filename/class mismatch (gap GP-1). `FleeceChangeApplicationService.cs` contains four public types across two service boundaries (gap GP-2). |
| IX   | `Fleece.Core` ↔ `Fleece.Cli` version sync | [x] | Both at 2.1.1 (`src/Homespun.Server/Homespun.Server.csproj`, `src/Homespun.Shared/Homespun.Shared.csproj`, `Dockerfile.base`). CLAUDE.md notes this invariant; enforce it on every bump. |
| X    | Container & mock-shell safety preserved | [x] | Slice never targets the `homespun` / `homespun-prod` containers. `git` subprocesses run under the user's clone path; `Fleece.Cli` is installed inside the Dockerfile.base image for user-side use, not invoked by server code. |
| XI   | Logs queried via Loki | [x] | Every service + controller logs via `ILogger<T>`; no ad-hoc file sinks. |

## Project Structure

### Documentation (this feature)

```text
specs/fleece-issues/
├── spec.md                # User-visible feature description (migrated)
├── plan.md                # This file
├── tasks.md               # Retrospective task list, all completed except gaps
└── follow-up-issues.md    # Fleece issue IDs backing the gaps (source of truth: the .fleece/ JSONL)
```

### Source Code (repository root — as-built)

```text
src/
├── Homespun.Server/
│   └── Features/Fleece/
│       ├── Controllers/
│       │   ├── IssuesController.cs                (~724 LOC) # Core CRUD + hierarchy + agent-run + history
│       │   ├── IssuesAgentController.cs           (~426 LOC) # Issues Agent session lifecycle + diff/accept/cancel
│       │   └── FleeceIssueSyncController.cs       (~110 LOC) # branch-status / sync / pull / discard-and-pull
│       ├── Services/
│       │   ├── IProjectFleeceService.cs           ← defined in IFleeceService.cs (gap GP-1)
│       │   ├── ProjectFleeceService.cs            ← defined in FleeceService.cs  (gap GP-1)
│       │   ├── IFleeceIssuesSyncService.cs        # sync/pull interface
│       │   ├── FleeceIssuesSyncService.cs         (~838 LOC) # git-backed sync implementation
│       │   ├── IFleeceIssueTransitionService.cs   # status-transition rules
│       │   ├── FleeceIssueTransitionService.cs
│       │   ├── IIssueBranchResolverService.cs     # map issue → existing branch (PR or clone)
│       │   ├── IssueBranchResolverService.cs
│       │   ├── IIssueHistoryService.cs            # MaxHistoryEntries = 100
│       │   ├── IssueHistoryService.cs             (~488 LOC)
│       │   ├── IIssueSerializationQueue.cs        # background disk-writer queue
│       │   ├── IssueSerializationQueueService.cs  # BackgroundService implementation
│       │   ├── FleeceIssueDiffService.cs          # diff between two issue graphs (interface + class in one file)
│       │   ├── FleeceChangeDetectionService.cs    # detect agent-introduced changes
│       │   ├── FleeceChangeApplicationService.cs  (~881 LOC) # gap GP-2: also contains IFleeceConflictDetectionService + FleeceConflictDetectionService
│       │   └── FleecePostMergeService.cs          # rebuild cache after git merge
│       ├── FleeceFileHelper.cs                    # path helpers for .fleece/ folder
│       ├── IssueDtoMapper.cs                      # Issue → IssueResponse mapping (FR-016)
│       └── BlockingIssuesResult.cs                # DTO for GetBlockingIssuesAsync
│
├── Homespun.Shared/
│   ├── Models/
│   │   ├── Fleece/
│   │   │   ├── IssueDto.cs                        # IssueResponse + ParentIssueRefResponse (API DTO) — gap GP-3
│   │   │   ├── FleeceIssueSyncResult.cs           # 4 records: FleeceIssueSyncResult, FleecePullResult, PullResult, BranchStatusResult
│   │   │   ├── IssueChangeType.cs                 # enum (Created/Updated/Deleted) for SignalR + apply-changes
│   │   │   ├── IssueDiff.cs                       # diff structure
│   │   │   ├── IssueHistoryModels.cs              # IssueHistoryEntry / State / OperationResponse
│   │   │   └── TaskGraphDto.cs                    # TaskGraphResponse / TaskGraphNodeResponse
│   │   └── Issues/
│   │       ├── IssueDto.cs                        # separate IssueDto used by ApplyAgentChanges — gap GP-3
│   │       └── ApplyAgentChangesModels.cs         # full agent-change contract
│   ├── Requests/
│   │   ├── IssueRequests.cs                       # Create/Update/SetParent/RemoveParent/RemoveAllParents/MoveSeriesSibling/RunAgent + RunAgentResponse (dead — gap GP-4) + AgentAlreadyRunningResponse + RunAgentAcceptedResponse + ResolvedBranchResponse + MoveDirection enum
│   │   └── IssuesAgentRequests.cs                 # CreateIssuesAgentSessionRequest/Response + IssueDiffResponse + IssueDiffSummary + AcceptIssuesAgentChangesRequest/Response
│   └── Hubs/
│       └── INotificationHubClient.cs              # IssuesChanged + BranchIdGenerated/Failed + AgentStarting/Failed
│
└── Homespun.Web/src/
    ├── features/issues/
    │   ├── components/                            # 24 components incl. task graph (SVG + Konva), inline editor, toolbars, assignee combobox, filter help popover, pr-status-indicator, execution-mode-toggle, issue-row-actions, etc.
    │   │   └── task-graph-konva/                  # Konva sub-slice: layout, camera, edge paths, html rows, virtual node compute
    │   ├── hooks/                                 # 17 hooks incl. use-task-graph, use-issue, use-create-issue, use-update-issue, use-keyboard-navigation (598 LOC), use-issue-selection, use-toolbar-shortcuts, use-issue-history, use-project-assignees, use-default-filter, use-branch-id-generation-events, use-linked-pr-status, use-apply-agent-changes
    │   ├── services/                              # filter-query-parser (470 LOC), task-graph-layout (1056 LOC), branch-name, inherited-parent, priority-colors
    │   ├── stores/                                # branch-id-generation-store (Zustand)
    │   ├── types.ts                               # Issue-adjacent types, enums
    │   └── index.ts                               # Public surface
    │
    ├── features/issues-agent/
    │   ├── components/                            # issue-change-detail-panel + issue-diff-view
    │   ├── hooks/                                 # use-issues-diff, use-refresh-issues-diff, use-accept-issues, use-cancel-issues-session, use-create-issues-agent-session, use-issue-agent-available-prompts
    │   └── index.ts
    │
    └── routes/
        ├── projects.$projectId.issues.tsx                    # Layout <Outlet />
        ├── projects.$projectId.issues.index.tsx              # (~537 LOC) Task graph page + filter + run-agent dialog
        ├── projects.$projectId.issues.$issueId.edit.tsx      # (~755 LOC) Edit form + dirty-state blocker
        └── sessions.$sessionId.issue-diff.tsx                # (~170 LOC) Issues Agent diff / accept / cancel view

tests/
├── Homespun.Tests/
│   ├── Features/Fleece/
│   │   ├── FleeceServiceTests.cs                  (~1193 LOC)
│   │   ├── FleeceServiceRelationshipTests.cs      (~549 LOC)
│   │   ├── FleeceIssuesSyncServiceTests.cs        (~1117 LOC)
│   │   ├── FleeceIssueSyncIntegrationTests.cs     (~688 LOC)
│   │   ├── FleecePostMergeServiceTests.cs         (~177 LOC)
│   │   ├── IssuesControllerTests.cs               (~1249 LOC)
│   │   ├── IssuesAgentControllerTests.cs          (~359 LOC)
│   │   ├── IssueBranchResolverServiceTests.cs     (~428 LOC)
│   │   ├── IssueDtoMapperTests.cs                 (~143 LOC)
│   │   ├── IssueSerializationQueueServiceTests.cs (~329 LOC)
│   │   ├── SetParentTests.cs                      (~176 LOC)
│   │   └── Services/
│   │       ├── FleeceChangeApplicationServiceTests.cs       (~847 LOC)
│   │       ├── FleeceChangeDetectionServiceTests.cs         (~780 LOC)
│   │       ├── FleeceChangeDetectionIntegrationTests.cs     (~418 LOC)
│   │       ├── FleeceIssueDiffServiceTests.cs               (~364 LOC)
│   │       ├── IssueHistoryServiceTests.cs                  (~287 LOC)
│   │       └── IssueTreeFormatterTests.cs                   (~291 LOC)
│   ├── Components/
│   │   ├── IssueDetailPanelRaceConditionTests.cs
│   │   └── IssueEditPageTests.cs
│   └── Features/Testing/FleeceIssueSeederTests.cs
└── Homespun.Api.Tests/
    └── Features/
        ├── IssuesApiTests.cs                      (~670 LOC)
        ├── IssuesAgentApiTests.cs                 (~201 LOC)
        ├── IssuesRelationshipApiTests.cs          (~78 LOC)
        └── FleeceSyncApiTests.cs                  (~243 LOC)

src/Homespun.Web/e2e/
├── inline-issue-hierarchy.spec.ts                 # TAB / Shift+TAB inline creation
├── issue-edit-ctrl-enter.spec.ts                  # Keyboard save from edit page
└── agent-and-issue-agent-launching.spec.ts        # End-to-end agent run + issues-agent creation
```

**Structure Decision**: The server slice is a single large feature folder (`Features/Fleece/`) with a `Controllers/` + `Services/` split — no per-capability sub-folders, because most services depend on `IProjectFleeceService` and splitting would create layered dependencies. The web side is split into two slices (`features/issues/` and `features/issues-agent/`) because the agent flow is session-scoped and reuses session infrastructure rather than issue infrastructure. The Konva task graph lives as a sub-folder (`components/task-graph-konva/`) rather than its own slice because its only consumer is the issues list route.

## Complexity Tracking

*Gaps and boundary issues that did not meet the constitution; each is tracked as a Fleece issue in `follow-up-issues.md`.*

| # | Finding | Why it exists | Remediation |
|---|---------|---------------|-------------|
| GP-1 | **Filename/class mismatch for `ProjectFleeceService`.** `FleeceService.cs` defines class `ProjectFleeceService`; `IFleeceService.cs` defines interface `IProjectFleeceService`. Violates Constitution §VIII naming symmetry. Tracked as **`fleece:KSXXVP`**. | The class was renamed from `FleeceService` → `ProjectFleeceService` at some point to disambiguate from `Fleece.Core.IFleeceService`, but the files were not renamed. | Rename `FleeceService.cs` → `ProjectFleeceService.cs` and `IFleeceService.cs` → `IProjectFleeceService.cs`; update any `#load` / test-fixture references. Run `dotnet test` to confirm nothing breaks. |
| GP-2 | **Multiple public types in `FleeceChangeApplicationService.cs` (881 lines).** Contains `IFleeceChangeApplicationService`, `FleeceChangeApplicationService`, `IFleeceConflictDetectionService`, and `FleeceConflictDetectionService`. Conflict-detection logic is hidden inside a file named after a different service. Tracked as **`fleece:yf2OTa`**. | Conflict detection was added as a private helper, then promoted to a public type, then never moved to its own file. | Split into `IFleeceChangeApplicationService.cs`, `FleeceChangeApplicationService.cs`, `IFleeceConflictDetectionService.cs`, `FleeceConflictDetectionService.cs`. Keep tests in one service-test file per new public type. |
| GP-3 | **Two `IssueDto.cs` files in sibling namespaces.** `Homespun.Shared/Models/Fleece/IssueDto.cs` defines `IssueResponse` (API); `Homespun.Shared/Models/Issues/IssueDto.cs` defines `IssueDto` (only inside `ApplyAgentChanges*`). Tracked as **`fleece:JIGk7h`**. | The API path was built first with `IssueResponse`; the agent-changes path was added later and reached for an `IssueDto` name without first checking what existed. | Rename `Fleece/IssueDto.cs` → `IssueResponse.cs` and `Issues/IssueDto.cs` → `AgentIssueDto.cs`, or consolidate into a single `IssueDto` in `Shared/Models/Issues/` with the agent flows depending on it. Decide in spec §A-3 once implemented. |
| GP-4 | **`RunAgentResponse` is dead code.** Defined in `IssueRequests.cs` but unused — the controller returns `RunAgentAcceptedResponse` (202). Tracked as **`fleece:vOx09w`**. | Endpoint returned 200-with-full-session when first built; later switched to 202-accepted but the old DTO was not removed. | Delete `RunAgentResponse` and rerun `npm run generate:api:fetch`; confirm the generated client no longer emits the type. |
| GP-5 | **`// TODO: Implement load more PRs` in `task-graph-view.tsx:1103`.** The LoadMore row renders but the click handler is absent; practically harmless today because there's rarely >1 PR per issue. Tracked as **`fleece:KuhsyT`**. | The UI scaffolding landed before the paging endpoint existed; the endpoint was never added. | Either remove the LoadMore row (if paging is out of scope) or wire it to a paging call on `/api/projects/{projectId}/issues?...`. Add a component test covering whichever path is chosen. |
| GP-6 | **Assignees endpoint returns raw `List<string>`.** `GET /api/projects/{projectId}/issues/assignees` has no DTO wrapper; every other endpoint does. Tracked as **`fleece:Xqg4qH`**. | Quick addition for the assignee combobox; never rounded out to a proper DTO. | Introduce `ProjectAssigneesResponse { List<string> Assignees }` (or a richer `AssigneeDto` with display name) and regenerate the OpenAPI client. Update `use-project-assignees.ts`. |
| GP-7 | **No e2e coverage for Fleece sync or undo/redo.** The existing e2e specs touch inline-create, ctrl-enter save, and agent launching only. Tracked as **`fleece:SGhfxQ`**. | Sync + history were added after the e2e suite was already established; no one added specs for them. | Add `src/Homespun.Web/e2e/fleece-sync.spec.ts` covering: sync happy path, pull-while-behind, discard-non-fleece-and-pull. Add `src/Homespun.Web/e2e/fleece-history.spec.ts` covering undo after create + redo after undo. |
| GP-8 | **Asymmetric route addressing.** `IssuesController` uses `/api/issues/{issueId}` and `/api/projects/{projectId}/issues*`; `IssuesAgentController` uses `/api/issues-agent/{sessionId}/*`. The rationale (session-scoped lookup) is undocumented. Tracked as **`fleece:0HQOyi`**. | Issues Agent is session-first by design; most addressing is derivable from the session object. But the departure is visible and confusing. | Either document the pattern in `spec.md` under Assumptions (§A-7 already names it — expand), or realign the routes to include `projectId`. Pick one and commit. |
| GP-9 | **Hard-coded history depth (100).** `IIssueHistoryService.MaxHistoryEntries = 100` is a constant; projects with high issue churn have no knob. Tracked as **`fleece:Nee2Cy`**. | Safe default chosen at implementation time; configuration was deferred. | Bind to `IOptions<FleeceHistoryOptions>` (with `.MaxEntries = 100` default); surface in `appsettings.json`; add a test asserting the bound is honoured. |
