# Feature Specification: Agent Dispatch

**Feature Branch**: n/a (pre-spec-kit; built on `main` over many PRs)
**Created**: 2026-04-14 (migrated)
**Status**: Migrated
**Input**: Reverse-engineered from existing implementation in `src/Homespun.Server/Features/AgentOrchestration/`, the RunAgent-related surface of `src/Homespun.Server/Features/Fleece/Controllers/IssuesController.cs`, the shared DTOs under `src/Homespun.Shared/Requests/OrchestrationRequests.cs` + the `RunAgent*` types in `IssueRequests.cs`, the hub methods `AgentStarting`/`AgentStartFailed` in `src/Homespun.Shared/Hubs/INotificationHubClient.cs`, `src/Homespun.Web/src/features/agents/`, and the tests under `tests/Homespun.Tests/Features/AgentOrchestration/` + `tests/Homespun.Api.Tests/Features/AgentOrchestration/`.

> **Migration note.** This spec documents *what exists*, not a future design. "Agent Dispatch" is the **dispatch-and-orchestrate** layer that sits above the already-migrated `claude-agent-sessions` feature: it decides *whether* to start an agent, *which branch* it should run on, and *in what order* when multiple issues are queued — then hands off to `IClaudeSessionService` to actually run the session. It deliberately does NOT own: the session streaming loop (→ `claude-agent-sessions`), issue/graph data or sync (→ `fleece-issues`), project lookup or default-branch persistence (→ `projects`), or the prompt template catalogue itself (→ the upcoming `prompts` migration — this slice only *consumes* prompts).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Dispatch an agent against a single issue (Priority: P1) 🎯 MVP

As a Homespun user on an issue page I click "Run Agent", pick a model / prompt / base-branch / optional extra instructions, and the app kicks off a Claude Code session that is already scoped to that issue — without making me wait on clone creation or session startup in the foreground.

**Why this priority**: Nothing else in this feature has value without the primary "run an agent on an issue" loop. Queue orchestration (US2), branch resolution (US3), and the active-agents badge (US4) all exist to make this single flow fast, correct, and observable.

**Independent Test**: `POST /api/issues/{issueId}/run` with `{ projectId, mode, model?, baseBranch?, userInstructions? }` returns `202 Accepted` with a `RunAgentAcceptedResponse { issueId, branchName, message }`; within the 5-minute background-startup budget, `AgentStarting(issueId, projectId, branchName)` fires on `NotificationHub`, a `ClaudeSession` appears via `IClaudeSessionService`, and the clone for `branchName` exists on disk. A second identical POST while the first is still live returns `409 Conflict` with `AgentAlreadyRunningResponse { sessionId, status, message }`. Covered by `StackedPrAgentStartTests`, `AgentStartBackgroundServiceTests` (12 cases), and web-side `run-agent-dialog.test.tsx` + `use-run-agent.test.ts`.

**Acceptance Scenarios**:

1. **Given** no session is active for the issue, **When** the user submits the Task tab of `RunAgentDialog`, **Then** the web client calls `useRunAgent().mutateAsync()` → `POST /api/issues/{issueId}/run`, the server validates project + issue, atomically marks the issue as "starting" via `IAgentStartupTracker`, queues an `AgentStartRequest` on `IAgentStartBackgroundService`, and returns `202` immediately.
2. **Given** the request carries `mode` and `model`, **When** the background service starts the session, **Then** those values override any mode inferred from the prompt; if omitted, mode falls back to the prompt's mode (e.g. Plan) and model falls back to the project's `DefaultModel` and finally to `"sonnet"`.
3. **Given** a prompt template is selected, **When** the background service prepares the initial message, **Then** it renders the template with the issue context (title, description, branch, type, tree hierarchy) and sends the rendered message as a fire-and-forget `IClaudeSessionService.SendMessageAsync` *after* the session is created.
4. **Given** the dispatch fails during base-branch resolution, clone creation, or session creation, **When** the 5-minute timeout elapses or an exception is thrown, **Then** the server logs the error, calls `IAgentStartupTracker.MarkAsFailed` + `Clear`, and broadcasts `AgentStartFailed(issueId, projectId, error)` over `NotificationHub`.
5. **Given** a session is already running for the issue, **When** a second dispatch is attempted, **Then** the controller returns `409 Conflict` without queueing a new background task.

