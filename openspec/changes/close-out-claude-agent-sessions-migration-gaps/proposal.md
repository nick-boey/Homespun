## Why

Seven gaps remain from the brownfield migration of the Claude Agent Sessions feature (tracked under verify issue **OMfXwp**). Each is small in isolation but together they let real defects ship: recovered sessions silently lose their Plan/Build mode, the `POST /api/sessions` initial dispatch swallows errors, plan artefacts leak on the disk, long sessions blow past the model context window, and the test pyramid is missing both Playwright e2e coverage of the shipped slice and Vitest coverage of the worker. We are bundling them into a single change so the close-out is coherent, sequenced, and reviewable, and so the verify issue can be retired with one paper trail rather than seven.

## What Changes

- **FI-3 (oW5gur)**: Persist and restore `Mode` and `Model` on `DockerSession` so `GetSessionStatusAsync` and `ListSessionsAsync` no longer return hardcoded `Build`/`sonnet`. Wire `SessionMetadataStore` into Docker-mode recovery paths so a Plan-mode session stays Plan-mode across server restarts.
- **FI-4 (CcVxJ3)**: Replace the fire-and-forget `Task.Run` in `SessionsController.Create` with a path that surfaces dispatch failures to the caller — either an awaited bounded-timeout call or a correlation handle backed by a hub broadcast.
- **FI-5 (pa2peh)**: Make `ClaudeSessionStore.Update` atomic (currently a non-atomic `ContainsKey` + indexer assign that can race with `Remove`). Add a multi-threaded stress test asserting no dropped updates and no `InvalidOperationException` under contention.
- **FI-6 (c7FbVC)**: Own the lifecycle of `PlanFilePath` artefacts. Delete the file when the owning session is stopped or its container is removed; degrade gracefully in `PlanApprovalPanel` / `usePlanFiles` when the file is missing; cover both paths with tests.
- **FI-1 (P2ZkoA)**: Add Playwright e2e coverage for the six user stories (US1–US6) under `src/Homespun.Web/e2e/sessions/`: stream, plan approval, question answer, resume, mode/model switch, clear/interrupt/stop. Tests run in CI via the existing `webServer` config.
- **FI-2 (PDEv8G)**: Bring `tests/Homespun.Worker/` coverage to ≥80% on changed/added lines and put the module on track for the Constitution V 60%/2026-06-30 target. Audit `session-manager`, `session-discovery`, `a2a-translator`, and `sse-writer` test depth and fill the gaps; remove the stale `workflow-tools.ts` reference (file no longer exists).
- **FI-7 (1U3jzM)**: Introduce automatic context management for long-running sessions — threshold-based summarisation or trimming triggered before the model's context window fills, with a user-visible signal when it happens and per-project configuration. Includes a design note covering the summarise-vs-trim decision and a switch to enable/disable per project.

## Capabilities

### New Capabilities

(none — every gap modifies existing behaviour)

### Modified Capabilities

- `claude-agent-sessions`: Adds requirements covering (a) recovered sessions retaining their persisted `Mode` and `Model`, (b) `POST /api/sessions` dispatch surfacing failures to the caller, (c) `ClaudeSessionStore` atomic updates under contention, (d) plan-file lifecycle ownership, and (e) automatic context management with user-visible signalling and per-project configuration.

## Impact

**Server (`src/Homespun.Server/Features/ClaudeCode/`)**
- `Services/DockerAgentExecutionService.cs` — extend `DockerSession` record with `Mode`/`Model`; replace hardcoded values in `GetSessionStatusAsync` (~L1236) and `ListSessionsAsync` (~L1299); thread persisted mode/model through container-recovery paths.
- `Services/ClaudeSessionStore.cs` — make `Update` atomic; add concurrency tests.
- `Controllers/SessionsController.cs` — surface dispatch failures from POST /api/sessions; add API integration tests for success and failure paths.
- `Services/ToolInteractionService.cs` + new `Services/PlanArtefactService.cs` (or equivalent) — own plan-file deletion on session stop / container removal.
- New context-management service + per-project setting on the `Project` entity for FI-7.

**Web (`src/Homespun.Web/`)**
- `features/sessions/components/PlanApprovalPanel.tsx` + `hooks/usePlanFiles.ts` — handle missing plan-file errors.
- `features/sessions/components/...` — UI affordance for context-management trigger (banner / toast).
- `e2e/sessions/` — six new specs.

**Worker (`src/Homespun.Worker/`)**
- `tests/Homespun.Worker/services/*.test.ts` — coverage gap fill.
- Possibly a thin worker hook to expose token-usage telemetry the server's context-manager can read (TBD in design).

**Shared / DTOs**
- `Homespun.Shared` may gain a per-project `ContextManagement` settings DTO and a hub event for "context-trim/summarise occurred".

**Tests**
- `tests/Homespun.Tests/Features/ClaudeCode/` — new unit tests for FI-3, FI-5, FI-6, FI-7.
- `tests/Homespun.Api.Tests/Features/SessionsApiTests.cs` — extended for FI-4.
- `src/Homespun.Web/e2e/sessions/` — six new Playwright specs.
- `tests/Homespun.Worker/` — coverage delta.

**Constitution gates** Each phase must clear `dotnet test`, `npm run lint:fix`, `npm run format:check`, `npm run typecheck`, `npm test`, `npm run test:e2e`, `npm run build-storybook`, and (for OpenAPI changes) `npm run generate:api:fetch`.

**Out of scope**
- Multi-user or ACA-mode execution (tracked separately under `xJ1xoN`).
- Worker `single-container` mode parity for ACA.
- Token-budget enforcement on per-tier model — only the trim/summarise mitigation is in scope.
