## ADDED Requirements

### Requirement: Recovered sessions retain persisted Mode and Model

When the server reconstructs an in-memory session entry — whether on resume of a stopped session, on container-discovery startup recovery, or on a restart of the worker container under a still-living session — the resulting `ClaudeSession.Mode` and `ClaudeSession.Model` SHALL match the values last persisted by `SessionMetadataStore`, falling back to the live worker's `/sessions/active` response when present and only then to project defaults. The values SHALL NOT be hardcoded to `Build` / `sonnet` regardless of input.

`AgentSessionStatus` returned by `IDockerAgentExecutionService.GetSessionStatusAsync` and `ListSessionsAsync` SHALL carry the persisted `Mode` and `Model` for every session in the in-memory store.

#### Scenario: Plan-mode session survives a server restart

- **GIVEN** a session was created with `Mode = Plan`, `Model = "opus"` and the worker container is still running
- **WHEN** the server restarts and `ContainerRecoveryHostedService` rediscovers the container
- **THEN** the recovered `ClaudeSession.Mode` SHALL equal `SessionMode.Plan`
- **AND** the recovered `ClaudeSession.Model` SHALL equal `"opus"`

#### Scenario: ListSessionsAsync returns persisted mode and model

- **GIVEN** an in-memory session created with `Mode = Plan`, `Model = "haiku"`
- **WHEN** a caller invokes `IDockerAgentExecutionService.ListSessionsAsync`
- **THEN** the corresponding `AgentSessionStatus.Mode` SHALL equal `SessionMode.Plan`
- **AND** `AgentSessionStatus.Model` SHALL equal `"haiku"`

#### Scenario: GetSessionStatusAsync returns persisted mode and model

- **GIVEN** an in-memory session created with `Mode = Build`, `Model = "opus"`
- **WHEN** a caller invokes `IDockerAgentExecutionService.GetSessionStatusAsync(sessionId)`
- **THEN** the returned `AgentSessionStatus.Mode` SHALL equal `SessionMode.Build`
- **AND** the returned `AgentSessionStatus.Model` SHALL equal `"opus"`

### Requirement: POST /api/sessions surfaces initial-message dispatch failures

`POST /api/sessions` SHALL NOT silently drop failures from the initial-message dispatch. The controller SHALL await the dispatch with a bounded timeout (default 30 seconds, configurable via `SessionEvents:DispatchTimeoutSeconds`) and:

- on success, return `201 Created` with the session DTO,
- on timeout, return `202 Accepted` with the session DTO and let the SignalR stream surface the eventual result,
- on dispatch exception, return an HTTP error response that includes the session id so the caller can act on the failure even without an active hub connection.

The fire-and-forget `Task.Run(...)` pattern SHALL NOT be used for initial-message dispatch.

#### Scenario: Successful dispatch returns 201

- **WHEN** a client posts a valid `CreateSessionRequest` with an `InitialMessage`
- **AND** the worker accepts the message within the dispatch timeout
- **THEN** the response status SHALL be `201 Created`
- **AND** the response body SHALL contain the new session DTO

#### Scenario: Dispatch failure returns an error response containing the session id

- **GIVEN** the worker rejects the initial message (e.g. with a 500)
- **WHEN** a client posts a valid `CreateSessionRequest`
- **THEN** the response SHALL be a 4xx or 5xx HTTP response (NOT 201/202)
- **AND** the response body SHALL include the session id of the partially-created session

#### Scenario: Dispatch timeout returns 202

- **GIVEN** the worker is unreachable for longer than the configured dispatch timeout
- **WHEN** a client posts a valid `CreateSessionRequest`
- **THEN** the response status SHALL be `202 Accepted`
- **AND** the response body SHALL contain the session DTO so the client can subscribe to its event stream

### Requirement: ClaudeSessionStore atomic update under contention

`IClaudeSessionStore.Update` SHALL be atomic with respect to concurrent `Remove` and concurrent `Update` calls for the same session id. A removed session SHALL NOT be re-inserted by a concurrent `Update`. The store SHALL NOT throw `InvalidOperationException` under concurrent access from hub methods, controllers, and background services.

#### Scenario: Concurrent Update and Remove do not re-insert

