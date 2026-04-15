## Why

Claude Agent Sessions is the core runtime that runs Claude Agent SDK sessions inside per-session Docker containers, streams their output over SignalR as AG-UI events, and gives users interactive control over the lifecycle. This is the spine every other AI-driven workflow in Homespun sits on — dispatch, issues agent, and workflow execution all depend on this session runtime.

## What Changes

- Session lifecycle management: create, resume, stop, interrupt, with states `Starting → RunningHooks → Running → WaitingForInput/QuestionAnswer/PlanExecution → Stopped/Error`.
- AG-UI event streaming over SignalR: `RunStarted`, `TextMessageContent`, `ToolCallStart/Args/End/Result`, `RunFinished`.
- Plan mode with explicit approval: read-only tools only, `ExitPlanMode` tool call pauses session, user approves/rejects before writes.
- Structured question answering: `AskUserQuestion` tool call pauses session, user answers, execution resumes.
- Mid-session mode/model switching without container restart.
- Session discovery and resume from on-disk JSONL cache + metadata sidecars.
- Container reconciliation at server startup via `ContainerRecoveryHostedService`.

## Capabilities

### New Capabilities
- `claude-agent-sessions`: Session lifecycle, AG-UI streaming, plan approval, Q&A, mode/model switching, resume, cache persistence, container reconciliation.

### Modified Capabilities
<!-- None — brownfield migration. -->

## Impact

- **Backend**: `Features/ClaudeCode/` — ~57 files / ~12.3K LOC (controllers, hubs, services, data types).
- **Frontend**: `features/sessions/` — ~89 files / ~15.1K LOC (chat UI, plan approval, Q&A, info panel).
- **Worker**: `Homespun.Worker` — ~10 files / ~2-3K LOC (SDK host, A2A translation, SSE streaming).
- **Shared**: ~26 files / ~1.7K LOC (session DTOs, enums, hub contracts).
- **Testing**: 27 backend unit test files (~15K LOC), API tests, ~36 co-located web tests.
- **Status**: Migrated — documents the as-built implementation.
