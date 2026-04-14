# Follow-up Issues — agent-dispatch

These are stubs for the gaps identified during the brownfield migration (see
`spec.md` → *Identified Gaps*). They are committed to Fleece under a parent
`verify` issue so the group can be closed out once every child is `complete`.

The canonical record is whatever `fleece show <id>` returns; this file is a
human-readable snapshot at migration time.

## Parent

| FI | Fleece ID | Type | Title |
|---|---|---|---|
| FI-0 | `smW5ZB` | verify | Close out Agent Dispatch migration gaps |

## Children

| FI | Fleece ID | Type | Priority | Title | Why it matters | Acceptance |
|---|---|---|---|---|---|---|
| FI-1 | `Hlsa5H` | task | 2 | Add Playwright e2e coverage for the Run Agent dispatch flow | Constitution §IV expects `npm run test:e2e` to exercise shipped slices. Today there is no e2e spec that exercises the full "click Run Agent → 202 Accepted → `AgentStarting` broadcast → session visible in `/sessions`" loop. Covers gap GP-1 as a side effect. | New spec under `src/Homespun.Web/e2e/agents/` covering: single-issue dispatch (US1), double-click → 409 guard, blocked-base-branch failure path, `AgentStartFailed` surfacing. API-level test under `tests/Homespun.Api.Tests/Features/AgentOrchestration/` asserts the 202 Accepted happy path of `POST /api/issues/{id}/run`. Both pass in CI. |
| FI-2 | `k48WkE` | bug | 2 | Surface `AgentStartFailed` to users who have navigated away from the issue | `AgentStartFailed` is broadcast over SignalR but there is no consistent UI handler; a user who navigates away before the background dispatch finishes sees no surfaced error, yet the issue is left with a "started" tracker marker cleared and no session. GP-2. | Global SignalR handler in `features/agents/` (or a shared toast consumer) catches `AgentStartFailed` and renders a notification via the existing `features/notifications/` slice regardless of current route. Regression test simulates a failed dispatch and asserts the toast renders. |
| FI-3 | `SYGjIj` | bug | 3 | `BaseBranchSelector` needs an explicit error fallback when `useBranches` rejects | The selector renders "Loading branches…" forever if the branches query rejects; there is no inline error state or retry. GP-3. | `BaseBranchSelector` renders an inline error state with a retry action when `useBranches` is in error; component test covers the error path. |
| FI-4 | `sLeidS` | bug | 3 | `MiniPromptService` needs a startup health check and a sync-endpoint fallback | The sidecar is required for `POST /api/orchestration/generate-branch-id`; if the sidecar is absent at boot the first call returns a `500` with `InvalidOperationException`, and the background path is the only one with a deterministic fallback. GP-4. | (a) An `IHostedService` probes `/api/mini-prompt` on startup and logs a warning if unreachable; (b) `OrchestrationController.GenerateBranchId` falls back to the deterministic slug path used by `BranchIdBackgroundService` when the sidecar is unavailable, returning the fallback id with a `Warning` header; (c) `MiniPromptServiceTests` covers both scenarios. |
| FI-5 | `QJTTub` | bug | 3 | Wrap `IWorkflowSessionCallback.RegisterSession` with try/catch inside `QueueCoordinator` | A throw in the workflow callback crashes the entire dispatch. The workflow callback is owned by a separate slice; dispatch must not be at its mercy. GP-5. | `QueueCoordinator` catches exceptions from `RegisterSession`, logs them with structured context `(queueId, issueId, workflowId)`, and continues dispatch; failing the workflow side without failing the agent. Regression test: fake callback throws, dispatch succeeds, session is created, warning is logged. |
| FI-6 | `YJNeiw` | task | 4 | Paginate `GET /api/projects/{projectId}/queue/status` | `QueueStatusResponse.Queues[]` is unbounded — a long-running project with many historic queues returns everything on every poll. GP-6. | Endpoint accepts `?limit=&offset=` (with sane defaults: limit 50, offset 0) and returns `QueueStatusResponse` + a `paging` envelope; `QueueApiTests` asserts defaults + bounds; web `use-queue-status` query is updated to page. |

## Status

Created via `/speckit-brownfield-migrate` follow-up. Inspect with:

```bash
fleece show smW5ZB --json
fleece list --tree | rg -A 8 smW5ZB
```
