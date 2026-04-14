# Implementation Plan: Claude Agent Sessions

**Branch**: n/a (pre-spec-kit; built on `main` over many PRs)  |  **Date**: 2026-04-14 (migrated)  |  **Spec**: [`./spec.md`](./spec.md)
**Status**: Migrated — describes the as-built implementation, not a future design.

## Summary

Run Claude Agent SDK sessions inside per-session Docker containers, stream their output over SignalR as AG-UI events, and give users interactive control over the lifecycle (mode/model switching, plan approval, question answering, context clearing, interrupt/stop, resume). A TypeScript worker (`Homespun.Worker`) is the SDK host; the ASP.NET server owns session identity, persistence of the JSONL cache, container reconciliation, and the web client contract via shared DTOs + generated OpenAPI.

## Technical Context

**Language/Version**: C# / .NET 10 (server, shared), TypeScript 5.9 + React 19 (web), TypeScript + Node (worker)

**Primary Dependencies**:
- Server: ASP.NET Core, SignalR, Swashbuckle, A2A, AGUI, Docker client APIs
- Web: TanStack Query, Zustand, shadcn/ui, prompt-kit, `@microsoft/signalr`, generated OpenAPI client
- Worker: Hono, `@anthropic-ai/claude-agent-sdk`, `@a2a-js/sdk`

**Storage**:
- In-memory `ClaudeSessionStore` (dictionary) for live session state.
- On-disk JSONL files per session at `/root/.claude_code/sessions/{id}.jsonl` inside the worker container, with JSON metadata sidecars under `.meta/`.
- **No SQLite tables** are introduced by this feature.

**Testing**: NUnit + Moq (unit), WebApplicationFactory (API), Vitest + RTL (web), Playwright (web e2e — **gap**), Vitest (worker — **gap**)

**Target Platform**: Linux containers; each session spawns a worker container alongside the main server.

**Project Type**: Multi-module monorepo — ASP.NET API + React SPA + Node worker + shared contracts.

**Performance Goals**:
- First streamed token visible to the client within the worker container's cold-start budget (not separately instrumented — gap SC-001).
- No observable backpressure on the SignalR hub when fanning out per-token AG-UI events for a single active session.

**Constraints**:
- Session state is in-memory on the server; server restarts lose live handles but never lose history (JSONL cache is authoritative for past turns).
- Container status is authoritative over in-memory status when the two disagree.
- Cross-process DTOs MUST originate in `src/Homespun.Shared` (Constitution §III).

**Scale/Scope**:
- One server ↔ N worker containers (N = concurrent live sessions), typically small N per user.
- Server slice alone is ~57 files / ~12.3K LOC; paired web slice ~89 files / ~15.1K LOC; worker ~10 files / ~2–3K LOC; shared DTOs ~26 files / ~1.7K LOC.

## Constitution Check

*Retrospective check for the as-built feature. Any box unchecked is called out under **Complexity Tracking** with a remediation note.*

