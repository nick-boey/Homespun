---
description: "Retrospective task list for the migrated Agent Dispatch feature"
---

# Tasks: Agent Dispatch

**Input**: Design documents from `/specs/agent-dispatch/`
**Status**: Migrated — all in-scope tasks reflect work that is already complete. Gaps are left **unchecked** and tracked in `follow-up-issues.md`.

> **Migration semantics.** `[x]` marks observed as-built work. Unchecked items are real, remediable gaps — do not delete them. Task groups mirror the user-story structure of `spec.md` so the backlog remains coherent with the SDD workflow going forward.

## Path Conventions (Homespun)

| Concern | Path |
|---------|------|
| Server slice | `src/Homespun.Server/Features/AgentOrchestration/...` |
| RunAgent endpoint host | `src/Homespun.Server/Features/Fleece/Controllers/IssuesController.cs` (scoped under an issue) |
| Web slice | `src/Homespun.Web/src/features/agents/...` |
| Shared contracts | `src/Homespun.Shared/Requests/{IssueRequests,OrchestrationRequests}.cs`, `src/Homespun.Shared/Hubs/INotificationHubClient.cs` |
| Server unit tests | `tests/Homespun.Tests/Features/AgentOrchestration/...` |
| Server API tests | `tests/Homespun.Api.Tests/Features/AgentOrchestration/...` |
| Web unit tests | co-located `*.test.ts(x)` next to the source |
| Web e2e tests | `src/Homespun.Web/e2e/...` (gap — FI-1) |

---

## Phase 1: Setup (Shared Infrastructure)

- [x] T001 AgentOrchestration slice scaffolding under `src/Homespun.Server/Features/AgentOrchestration/` with `Controllers/` + `Services/` subfolders.
- [x] T002 Web slice scaffolding under `src/Homespun.Web/src/features/agents/` with `components/`, `hooks/`, barrel `index.ts` files.
- [x] T003 Test project folders: `tests/Homespun.Tests/Features/AgentOrchestration/`, `tests/Homespun.Api.Tests/Features/AgentOrchestration/`.

---

## Phase 2: Foundational (Blocking Prerequisites)

- [x] T004 [P] Shared request DTOs in `src/Homespun.Shared/Requests/IssueRequests.cs` — `RunAgentRequest`, `RunAgentResponse`, `RunAgentAcceptedResponse`, `AgentAlreadyRunningResponse`.
- [x] T005 [P] Shared DTOs in `src/Homespun.Shared/Requests/OrchestrationRequests.cs` — `GenerateBranchIdRequest`, `GenerateBranchIdResponse`, `StartQueueRequest`, `QueueStatusResponse`, `QueueDetail`, `QueueHistoryEntry`, `QueueProgress`.
- [x] T006 [P] Hub contract in `src/Homespun.Shared/Hubs/INotificationHubClient.cs` — `AgentStarting(Guid, string, string)` and `AgentStartFailed(Guid, string, string)` methods.
- [x] T007 [P] `AgentStartRequest` record consolidating the dispatch payload (issue, project, branch, mode, model, instructions, workflow linkage).
- [x] T008 `IAgentStartupTracker` consumed from the `claude-agent-sessions` slice — atomic `TryMarkAsStarting`, `MarkAsStarted`, `MarkAsFailed`, `Clear`.

---

## Phase 3: User Story 1 — Dispatch an agent against a single issue (P1) 🎯 MVP

**Goal**: A user clicks "Run Agent" on an issue and gets a Claude Code session started against that issue within the 5-minute background budget, with `409` guarding the double-click case and `AgentStarting` / `AgentStartFailed` as end-of-pipeline SignalR signals.

**Independent Test**: `POST /api/issues/{issueId}/run` returns `202` + `RunAgentAcceptedResponse`; background pipeline completes → `AgentStarting` fires; session visible via `IClaudeSessionService`; clone exists on disk. Second identical POST returns `409`.

### Backend