---

### User Story 2 - Queue-orchestrate a tree of issues (Priority: P2)

As a user with a parent issue whose children are divided into series and parallel groups I start "Run Queue" on the parent and the system fans agents out in the correct order — parallel siblings run together, series siblings run one after another, and the coordinator reports live progress.

**Why this priority**: Bulk dispatch is the lever that turns Homespun from "run one agent" into "run a plan". Lower than US1 because a user can still complete work manually one issue at a time.

**Independent Test**: `POST /api/projects/{projectId}/queue/start { issueId, workflowMappings? }` expands the issue tree through `IQueueCoordinator.StartExecution`, producing one `ITaskQueue` per series lane plus a single parallel group for parallel siblings; `GET /api/projects/{projectId}/queue/status` returns a `QueueStatusResponse` whose `Queues[]` reflect each lane's `Pending | Running | Completed | Failed` with a `Progress { total, completed }` counter; `POST /api/projects/{projectId}/queue/cancel` stops pending items. Covered by `QueueCoordinatorTests`, `QueueCoordinatorWorkflowTests`, `TaskQueueTests`, `TaskQueueWorkflowTests`, `QueueControllerTests`, and the API-level `QueueApiTests`.

**Acceptance Scenarios**:

1. **Given** a parent issue with three series children A→B→C, **When** the user starts the queue, **Then** `QueueCoordinator` creates one `ITaskQueue` with A, B, C in order; each child dispatches only after the previous one reaches a terminal session status; `QueueCreated` → `QueueCompleted` → `AllQueuesCompleted` events are emitted in that order.
2. **Given** a parent whose children are two parallel groups [P1a, P1b] and [P2a, P2b], **When** the queue starts, **Then** the coordinator groups them into parallel lanes and both items in a group run concurrently; the next parallel group only starts after every item in the previous group reaches a terminal session status.
3. **Given** any queued item fails its agent startup, **When** the background service broadcasts `AgentStartFailed`, **Then** the owning `TaskQueue` marks that item Failed, the coordinator emits `ExecutionFailed`, and the remaining pending items in that lane are **not** processed (sibling lanes continue unaffected).
4. **Given** the queue has been started, **When** `POST /queue/cancel` is called, **Then** every pending item in every lane is marked cancelled; already-running items continue to completion (the coordinator does not interrupt an in-flight Claude session).
5. **Given** `workflowMappings` is supplied mapping issue types to workflow ids, **When** the coordinator dispatches each item, **Then** the matching `WorkflowId` is threaded through to `AgentStartBackgroundService.QueueAgentStartAsync` so the session is registered with `IWorkflowSessionCallback.RegisterSession`.

---

### User Story 3 - Resolve a base branch (incl. stacked-PR + AI branch-id generation) (Priority: P2)

As a user dispatching an agent against an issue that is part of a stacked sequence I trust Homespun to (a) refuse to start if I would be overwriting a blocking sibling's work and (b) otherwise pick the correct base branch — my explicit override if given, else a prior-sibling's open PR branch, else the project default — and to generate a meaningful branch id for the new work via a small AI sidecar call.

**Why this priority**: Correct base-branch selection is the difference between a clean stacked PR and a merge disaster. US1 can technically work with only the default branch, but stacked workflows need US3.

**Independent Test**: `IBaseBranchResolver.ResolveBaseBranchAsync` returns a `BaseBranchResolution` whose `.Branch` matches the explicit override if provided; otherwise the PR branch of the first open sibling with an open PR; otherwise the project's default branch; and whose `.Blocked` is `true` when the issue has open child issues or open prior series siblings. `POST /api/orchestration/generate-branch-id { issueTitle, issueDescription? }` returns `{ branchId }` (kebab-case, ≤50 chars) via `MiniPromptService` → sidecar at `/api/mini-prompt`. Covered by `BaseBranchResolutionTests`, `BranchIdGeneratorServiceTests`, `BranchIdBackgroundServiceTests`, `MiniPromptServiceTests`, and `StackedPrAgentStartTests`.

