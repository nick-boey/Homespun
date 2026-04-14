# Implementation Plan: Agent Dispatch

**Branch**: n/a (pre-spec-kit; built on `main` over many PRs)  |  **Date**: 2026-04-14 (migrated)  |  **Spec**: [`./spec.md`](./spec.md)
**Status**: Migrated — describes the as-built implementation, not a future design.

## Summary

Accept an HTTP dispatch request for an issue (or a tree of issues), atomically claim the issue, resolve its base branch, ensure a clone exists on disk, create a Claude Code session via the already-migrated `claude-agent-sessions` slice, send the rendered initial message fire-and-forget, and broadcast the outcome over SignalR — all inside a 5-minute background budget so the HTTP caller gets `202` immediately. Orchestrate multiple issues through series/parallel queues with topological execution. Generate kebab-case branch ids via a tiny AI sidecar, with a deterministic fallback on the background path.

## Technical Context

**Language/Version**: C# / .NET 10 (server + shared), TypeScript 5.9 + React 19 (web).

**Primary Dependencies**:
- Server: ASP.NET Core minimal API + controllers, SignalR (`NotificationHub`), `IHostedService` / background queue via `System.Threading.Channels`, `HttpClient` (for sidecar), existing `IClaudeSessionService`, `IGitCloneService`, `IFleeceIssuesSyncService`, `IProjectFleeceService`.
- Web: TanStack Query mutations (`useRunAgent`, `useStartAgent`, `useGenerateBranchId`), `@microsoft/signalr` subscriptions (`AgentStarting`, `AgentStartFailed`, session-status events), shadcn/ui dialog + form primitives.
- Sidecar: internal `/api/mini-prompt` service — an out-of-process worker delivering short structured completions; this slice owns the client (`MiniPromptService`) but not the sidecar itself.

**Storage**: None. This feature is purely orchestration — it reads issue data from `fleece-issues`, clones from `fleece-issues` / `Git` slices, project metadata from `projects`, and delegates persistence of session + message state to `claude-agent-sessions`. The `IAgentStartupTracker` and `IQueueCoordinator` both hold state in-memory; **a server restart drops queue state entirely** (see Complexity Tracking).

**Testing**: NUnit + Moq (unit) covering every service and resolver; WebApplicationFactory (API) for the queue endpoints; Vitest + RTL (web) for hooks + components; Playwright e2e coverage is **absent** for this slice (follow-up FI-1).

**Target Platform**: Linux containers for server + worker; branch-id sidecar runs as a separate container in the same Aspire graph.

**Project Type**: Cross-slice orchestration module inside the ASP.NET monolith, with a matching paired feature slice on the React SPA.

**Performance Goals**:
- `POST /api/issues/{id}/run` returns `202` in p95 < 250 ms (synchronous portion: validate + queue).
- Background dispatch pipeline completes within 5 minutes from queueing to `AgentStarting` broadcast; exceeding the budget aborts the pipeline and broadcasts `AgentStartFailed`.

**Constraints**:
- At-most-one concurrent dispatch per `(projectId, issueId)` — enforced in-process by `IAgentStartupTracker`; there is **no cross-server coordination** (single-server deployment assumed).
- Cross-process DTOs live in `src/Homespun.Shared` (Constitution §III).
- Must not interrupt already-running agents when cancelling a queue — cancellation only drains pending items.

**Scale/Scope**:
- Server slice: 16 C# files / ~2,570 LOC across `Controllers/` and `Services/`, plus touching the RunAgent endpoint inside `Features/Fleece/Controllers/IssuesController.cs`.
- Web slice: 22 production files / ~2,230 LOC plus 7 test files / ~2,660 LOC under `features/agents/`.
- Shared contracts: `Requests/OrchestrationRequests.cs` and the `RunAgent*` types in `Requests/IssueRequests.cs`; two hub methods on `INotificationHubClient`.
- Tests: 12 files / ~5,160 LOC spanning `tests/Homespun.Tests/Features/AgentOrchestration/` (unit) and `tests/Homespun.Api.Tests/Features/AgentOrchestration/` (API).

## Constitution Check

*Retrospective check for the as-built feature. Any box unchecked is called out under **Complexity Tracking** with a remediation note.*