- [x] T009 `IssuesController.RunAgent` handler in `Features/Fleece/Controllers/IssuesController.cs` — project/issue validation, atomic `TryMarkAsStarting`, queue dispatch, return 202.
- [x] T010 `AgentStartBackgroundService.QueueAgentStartAsync` in `Features/AgentOrchestration/Services/AgentStartBackgroundService.cs` — writes to the internal channel.
- [x] T011 `AgentStartBackgroundService.StartAgentAsync` — the 5-minute dispatch pipeline end-to-end: base-branch resolve → clone ensure → mode/model resolve → `IClaudeSessionService.StartSessionAsync` → fire-and-forget initial message → tracker state transition → `BroadcastAgentStarting`.
- [x] T012 Rendered-prompt-template path: when `PromptId` is present the service renders the template with issue context (title, description, branch, type, tree hierarchy); `Instructions` / `UserInstructions` override rendering when provided.
- [x] T013 Error paths inside `StartAgentAsync` — timeout (5 min), exception, blocked base branch — all call `MarkAsFailed` + `Clear` and broadcast `AgentStartFailed`.

### Web

- [x] T014 `RunAgentDialog` component in `features/agents/components/run-agent-dialog.tsx` with Task / Issues / Workflow tabs.
- [x] T015 `useRunAgent` hook in `features/agents/hooks/use-run-agent.ts` — mutation with 202/409/400 handling.
- [x] T016 `useStartAgent` hook in `features/agents/hooks/use-start-agent.ts` — direct `POST /api/sessions` for the non-issue-scoped case.
- [x] T017 `useAgentPrompts` hook in `features/agents/hooks/use-agent-prompts.ts` — prompt catalogue query (consumed from the upcoming `prompts` slice).
- [x] T018 `useEnsureClone` hook in `features/agents/hooks/use-ensure-clone.ts` — clone existence check before submit.

### Tests

- [x] T019 [P] `AgentStartBackgroundServiceTests` in `tests/Homespun.Tests/Features/AgentOrchestration/` — 12+ cases covering startup, timeout, tracker transitions, error broadcasting.
- [x] T020 [P] `StackedPrAgentStartTests` in `tests/Homespun.Api.Tests/Features/AgentOrchestration/` — explicit override, prior-sibling-PR auto-pick, default fallback.
- [x] T021 [P] `run-agent-dialog.test.tsx` and `use-run-agent.test.ts`.
- [x] T022 [P] `use-start-agent.test.tsx`, `use-agent-prompts.test.tsx`, `use-ensure-clone.test.tsx`.
- [x] T023 **GP-1**: API test covering the `202 Accepted` happy path of `POST /api/issues/{issueId}/run` — see `RunAgentApiTests` in `tests/Homespun.Api.Tests/Features/AgentOrchestration/`.

### Checkpoint

- [x] T024 US1 checkpoint: single-issue dispatch produces `202` synchronously and `AgentStarting` within the 5-minute budget, verified end-to-end by existing tests for the invariants.

---

## Phase 4: User Story 2 — Queue-orchestrate a tree of issues (P2)

**Goal**: `POST /queue/start` fans agents out across a tree of issues with correct series/parallel semantics; `GET /queue/status` exposes per-lane progress; `POST /queue/cancel` drains pending items without interrupting running sessions.

**Independent Test**: Start a queue over a mixed series/parallel tree; observe topological dispatch order; observe `QueueCreated` → `QueueCompleted` → `AllQueuesCompleted` events; cancel mid-run and see pending items drained while running items complete normally.

### Backend

- [x] T025 `QueueController` in `Features/AgentOrchestration/Controllers/QueueController.cs` — three endpoints under `/api/projects/{projectId}/queue/`.
- [x] T026 `QueueCoordinator.StartExecution` in `Features/AgentOrchestration/Services/QueueCoordinator.cs` — tree expansion into series/parallel lanes with deferred series continuations after parallel groups.
- [x] T027 `TaskQueue.Process` in `Features/AgentOrchestration/Services/TaskQueue.cs` — single-lane sequential processor that awaits each item's session reaching a terminal status before dispatching the next.
- [x] T028 Event surface: `QueueCreated`, `QueueCompleted`, `AllQueuesCompleted`, `ExecutionFailed` — emitted by `QueueCoordinator` / `TaskQueue`.
- [x] T029 Cancellation path: `POST /queue/cancel` marks pending items Cancelled without interrupting in-flight sessions.
- [ ] T030 Workflow integration seam: `WorkflowMappings` propagated through each dispatched item; session registered with `IWorkflowSessionCallback.RegisterSession` when mapping matches. **NOT BUILT** — design D5 was never implemented; the workflows slice has no callback into agent-dispatch today. Reopen if/when the workflows slice grows a registration seam.

