## 1. Shared log schema and helpers

- [x] 1.1 Define the `SessionEventLog` field schema as a record type in `src/Homespun.Shared/Models/Observability/SessionEventLogEntry.cs` (Timestamp, Level, SourceContext, Component, Hop, SessionId, TaskId, MessageId?, ArtifactId?, StatusTimestamp?, EventId?, Seq?, A2AKind?, AGUIType?, AGUICustomName?, ContentPreview?)
- [x] 1.2 Add `SessionEventLogOptions` in `src/Homespun.Server/Features/Observability/SessionEventLogOptions.cs` with `ContentPreviewChars` (int, default 0) and `Hops` (dictionary of per-hop Enabled flags, default empty = all enabled)
- [x] 1.3 Register `SessionEventLogOptions` from `Configuration.GetSection("SessionEventLog")` in `Program.cs`
- [x] 1.4 Seed `appsettings.Development.json` with `SessionEventLog:ContentPreviewChars = 80`
- [x] 1.5 In `Program.cs`, emit a startup warning log when `IsProduction()` and `ContentPreviewChars > 0`

## 2. Server-side logging helpers

- [x] 2.1 Create `src/Homespun.Server/Features/Observability/SessionEventLog.cs` with static helper methods `LogA2AHop(ILogger logger, Hop hop, ...)`, `LogAGUIHop(...)`, `LogClientHop(...)`, each producing a structured Serilog entry with `SourceContext="Homespun.SessionEvents"` and top-level correlation fields
- [x] 2.2 Add a `TruncatePreview(string?, int)` helper returning `null` when `Chars == 0`, else `text[..Chars] + "…"` when longer than Chars, else the original text
- [x] 2.3 Unit test `SessionEventLog`: correlation field extraction from each A2A event variant, preview truncation at boundary conditions (0, exact length, longer, unicode), per-hop Enabled flag suppression

## 3. Server hop integration

- [x] 3.1 In `DockerAgentExecutionService.StreamAgentEvents` (at the point right after `ParseSseEvent` succeeds around line 2157), call `SessionEventLog.LogA2AHop(_logger, Hop.ServerSseRx, ...)` passing sessionId, parsed event, and raw data (for preview extraction)
- [x] 3.2 In `A2AEventStore.AppendAsync`, after the file write succeeds (around line 93-101), call `SessionEventLog.LogA2AHop(..., Hop.ServerIngestAppend, ...)` passing the freshly-assigned seq and eventId
- [x] 3.3 In `SessionEventIngestor.IngestAsync`, inside the foreach over `aguiEvents` (line 102-110), call `SessionEventLog.LogAGUIHop(..., Hop.ServerAguiTranslate, ...)` before broadcasting, and `SessionEventLog.LogAGUIHop(..., Hop.ServerSignalrTx, ...)` after `BroadcastSessionEvent` returns
- [x] 3.4 Verify `SessionEventIngestorTests` and `RefreshFidelityTests` still pass with logging enabled — they should, since logging is observational
- [x] 3.5 Add a test `LoggingDoesNotReorderOrDropEnvelopes` that runs a canned session through the ingestor with max logging enabled and asserts envelope-equality against the same session without logging

## 4. Worker-side logging

