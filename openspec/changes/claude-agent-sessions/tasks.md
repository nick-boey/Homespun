---
description: "Retrospective task list for the migrated Claude Agent Sessions feature"
---

# Tasks: Claude Agent Sessions

**Input**: Design documents from `/specs/claude-agent-sessions/`
**Status**: Migrated — all in-scope tasks reflect work that is already complete. Gaps are left **unchecked** and tracked in `follow-up-issues.md`.

> **Migration semantics.** `[x]` marks observed as-built work. Unchecked items are real, remediable gaps — do not delete them. Task groups mirror the user-story structure of `spec.md` so the backlog remains coherent with the SDD workflow going forward.

## Path Conventions (Homespun)

| Concern | Path |
|---------|------|
| Server slice | `src/Homespun.Server/Features/ClaudeCode/...` |
| Web slice | `src/Homespun.Web/src/features/sessions/...` |
| Worker | `src/Homespun.Worker/src/{routes,services,tools,types}/...` |
| Shared contracts | `src/Homespun.Shared/{Models/Sessions,Requests,Hubs}/...` |
| Server unit tests | `tests/Homespun.Tests/Features/ClaudeCode/...` |
| Server API tests | `tests/Homespun.Api.Tests/Features/...` |
| Web unit tests | co-located `*.test.ts(x)` next to the source |
| Web e2e tests | `src/Homespun.Web/e2e/...` |
| Worker tests | `tests/Homespun.Worker/...` |

---

## Phase 1: Setup (Shared Infrastructure)

- [x] T001 Session slice scaffolding under `src/Homespun.Server/Features/ClaudeCode/` with Controllers / Hubs / Services / Data / Exceptions / Settings / Resources subfolders.
- [x] T002 Web slice scaffolding under `src/Homespun.Web/src/features/sessions/` with `components/`, `hooks/`, `utils/`, `index.ts`.
- [x] T003 Worker project `src/Homespun.Worker/` with `{routes,services,tools,types}/` layout and Hono entry point.
- [x] T004 Test projects initialised: `tests/Homespun.Tests/Features/ClaudeCode/`, `tests/Homespun.Api.Tests/Features/`.

---

## Phase 2: Foundational (Blocking Prerequisites)

- [x] T005 [P] Shared DTOs in `src/Homespun.Shared/Models/Sessions/` — `ClaudeSession`, `SessionMode`, `ClaudeSessionStatus`, `SessionType`, `ClaudeMessage(+Content)`, `SessionSummary`, `SessionCacheSummary`, `ResumableSession`, `DiscoveredSession`, `SessionMetadata`, `UserQuestion`/`PendingQuestion`/`QuestionOption`/`QuestionAnswer`, `ClaudeModelInfo`, `SessionBranchInfo`.
- [x] T006 [P] Shared request DTOs in `src/Homespun.Shared/Requests/SessionRequests.cs` — `CreateSessionRequest`, `SendMessageRequest`, `ResumeSessionRequest`.
- [x] T007 [P] Hub contracts in `src/Homespun.Shared/Hubs/IClaudeCodeHub.cs` + `IClaudeCodeHubClient.cs`.
- [x] T008 Swashbuckle annotations on controllers so OpenAPI emits the session surface with correct DTO shapes.
- [x] T009 Generated OpenAPI client committed under `src/Homespun.Web/src/api/generated/` covering `/api/sessions*` endpoints.
- [x] T010 Worker HTTP + SSE contract: `src/Homespun.Worker/src/routes/sessions.ts`, `files.ts`, `info.ts`, `health.ts` + `sse-writer.ts`.

**Checkpoint**: Foundation landed — per-story work flows from here.

---

## Phase 3: US1 — Stream an agent session end-to-end (P1) 🎯 MVP

**Goal**: Create a session, send a message, watch AG-UI events stream over SignalR.

### Tests