- **GIVEN** session `S` is present in the store
- **WHEN** thread A invokes `Remove(S.Id)` concurrently with thread B invoking `Update(S')` for the same id
- **THEN** the final state of the store SHALL reflect either Remove-then-Update (S' present) or Update-then-Remove (absent), but NEVER an "S removed but S' resurrected by a non-atomic Update" outcome
- **AND** neither operation SHALL throw

#### Scenario: Stress test under contention

- **GIVEN** N=100 concurrent threads each performing 1000 random `Add`/`Update`/`Remove`/`Get` operations on a shared store
- **WHEN** the test runs to completion
- **THEN** no `InvalidOperationException` SHALL be thrown
- **AND** every successful `Update` SHALL be observable by a subsequent `GetById` (no dropped writes)

### Requirement: Plan-file artefacts are owned by their owning session

`ClaudeSession.PlanFilePath` artefacts SHALL be deleted when the owning session is stopped, when the owning worker container is removed, or when the session is cleared via `ClearContextAndStartNew`. Plan files orphaned by a server crash SHALL be reclaimable by `ContainerRecoveryHostedService` startup reconciliation.

The web client SHALL handle a missing plan file (HTTP 404 from the read endpoint or `null` content) without throwing or rendering a broken state. `PlanApprovalPanel` SHALL show a "plan file no longer available" affordance when the file cannot be read.

#### Scenario: Plan file is deleted on session stop

- **GIVEN** a session with a non-null `PlanFilePath` pointing to a file that exists on disk
- **WHEN** the session is stopped via `IClaudeSessionService.StopSessionAsync`
- **THEN** the file at `PlanFilePath` SHALL no longer exist
- **AND** subsequent reads of that path SHALL return `null` / 404

#### Scenario: Plan file is deleted on container removal

- **GIVEN** a session whose worker container is removed by `DockerAgentExecutionService.RestartContainerAsync` or by container-recovery cleanup
- **WHEN** the container is removed
- **THEN** the file at `PlanFilePath` for that session SHALL no longer exist

#### Scenario: PlanApprovalPanel renders gracefully when the plan file is missing

- **GIVEN** a session whose `PlanFilePath` points to a file that has been deleted
- **WHEN** `PlanApprovalPanel` mounts
- **THEN** it SHALL render a "plan file no longer available" affordance
- **AND** it SHALL NOT throw or render a broken UI

### Requirement: Automatic context management for long-running sessions

The server SHALL automatically mitigate context-window exhaustion for long-running sessions according to per-project `Project.ContextManagement` configuration. When `Mode = Auto` (default), the server SHALL summarise the conversation when `cumulative_input_tokens / context_window_for_model` crosses `SummariseThreshold` (default 0.75); on summarise failure or if the post-summary ratio still exceeds 0.9, the server SHALL fall back to trimming the oldest non-system messages until the ratio drops below `TrimFloor` (default 0.5). When `Mode = Off`, no automatic management SHALL occur.

The server SHALL broadcast a `SessionContextManaging` AG-UI custom event before the operation begins and a `SessionContextManaged` AG-UI custom event when the operation completes. Both events SHALL be persisted to the A2A event log so that live and replay produce equal envelopes (per the existing live==replay invariant on `claude-agent-sessions`).

The web client SHALL render a user-visible signal (banner or toast) for both events. The trim path SHALL preserve all system messages and the first user turn.

#### Scenario: Session crosses the summarise threshold and the agent summarises

- **GIVEN** a project with `ContextManagement.Mode = Auto`, `SummariseThreshold = 0.75`
- **AND** an active session whose `cumulative_input_tokens / context_window` is 0.78 after the latest `RunFinished`
- **WHEN** the next user message would push the ratio above the threshold
- **THEN** the server SHALL broadcast a `SessionContextManaging { strategy: "summarise", reason: "threshold-exceeded" }` envelope
- **AND** start a context-summary turn
- **AND** broadcast a `SessionContextManaged { strategy: "summarise", prevRatio, newRatio, droppedTurnCount }` envelope on completion

#### Scenario: Summarise failure falls back to trim

- **GIVEN** a session that has triggered summarise but the summarise turn errored
- **WHEN** the post-summary ratio still exceeds 0.9
- **THEN** the server SHALL broadcast `SessionContextManaging { strategy: "trim", reason: "summarise-failed" }`
- **AND** drop the oldest non-system, non-first-user messages until the ratio drops below `TrimFloor`
- **AND** broadcast `SessionContextManaged { strategy: "trim", ... }` on completion

#### Scenario: Mode = Off disables automatic management

- **GIVEN** a project with `ContextManagement.Mode = Off`
- **WHEN** an active session's ratio crosses any threshold
- **THEN** the server SHALL NOT broadcast a `SessionContextManaging` envelope
- **AND** the conversation SHALL continue without automatic mitigation

#### Scenario: Live and replay produce equal context-management envelopes

- **WHEN** a session that triggered context management is later replayed via `GET /api/sessions/{id}/events?mode=full`
- **THEN** the replayed envelopes SHALL include the original `SessionContextManaging` and `SessionContextManaged` events with identical `seq`, `eventId`, and payload as the live broadcast

### Requirement: Worker session module test coverage

Tests under `tests/Homespun.Worker/services/` SHALL cover at least 80% of changed/added lines in `session-manager.ts`, `session-discovery.ts`, `a2a-translator.ts`, and `sse-writer.ts` for any PR that modifies those files. The module-wide line coverage trend for the worker SHALL be on track for the Constitution V 60%/2026-06-30 target.

#### Scenario: Coverage gate enforces ≥80% on changed lines

- **GIVEN** a PR that modifies `src/Homespun.Worker/src/services/session-manager.ts`
- **WHEN** `npm run test:coverage` runs in `tests/Homespun.Worker/`
- **THEN** the coverage report for changed lines SHALL be ≥ 80%
- **AND** the worker module's overall coverage SHALL not regress relative to `main`

### Requirement: Playwright e2e coverage for shipped Claude agent session user stories

`src/Homespun.Web/e2e/sessions/` SHALL contain Playwright specs covering the six shipped user stories: stream a message (US1), approve/reject a plan (US2), answer a structured question (US3), resume a session (US4), switch mode and model (US5), clear / interrupt / stop (US6). Each spec SHALL run against the existing mock-mode AppHost via the `webServer` config in `playwright.config.ts` and SHALL pass in CI.

#### Scenario: All six specs pass against the mock server

- **GIVEN** the mock-mode AppHost auto-started by `playwright.config.ts`'s `webServer`
- **WHEN** `npm run test:e2e -- e2e/sessions/` runs
- **THEN** every spec under `e2e/sessions/` SHALL pass
- **AND** the run SHALL exit 0