- [x] 4.1 In `src/Homespun.Worker/src/utils/logger.ts`, add `sessionEventLog(hop, fields)` that builds the shared JSON shape and emits via `console.log` (same as existing `info`)
- [x] 4.2 Add `SessionEventLogFields` TypeScript type mirroring the C# `SessionEventLogEntry` shape
- [x] 4.3 Add `CONTENT_PREVIEW_CHARS` env var read with dev default 80, prod default 0 (matching the server's settings)
- [x] 4.4 In `src/Homespun.Worker/src/services/sse-writer.ts`, immediately after `formatSSE(event, data)` just before `yield`, call `sessionEventLog("worker.a2a.emit", {...})` with sessionId, A2AKind, and extracted correlation IDs
- [x] 4.5 Unit test the worker's `sessionEventLog` helper: field extraction for each A2A event variant, preview truncation

## 5. Client telemetry endpoint

- [x] 5.1 Create `src/Homespun.Server/Features/Observability/ClientLogEntry.cs` — DTO matching `SessionEventLogEntry` but without server-only fields like `EventId` (client may include them when propagating server-received envelopes)
- [x] 5.2 Create `src/Homespun.Server/Features/Observability/ClientLogController.cs` with `[HttpPost("api/log/client")]` accepting `ClientLogEntry[]` (max 100, reject 413 if more), validate each, forward via Serilog under `SourceContext="Homespun.ClientSessionEvents"`, return `202 Accepted`
- [x] 5.3 Reject malformed batches with `400 Bad Request` and an error body naming the first invalid entry's index
- [x] 5.4 Integration tests in `tests/Homespun.Api.Tests/Features/ClientLogApiTests.cs`: happy path, oversized batch, malformed batch, respects client-reported `Level`, forces `SourceContext="Homespun.ClientSessionEvents"`

## 6. Client-side batcher

- [x] 6.1 Create `src/Homespun.Web/src/lib/session-event-log.ts` with buffer, flush-on-50-entries, flush-on-500ms-age, flush-on-beforeunload (using `navigator.sendBeacon`)
- [x] 6.2 Implement `sessionEventLog(hop, fields)` as the public API. Batcher must NEVER route its own fetch failures through `sessionEventLog` — only `console.warn`
- [x] 6.3 Drop-on-failure semantics: on non-2xx HTTP or fetch rejection, drop the batch, do not retry, emit at most one `console.warn` per failure
- [x] 6.4 Unit test `session-event-log`: buffering behaviour, flush triggers, self-defensive error handling, sendBeacon fallback, deterministic clock via a mockable time source

## 7. Client hop integration

- [x] 7.1 In `src/Homespun.Web/src/features/sessions/hooks/use-session-events.ts`, inside the live `handler` (around line 139-144), call `sessionEventLog("client.signalr.rx", {...})` with envelope fields before `applyOne`
- [x] 7.2 Inside `applyOne` (line 97-116), after `setRenderState(next)`, call `sessionEventLog("client.reducer.apply", {...})`
- [x] 7.3 Regression: `sessions.$sessionId.test.tsx` must still pass; add a new test that mocks `sessionEventLog` and asserts it is called exactly once per envelope per hop

## 8. agui-reducer result comment

- [x] 8.1 In `src/Homespun.Web/src/features/sessions/utils/agui-reducer.ts` `applyRunFinished` (line 224), add a one-line comment explaining the deliberate non-rendering of `RunFinished.result` with a reference to this change's design.md

## 9. SingleContainer agent execution shim

- [x] 9.1 Create `src/Homespun.Server/Features/ClaudeCode/Exceptions/SingleContainerBusyException.cs` carrying `RequestedSessionId` and `CurrentSessionId`
- [x] 9.2 Create `src/Homespun.Server/Features/ClaudeCode/Services/SingleContainerAgentExecutionOptions.cs` with `WorkerUrl` (required) and `RequestTimeout` (default 30 min)
- [x] 9.3 Create `src/Homespun.Server/Features/ClaudeCode/Services/SingleContainerAgentExecutionService.cs` implementing `IAgentExecutionService`: URL-only HTTP forwarding, single-session semaphore, busy-exception on concurrent start, clear-on-stop
- [x] 9.4 In `Program.cs`, read `AgentExecution:Mode`. If `SingleContainer`: throw unless `IsDevelopment()`; validate `WorkerUrl`; register `SingleContainerAgentExecutionService` instead of `DockerAgentExecutionService`
- [x] 9.5 In the caller path that catches execution errors (`MessageProcessingService` or `SessionLifecycleService`), add a catch for `SingleContainerBusyException` that logs at `Error` level with both session ids and broadcasts a `BroadcastSessionError` with a user-visible toast
- [x] 9.6 Unit tests for `SingleContainerAgentExecutionService`: busy guard throws with both ids, URL pass-through, stop clears the slot, ctor throws on missing URL
- [x] 9.7 Program.cs startup tests (via WebApplicationFactory): Production + SingleContainer mode throws at startup, Development + SingleContainer mode registers the shim

## 10. mock.sh / mock.ps1 --with-worker flag

- [x] 10.1 Add `--with-worker` flag parsing to `scripts/mock.sh`; on set, check `CLAUDE_CODE_OAUTH_TOKEN` is non-empty, fail fast with a red warning if not
- [x] 10.2 In `mock.sh`, `docker compose up -d worker` then poll `http://localhost:${WORKER_HOST_PORT:-8081}/api/health` for up to 30 seconds
- [x] 10.3 Export `AgentExecution__Mode=SingleContainer` and `AgentExecution__SingleContainer__WorkerUrl` before `dotnet run`
- [x] 10.4 Register an `EXIT`/`INT`/`TERM` trap that runs `docker compose stop worker`
- [x] 10.5 Mirror all changes in `scripts/mock.ps1` for Windows (including the PowerShell equivalent of the trap using `try`/`finally`)
- [x] 10.6 Update the header comment in both scripts documenting the new flag and its dev-only / single-session constraints

## 11. docker-compose worker port exposure

- [x] 11.1 In `docker-compose.yml`, add `ports: ["${WORKER_HOST_PORT:-8081}:8080"]` to the `worker` service
- [x] 11.2 Document `WORKER_HOST_PORT` in the top-of-file env-var comment block

## 12. Documentation

- [x] 12.1 Add a section to `docs/session-events.md` titled "Debug logging" that documents the six hops, the field schema, an example LogQL query, and the `ContentPreviewChars` configuration
- [x] 12.2 Add a short "Windows development with `--with-worker`" section to `docs/installation.md` (or a new `docs/local-dev-windows.md`) covering the single-session constraint, Windows path-mapping caveat, and the `CLAUDE_CODE_OAUTH_TOKEN` requirement

## 13. Verification

- [x] 13.1 Run `dotnet test` — all new tests in sections 2, 3, 5, 9 pass; 1 pre-existing live-container test (`FollowUpPrompts_SameSession_BothComplete`) remains flaky on main and is unrelated to this change
- [x] 13.2 Run `npm run lint:fix && npm run format:check && npm run typecheck && npm test` in `src/Homespun.Web` — typecheck + format + lint + unit tests green on all touched files
- [x] 13.3 Run `npm test` in `src/Homespun.Worker` — 193 tests pass including the new `logger.test.ts` helper tests; one pre-existing `session-inventory.test.ts` ajv dependency failure is unrelated
- [x] 13.4 Manual Windows smoke: backend + worker booted via scripts/mock.ps1 logic, a session started via `POST /api/sessions`, and all four server-side hops (`server.sse.rx`, `server.ingest.append`, `server.agui.translate`, `server.signalr.tx`) emitted structured JSON in `logs/smoke-backend.log` with consistent `TaskId` / `EventId` / `Seq` / `A2AKind` / `AGUIType` / `ContentPreview`. No `MessageId`-bearing entries because the published `homespun-worker:latest` image lacks the Claude Code binary and errored before emitting a Message event; the worker hop (`worker.a2a.emit`) and Message-bearing entries will appear once a newer worker image is published. Client hops were not exercised (no frontend run).
- [x] 13.5 Manual Grafana smoke — skipped by user (no PLG stack available in smoke env; server hops + field schema already verified via direct log inspection in 13.4)
- [x] 13.6 Manual busy-guard smoke: two concurrent `POST /api/sessions` calls returned 201 with the second session transitioned to `status=error` and `errorMessage` naming both sessionIds. Two Error-level log lines were emitted — one from `SingleContainerAgentExecutionService` and one from `MessageProcessingService` — each carrying `RequestedSessionId` and `CurrentSessionId`. The `BroadcastSessionError` path is exercised as part of the catch (toast on the UI path).