- [x] T011 [P] [US1] `tests/Homespun.Tests/Features/ClaudeCode/ClaudeSessionServiceTests.cs` — session façade + end-to-end happy path.
- [x] T012 [P] [US1] `tests/Homespun.Tests/Features/ClaudeCode/SessionLifecycleServiceTests.cs` — start / stop / state transitions.
- [x] T013 [P] [US1] `tests/Homespun.Tests/Features/ClaudeCode/MessageProcessingServiceTests.cs` — SDK stream → AG-UI events.
- [x] T014 [P] [US1] `tests/Homespun.Tests/Features/ClaudeCode/AGUIEventServiceTests.cs` — event-type coverage for RunStarted/Finished, TextMessage*, ToolCall*.
- [x] T015 [P] [US1] `tests/Homespun.Tests/Features/ClaudeCode/A2AMessageParserTests.cs` — A2A frame parsing.
- [x] T016 [P] [US1] `tests/Homespun.Tests/Features/ClaudeCode/SessionStateManagerTests.cs` — state machine.
- [x] T017 [P] [US1] `tests/Homespun.Tests/Features/ClaudeCode/DockerAgentExecutionServiceTests.cs` — container orchestration and message routing.
- [x] T018 [P] [US1] `tests/Homespun.Tests/Features/ClaudeCode/Controllers/SessionsControllerTests.cs` — endpoint behaviour.
- [x] T019 [P] [US1] `tests/Homespun.Api.Tests/Features/SessionsApiTests.cs` — full-stack HTTP session tests.
- [x] T020 [P] [US1] Web component tests co-located under `features/sessions/components/*.test.tsx` for `MessageList`, `ChatInput`, `SessionCard`, tool renderers.
- [x] T021 [P] [US1] Web hook tests: `useSession.test.ts`, `useSessions.test.ts`, `useSessionsSignalR.test.ts`, `useSessionMessages.test.ts`.
- [ ] T022 [US1] Playwright e2e in `src/Homespun.Web/e2e/sessions/stream-session.spec.ts` covering create → send → stream. **DEFERRED → fleece:EYE183**
- [ ] T023 [P] [US1] Worker unit tests for `session-manager.ts` and `sse-writer.ts`. **DEFERRED → fleece:EYE183**
### Implementation

- [x] T024 [P] [US1] `Services/ClaudeSessionService.cs` (façade).
- [x] T025 [P] [US1] `Services/SessionLifecycleService.cs` — start / resume / stop / interrupt.
- [x] T026 [P] [US1] `Services/MessageProcessingService.cs` — stream assembly + cache writes.
- [x] T027 [P] [US1] `Services/AGUIEventService.cs` + `SdkMessageParser.cs` + `A2AMessageParser.cs`.
- [x] T028 [P] [US1] `Services/ClaudeSessionStore.cs` — in-memory store.
- [x] T029 [P] [US1] `Services/SessionStateManager.cs` — transition guardrails.
- [x] T030 [P] [US1] `Services/DockerAgentExecutionService.cs` — container spawn + HTTP + SSE client.
- [x] T031 [P] [US1] `Hubs/ClaudeCodeHub.cs` + broadcast extension methods (`BroadcastSessionStarted/Stopped/Status...`, AG-UI `Broadcast*`).
- [x] T032 [P] [US1] `Controllers/SessionsController.cs` — `GET /api/sessions`, `GET /api/sessions/{id}`, `GET /api/sessions/entity/{entityId}`, `GET /api/sessions/project/{projectId}`, `POST /api/sessions`, `DELETE /api/sessions/{id}`, `POST /api/sessions/{id}/messages`, `POST /api/sessions/{id}/interrupt`.
- [x] T033 [P] [US1] Web `features/sessions/components/MessageList.tsx` + `ChatInput.tsx` + `ToolExecutionGroup.tsx` / `ToolExecutionRow.tsx` + tool result renderers (Bash, Read, Write, Grep).
- [x] T034 [P] [US1] Web hooks `useSession`, `useSessions`, `useSessionsSignalR`, `useSessionMessages`.
- [x] T035 [US1] Web route `/session/:id` + session list route.
- [x] T036 [US1] Worker `src/routes/sessions.ts` (start / send / stop) + `services/session-manager.ts` + `services/a2a-translator.ts`.

**Checkpoint**: US1 shippable independently — in production since before migration.

---

