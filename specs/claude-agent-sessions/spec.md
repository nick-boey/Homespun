# Feature Specification: Claude Agent Sessions

**Feature Branch**: n/a (pre-spec-kit; built on `main` over many PRs)
**Created**: 2026-04-14 (migrated)
**Status**: Migrated
**Input**: Reverse-engineered from existing implementation in `src/Homespun.Server/Features/ClaudeCode/`, `src/Homespun.Shared/` (Sessions models + hubs), `src/Homespun.Web/src/features/sessions/`, `src/Homespun.Worker/`, and their tests.

> **Migration note.** This spec was produced by `/speckit-brownfield-migrate` against an already-shipped feature. It documents *what exists*, not a future design. The ancillary slices `features/agents` (dispatch UI), `features/prompts` (prompt CRUD), and `features/issues-agent` (Fleece-aware variant) consume this feature's API surface but are intentionally out of scope here; they are candidates for their own migration passes.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Stream an agent session end-to-end (Priority: P1) 🎯 MVP

As a Homespun user I open a session against a project entity (issue, PR, or free-form workspace), send a prompt, and watch Claude's response stream into the chat in real time with tool calls visible as they happen.

**Why this priority**: Nothing else in this feature has value without the basic run-one-turn-of-conversation loop. The session lifecycle, SDK message streaming, and SignalR fan-out are the spine every other story sits on.

**Independent Test**: Create a session via `POST /api/sessions`, connect to `ClaudeCodeHub`, send a message, and observe AG-UI `RunStarted` → `TextMessageContent*` → `ToolCall*` → `RunFinished` events arriving over SignalR; verify the session reaches `Running` then `WaitingForInput`. Covered end-to-end by `SessionsApiTests` + `ClaudeSessionServiceTests` + web `useSessionMessages` + `MessageList` component tests.

**Acceptance Scenarios**:

1. **Given** a project entity with a git clone available, **When** a user creates a session with an initial message, **Then** a container spins up, the session transitions `Starting → RunningHooks → Running`, and streamed tokens are broadcast to all clients joined to that session.
2. **Given** a running session, **When** Claude invokes a tool (e.g. `Bash`, `Read`, `Grep`), **Then** a `ToolCallStart` event is broadcast before execution and `ToolCallEnd` + tool result blocks arrive after.
3. **Given** a running session that completes its turn, **When** the SDK emits `RunFinished`, **Then** the session transitions to `WaitingForInput` and the final message is persisted to the JSONL cache.

---

### User Story 2 - Plan mode with explicit approval before Build (Priority: P1)

A user runs the session in **Plan** mode (read-only tools: `Read`, `Glob`, `Grep`, `WebFetch`, `WebSearch`). Claude produces a plan and calls the `ExitPlanMode` tool. The user sees the plan, reviews it, and either approves (optionally preserving context) or rejects with feedback. Only after approval can writes happen.

**Why this priority**: Plan-approval is the core safety rail — it is the reason the Plan/Build split exists. Without it, read-only analysis cannot flow safely into implementation. Ships alongside US1 because the hub broadcasts and state machine are shared.

**Independent Test**: Start a Plan-mode session that provokes an `ExitPlanMode` tool call; verify the session enters `WaitingForPlanExecution`, a `PendingPlan` payload is visible via `GET /api/sessions/{id}`, and invoking hub method `ApprovePlan` (with `keepContext=true|false`) resumes the session in Build mode. Covered by `ToolInteractionServiceTests` + web `PlanApprovalPanel.test.tsx` + `useApprovePlan.test.ts`.

**Acceptance Scenarios**:

1. **Given** a Plan-mode session, **When** Claude calls `ExitPlanMode`, **Then** the session status becomes `WaitingForPlanExecution` and the plan content is available to the client.
2. **Given** a session waiting for plan execution, **When** the user approves with `keepContext=true`, **Then** the session resumes in Build mode with conversation history preserved.
3. **Given** a session waiting for plan execution, **When** the user approves with `keepContext=false`, **Then** the session resumes in Build mode with a cleared context.
4. **Given** a session waiting for plan execution, **When** the user rejects with feedback, **Then** the rejection is injected as a user message and the session returns to `Running` in Plan mode.

---

### User Story 3 - Answer structured questions from the agent (Priority: P2)

Claude calls the `AskUserQuestion` tool with one or more structured questions (each with options). The session pauses, surfaces the questions in the UI, collects the user's answers, and resumes execution with the answers injected.

**Why this priority**: Unblocks agents from deadlocking when they need a human decision. Lower than plan approval because it's a narrower interaction surface but still a primary UX.