**Acceptance Scenarios**:

1. **Given** the issue has one or more open child issues, **When** the resolver runs, **Then** it returns `Blocked = true` with a message naming the blocking children; dispatch aborts and `AgentStartFailed` is broadcast.
2. **Given** the issue is a child of a series parent and has one or more open prior siblings, **When** the resolver runs, **Then** it returns `Blocked = true` naming the prior siblings.
3. **Given** `BaseBranch` is supplied explicitly in the request, **When** the resolver runs, **Then** the explicit branch wins over any inferred value.
4. **Given** no explicit override and the issue has a completed prior series sibling whose PR is still open, **When** the resolver runs, **Then** it returns that sibling PR's branch so work stacks cleanly.
5. **Given** the AI branch-id sidecar is reachable, **When** `BranchIdGeneratorService.GenerateAsync` is called, **Then** the result is kebab-case, issue-summary-derived, and shorter than 50 characters; if the sidecar is unreachable, the service falls back to a deterministic slug of the issue title.

---

### User Story 4 - See which agents are active right now (Priority: P3)

As a user I glance at the header and see how many agents are currently working, waiting, or in error, so I can decide whether to pile more work on or whether to babysit what's already running.

**Why this priority**: Purely ambient awareness — no workflow is blocked without it, but it prevents the class of "I forgot I had four agents running" mistakes that follow from US2.

**Independent Test**: `ActiveAgentsIndicator` subscribes to `useActiveSessionCount`; the count auto-updates when `AgentStarting` or session-status hub events fire; clicking the indicator navigates to `/sessions`. Covered by `active-agents-indicator.test.tsx`, `use-active-session-count.test.tsx`, `use-all-sessions-count.test.tsx`.

**Acceptance Scenarios**:

1. **Given** zero active agents, **When** the header renders, **Then** the indicator renders compactly (count = 0) but remains clickable.
2. **Given** one or more agents in `Running`, `WaitingForInput`, `WaitingForQuestionAnswer`, or `WaitingForPlanExecution`, **When** the count changes, **Then** the badge updates without a full page reload (TanStack Query + hub invalidation).
3. **Given** any agent is in `Error` status, **When** the indicator renders, **Then** an error accent is applied to communicate that at least one session needs attention.

---

### Edge Cases