### Tests

- [x] T031 [P] `QueueCoordinatorTests` — tree expansion across leaf / series / parallel configurations.
- [ ] T032 [P] `QueueCoordinatorWorkflowTests` — workflow-mapping propagation. **NOT BUILT** — depends on T030.
- [x] T033 [P] `TaskQueueTests` — sequential processing, terminal-status await, failure-stops-lane.
- [ ] T034 [P] `TaskQueueWorkflowTests` — workflow callback registration per item. **NOT BUILT** — depends on T030.
- [x] T035 [P] `QueueControllerTests` — unit coverage of controller wiring.
- [x] T036 [P] `QueueApiTests` — integration coverage via WebApplicationFactory.
- [ ] T037 **GP-5**: regression test that a throwing `IWorkflowSessionCallback.RegisterSession` is surfaced to the client cleanly. **OBSOLETE pending T030** — the seam doesn't exist, so there is no callback to throw. Tracked alongside T030; reopen with that work.
- [x] T038 **GP-6**: pagination for `GET /queue/status` + tests — `?limit` (default 50, max 200) and `?offset` (default 0); `QueueStatusResponse` carries `totalQueueCount`, `limit`, `offset`. Tests in `QueueControllerTests` + `QueueApiTests`.

### Checkpoint

- [x] T039 US2 checkpoint: a mixed tree dispatches with correct topological order and events, and cancellation semantics match spec.

---

## Phase 5: User Story 3 — Resolve a base branch (incl. stacked-PR + AI branch-id) (P2)

**Goal**: Base-branch resolution respects blocking issues and stacked-PR semantics; branch-id generation produces meaningful kebab-case ids via the sidecar (with a deterministic fallback on the background path).

**Independent Test**: `BaseBranchResolver` returns the correct branch for all three resolution modes and `Blocked = true` whenever applicable. `POST /api/orchestration/generate-branch-id` returns a kebab-case id ≤50 chars; background generator falls back when sidecar unreachable.

### Backend

- [x] T040 `BaseBranchResolver` in `Features/AgentOrchestration/Services/BaseBranchResolver.cs` — blocking checks (open children, open prior siblings) followed by branch selection (explicit > prior-PR > default).
- [x] T041 `OrchestrationController.GenerateBranchId` in `Features/AgentOrchestration/Controllers/OrchestrationController.cs` — sync endpoint backed by `IBranchIdGeneratorService`.
- [x] T042 `BranchIdGeneratorService` in `Features/AgentOrchestration/Services/BranchIdGeneratorService.cs` — AI generation + kebab-case normalisation + ≤50-char clamp.
- [x] T043 `MiniPromptService` + `MiniPromptOptions` — `HttpClient` → `/api/mini-prompt` sidecar with configurable URL + timeout.
- [x] T044 `BranchIdBackgroundService` — async branch-id generation with deterministic fallback when the sidecar is unreachable.

### Web

- [x] T045 `BaseBranchSelector` component in `features/agents/components/base-branch-selector.tsx` — dropdown backed by `useBranches`.
- [x] T046 `useBranches` hook — branch listing query.
- [x] T047 `useGenerateBranchId` hook — mutation calling the sync endpoint; used from the issue-edit dialog.

### Tests