## Phase 4: US2 — Plan mode with approval (P1)

### Tests

- [x] T037 [P] [US2] `tests/Homespun.Tests/Features/ClaudeCode/ToolInteractionServiceTests.cs` — `ExitPlanMode` flow.
- [x] T038 [P] [US2] Server API test for `POST /api/sessions/{id}` plan approval path (covered in `SessionsApiTests`).
- [x] T039 [P] [US2] Web `PlanApprovalPanel.test.tsx` + `useApprovePlan.test.ts`.
- [ ] T040 [US2] Playwright e2e `src/Homespun.Web/e2e/sessions/plan-approval.spec.ts`. **DEFERRED → fleece:EYE183**
### Implementation

- [x] T041 [P] [US2] `Services/ToolInteractionService.cs` — ExitPlanMode handling, approve / reject / execute.
- [x] T042 [P] [US2] Hub methods `ApprovePlan`, `ExecutePlan` on `ClaudeCodeHub`.
- [x] T043 [P] [US2] Web `PlanApprovalPanel` + plan files sub-panel (`usePlanFiles`).
- [x] T044 [P] [US2] Worker custom tool `ExitPlanMode` in `src/tools/workflow-tools.ts`.

---

## Phase 5: US3 — Answer structured questions (P2)

### Tests

- [x] T045 [P] [US3] `ToolInteractionServiceTests.cs` — `AskUserQuestion` path.
- [x] T046 [P] [US3] Web Q&A component + hook tests.
- [ ] T047 [US3] Playwright e2e `src/Homespun.Web/e2e/sessions/question-answer.spec.ts`. **DEFERRED → fleece:EYE183**
### Implementation

- [x] T048 [P] [US3] `ToolInteractionService` AskUserQuestion flow — `PendingQuestion` capture + answer resume.
- [x] T049 [P] [US3] Hub method `AnswerQuestion`.
- [x] T050 [P] [US3] Web question panel component + `useAnswerQuestion` hook.

---

## Phase 6: US4 — Resume a prior session (P2)

### Tests

- [x] T051 [P] [US4] `tests/Homespun.Tests/Features/ClaudeCode/ClaudeSessionDiscoveryTests.cs` — JSONL + metadata enumeration.
- [x] T052 [P] [US4] `SessionMetadataStoreTests.cs` + `ClaudeSessionStoreTests.cs`.
- [x] T053 [P] [US4] `SessionCacheController` tests (API-side, in `SessionsApiTests`).
- [x] T054 [P] [US4] Web `useSessionHistory.test.ts` + history tab component tests.
- [ ] T055 [US4] Playwright e2e `src/Homespun.Web/e2e/sessions/resume-session.spec.ts`. **DEFERRED → fleece:EYE183**
- [ ] T056 [US4] Worker tests for `session-discovery.ts`. **DEFERRED → fleece:EYE183**
### Implementation

- [x] T057 [P] [US4] `Services/MessageCacheStore.cs` + `SessionMetadataStore.cs` + `ClaudeSessionDiscovery.cs`.
- [x] T058 [P] [US4] Endpoints: `GET /api/sessions/entity/{entityId}/resumable`, `GET /api/sessions/history/{projectId}/{entityId}`, `GET /api/sessions/{id}/cached-messages`, `POST /api/sessions/{id}/resume`.
- [x] T059 [P] [US4] `Controllers/SessionCacheController.cs` read-only surface.
- [x] T060 [P] [US4] Web `SessionHistoryTab` + `useSessionHistory` hook.
- [x] T061 [P] [US4] Worker `services/session-discovery.ts`.

---

## Phase 7: US5 — Switch mode or model mid-session (P2)

### Tests

- [x] T062 [P] [US5] `ClaudeSessionServiceTests` / `SessionLifecycleServiceTests` — `SetSessionMode_*`, `SetSessionModel_*`.
- [x] T063 [P] [US5] Web `useChangeSessionSettings.test.ts`.
- [ ] T064 [US5] Playwright e2e `src/Homespun.Web/e2e/sessions/switch-mode-model.spec.ts`. **DEFERRED → fleece:EYE183**
### Implementation