| # | Gate | Pass? | Notes |
|---|------|-------|-------|
| I    | Test-First — failing tests written before production code | [~] | Service-level coverage is strong (12 unit/API test files; ~2:1 ratio test-to-prod LOC). E2E coverage is missing (FI-1). The `202` happy path of the dispatch endpoint itself lacks a direct API test (GP-1). |
| II   | Vertical Slice Architecture — change scoped to identified slice(s) | [x] | All server code under `Features/AgentOrchestration/`; paired web code under `features/agents/`. The one cross-slice touch — `IssuesController.RunAgent` — is unavoidable because the endpoint is scoped to an issue, but its body delegates to this slice's services. |
| III  | Shared Contract Discipline — DTOs in `Homespun.Shared`; OpenAPI client regenerated, not hand-edited | [x] | `RunAgent*` and all `Orchestration*` DTOs + both hub methods live in `src/Homespun.Shared`. Web consumes the generated OpenAPI client. |
| IV   | Pre-PR Quality Gate — `dotnet test`, `npm run lint:fix`, `format:check`, `generate:api:fetch`, `typecheck`, `test`, `test:e2e` all pass | [~] | Historic PRs ran the gate. `test:e2e` is currently thin for this slice (FI-1). |
| V    | Coverage — delta ≥ 80% on changed lines AND no regression vs `main`; on track for 60%/2026-06-30 and 80%/2026-09-30 | [~] | Production-to-test LOC ratio ≈ 1:2 on the server side, good component coverage on the web side. Missing: 202-path API test, sidecar-absent integration coverage, error-UI component coverage. |
| VI   | Fleece-Driven Workflow — issue exists, status transitions, `.fleece/` committed | [n/a] | Slice predates the workflow. Follow-ups drafted in `follow-up-issues.md`. |
| VII  | Conventional Commits + PR suffix; allowed branch prefix | [x] | Observed in history. |
| VIII | Naming — PascalCase (C#) / kebab-case (web feature folders) / co-located tests | [x] | Observed throughout. |
| IX   | Fleece.Core ↔ Fleece.Cli version sync | [n/a] | Feature does not touch `Fleece.Core`. |
| X    | Container & mock-shell safety preserved | [x] | The slice never targets `homespun` / `homespun-prod` containers; session containers are owned by `claude-agent-sessions`. |
| XI   | Logs queried via Loki | [x] | All dispatch failures log through the standard pipeline → Loki. |

## Project Structure

```
src/Homespun.Server/Features/AgentOrchestration/
├── Controllers/
│   ├── OrchestrationController.cs        # POST /api/orchestration/generate-branch-id
│   └── QueueController.cs                # /api/projects/{projectId}/queue/{start|status|cancel}
└── Services/
    ├── AgentStartBackgroundService.cs    # the 5-minute dispatch pipeline
    ├── IAgentStartBackgroundService.cs
    ├── BaseBranchResolver.cs             # blocking + base-branch selection
    ├── IBaseBranchResolver.cs
    ├── BranchIdBackgroundService.cs      # async branch-id generation w/ fallback
    ├── IBranchIdBackgroundService.cs
    ├── BranchIdGeneratorService.cs       # sidecar-backed kebab-case generator
    ├── IBranchIdGeneratorService.cs
    ├── MiniPromptService.cs              # HttpClient → /api/mini-prompt
    ├── IMiniPromptService.cs
    ├── MiniPromptOptions.cs              # sidecar URL + timeouts
    ├── QueueCoordinator.cs               # series/parallel expansion + events
    ├── IQueueCoordinator.cs
    ├── TaskQueue.cs                      # single-lane sequential queue
    └── ITaskQueue.cs

src/Homespun.Web/src/features/agents/
├── components/
│   ├── active-agents-indicator.tsx       # header badge
│   ├── agent-control-panel.tsx           # stop/pause/resume buttons
│   ├── agent-status-indicator.tsx        # status + duration + tokens
│   ├── base-branch-selector.tsx          # dropdown w/ useBranches()
│   ├── run-agent-dialog.tsx              # Task | Issues | Workflow tabs
│   └── index.ts
├── hooks/
│   ├── use-active-session-count.ts
│   ├── use-agent-prompts.ts
│   ├── use-all-sessions-count.ts
│   ├── use-branches.ts
│   ├── use-ensure-clone.ts
│   ├── use-generate-branch-id.ts
│   ├── use-project-sessions.ts
│   ├── use-run-agent.ts                  # POST /api/issues/{id}/run
│   ├── use-start-agent.ts                # POST /api/sessions (direct start)
│   └── index.ts
└── index.ts

src/Homespun.Shared/
├── Requests/
│   ├── IssueRequests.cs                  # RunAgentRequest/Response/AcceptedResponse/AgentAlreadyRunningResponse
│   └── OrchestrationRequests.cs          # GenerateBranchId*, StartQueue*, QueueStatus*
└── Hubs/
    └── INotificationHubClient.cs         # AgentStarting, AgentStartFailed

tests/
├── Homespun.Tests/Features/AgentOrchestration/
│   ├── AgentStartBackgroundServiceTests.cs
│   ├── BaseBranchResolutionTests.cs
│   ├── BranchIdBackgroundServiceTests.cs
│   ├── BranchIdGeneratorServiceTests.cs
│   ├── MiniPromptServiceTests.cs
│   ├── QueueControllerTests.cs
│   ├── QueueCoordinatorTests.cs
│   ├── QueueCoordinatorWorkflowTests.cs
│   ├── TaskQueueTests.cs
│   └── TaskQueueWorkflowTests.cs
└── Homespun.Api.Tests/Features/AgentOrchestration/
    ├── QueueApiTests.cs
    └── StackedPrAgentStartTests.cs
```

## Architecture Notes

### Dispatch pipeline (US1)

```
POST /api/issues/{id}/run
  └── IssuesController.RunAgent
        ├── validate project + issue via IProjectFleeceService
        ├── IAgentStartupTracker.TryMarkAsStarting           (atomic)
        ├── IAgentStartBackgroundService.QueueAgentStartAsync
        └── return 202 RunAgentAcceptedResponse
                      │
                      ▼
  AgentStartBackgroundService.StartAgentAsync              [5-min timeout]
        ├── IBaseBranchResolver.ResolveBaseBranchAsync     (blocking? prior-PR? default?)
        │       └── if Blocked → AgentStartFailed + exit
        ├── IGitCloneService.GetClonePathForBranchAsync
        │       └── if missing → PullFleeceOnlyAsync + CreateCloneAsync
        ├── resolve mode + model + render prompt template
        ├── IClaudeSessionService.StartSessionAsync
        ├── (optional) IWorkflowSessionCallback.RegisterSession
        ├── fire-and-forget Task.Run → SendMessageAsync
        ├── IAgentStartupTracker.MarkAsStarted + Clear
        └── NotificationHub.BroadcastAgentStarting
```

### Queue expansion (US2)

```
StartQueueRequest { issueId, workflowMappings? }
  └── QueueCoordinator.StartExecution
        ├── load issue tree from IProjectFleeceService
        ├── for each node, classify as leaf | series-parent | parallel-parent
        │     ├── leaf           → 1 TaskQueue with 1 item
        │     ├── series-parent  → 1 TaskQueue with children in series
        │     └── parallel-parent→ parallel group; series children after parallel group
        ├── start all queues respecting inter-queue dependencies
        ├── emit QueueCreated / QueueCompleted / AllQueuesCompleted / ExecutionFailed
        └── each TaskQueue.Process item → AgentStartBackgroundService.QueueAgentStartAsync
```

### Branch-id generation (US3)

```
sync:        POST /api/orchestration/generate-branch-id
             → OrchestrationController → BranchIdGeneratorService → MiniPromptService → sidecar
             (no fallback — see GP-4)

async/bg:    BranchIdBackgroundService.QueueBranchIdGeneration
             → BranchIdGeneratorService (try sidecar)
             → on failure, fall back to deterministic slug of issue title
```

## Complexity Tracking

| Area | Why it's complex | Mitigation today |
|------|------------------|------------------|
| In-memory tracker + queue state | Avoids a persistence layer, keeps dispatch fast, and fits the single-server deployment. | Documented assumption (A-5). Any move to multi-server will need a shared store. |
| 5-minute fixed timeout | A pragmatic ceiling on clone + session startup, not a knob. | Hard-coded in `AgentStartBackgroundService`; moving to options is cheap but no pressure yet. |
| Workflow callback seam inside coordinator | Keeps the slice from directly depending on the `workflows` slice. | The seam is a single interface (`IWorkflowSessionCallback`); documented in FR-012 and the Workflow tests. |
| Sidecar required for sync branch-id endpoint | Preserves fast path without a local model; fallback only exists on the bg path. | Logged as GP-4; acceptable because the endpoint is only used from the issue-edit dialog, where a user can retry. |

## Identified Gaps (mirror of `spec.md` → Identified Gaps)

- **GP-1**: No API test covers the 202 happy path of `RunAgent` — remediated by FI-1.
- **GP-2**: `AgentStartFailed` has no consistent UI handler — FI-2.
- **GP-3**: `BaseBranchSelector` has no error fallback — FI-3.
- **GP-4**: `MiniPromptService` has no startup health check + no sync fallback — FI-4.
- **GP-5**: `QueueCoordinator.RegisterSession` workflow callback unwrapped — FI-5.
- **GP-6**: `QueueStatusResponse.Queues[]` unpaginated — FI-6.

See `follow-up-issues.md` for draft Fleece stubs.