- **Rapid double-click on Run Agent** — `IAgentStartupTracker.TryMarkAsStarting` atomically wins exactly one caller; the other observes the existing session handle and returns `409`. Covered by `AgentStartBackgroundServiceTests.TryMarkAsStarting_*` cases.
- **Server restart mid-dispatch** — background task is lost. There is no retry or resume; the issue remains in the `Starting` tracker state until the next restart clears in-memory state. Gap — see `plan.md` §Complexity Tracking.
- **Sidecar absent at boot** — `MiniPromptService` does not health-check at startup. The first call to `/api/orchestration/generate-branch-id` returns `500` with `InvalidOperationException`. The background generator (`BranchIdBackgroundService`) catches and falls back to a deterministic slug; the synchronous endpoint does not. Gap GP-4.
- **Queue cancellation races** — Cancelling a queue while its *first* item is being dispatched (background task already queued but session not yet created) leaves that item in `Running` until the session starts or the background task times out; other items cancel cleanly.
- **Workflow callback registration failure** — `QueueCoordinator` registers the session with `IWorkflowSessionCallback` without wrapping in try/catch; a throw here surfaces as a `500` from the dispatch endpoint. Gap GP-5.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Expose `POST /api/issues/{issueId}/run` returning `202 Accepted` + `RunAgentAcceptedResponse` for the single-issue dispatch flow, `409 Conflict` + `AgentAlreadyRunningResponse` when a session is already live, `404` for unknown project or issue, and `400` when the issue is Blocked by the base-branch resolver.
- **FR-002**: Expose `POST /api/projects/{projectId}/queue/start`, `GET /api/projects/{projectId}/queue/status`, and `POST /api/projects/{projectId}/queue/cancel` for queue orchestration, with DTOs defined in `src/Homespun.Shared/Requests/OrchestrationRequests.cs`.
- **FR-003**: Expose `POST /api/orchestration/generate-branch-id` returning `{ branchId }` generated via the mini-prompt sidecar; on sidecar failure the controller returns `500` (fallback is currently only on the background path — see GP-4).
- **FR-004**: `IAgentStartupTracker` MUST guarantee at-most-one concurrent "starting" marker per `(projectId, issueId)` pair; the tracker entry MUST be cleared after the agent is either fully started or has failed.
- **FR-005**: `AgentStartBackgroundService.StartAgentAsync` MUST enforce a 5-minute wall-clock timeout on the entire dispatch pipeline (base-branch resolve → pull → clone → session-create → initial message) and MUST broadcast `AgentStartFailed` with the timeout error if exceeded.
- **FR-006**: `IBaseBranchResolver.ResolveBaseBranchAsync` MUST check for blocking issues (open children; open prior series siblings) BEFORE any branch selection; a Blocked result MUST short-circuit the dispatch and MUST NOT create a clone.
- **FR-007**: Base-branch selection order MUST be: (a) explicit `BaseBranch` in the request; (b) PR branch of the first open prior series sibling that has an open PR; (c) project default branch.
- **FR-008**: Mode resolution order: explicit `Mode` in the request > prompt template's mode > `Build`. Model resolution order: explicit `Model` > project `DefaultModel` > `"sonnet"`.
- **FR-009**: If the clone for the resolved branch does not exist, the background service MUST (a) call `IFleeceIssuesSyncService.PullFleeceOnlyAsync` on the default branch and (b) call `IGitCloneService.CreateCloneAsync` before creating the Claude session.
- **FR-010**: After session creation, the background service MUST send the rendered initial message via a fire-and-forget `Task.Run(() => IClaudeSessionService.SendMessageAsync(...))` — never inline.
- **FR-011**: `AgentStarting` / `AgentStartFailed` MUST be broadcast only AFTER all dispatch steps have run; they are end-of-pipeline signals, not mid-pipeline progress events.
- **FR-012**: `IQueueCoordinator.StartExecution` MUST expand an issue tree into: one `ITaskQueue` per leaf, one sequential `ITaskQueue` per series parent, and one parallel-group structure with deferred series continuations per parallel parent. Execution order MUST be topological: a child never runs before its parent dependency resolves.
- **FR-013**: `ITaskQueue` MUST emit `QueueCreated`, `QueueCompleted`, `AllQueuesCompleted`, and `ExecutionFailed` events and MUST stop processing its own lane on first `ExecutionFailed` (sibling lanes unaffected).
- **FR-014**: `IBranchIdGeneratorService.GenerateAsync` MUST return a kebab-case id ≤50 chars derived from the issue title/description; on sidecar failure the *background* path MUST fall back to a deterministic slug of the issue title.

### Key Entities

- **`AgentStartRequest`** (`Features/AgentOrchestration/Services/AgentStartBackgroundService.cs`) — internal record carrying the full dispatch payload: `IssueId`, `ProjectId`, `BranchName`, `Mode?`, `Model?`, `BaseBranch?`, `Instructions?`, `UserInstructions?`, `PromptId?`, `WorkflowId?`, `WorkflowExecutionId?`, `WorkflowStepId?`.
- **`RunAgentRequest` / `RunAgentResponse` / `RunAgentAcceptedResponse` / `AgentAlreadyRunningResponse`** (`Homespun.Shared.Requests.IssueRequests`) — HTTP contract for `/api/issues/{id}/run`.
- **`BaseBranchResolution`** (internal) — `{ Branch, Blocked, Reason? }`.
- **`GenerateBranchIdRequest` / `GenerateBranchIdResponse`** (`Homespun.Shared.Requests.OrchestrationRequests`).
- **`StartQueueRequest` / `QueueStatusResponse` / `QueueDetail` / `QueueHistoryEntry` / `QueueProgress`** (same file) — queue HTTP contract.
- **Hub methods** on `INotificationHubClient`: `AgentStarting(Guid issueId, string projectId, string branchName)`, `AgentStartFailed(Guid issueId, string projectId, string error)`.

