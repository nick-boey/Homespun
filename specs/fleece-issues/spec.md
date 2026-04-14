# Feature Specification: Fleece Issues

**Feature Branch**: n/a (pre-spec-kit; built on `main` over many PRs)
**Created**: 2026-04-14 (migrated)
**Status**: Migrated
**Input**: Reverse-engineered from existing implementation in `src/Homespun.Server/Features/Fleece/`, `src/Homespun.Shared/Models/{Fleece,Issues}/` + `src/Homespun.Shared/Requests/Issue*.cs`, `src/Homespun.Web/src/features/issues/` + `src/Homespun.Web/src/features/issues-agent/`, the project-scoped routes under `src/Homespun.Web/src/routes/projects.$projectId.issues*` plus `sessions.$sessionId.issue-diff.tsx`, and the backend tests in `tests/Homespun.Tests/Features/Fleece/` + `tests/Homespun.Api.Tests/Features/{Issues*,FleeceSync*}.cs`.

> **Migration note.** This spec documents *what exists*, not a future design. "Fleece Issues" is the integration with the external [Fleece](https://www.nuget.org/packages/Fleece.Core) library for local, file-based issue tracking. `Fleece.Core` provides the JSONL storage + in-process model (`Issue`, `TaskGraph`, `DependencyService`); Homespun wraps it in an HTTP API, a project-aware cache (`ProjectFleeceService`), a sync layer over `git`, an "Issues Agent" specialised session flow, and a React UI centered on an interactive task-graph. The slice is large (6 kLOC server, ~23 kLOC web incl. tests) but has a clear surface: everything flows through three controllers — `IssuesController`, `IssuesAgentController`, `FleeceIssueSyncController`.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - View and filter a project's issues on the task graph (Priority: P1) 🎯 MVP

As a user on `/projects/{id}/issues` I see my project's open issues laid out as a task graph — actionable work on the left, blocking parents on the right — so I can tell at a glance what to pick up next.

**Why this priority**: This is the primary entry to every Fleece workflow. Without the list + graph the rest of the slice (create, edit, run-agent, sync, history) is unreachable through the UI.

**Independent Test**: `GET /api/projects/{projectId}/issues` returns the issue list in an expected shape; the root route `/projects/{id}/issues/` renders `<TaskGraphView>` or `<TaskGraphKonvaView>` depending on the app-store render mode; typing a filter query into the toolbar narrows the graph. Covered by `IssuesApiTests.GetByProject_*` (8 cases), `FleeceServiceTests.ListIssuesAsync_*`, and the co-located component/hook tests under `features/issues/` (`task-graph-konva-view.test.tsx`, `use-task-graph.test.ts`, `filter-query-parser.test.ts` — 613 cases).

**Acceptance Scenarios**:

1. **Given** a project with open issues, **When** the user navigates to `/projects/{id}/issues`, **Then** `<TaskGraphView>` renders every open issue with `IsActionable === true` at lane 0 and blockers at higher lanes.
2. **Given** the user types `type:bug priority:1` into the toolbar filter, **When** the filter is applied, **Then** only matching issues remain and `filterMatchCount` updates; `parseFilterQuery` supports `status:`, `type:`, `priority:`, `assignee:`, plain text, and the `me` keyword resolving to `userEmail`.
3. **Given** the filter query is empty on page load, **When** the route mounts, **Then** `useDefaultFilter` applies no filter and the graph shows the full set returned by `/api/projects/{projectId}/issues` (no server-side filter applied unless `status`/`type`/`priority` query-string params are present).
4. **Given** the user toggles between SVG (`TaskGraphView`) and Konva (`TaskGraphKonvaView`) render modes via the toolbar, **When** the render mode changes, **Then** `issuesRenderMode` persists in the Zustand `app-store` and the graph re-renders without losing selection.
5. **Given** a SignalR `IssuesChanged` notification arrives, **When** any client has the issues tab open, **Then** the `['task-graph', projectId]` query is invalidated and the view refetches.

---

### User Story 2 - Create an issue (optionally with hierarchy positioning) (Priority: P1)

As a user I add a new issue inline on the task graph or via the edit page. If I'm creating a sub-issue or an issue that should sit before/after a sibling, the UI passes the positioning hints through.

**Why this priority**: Creation is the write side of the MVP. Every other write (edit, hierarchy, run-agent) assumes an issue exists.

**Independent Test**: `POST /api/issues { projectId, title, type, description?, priority?, parentIssueId?, siblingIssueId?, insertBefore?, childIssueId?, workingBranchId? }` returns `201 Created` with the full `IssueResponse`. Covered by `FleeceServiceTests.CreateIssueAsync_*`, `IssuesApiTests.Create_*`, the relationship-positioning cases in `FleeceServiceRelationshipTests`, and the Playwright `inline-issue-hierarchy.spec.ts` (TAB / Shift+TAB inline creation).

**Acceptance Scenarios**:

1. **Given** a valid `CreateIssueRequest`, **When** the client POSTs to `/api/issues`, **Then** the server persists the issue via `Fleece.Core` (JSONL in `.fleece/`), broadcasts `IssuesChanged(projectId, Created, issueId)` on the `NotificationHub`, and returns the issue with `createdAt`/`lastUpdate` stamped.
2. **Given** no `workingBranchId` is provided but a `title` is, **When** the issue is created, **Then** `IBranchIdBackgroundService.QueueBranchIdGenerationAsync` is invoked so the client receives a later `BranchIdGenerated` SignalR event populating the field.
3. **Given** a `parentIssueId` is provided, **When** the server processes the request, **Then** `AddParentAsync` is invoked with the optional `siblingIssueId` + `insertBefore` for positioning within the parent's children.
4. **Given** a `childIssueId` is provided, **When** the server processes the request, **Then** the newly-created issue becomes a parent of that child (inverse hierarchy insertion).
5. **Given** the `projectId` does not exist, **When** the client POSTs, **Then** the server returns `404 Not Found`.
6. **Given** the user is configured with a `UserEmail` in the data store, **When** a new issue is created without an explicit `assignedTo`, **Then** the issue is auto-assigned to the current user.

---

### User Story 3 - Edit an issue on a dedicated edit page (Priority: P1)

As a user I open `/projects/{id}/issues/{issueId}/edit`, change any editable field (title, status, type, description, priority, execution mode, working branch id, assignee), and save.

**Why this priority**: Editing is the day-to-day workflow once issues exist; dirty-state protection (via `useBlocker`) is load-bearing for users who context-switch.

**Independent Test**: `PUT /api/issues/{issueId} { projectId, ...fields }` returns `200` with the updated `IssueResponse`. Covered by `FleeceServiceTests.UpdateIssueAsync_*`, `IssuesApiTests.Update_*`, and the route test `projects.$projectId.issues.$issueId.edit.test.tsx` (1175 lines — covers validation, dirty-state prompts, Ctrl+Enter save, assignee combobox, branch-id generation integration, execution-mode toggle).

**Acceptance Scenarios**:

1. **Given** the user has mutated any field on the edit form, **When** they attempt to navigate away, **Then** `useBlocker` shows an `AlertDialog` asking to confirm discarding changes.
2. **Given** the user presses Ctrl+Enter anywhere in the form, **When** validation passes, **Then** `useUpdateIssue` is invoked and the user navigates back to the issues list — covered by `issue-edit-ctrl-enter.spec.ts`.
3. **Given** the title is cleared, **When** validation runs, **Then** the Zod schema (`title: z.string().min(1)`) blocks submission with "Title is required".
4. **Given** an issue currently has no `assignedTo` and the request doesn't specify one, **When** the server processes `PUT /api/issues/{issueId}`, **Then** it auto-assigns the current user's email if `dataStore.UserEmail` is configured.
5. **Given** the title changes and `workingBranchId` is empty, **When** the update is persisted, **Then** a branch-id regeneration is queued and the client receives a `BranchIdGenerated` SignalR event.
6. **Given** the assignee combobox is opened, **When** the user types, **Then** the suggestions list is drawn from `GET /api/projects/{projectId}/issues/assignees` (unique existing assignees + current user).

---

### User Story 4 - Manage issue hierarchy (set-parent, remove-parent, move-sibling) (Priority: P2)

As a user I reshape the task graph by dragging / menu-actioning an issue under a different parent, removing a parent, or reordering series siblings.

**Why this priority**: Hierarchy is the heart of the graph. Without it, `ExecutionMode.Series` and the "ready issues" queue don't work.

**Independent Test**: Cycle detection is enforced by `Fleece.Core`'s `DependencyService`; Homespun surfaces it as `400 Bad Request` with a "cycle" message. Covered by `SetParentTests` (176 cases), `FleeceServiceRelationshipTests` (549 cases), and `IssuesRelationshipApiTests`.

**Acceptance Scenarios**:

1. **Given** two unrelated issues A and B, **When** the user POSTs to `/api/issues/A/set-parent { parentIssueId: "B", addToExisting: false }`, **Then** A's `ParentIssues` is replaced by a single entry pointing at B, and SignalR broadcasts `IssuesChanged(Updated, "A")`.
2. **Given** `SetParentRequest.AddToExisting = true`, **When** the server processes it, **Then** the new parent is added alongside any existing parents (many-parents model is supported).
3. **Given** a relationship would create a cycle, **When** the server processes `set-parent`, **Then** `Fleece.Core` throws and the controller returns `400 Bad Request` with the cycle message verbatim.
4. **Given** a series sibling at the top of its group, **When** the user calls `/api/issues/{id}/move-sibling { Direction: Up }`, **Then** the server returns `400 Bad Request` from the `InvalidOperationException` catch (no sibling above).
5. **Given** an issue with multiple parents or no parent, **When** `move-sibling` is called, **Then** the server rejects with `400 Bad Request` (series move is only meaningful for single-parent issues).
6. **Given** `remove-parent` or `remove-all-parents` is called for a missing issue, **When** the server processes it, **Then** `404 Not Found` is returned.

---

### User Story 5 - Run an agent on an issue (Priority: P2)

As a user I click "Run Agent" on an issue, choose `Plan` or `Build` mode, and Homespun creates a clone on a working branch, starts a Claude session seeded with the issue context, and surfaces progress via SignalR.

**Why this priority**: Agent-driven work is the differentiator of Homespun over plain Fleece — but it's gated behind list + create, so P2 rather than P1.

**Independent Test**: `POST /api/issues/{issueId}/run` returns `202 Accepted` with a `RunAgentAcceptedResponse` containing the resolved branch name; the heavy lifting (clone, prompt render, session start) happens in `IAgentStartBackgroundService`. Covered by `IssuesControllerTests.RunAgent_*` and the e2e `agent-and-issue-agent-launching.spec.ts::"selecting an issue and running a task agent with default prompt"`.

**Acceptance Scenarios**:

1. **Given** no active session exists for the issue, **When** the user POSTs `/api/issues/{id}/run`, **Then** the branch name is resolved via `IIssueBranchResolverService.ResolveIssueBranchAsync` (checking linked PRs and existing clones first, then `BranchNameGenerator.GenerateBranchName(issue)` as fallback), queued for background startup, and the controller immediately returns `202`.
2. **Given** an active session already exists on the issue, **When** the request arrives, **Then** the server returns `409 Conflict` with `AgentAlreadyRunningResponse { sessionId, status, message }`.
3. **Given** two clients race, **When** `agentStartupTracker.TryMarkAsStarting(issueId)` loses, **Then** the losing request gets `409` with `status = Starting` to prevent duplicate startups.
4. **Given** the request omits `model`, **When** the server processes it, **Then** it falls back to `project.DefaultModel` or `"sonnet"` as a final default.
5. **Given** the agent finishes starting or fails, **When** the background service completes, **Then** the client receives a SignalR `AgentStarting` or `AgentStartFailed` notification for the originating issue.

---

### User Story 6 - Issues Agent session: diff, accept, cancel (Priority: P2)

As a user I want to ask an LLM to bulk-edit my issues — re-prioritise, split into sub-tasks, rewrite descriptions — and then review the resulting diff before accepting changes back onto `main`.

**Why this priority**: Issues Agent is a distinct, optional flow from the standard agent run. It operates on issues as data rather than code.

**Independent Test**: `POST /api/issues-agent/session` creates a new session + clone + branch (`issues-agent-{selectedIssueId?}-{timestamp}`) seeded with the `IssueAgentSystem` prompt; `GET /api/issues-agent/{sessionId}/diff` returns `IssueDiffResponse { MainBranchGraph, SessionBranchGraph, Changes, Summary }`; `POST /api/issues-agent/{sessionId}/accept` merges the session's `.fleece/` changes back to main via `IFleeceChangeApplicationService`; `POST /api/issues-agent/{sessionId}/cancel` discards the session. Covered by `IssuesAgentControllerTests` (359 cases), `IssuesAgentApiTests` (201 cases), `FleeceChangeApplicationServiceTests` (847 cases), `FleeceChangeDetectionServiceTests` + integration (780 + 418), `FleeceIssueDiffServiceTests` (364), `FleecePostMergeServiceTests` (177), and e2e `"selecting an issue and running an issues agent"`.

**Acceptance Scenarios**:

1. **Given** a `CreateIssuesAgentSessionRequest { projectId, selectedIssueId?, model?, mode?, userInstructions? }`, **When** the server processes it, **Then** it (a) pulls the latest main via `IFleeceIssuesSyncService.PullFleeceOnlyAsync`, (b) creates a clone via `IGitCloneService.CreateCloneAsync`, (c) starts a session of `SessionType.IssueAgentModification` seeded with the `IssueAgentSystem` prompt, (d) defaults the model to `project.DefaultModel ?? "opus"`.
2. **Given** an active Issues Agent session, **When** the UI calls `GET /api/issues-agent/{sessionId}/diff`, **Then** the server compares the main branch's `.fleece/` issues with the session branch's and returns per-issue `IssueChangeDto` entries plus a summary.
3. **Given** the user clicks "Accept", **When** the server processes `/accept`, **Then** `IFleeceChangeApplicationService.ApplyChangesAsync` runs with the session's `ConflictResolutionStrategy` (default `Manual`), conflicts are surfaced via `IssueConflictDto`, and the main branch's `.fleece/` cache is reloaded.
4. **Given** conflicts exist, **When** the user resolves them, **Then** `/api/issues/{issueId}/resolve-conflicts` with an array of `FieldResolution { fieldName, choice, customValue? }` completes the apply, choosing from `ConflictChoice.UseMain | UseAgent | Custom`.
5. **Given** the user clicks "Cancel", **When** the server processes `/cancel`, **Then** the session is stopped and its clone is cleaned up; no changes land on main.
6. **Given** the session type is not `IssueAgentModification`, **When** any `/api/issues-agent/{sessionId}/*` endpoint is called, **Then** the server returns `400 Bad Request`.
7. **Given** the user opens `/sessions/{sessionId}/issue-diff` on a non-IssueAgentModification session, **When** the route renders, **Then** it shows a "This is not an Issues Agent session" fallback linking back to the session.

---

### User Story 7 - Sync `.fleece/` with the git remote (Priority: P3)

As a user I need the `.fleece/` JSONL files on disk to stay in sync with the project's git remote so multiple contributors don't clobber each other.

**Why this priority**: Necessary for multi-contributor projects but invisible in single-user local work; many users never touch it.

**Independent Test**: `POST /api/fleece-sync/{projectId}/sync` commits-and-pushes; `POST /pull` pulls fleece-only; `POST /discard-non-fleece-and-pull` resets non-`.fleece/` working-tree changes before pulling; `GET /branch-status` reports branch/ahead/behind counts. Covered by `FleeceSyncApiTests` (243 cases), `FleeceIssuesSyncServiceTests` (1117 cases), `FleeceIssueSyncIntegrationTests` (688 cases), `FleecePostMergeServiceTests` (177 cases).

**Acceptance Scenarios**:

1. **Given** the project has un-pushed changes under `.fleece/`, **When** the user POSTs `/api/fleece-sync/{projectId}/sync`, **Then** the server commits all `.fleece/` paths, pushes to `project.DefaultBranch`, reloads the cache, and returns `FleeceIssueSyncResult { Success, FilesCommitted, PushSucceeded, RequiresPullFirst?, HasNonFleeceChanges?, NonFleeceChangedFiles? }`.
2. **Given** the local branch is behind remote, **When** sync is invoked, **Then** the service auto-pulls first; if pull fails the result carries `RequiresPullFirst = true`.
3. **Given** the user calls `/pull`, **When** the pull succeeds, **Then** the cache is reloaded via `IProjectFleeceService.ReloadFromDiskAsync` and the result reports `CommitsPulled` + `IssuesMerged` + `WasBehindRemote`.
4. **Given** non-`.fleece/` changes would block a clean pull, **When** the user calls `/discard-non-fleece-and-pull`, **Then** `DiscardNonFleeceChangesAsync` wipes only those paths before the pull, preserving `.fleece/` edits.
5. **Given** a merge conflict occurs during pull, **When** the result is returned, **Then** `HasMergeConflict = true` and the UI's `<PullSyncButton>` opens an alert dialog instead of auto-applying.
6. **Given** the user is on a non-default branch, **When** `GET /branch-status` is called, **Then** the server reports `IsOnCorrectBranch = false` and the UI disables sync actions.

---

### User Story 8 - Undo / redo issue changes (Priority: P3)

As a user I mis-click and want to revert the last change across any of my issue mutations.

**Why this priority**: Safety net; nice-to-have once CRUD is solid.

**Independent Test**: `GET /api/projects/{projectId}/issues/history/state` returns `IssueHistoryState { CanUndo, CanRedo, TotalEntries, UndoDescription?, RedoDescription? }`; `POST /undo` / `POST /redo` roll the `.fleece/` cache + disk forward/back; history is capped at **100 entries** per project (`IIssueHistoryService.MaxHistoryEntries`). Covered by `IssueHistoryServiceTests` (287 cases) and `IssuesControllerTests.History*`.

**Acceptance Scenarios**:

1. **Given** a user creates, edits, and deletes issues, **When** they call `/undo`, **Then** the most recent mutation is reversed by re-applying the preceding snapshot via `IProjectFleeceService.ApplyHistorySnapshotAsync`, and the cache is reloaded.
2. **Given** undo has been performed, **When** `/redo` is called, **Then** the undone state is re-applied.
3. **Given** no history exists, **When** `/undo` is called, **Then** the server returns `200` with `{ Success: false, ErrorMessage: "Nothing to undo" }`.
4. **Given** `MaxHistoryEntries` (100) is exceeded, **When** a new mutation is made, **Then** the oldest entry is pruned silently.

---

### Edge Cases

- **Renaming a `FleeceService.cs` vs `ProjectFleeceService` class** — the file is named `FleeceService.cs` but defines `ProjectFleeceService`; likewise `IFleeceService.cs` → `IProjectFleeceService`. Historic; see Assumptions §A-1.
- **`FleeceChangeApplicationService.cs` is 881 lines** — contains two interfaces and two classes (both `*ChangeApplication*` and `*ConflictDetection*`). Conflict detection is hidden inside this file; see Assumptions §A-2.
- **Two `IssueDto.cs` files in sibling namespaces** — `Homespun.Shared.Models.Fleece.IssueDto.cs` defines `IssueResponse`; `Homespun.Shared.Models.Issues.IssueDto.cs` defines `IssueDto`. The first is used by the regular API; the second only inside `ApplyAgentChangesRequest/Response`. See Assumptions §A-3.
- **`RunAgentResponse` is dead** — defined in `IssueRequests.cs` but the controller returns `RunAgentAcceptedResponse`. See Assumptions §A-4.
- **Assignees endpoint returns raw `List<string>`** — every other endpoint uses a DTO. See Assumptions §A-5.
- **Hard-coded history depth** — `MaxHistoryEntries = 100` is a constant on `IIssueHistoryService`, not configurable. See Assumptions §A-6.
- **IssuesAgentController addressing** — uses `/api/issues-agent/{sessionId}/*`; every other Fleece endpoint uses `{projectId}` or `{issueId}`. See Assumptions §A-7.
- **`task-graph-view.tsx` has a TODO** — "Implement load more PRs" at line 1103. See Assumptions §A-8.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Expose the issue CRUD surface over `/api`: `GET /api/projects/{projectId}/issues`, `GET /api/projects/{projectId}/issues/ready`, `GET /api/projects/{projectId}/issues/assignees`, `GET /api/issues/{issueId}`, `GET /api/issues/{issueId}/resolved-branch`, `POST /api/issues`, `PUT /api/issues/{issueId}`, `DELETE /api/issues/{issueId}`.
- **FR-002**: Expose hierarchy mutations over `/api`: `POST /api/issues/{childId}/set-parent`, `POST /api/issues/{childId}/remove-parent`, `POST /api/issues/{issueId}/remove-all-parents`, `POST /api/issues/{issueId}/move-sibling`.
- **FR-003**: Expose the agent-run surface: `POST /api/issues/{issueId}/run` returning `202 Accepted`; `POST /api/issues/{issueId}/apply-agent-changes`; `POST /api/issues/{issueId}/resolve-conflicts`. Conflict detection returns `400 Bad Request` for invalid inputs and `ApplyAgentChangesResponse { Success, Changes, Conflicts, Message, WouldApply? }` for valid ones.
- **FR-004**: Expose the Issues Agent session surface over `/api/issues-agent`: `POST /session`, `GET /{sessionId}/diff`, `POST /{sessionId}/accept`, `POST /{sessionId}/cancel`, `POST /{sessionId}/refresh-diff`. All endpoints MUST validate `session.SessionType == IssueAgentModification` before mutating.
- **FR-005**: Expose the sync surface over `/api/fleece-sync/{projectId}`: `GET /branch-status`, `POST /sync`, `POST /pull`, `POST /discard-non-fleece-and-pull`. Every successful mutation MUST call `IProjectFleeceService.ReloadFromDiskAsync` so other clients see the new state.
- **FR-006**: Expose the history surface: `GET /api/projects/{projectId}/issues/history/state`, `POST /undo`, `POST /redo`. History MUST be capped at `IIssueHistoryService.MaxHistoryEntries = 100`; older entries MUST be pruned silently.
- **FR-007**: `IProjectFleeceService` MUST wrap `Fleece.Core.IFleeceService` with a project-path-first signature (e.g. `ListIssuesAsync(string projectPath, ...)`) and maintain a per-project in-memory write-through cache backed by `IIssueSerializationQueue` to disk.
- **FR-008**: Every write endpoint MUST broadcast `IssuesChanged(projectId, IssueChangeType, issueId?)` on the `NotificationHub` so other clients invalidate the `['task-graph', projectId]` query.
- **FR-009**: Issue creation with a non-empty title and no `workingBranchId` MUST queue branch-id generation via `IBranchIdBackgroundService`; the client receives a later `BranchIdGenerated` or `BranchIdGenerationFailed` SignalR event.
- **FR-010**: `POST /api/issues/{issueId}/run` MUST be idempotent under race: an atomic `IAgentStartupTracker.TryMarkAsStarting(issueId)` call gates the startup; losers receive `409 Conflict`.
- **FR-011**: The task graph UI MUST support both SVG (`TaskGraphView`) and Konva (`TaskGraphKonvaView`) render modes, selectable via the toolbar and persisted in the Zustand `app-store` under `issuesRenderMode`.
- **FR-012**: The filter query language parsed by `filter-query-parser.ts` MUST support `status:`, `type:`, `priority:`, `assignee:` predicates, the `me` keyword (resolved to `userEmail`), and free-text search on `title` + `description`.
- **FR-013**: `PUT /api/issues/{issueId}` MUST auto-assign `dataStore.UserEmail` when the existing issue has no assignee, the request doesn't specify one, and a current user email is configured.
- **FR-014**: `POST /api/fleece-sync/{projectId}/sync` MUST detect non-`.fleece/` working-tree changes and report them on the result (`HasNonFleeceChanges`, `NonFleeceChangedFiles`) without committing them.
- **FR-015**: The Issues Agent session MUST auto-pull the latest main branch via `PullFleeceOnlyAsync` before creating its clone so the session starts from the latest issues state.
- **FR-016**: `IssueDtoMapper.ToResponse` MUST be used by every controller that returns an `Issue` so the wire DTO (`IssueResponse`) never leaks `Fleece.Core`'s `Issue` init-only properties (which break trimmed Blazor/client environments).
- **FR-017**: `/sessions/{sessionId}/issue-diff` MUST refuse to render for sessions where `sessionType !== SessionType.IssueAgentModification` and offer a link back to the plain session view.
- **FR-018**: All apply-changes operations on a session MUST run through `IFleeceChangeApplicationService.ApplyChangesAsync` + `ResolveConflictsAsync`; no controller may mutate `.fleece/` directly on accept.

### Key Entities

- **`Issue`** (`Fleece.Core.Models.Issue` — not defined in Homespun) — the canonical model with `Id`, `Title`, `Status`, `Type`, `Priority`, `LinkedPRs`, `LinkedIssues`, `ParentIssues`, `Tags`, `WorkingBranchId`, `ExecutionMode`, `CreatedBy`, `AssignedTo`, `LastUpdate`, `CreatedAt`. Persisted as JSONL under `{projectLocalPath}/.fleece/`.
- **`IssueResponse`** (`src/Homespun.Shared/Models/Fleece/IssueDto.cs`) — API DTO. Regular settable properties so Blazor WASM (trimmed) can deserialize.
- **`IssueDto`** (`src/Homespun.Shared/Models/Issues/IssueDto.cs`) — **separate** DTO used only inside `ApplyAgentChangesRequest/Response` and `IssueDiffResponse`. Near-duplicate of `IssueResponse`; see Assumptions §A-3.
- **`TaskGraphResponse`** (`src/Homespun.Shared/Models/Fleece/TaskGraphDto.cs`) — wire shape for the graph: nodes with `{ Issue, Lane, Row, IsActionable }` + `TotalLanes`.
- **`FleeceIssueSyncResult` / `FleecePullResult` / `PullResult` / `BranchStatusResult`** (`src/Homespun.Shared/Models/Fleece/FleeceIssueSyncResult.cs`) — four records covering the sync surface outcomes.
- **`IssueHistoryState` / `IssueHistoryEntry` / `IssueHistoryOperationResponse`** (`src/Homespun.Shared/Models/Fleece/IssueHistoryModels.cs`) — undo/redo wire models.
- **`ApplyAgentChangesRequest/Response`, `IssueChangeDto`, `IssueConflictDto`, `FieldChangeDto`, `FieldConflictDto`, `ConflictResolution`, `FieldResolution`, `ConflictResolutionStrategy`, `ConflictChoice`, `ChangeType`** (`src/Homespun.Shared/Models/Issues/ApplyAgentChangesModels.cs`) — the full agent-changes contract.
- **`IssuesAgent` request/response family** (`src/Homespun.Shared/Requests/IssuesAgentRequests.cs`) — `CreateIssuesAgentSessionRequest/Response`, `IssueDiffResponse`, `IssueDiffSummary`, `AcceptIssuesAgentChangesRequest/Response`.
- **`RunAgentRequest / RunAgentAcceptedResponse / AgentAlreadyRunningResponse`** (`src/Homespun.Shared/Requests/IssueRequests.cs`) — the agent-run contract (note: `RunAgentResponse` is also defined but unused — see Assumptions §A-4).

### Assumptions

- **A-1**: `FleeceService.cs` defines `ProjectFleeceService`, and `IFleeceService.cs` defines `IProjectFleeceService`. This filename/class mismatch predates a rename; the files should be renamed to match the primary type. **Gap GP-1 / fleece:KSXXVP.**
- **A-2**: `FleeceChangeApplicationService.cs` (881 lines) contains both `IFleeceChangeApplicationService`/`FleeceChangeApplicationService` *and* `IFleeceConflictDetectionService`/`FleeceConflictDetectionService`. The conflict-detection types should live in their own files. **Gap GP-2 / fleece:yf2OTa.**
- **A-3**: Two `IssueDto.cs` files exist — `Fleece/IssueDto.cs` defines `IssueResponse` (API surface), `Issues/IssueDto.cs` defines `IssueDto` (used only inside `ApplyAgentChanges*`). The duplication is historical; both types are near-identical but intentionally kept separate because the agent-change path handles the "before" and "after" issue state via simple settable DTOs and the API path goes through a dedicated `IssueResponse` so the Fleece.Core `Issue` type never leaks. **Gap GP-3 / fleece:JIGk7h.**
- **A-4**: `RunAgentResponse` in `IssueRequests.cs` is dead code. The controller returns `RunAgentAcceptedResponse` (202). **Gap GP-4 / fleece:vOx09w.**
- **A-5**: `GET /api/projects/{projectId}/issues/assignees` returns a raw `List<string>`. Every other endpoint returns a DTO. Deliberate simplicity, but inconsistent. **Gap GP-6 / fleece:Xqg4qH.**
- **A-6**: `IIssueHistoryService.MaxHistoryEntries = 100` is a constant; no configuration knob exists. Intentional upper bound so per-project history never dominates memory; but should be opt-in-configurable. **Gap GP-9 / fleece:Nee2Cy.**
- **A-7**: `IssuesAgentController` uses `/api/issues-agent/{sessionId}/*` addressing; every other Fleece controller uses `{projectId}` or `{issueId}`. The rationale is that once a session is created, all subsequent operations are session-scoped — the project-id is discoverable via the session — but the asymmetry is undocumented. **Gap GP-8 / fleece:0HQOyi.**
- **A-8**: `task-graph-view.tsx:1103` has a TODO to implement "load more PRs". The row renders but the handler is absent; practically there are rarely >1 PR per issue in current use. **Gap GP-5 / fleece:KuhsyT.**
- **A-9**: `Fleece.Core` NuGet version **MUST** match the `Fleece.Cli` version installed in `Dockerfile.base` (currently both `2.1.1`). This is enforced by convention, not by CI. Updating one without the other will produce schema-drift bugs. (Constitution §IX.)
- **A-10**: The `.fleece/` directory lives at the project's `LocalPath` root (e.g. `{HomespunBasePath}/{repo}/{defaultBranch}/.fleece/`). Moving the directory is not supported.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Core CRUD is covered end-to-end — `IssuesControllerTests` (1249 lines) + `IssuesApiTests` (670 lines) + `FleeceServiceTests` (1193 lines) together exercise every controller branch, including 404s, 409 conflicts, cycle rejections, and 400s. Test LOC to production LOC ratio for `IProjectFleeceService` is ~2:1.
- **SC-002**: Creating an issue from the inline editor produces an `IssueResponse` round-trip within a single React render cycle of the mutation resolving; the task graph re-renders in place without a full refetch (thanks to the SignalR `IssuesChanged` → query-invalidate loop).
- **SC-003**: The Konva task graph renders at 60fps for ≥500 issues. `computeLayout` + `task-graph-layout.ts` (1056 LOC) run in memoized selectors so lane/row assignments are only recomputed on graph change.
- **SC-004**: `Run Agent` on an issue consistently returns a 202 under race: two simultaneous POSTs produce exactly one 202 and one 409, with no duplicate clone or session created (`agentStartupTracker.TryMarkAsStarting` is the single atomic point).
- **SC-005**: Issues Agent accept-with-conflicts is reversible — the user is never in a state where the session's branch has partially applied changes; either the full `ApplyChangesAsync` succeeds or the `Conflicts[]` path returns zero mutated state on main.
- **SC-006**: `/api/fleece-sync/{projectId}/sync` is safe on a dirty working tree — `HasNonFleeceChanges` + `NonFleeceChangedFiles` are always reported back; the user must opt into `/discard-non-fleece-and-pull` to lose them.
- **SC-007**: Undo/redo is O(1) per operation in cache reloading: each call reads a single snapshot from the history buffer and writes it through the serialization queue; no full-directory re-scan is required.
- **SC-008**: Every controller endpoint has a matching test file in `tests/Homespun.Tests/Features/Fleece/` or `tests/Homespun.Api.Tests/Features/` — verifiable by grep; no controller method is uncovered at either the service or API layer.