- [x] T048 [P] `BaseBranchResolutionTests` — blocking children, blocking prior siblings, explicit override, prior-PR auto-pick, default fallback.
- [x] T049 [P] `BranchIdGeneratorServiceTests` — sidecar path + normalisation.
- [x] T050 [P] `BranchIdBackgroundServiceTests` — sidecar-failure fallback.
- [x] T051 [P] `MiniPromptServiceTests` — HTTP client behaviour + timeout handling.
- [x] T052 [P] `use-generate-branch-id.test.tsx`, `use-branches.test.tsx`, `base-branch-selector.test.tsx`.
- [x] T053 **GP-3**: `BaseBranchSelector` explicit error fallback when `useBranches` rejects — alert region with retry control. Tests in `base-branch-selector.test.tsx`.
- [x] T054 **GP-4**: sidecar health check at startup + fallback on the sync endpoint — `MiniPromptHealthCheckHostedService` probes the sidecar at boot; `OrchestrationController.GenerateBranchId` emits a `Warning: 199` HTTP header when the deterministic fallback fires. Tests in `MiniPromptHealthCheckHostedServiceTests` + `OrchestrationApiTests`.

### Checkpoint

- [x] T055 US3 checkpoint: all three base-branch resolution modes and both branch-id generation paths (sync + background) behave per spec.

---

## Phase 6: User Story 4 — Active-agents visibility (P3)

**Goal**: A header indicator surfaces how many agents are currently working, waiting, or in error, updating live via SignalR and TanStack Query invalidation.

**Independent Test**: `ActiveAgentsIndicator` reflects the server's live view within one SignalR round-trip and routes to `/sessions` on click.

### Web

- [x] T056 `ActiveAgentsIndicator` in `features/agents/components/active-agents-indicator.tsx` — header badge with error accent.
- [x] T057 `useActiveSessionCount` + `useAllSessionsCount` in `features/agents/hooks/`.
- [x] T058 `useProjectSessions` hook — project-scoped session listing used by the indicator detail view.
- [x] T059 `AgentStatusIndicator` + `AgentControlPanel` components — per-session status + control buttons in session views.

### Tests

- [x] T060 [P] `active-agents-indicator.test.tsx`, `use-active-session-count.test.tsx`, `use-all-sessions-count.test.tsx`.
- [x] T061 [P] `agent-status-indicator.test.tsx`, `agent-control-panel.test.tsx`.
- [x] T062 [P] `use-project-sessions.test.tsx`.
- [x] T063 **GP-2**: consistent UI handler for `AgentStartFailed` so failures surface even after the user navigates away — `useNotificationEvents` now also fires a route-agnostic `toast.error` alongside the bell-dropdown entry. Test in `use-notification-events.test.tsx`.

### Checkpoint

- [x] T064 US4 checkpoint: indicator reflects live state within one SignalR round-trip; error accent applies when any session is in `Error`.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [x] T065 SignalR hub methods `BroadcastAgentStarting` / `BroadcastAgentStartFailed` on `NotificationHub` used by `AgentStartBackgroundService`.
- [x] T066 OpenAPI surface: `RunAgent*` and `Orchestration*` DTOs appear in the generated client under `src/Homespun.Web/src/api/generated/`.
- [x] T067 Shared contract discipline: no hand-duplicated DTOs between server and web (Constitution §III verified by grep).
- [x] T068 **FI-1**: Playwright e2e coverage for the Run Agent dispatch surface — `e2e/agents/run-agent-dispatch.spec.ts` covers 202 happy path, 409 double-dispatch, and 404 project-missing through the live Vite + .NET stack. Existing `agent-and-issue-agent-launching.spec.ts` covers the dialog → 202 UI flow. The route-agnostic AgentStartFailed toast is verified by `use-notification-events.test.tsx`.

---

## Summary

| Phase | Tasks | Complete | Gaps |
|-------|-------|----------|------|
| Setup | T001–T003 | 3/3 | — |
| Foundational | T004–T008 | 5/5 | — |
| US1 (P1 🎯) | T009–T024 | 16/16 | — |
| US2 (P2) | T025–T039 | 12/15 | T030 (not built), T032/T034 (depend on T030), GP-5 (obsolete pending T030) |
| US3 (P2) | T040–T055 | 16/16 | — |
| US4 (P3) | T056–T064 | 9/9 | — |
| Polish | T065–T068 | 4/4 | — |
| **Total** | **68** | **65/68** | **3 gaps (all blocked on T030 — workflow seam not built)** |