| # | Gate | Pass? | Notes |
|---|------|-------|-------|
| I    | Test-First — failing tests written before production code | [~] | Strong backend coverage (unit + API). Worker and e2e tests are missing (gaps 1 & 2). Retrospective TDD can't be reconstructed; forward work on this slice MUST follow the rule. |
| II   | Vertical Slice Architecture — change scoped to identified slice(s) | [x] | All server code under `Features/ClaudeCode/`, paired web code under `features/sessions/`, worker code under `Homespun.Worker/src/`. Cross-slice utilities (e.g. `ContainerDiscoveryService`'s Docker client usage) stay within the slice. |
| III  | Shared Contract Discipline — DTOs in `Homespun.Shared`; OpenAPI client regenerated, not hand-edited | [x] | All session DTOs, enums, and hub interfaces live in `src/Homespun.Shared/Models/Sessions/`, `Requests/`, and `Hubs/`. Web consumes the generated client. |
| IV   | Pre-PR Quality Gate — `dotnet test`, `npm run lint:fix`, `npm run format:check`, `npm run generate:api:fetch`, `npm run typecheck`, `npm test`, `npm run test:e2e` all pass | [~] | Historic PRs ran the gate. E2E is currently thin for this slice (gap 1). |
| V    | Coverage — delta ≥ 80% on changed lines AND no regression vs `main`; on track for 60%/2026-06-30 and 80%/2026-09-30 | [~] | Server slice has dense tests (~1:1 LOC ratio production:tests). Worker slice has no tests (gap 2) and pulls the module's overall coverage number down. |
| VI   | Fleece-Driven Workflow — issue exists, status transitions, `.fleece/` committed | [n/a] | The slice predates the current workflow. Follow-up issues are drafted in `follow-up-issues.md` to backfill governance for the gaps. |
| VII  | Conventional Commits + PR suffix; allowed branch prefix | [x] | Historic commits follow the convention. |
| VIII | Naming — PascalCase (C#) / kebab-case (web feature folders) / co-located tests | [x] | Observed throughout. |
| IX   | Fleece.Core ↔ Fleece.Cli version sync | [n/a] | Feature does not bump Fleece.Core. |
| X    | Container & mock-shell safety preserved | [x] | Session containers are ephemeral worker containers distinct from `homespun` / `homespun-prod`; the slice never targets those. |
| XI   | Logs queried via Loki | [x] | Applicable logs surface through the standard server/worker logging pipelines → Loki. |

## Project Structure

### Documentation (this feature)

```text
specs/claude-agent-sessions/
├── spec.md                # User-visible feature description (migrated)
├── plan.md                # This file
├── tasks.md               # Retrospective task list, all completed except gaps
└── follow-up-issues.md    # Draft Fleece issue stubs for gaps (non-authoritative — create via fleece CLI)
```

### Source Code (repository root — as-built)

```text
src/
├── Homespun.Server/
│   └── Features/ClaudeCode/
│       ├── Controllers/
│       │   ├── SessionsController.cs           # /api/sessions surface
│       │   ├── AgentPromptsController.cs       # (owned by prompts slice — co-located here historically)
│       │   └── SessionCacheController.cs       # /api/sessions/{id}/cache/* read surface
│       ├── Hubs/ClaudeCodeHub.cs               # SignalR hub + broadcast extension methods
│       ├── Services/
│       │   ├── ClaudeSessionService.cs         # Façade over lifecycle / messaging / tools / stores
│       │   ├── SessionLifecycleService.cs      # Start / resume / stop / interrupt / mode / model
│       │   ├── MessageProcessingService.cs     # SDK stream → AG-UI events + cache writes
│       │   ├── ToolInteractionService.cs       # AskUserQuestion, ExitPlanMode, WorkflowSignal
│       │   ├── ClaudeSessionStore.cs           # In-memory live session store
│       │   ├── MessageCacheStore.cs            # JSONL cache reads/writes
│       │   ├── SessionMetadataStore.cs         # JSON metadata sidecar (mode, model, timestamps)
│       │   ├── SessionStateManager.cs          # State transitions + pending-operation tracking
│       │   ├── DockerAgentExecutionService.cs  # Spawns / talks to worker containers
│       │   ├── ClaudeSessionDiscovery.cs       # Enumerates resumable sessions from disk
│       │   ├── ContainerDiscoveryService.cs    # Authoritative container status
│       │   ├── ContainerRecoveryHostedService  # Startup reconciliation of orphan containers
│       │   ├── AGUIEventService.cs             # SDK → AG-UI event translation
│       │   ├── SdkMessageParser.cs             # SDK stream parsing
│       │   ├── A2AMessageParser.cs             # A2A → internal message translation
│       │   ├── ToolResultParser.cs             # Tool result extraction
│       │   ├── HooksService.cs                 # Startup / shutdown hook execution
│       │   ├── AgentStartupTracker.cs          # Startup progress tracking
│       │   └── AgentExecutionOptions.cs        # Execution configuration
│       ├── Data/
│       │   ├── ResumableSession.cs
│       │   ├── RunningSessionInfo.cs
│       │   ├── SessionCacheSummary.cs
│       │   ├── SessionTodoItem.cs
│       │   ├── WorkflowAgentStatus.cs
│       │   ├── MessageDisplayItem.cs
│       │   ├── SdkMessages.cs
│       │   ├── AGUIEvents.cs
│       │   └── HomespunA2AExtensions.cs
│       ├── Exceptions/AgentExecutionException.cs
│       ├── Settings/ClaudeSettings.cs
│       └── Resources/default-prompts.json
│
├── Homespun.Shared/
│   ├── Models/Sessions/                        # ClaudeSession, SessionMode, ClaudeSessionStatus,
│   │                                           # SessionType, ClaudeMessage(+Content), SessionSummary,
│   │                                           # SessionCacheSummary, ResumableSession, DiscoveredSession,
│   │                                           # SessionMetadata, PendingQuestion, UserQuestion,
│   │                                           # QuestionOption, QuestionAnswer, ClaudeModelInfo,
│   │                                           # SessionBranchInfo
│   ├── Requests/SessionRequests.cs             # CreateSessionRequest, SendMessageRequest, ResumeSessionRequest
│   └── Hubs/
│       ├── IClaudeCodeHub.cs
│       └── IClaudeCodeHubClient.cs
│
├── Homespun.Web/
│   └── src/features/sessions/
│       ├── components/                         # MessageList, ChatInput, BottomSheet, SessionCard,
│       │                                       # PlanApprovalPanel, ToolExecutionGroup/Row,
│       │                                       # ToolResult renderers (Bash/Read/Write/Grep),
│       │                                       # SessionInfoPanel tabs (Branch/Files/History/Issue/PR/Plans/Todos)
│       ├── hooks/                              # useSession, useSessions, useSessionsSignalR,
│       │                                       # useSessionMessages, useApprovePlan, useChangedFiles,
│       │                                       # useIssuePRStatus, useSessionBranchInfo, usePlanFiles,
│       │                                       # useSessionHistory, useClearContext,
│       │                                       # useChangeSessionSettings, useSessionShortcuts,
│       │                                       # useSessionNavigation
│       ├── utils/                              # TodoParser, ToolExecutionGrouper,
│       │                                       # SignalRMessageAdapter, renderPromptTemplate
│       └── index.ts                            # Public exports
│
└── Homespun.Worker/
    └── src/
        ├── index.ts                            # Hono entry point
        ├── routes/
        │   ├── sessions.ts                     # start / resume / send / stop
        │   ├── files.ts
        │   ├── info.ts
        │   └── health.ts
        ├── services/
        │   ├── session-manager.ts              # SDK client lifecycle
        │   ├── session-discovery.ts            # JSONL enumeration
        │   ├── a2a-translator.ts               # A2A ↔ SDK translation
        │   └── sse-writer.ts                   # SSE stream encoder
        ├── tools/workflow-tools.ts             # WorkflowSignal + ExitPlanMode custom tools
        └── types/a2a.ts

tests/
├── Homespun.Tests/Features/ClaudeCode/         # 27 unit test files (~15K LOC)
├── Homespun.Api.Tests/Features/                # SessionsApiTests.cs (+ supporting)
└── Homespun.Worker/                            # EMPTY — gap 2
src/Homespun.Web/e2e/                           # NO session specs — gap 1
src/Homespun.Web/src/features/sessions/**/*.test.tsx  # ~36 co-located tests
```

**Structure Decision**: File layout above reflects the as-built tree. Further work on this slice MUST keep additions under these same paths — any deviation is a Constitution §II violation and needs justification in the relevant PR's *Complexity Tracking*.

## Key Design Decisions (observed)

1. **Façade + focused services** — `ClaudeSessionService` is a thin façade; real work lives in `SessionLifecycleService`, `MessageProcessingService`, `ToolInteractionService`. This keeps individual services testable in isolation (evident in the dedicated `*ServiceTests.cs` suites) but at the cost of chasing behaviour through multiple files.
2. **Docker-OOD execution** — `DockerAgentExecutionService` runs worker containers out-of-process instead of embedding the SDK. Isolation + independent crash domain justify the added operational complexity (container reconciliation at startup, status divergence handling).
3. **File-based message cache** — JSONL on disk rather than SQLite. Aligns with the Claude Agent SDK's own on-disk session format (`--resume` is free) and avoids schema churn. Trade-off: concurrent writes need care, and the cache is only as durable as the container volume.
4. **AG-UI as the on-wire streaming vocabulary** — SDK events are translated to AG-UI before fan-out so the web never depends on SDK-internal shapes. `AGUIEventService` + `A2AMessageParser` are the translation layer.
5. **Container status as ground truth** — `ContainerDiscoveryService` beats the in-memory `ClaudeSessionStore` when they disagree. Rationale: containers can crash asynchronously and the in-memory state would otherwise drift.
6. **Plan / Build modes as tool allow-lists** — Mode isn't merely a label; it's a concrete allow-list forwarded to the SDK. Approval gates the transition.

## Complexity Tracking

| Violation / Partial gate | Why Needed | Remediation |
|---|---|---|
| Gate I partial — no worker or e2e tests | Pre-spec-kit code; the worker slice was kept small and "tested via integration" in practice. | Track remediation via `follow-up-issues.md` entries (FI-1, FI-2). Any future change to the worker must add tests first. |
| Gate IV partial — `npm run test:e2e` thin for sessions | E2E was deprioritised while the slice was in rapid iteration. | FI-1 (Playwright coverage). Until it lands, changes to this slice MUST add an e2e spec for the affected flow. |
| Gate V partial — worker pulls overall module coverage down | Worker has zero coverage today. | FI-2 (worker tests) explicitly targets the 60%/80% dated targets. |
| Recovered sessions hardcode Build/"sonnet" | Short-term shortcut — persisted metadata was available but not read on the recovery path. | FI-3. |
| `SessionsController.Create` fires and forgets | Async initial-message dispatch decoupled HTTP latency from SDK start time. | FI-4 — either return a correlation handle clients can await via the hub, or surface errors through a dedicated endpoint. |
| `ClaudeSessionStore` is unlocked | Single-writer assumption in early design; now violated by concurrent hub + HTTP paths. | FI-5. |
| `PlanFilePath` orphans | Plan files were written per turn without a lifecycle owner. | FI-6. |
| No context trimming | Out of scope when shipped; sessions were expected to be short-lived. | FI-7. |

All follow-up issues are drafted in `./follow-up-issues.md` and should be created in Fleece (`status: open`) as a first step after this migration lands.