### Assumptions

- **A-1**: "Agent dispatch" assumes the `claude-agent-sessions` slice exposes `IClaudeSessionService.StartSessionAsync` / `SendMessageAsync` and `IAgentStartupTracker`; this slice does not re-implement session lifecycle.
- **A-2**: "Agent dispatch" assumes `fleece-issues` exposes `IProjectFleeceService` (issue hierarchy lookup), `IFleeceIssuesSyncService.PullFleeceOnlyAsync`, and `IGitCloneService` (clone resolution + creation).
- **A-3**: The AI branch-id sidecar is optional *for the background flow* (has fallback) but *required* for the synchronous `/api/orchestration/generate-branch-id` endpoint. Turning the endpoint off when the sidecar is missing is left to deployment config; no in-app feature flag exists today.
- **A-4**: The 5-minute timeout in `AgentStartBackgroundService` is hard-coded. No per-project or per-environment override.
- **A-5**: `QueueCoordinator` does NOT persist queue state. A server restart mid-run drops all queue state and history.
- **A-6**: Workflow integration is an opt-in layer: if `WorkflowMappings` is empty the coordinator behaves as plain issue-tree orchestration; the workflow callback machinery is owned by a separate (not-yet-migrated) `workflows` slice — this spec only documents the dispatch seam.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The single-issue dispatch loop (click Run Agent → first streamed token visible) never blocks the HTTP request thread for more than it takes to validate + queue. The `RunAgent` endpoint returns `202` in p95 < 250 ms excluding the background pipeline — asserted implicitly by `QueueApiTests` holding the happy path under its default test timeout.
- **SC-002**: Dispatching the same issue twice within the startup window is impossible: a second call returns `409` deterministically (no test has ever observed two concurrent sessions for one issue).
- **SC-003**: Stacked-PR workflows produce a correct base-branch chain: `StackedPrAgentStartTests` cover the three primary configurations (explicit override, prior-sibling-PR auto-pick, default-branch fallback) with no cross-case drift.
- **SC-004**: A queue of N issues completes with exactly N agent sessions created; no item is dispatched twice and no item is silently dropped — covered by `QueueCoordinatorTests.Orchestrates_*` and `TaskQueueTests.Processes_All_*`.
- **SC-005**: The active-agents indicator reflects the server's view within one SignalR round-trip — asserted by `active-agents-indicator.test.tsx` simulating `AgentStarting` and session-status events.
- **SC-006**: Every error path in `AgentStartBackgroundService` ends with `IAgentStartupTracker.Clear(projectId, issueId)` — no "stuck starting" markers can leak past a failed dispatch.

## Identified Gaps

Detailed remediation lives in `plan.md` → Complexity Tracking and `follow-up-issues.md`.

- **GP-1**: No integration test covers the `202 Accepted` happy path of `POST /api/issues/{issueId}/run`; only `409`, `404`, and blocked-base-branch cases are directly asserted.
- **GP-2**: `AgentStartFailed` has no consistent UI handler; a user who navigates away from the issue page before failure arrives sees no surfaced error.
- **GP-3**: `BaseBranchSelector` has a loading state but no explicit error fallback when `useBranches` rejects.
- **GP-4**: `MiniPromptService` throws at runtime when the sidecar is absent; there is no startup health check and the synchronous branch-id endpoint has no fallback path (only the background generator does).
- **GP-5**: `QueueCoordinator` invokes `IWorkflowSessionCallback.RegisterSession` without try/catch — a workflow callback failure crashes the dispatch.
- **GP-6**: `QueueStatusResponse.Queues[]` is unbounded; no pagination or cap on `GET /queue/status`.
