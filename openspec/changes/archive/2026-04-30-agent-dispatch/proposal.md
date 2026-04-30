## Why

Agent Dispatch is the dispatch-and-orchestrate layer above the session runtime. It decides whether to start an agent, which branch it should run on, and in what order when multiple issues are queued — then hands off to `IClaudeSessionService` to run the session. Without it, users would have to manually clone branches, resolve stacked-PR base branches, and sequence multi-issue work.

## What Changes

- Single-issue dispatch: `POST /api/issues/{issueId}/run` → `202 Accepted`, with atomic deduplication via `IAgentStartupTracker`, 5-minute background pipeline (base-branch resolve → pull → clone → session-create → initial message).
- Queue orchestration: `POST /queue/start` fans agents across issue trees with series/parallel semantics, topological execution order, cancel support.
- Base-branch resolution: blocking checks (open children, open prior siblings), stacked-PR branch selection (explicit > prior-PR > default), AI branch-id generation via sidecar.
- Active-agents indicator: header badge showing running/waiting/error agent counts, live-updated via SignalR.

## Capabilities

### New Capabilities
- `agent-dispatch`: Single-issue dispatch, queue orchestration, base-branch resolution, branch-id generation, active-agents visibility.

### Modified Capabilities
<!-- None — brownfield migration. -->

## Impact

- **Backend**: `Features/AgentOrchestration/` — 16 files / ~2,570 LOC (controllers, services).
- **Frontend**: `features/agents/` — 22 files / ~2,230 LOC + 7 test files / ~2,660 LOC.
- **Shared**: `OrchestrationRequests.cs`, `RunAgent*` types in `IssueRequests.cs`, 2 hub methods.
- **Testing**: 12 files / ~5,160 LOC (unit + API tests). No e2e coverage (gap).
- **Status**: Migrated — documents the as-built implementation.