**Independent Test**: Trigger an `AskUserQuestion` tool call in a running session; confirm the session status becomes `WaitingForQuestionAnswer` and `PendingQuestion` is populated; invoke hub method `AnswerQuestion` with a selected option; verify the session resumes and the answer appears in the conversation. Covered by `ToolInteractionServiceTests` + web `useAnswerQuestion` / question component tests.

**Acceptance Scenarios**:

1. **Given** a running session, **When** Claude calls `AskUserQuestion`, **Then** the session status becomes `WaitingForQuestionAnswer` and the question payload is broadcast.
2. **Given** a session awaiting a question answer, **When** the user submits an answer, **Then** the session resumes and the answer is included in the next SDK turn.

---

### User Story 4 - Resume a prior session (Priority: P2)

A user selects a previously stopped session for the same entity and resumes it. Claude re-opens with conversation context intact via the SDK's `--resume` flag; the JSONL cache is read back so prior messages are visible immediately.

**Why this priority**: Sessions can be long; users expect continuity across restarts, container recreations, and reconnects. Independent of US1–US3 once the cache and discovery layers exist.

**Independent Test**: Stop a session that has a populated JSONL cache; list resumable sessions for the entity via `GET /api/sessions/entity/{entityId}/resumable`; resume via `POST /api/sessions/{id}/resume`; verify the prior messages are visible in `GET /api/sessions/{id}/cached-messages` and that new turns continue the prior thread. Covered by `ClaudeSessionDiscoveryTests` + `SessionCacheController` tests + web `useSessionHistory.test.ts`.

**Acceptance Scenarios**:

1. **Given** a stopped session with a JSONL cache on disk, **When** the user queries resumable sessions for the entity, **Then** that session is listed with its mode, model, and last-activity timestamp.
2. **Given** a resumable session, **When** the user resumes it, **Then** the session status returns to `Running` and prior conversation is available.

---

### User Story 5 - Switch mode or model mid-session (Priority: P2)

Without restarting, a user switches a session between `Plan` and `Build` modes or between Claude models (e.g. Opus ↔ Sonnet ↔ Haiku). The change takes effect on the next turn.

**Why this priority**: Common mid-flow need: start in Plan, approve, continue — or switch models when a turn is context-heavy. Implemented as dedicated hub methods rather than through stop/start.

**Independent Test**: Invoke hub `SetSessionMode(Plan→Build)` on a running session; verify the session's mode is updated and the next turn honours the new tool allow-list. Same for `SetSessionModel`. Covered by `SessionLifecycleServiceTests` + `ClaudeSessionServiceTests` + web `useChangeSessionSettings.test.ts`.

**Acceptance Scenarios**:

1. **Given** a running Plan-mode session, **When** `SetSessionMode(Build)` is invoked, **Then** subsequent tool calls have full write access.
2. **Given** a running session on model A, **When** `SetSessionModel(B)` is invoked, **Then** the next turn is executed against model B and the metadata store is updated.

---

### User Story 6 - Clear context or stop/interrupt a session (Priority: P3)

A user either clears the in-context conversation to reset token usage (messages remain visible in the UI for reference), starts a brand-new session for the same entity with prior context discarded, interrupts a running turn without stopping the session, or stops the session entirely.

**Why this priority**: Operational controls. Essential in practice for long sessions but each is a single verb — low design surface.

**Independent Test**: For each of `ClearContextAndStartNew`, `InterruptSession`, `StopSession`, issue the hub/HTTP call and verify the session transitions accordingly and the container/worker state is reconciled.

**Acceptance Scenarios**:

1. **Given** a long-running session, **When** the user clears context, **Then** subsequent turns start with no conversation history but cached messages remain readable.
2. **Given** a running session mid-turn, **When** the user interrupts, **Then** the current turn is aborted and the session returns to `WaitingForInput` without stopping.
3. **Given** a session in any live state, **When** the user stops it, **Then** the container is torn down and the session transitions to `Stopped`.

---

### Edge Cases

