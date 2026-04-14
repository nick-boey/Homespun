# Follow-up Issues — claude-agent-sessions

These are draft stubs for the gaps identified during the brownfield migration
(see `spec.md` → *Identified Gaps*). They are created in Fleece with
`status: open` under a parent `verify` issue so the group can be closed out
once every child is `complete`.

The canonical record is whatever `fleece show <id>` returns; this file is a
human-readable snapshot at migration time.

## Parent

| FI | Fleece ID | Type | Title |
|---|---|---|---|
| FI-0 | `OMfXwp` | verify | Close out Claude Agent Sessions migration gaps |

## Children

| FI | Fleece ID | Type | Priority | Title | Why it matters | Acceptance |
|---|---|---|---|---|---|---|
| FI-1 | `P2ZkoA` | task | 2 | Add Playwright e2e coverage for Claude agent session flows | Constitution §IV expects `npm run test:e2e` to exercise shipped slices; today there are zero e2e specs under `src/Homespun.Web/e2e/` for sessions, which means regressions to the chat, plan-approval, Q&A, resume, mode/model, and interrupt/stop flows can ship silently. | New specs under `src/Homespun.Web/e2e/sessions/` covering: stream a message (US1), approve/reject a plan (US2), answer a structured question (US3), resume a session (US4), switch mode + model (US5), clear/interrupt/stop (US6). All pass in CI. |
| FI-2 | `PDEv8G` | task | 2 | Add Vitest coverage for `Homespun.Worker` session modules | `tests/Homespun.Worker/` is empty; this pulls the worker's module coverage to ~0% and is a blocker for Constitution §V's 60%/2026-06-30 and 80%/2026-09-30 dated targets. | Unit tests for `session-manager.ts`, `session-discovery.ts`, `a2a-translator.ts`, `sse-writer.ts`, `tools/workflow-tools.ts`. Delta ≥ 80% on added/changed lines; worker module overall coverage on-track for 60% by 2026-06-30. |
| FI-3 | `oW5gur` | bug | 2 | Restore persisted mode/model on session recovery (no more hardcoded Build/"sonnet") | `DockerAgentExecutionService.cs` around lines 1102 and 1165 resets recovered sessions to Build / "sonnet" regardless of what was persisted. A user who was working in Plan mode gets silently promoted to Build across a server restart, which breaks the whole Plan/Build safety rail (US2). | `SessionMetadataStore` is consulted on recovery; the recovered `ClaudeSession.Mode` and `.Model` match the last persisted values. Regression test: write metadata, restart, assert recovered session carries the same mode/model. |
| FI-4 | `CcVxJ3` | bug | 3 | Surface failures from `POST /api/sessions` initial-message dispatch | The controller calls `Task.Run(async () => sessionService.SendMessageAsync(...))` and drops the task. Errors log server-side but never reach the HTTP caller; clients that don't have a hub connection up yet miss the failure entirely. | Either (a) await the dispatch inline with a reasonable timeout and return the error synchronously, or (b) return a correlation id the client can poll / subscribe to, and ensure failures are broadcast via `BroadcastSessionError` before the handle is dropped. Integration test covers both success and failure paths. |
| FI-5 | `pa2peh` | bug | 3 | Add concurrency control to `ClaudeSessionStore` | `ClaudeSessionStore` is a bare `Dictionary<Guid, ClaudeSession>` without locking. Hub methods, HTTP controllers, and background services all mutate it concurrently; races are latent but real. | Switch to `ConcurrentDictionary` or introduce explicit locking around read-modify-write paths. Add a multi-threaded stress test that asserts no dropped updates / no `InvalidOperationException` under contention. |
| FI-6 | `c7FbVC` | bug | 4 | Own the lifecycle of `PlanFilePath` artefacts | Plans referenced by `ClaudeSession.PlanFilePath` can outlive the worker container that wrote them; there is no deletion on stop and the UI degrades poorly when the file is missing. | Plans are deleted when their session is stopped (or when the owning container is torn down); `PlanApprovalPanel` and `usePlanFiles` handle missing-file errors gracefully; unit + component tests cover the missing-file path. |
| FI-7 | `1U3jzM` | feature | 4 | Automatic context management for long-running sessions | Sessions can grow past the model's context window with no mitigation; the only current escape is manual `ClearContext`. Long-running agent work (the common case) breaks. | Threshold-based summarisation or trimming triggered before the context window fills; user-visible indication when summarisation happens; behaviour configurable per project. Design doc + tests covering summarise/trim decisions. |

## Commands to run next

Each child is linked to the parent via `--parent-issues OMfXwp:<sortOrder>`. To start work on any of them:

```bash
fleece show <id> --json          # inspect
fleece edit <id> -s progress     # begin work
# …after PR opens…
fleece edit <id> -s review --linked-pr <pr-number>
# …after merge…
fleece edit <id> -s complete
```

When **every** child (FI-1 through FI-7) is `complete`, move the parent `OMfXwp` to `complete` to close out the migration cleanup.