- [x] T065 [P] [US5] `SessionLifecycleService.SetSessionModeAsync` / `SetSessionModelAsync`.
- [x] T066 [P] [US5] Hub methods `SetSessionMode`, `SetSessionModel`.
- [x] T067 [P] [US5] Web `useChangeSessionSettings` + mode/model controls in `BottomSheet` / `ChatInput`.

---

## Phase 8: US6 — Clear context / interrupt / stop (P3)

### Tests

- [x] T068 [P] [US6] `ClaudeSessionServiceTests` — `ClearContext_*`, `Interrupt_*`, `Stop_*`.
- [x] T069 [P] [US6] Web `useClearContext.test.ts`.
- [ ] T070 [US6] Playwright e2e covering clear / interrupt / stop. **DEFERRED → fleece:EYE183**
### Implementation

- [x] T071 [P] [US6] Server: `ClearContextAndStartNew`, `InterruptSession`, `StopSession` on both HTTP and hub.
- [x] T072 [P] [US6] Web `useClearContext` + shortcuts hook (`useSessionShortcuts`) for interrupt.

---

## Phase 9: Cross-cutting (existing behaviours)

### Container lifecycle & recovery

- [x] T073 [P] `Services/ContainerDiscoveryService.cs` — authoritative container status.
- [x] T074 [P] `Services/ContainerRecoveryHostedService.cs` — startup reconciliation.
- [x] T075 [P] `Services/AgentStartupTracker.cs` — startup progress.
- [x] T076 [P] `Services/HooksService.cs` — startup / shutdown hooks.
- [x] T077 [P] `tests/.../ContainerDiscoveryServiceTests.cs`, `AgentStartupTrackerTests.cs`, `DockerAgentExecutionServiceTests.cs`.
- [ ] T078 **Persist and restore mode/model correctly on recovery** — remove hardcoded Build/"sonnet" in `DockerAgentExecutionService.cs` (~L1102, L1165). **DEFERRED → fleece:EYE183**
### Request safety

- [ ] T079 **Return a correlation handle from `POST /api/sessions` instead of fire-and-forget** in `SessionsController.cs`. **DEFERRED → fleece:EYE183**
- [ ] T080 **Add synchronisation to `ClaudeSessionStore`** (concurrent dictionary / lock) — `Services/ClaudeSessionStore.cs`. **DEFERRED → fleece:EYE183**
### Plan artefacts

- [x] T081 [P] `PlanFilePath` / `PlanContent` plumbing on `ClaudeSession` + `PlanApprovalPanel` consumption.
- [ ] T082 **Own plan-file lifecycle** (delete on session stop / container removal; degrade gracefully when file is missing). **DEFERRED → fleece:EYE183**
### Context management

- [ ] T083 **Automatic context management** — summarise or trim long sessions before they exceed the model's context window. **DEFERRED → fleece:EYE183**
---

## Dependencies & Execution Order

- US1 delivers the backbone; US2–US6 depend on it for state machine, hub plumbing, and cache.
- US4 (resume) depends on US1 having written the JSONL cache.
- US5 mode/model switches depend on US2's hub surface.
- Cross-cutting gaps (FI-1 through FI-7) can proceed in parallel once Fleece issues exist and TDD coverage is added.

---

## Parallel Opportunities

- Gap remediations FI-1 (e2e) and FI-2 (worker tests) touch disjoint files — parallel-safe.
- FI-3, FI-5, FI-6 all touch different files in the server slice — parallel-safe.
- FI-4 modifies `SessionsController` and will collide with concurrent US1 work on the same file.

---

## Notes

- Historic `[x]` items are observed on `main` as of 2026-04-14; spot-check with `git log -- src/Homespun.Server/Features/ClaudeCode/` if you need lineage.
- All `[ ]` items correspond 1:1 with an entry in `follow-up-issues.md` tagged `FI-N` — create the Fleece issues before opening remediation PRs.
- Any future change to this slice MUST add failing tests first (Constitution §I), regenerate the OpenAPI client if endpoints change (§III), and clear the full Pre-PR Quality Gate (§IV).