- **Worker / container crashes mid-turn** — `ContainerDiscoveryService` authoritatively reports container state; in-memory session status reconciles to `Stopped` / `Error`. Timing gap exists (see *Identified Gaps*).
- **Container recovery on server restart** — `ContainerRecoveryHostedService` discovers orphan containers and reattaches their sessions at startup.
- **Resume across server restarts** — because session store is in-memory, a full server restart loses live session handles; JSONL cache preserves history so the session is still resumable via the discovery path.
- **Long contexts** — sessions can exceed Claude's context window; there is no automatic summarisation or trimming today (gap).
- **Concurrent hub clients** — multiple browser tabs may join the same session; all receive the same AG-UI event stream.
- **Orphaned plans** — a session's `PlanFilePath` may outlive the container it was written in (gap).
- **Empty entity / missing clone** — `CheckCloneState` rejects session start if the entity has no git clone prepared.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST support creating a session bound to a `(projectId, entityId)` pair with an optional `InitialMessage` that kicks off the first turn asynchronously.
- **FR-002**: System MUST expose two session modes — **Plan** (tools: `Read`, `Glob`, `Grep`, `WebFetch`, `WebSearch` only) and **Build** (all tools including `Write`, `Edit`, `Bash`).
- **FR-003**: System MUST model a finite session lifecycle with states: `Starting`, `RunningHooks`, `Running`, `WaitingForInput`, `WaitingForQuestionAnswer`, `WaitingForPlanExecution`, `Stopped`, `Error`.
- **FR-004**: System MUST stream SDK output to clients as AG-UI events — `RunStarted`, `TextMessageStart/Content/End`, `ToolCallStart/Args/End/Result`, `CustomEvent`, `RunFinished`, `RunError`.
- **FR-005**: System MUST broadcast session lifecycle events via the `ClaudeCodeHub` SignalR hub to all clients that joined the session's group.
- **FR-006**: Users MUST be able to send a message to a running session via either `POST /api/sessions/{id}/messages` or the hub's `SendMessage` method.
- **FR-007**: System MUST pause the session at `WaitingForPlanExecution` whenever the SDK emits an `ExitPlanMode` tool call, exposing `PendingPlan` data (content + optional file path).
- **FR-008**: Users MUST be able to approve a pending plan via `ApprovePlan(keepContext)`, reject it with feedback, or execute without explicit approval via `ExecutePlan`.
- **FR-009**: System MUST pause the session at `WaitingForQuestionAnswer` whenever the SDK emits an `AskUserQuestion` tool call, exposing a `PendingQuestion` with options.
- **FR-010**: Users MUST be able to answer a pending question via `AnswerQuestion`, which resumes execution with the selected option(s).
- **FR-011**: Users MUST be able to change a session's mode (`SetSessionMode`) and model (`SetSessionModel`) mid-session without restarting the container.
- **FR-012**: Users MUST be able to clear a session's in-context conversation (`ClearContextAndStartNew`) while preserving the readable JSONL cache.
- **FR-013**: Users MUST be able to interrupt a running turn (`InterruptSession`) without tearing down the container, and to stop the session entirely (`StopSession`) which tears down the container.
- **FR-014**: System MUST discover resumable sessions for a `(projectId, entityId)` pair from on-disk JSONL files and metadata sidecars.
- **FR-015**: System MUST resume a prior session using the SDK's `--resume` mechanism, re-hydrating conversation context.
- **FR-016**: System MUST persist each session's message stream to a JSONL file (`MessageCacheStore`) and a JSON metadata sidecar (`SessionMetadataStore`) keyed by session id, so history survives container recreation.
- **FR-017**: System MUST discover running containers (`ContainerDiscoveryService`) and treat container status as authoritative over in-memory status when the two disagree.
- **FR-018**: System MUST execute configured startup and shutdown hooks on session containers via `HooksService`.
- **FR-019**: System MUST reconcile orphan containers at server startup via `ContainerRecoveryHostedService` (reattach or clean up).
- **FR-020**: System MUST expose a read-only cache query surface (`SessionCacheController`) for listing sessions by project / entity and fetching cached messages or a summary, independent of whether the session is currently live.
- **FR-021**: System MUST enforce that session operations targeting non-existent or already-terminated sessions return deterministic errors rather than silently no-op.
- **FR-022**: System MUST carry session identity across the server (`ClaudeSession`), the worker (`session-manager.ts`), and the client (AG-UI events keyed by session id) without duplicating the schema — all shared DTOs originate in `src/Homespun.Shared/` (Constitution §III).

### Key Entities

- **ClaudeSession** — Core in-memory session record. Attributes: id, projectId, entityId, working directory, mode, model, status, pending question / plan, timestamps, cost, message count, context-clear markers.
- **SessionMode** — Enum: `Plan`, `Build`.
- **ClaudeSessionStatus** — Enum: `Starting`, `RunningHooks`, `Running`, `WaitingForInput`, `WaitingForQuestionAnswer`, `WaitingForPlanExecution`, `Stopped`, `Error`.
- **SessionType** — Enum: `Standard`, `IssueAgentModification`, `IssueAgentSystem` (the last two are the handles the out-of-scope `issues-agent` slice uses; they exist in this slice because the session runtime is shared).
- **ClaudeMessage / ClaudeMessageContent** — Per-turn messages with content blocks (`Text`, `ToolUse`, `ToolResult`).
- **ResumableSession** — File-discovered session metadata (id, last activity, mode, model, message count).
- **PendingQuestion / UserQuestion / QuestionOption / QuestionAnswer** — Structured Q&A payloads.
- **SessionCacheSummary** — Summary over cached messages (count, cost, duration).
- **SessionBranchInfo** — Git branch linking for the session's working directory.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A newly created session produces its first streamed token within the container's cold-start budget on the host (observed at ~container-start time; not separately instrumented — gap).
- **SC-002**: 100% of SDK stream events emitted by the worker are translated into AG-UI events and fanned out over SignalR (verified by `MessageProcessingServiceTests` and `AGUIEventServiceTests`).
- **SC-003**: Sessions survive server restarts for the purposes of history — JSONL cache + metadata suffice to re-list the session under "Resumable" and re-hydrate it via `--resume` (verified by `ClaudeSessionDiscoveryTests`).
- **SC-004**: A session that hits an `ExitPlanMode` tool call cannot execute write-capable tools until the user approves or explicitly executes the plan (verified by `ToolInteractionServiceTests` + `PlanApprovalPanel.test.tsx`).
- **SC-005**: Mode and model switches take effect on the next turn without container restart (verified by `ClaudeSessionServiceTests.SetSessionMode_*` / `SetSessionModel_*`).
- **SC-006**: The session runtime carries no hand-duplicated cross-process DTOs — every session payload flowing server ↔ web ↔ worker comes from a `Homespun.Shared` type or the generated OpenAPI client (Constitution §III).
- **SC-007**: Session, lifecycle, messaging, tool-interaction, cache, discovery, and hub surfaces each have unit and API integration tests in `tests/Homespun.Tests/Features/ClaudeCode/` and `tests/Homespun.Api.Tests/Features/` (verified — see tasks.md).

## Assumptions

- The server process has access to a Docker socket (or compatible worker-container runtime) capable of spawning the `Homespun.Worker` container image per session.
- A project entity's git clone has already been prepared by the `Git` slice before a session is created against it (`CheckCloneState` gates this).
- `Homespun.Worker` exposes the HTTP + SSE surface that `DockerAgentExecutionService` consumes (`/sessions` routes + SSE for message streams).
- Claude Agent SDK JSONL files live at `/root/.claude_code/sessions/` inside the worker container; metadata lives at `/root/.claude_code/sessions/.meta/`.
- Prompt CRUD (global + project-scoped overrides, handlebars templating) is owned by the out-of-scope `prompts` slice; this slice only consumes resolved prompt text.
- Agent launch UI / dispatch UX is owned by the out-of-scope `agents` slice; this slice only exposes the create/resume APIs it drives.
- Fleece-aware session variants (`IssueAgentModification`, `IssueAgentSystem`) exist as enum values on this runtime but their specialised workflows (`IssueWorkspaceService`, `RebaseAgentService`, accept/cancel-issue-changes endpoints) are co-owned by `issues-agent` and migrated with that slice.

## Affected Slices *(mandatory)*

| Side   | Slice path                                                | New / Existing | Why this slice is touched |
|--------|-----------------------------------------------------------|----------------|---------------------------|
| Server | `src/Homespun.Server/Features/ClaudeCode/`                | Existing       | Session lifecycle, messaging, tool interaction, cache, discovery, hub, controllers |
| Web    | `src/Homespun.Web/src/features/sessions/`                 | Existing       | Chat UI, plan approval, Q&A, mode/model switching, info panel, history |
| Worker | `src/Homespun.Worker/src/{routes,services,tools,types}/`  | Existing       | Claude Agent SDK execution host, A2A ↔ AG-UI translation, SSE streaming |
| Shared | `src/Homespun.Shared/` (Models/Sessions, Requests, Hubs)  | Existing       | Session DTOs, enums, `IClaudeCodeHub(+Client)` contracts, session request DTOs |

## API & Contract Impact *(mandatory)*

- [x] **Server API changes?** — Full surface exists. Endpoints documented in the migration report and enumerated in `tasks.md` under *Controllers*.
- [x] **OpenAPI regeneration required?** — Already regenerated historically; the web client under `src/Homespun.Web/src/api/generated/` carries the session surface. Any future change to this feature MUST run `npm run generate:api:fetch` and commit the diff.
- [x] **Shared DTO / hub interface changes?** — All already in `src/Homespun.Shared/Models/Sessions/`, `Requests/SessionRequests.cs`, `Hubs/IClaudeCodeHub{,Client}.cs`. No hand-duplicated contracts.
- [ ] **Breaking change for existing clients?** — N/A for migration; the feature is shipped and consumed.

## Realtime Impact *(SignalR / AG-UI)*

- [x] **SignalR hub events added or changed?** Hub: `ClaudeCodeHub`. Server → client events: `SessionStarted`, `SessionStopped`, `SessionStatusChanged`, `SessionModeModelChanged`, `SessionContextCleared`, `SessionResultReceived`, `SessionError`, plus AG-UI events (`RunStarted/Finished/Error`, `TextMessageStart/Content/End`, `ToolCallStart/Args/End/Result`, `CustomEvent`, generic `AGUIEvent`). Client → server methods: `JoinSession`, `LeaveSession`, `SendMessage`, `StopSession`, `InterruptSession`, `GetAllSessions`, `GetProjectSessions`, `GetSession`, `StartSessionWithTermination`, `RestartSession`, `CheckCloneState`, `SetSessionMode`, `SetSessionModel`, `ClearContextAndStartNew`, `AnswerQuestion`, `ExecutePlan`, `ApprovePlan`.
- [x] **AG-UI events broadcast?** — Yes; `AGUIEventService` converts SDK stream events into AG-UI payloads and `ClaudeCodeHub` extension methods fan them out to the session's group.
- [x] **Frontend subscriptions?** — `useSessionsSignalR` opens the hub connection; `useSessionMessages` and related hooks consume the stream; Zustand session stores track live status.
- [ ] N/A.

## Persistence Impact *(SQLite + Fleece)*

- [ ] **SQLite schema changes?** — No. This feature is file-based / in-memory only.
- [ ] **Fleece JSONL changes?** — No schema changes here. Fleece integration flows through the out-of-scope `issues-agent` slice.
- [x] **External state (GitHub, Loki, Komodo)?** — Per-session JSONL files at `/root/.claude_code/sessions/*.jsonl` inside the worker container; metadata sidecars under `.meta/`. Container lifecycle tracked via Docker API.
- [ ] N/A.

## Worker Impact *(Claude Agent SDK)*

- [x] **New agent prompt / tool / permission?** — The worker defines custom tools `WorkflowSignal` and `ExitPlanMode` under `src/Homespun.Worker/src/tools/workflow-tools.ts`. Permission set is scoped by the session mode resolved on the server and forwarded to the SDK.
- [x] **New worker route?** — `src/Homespun.Worker/src/routes/sessions.ts` (start / resume / send message / stop), `files.ts` (file discovery), `info.ts` (container info), `health.ts` (liveness).
- [x] **Changes to A2A / AG-UI message flow?** — `a2a-translator.ts` + `sse-writer.ts` translate SDK messages into A2A frames over SSE; the server's `A2AMessageParser` and `AGUIEventService` re-translate to AG-UI for the web.
- [ ] N/A.

## Operational Impact

- [ ] **New env var?** — No new ones introduced by this migration; existing Docker / worker image env config applies.
- [ ] **New container or compose change?** — No; worker image build already present.
- [ ] **Fleece.Core version bump?** — N/A.
- [ ] **Bicep / infra change?** — N/A for the spec; ACA deployment already runs the worker image.
- [x] **Architecture diagram update?** — If LikeC4 models under `docs/architecture/` exist, they should already depict this runtime; verify as part of the migration follow-up pass.
- [ ] N/A.

## Identified Gaps

Recorded here and tracked as draft Fleece issues (see `follow-up-issues.md`):

1. **No Playwright e2e coverage** for session flows (chat, plan approval, Q&A, mode switching) — violates Constitution §IV's expectation that `npm run test:e2e` exercises shipped slices.
2. **No Worker tests** under `tests/Homespun.Worker/` for `session-manager.ts`, `a2a-translator.ts`, `session-discovery.ts`, `workflow-tools.ts`.
3. **Hardcoded mode/model on recovery** — `DockerAgentExecutionService.cs` (~lines 1102, 1165) always restores recovered sessions as Build / "sonnet" instead of the persisted metadata.
4. **Fire-and-forget async work** in `SessionsController.Create` — errors from the async `SendMessageAsync` only surface via SignalR, never the HTTP response.
5. **`ClaudeSessionStore` concurrency** — bare `Dictionary` without locking; multi-threaded access can race.
6. **Orphaned plan artefacts** — `PlanFilePath` on `ClaudeSession` may outlive the container that wrote it; no cleanup path.
7. **No automatic context management** — sessions can exceed Claude's context window; `ClearContext` is manual only.
