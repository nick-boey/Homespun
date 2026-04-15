## Context

Run Claude Agent SDK sessions inside per-session Docker containers, stream their output over SignalR as AG-UI events, and give users interactive control over the lifecycle (mode/model switching, plan approval, question answering, context clearing, interrupt/stop, resume). A TypeScript worker (`Homespun.Worker`) is the SDK host; the ASP.NET server owns session identity, persistence of the JSONL cache, container reconciliation, and the web client contract.

## Goals / Non-Goals

**Goals:**
- Session CRUD bound to `(projectId, entityId)` with full lifecycle state machine.
- AG-UI event streaming via SignalR to all clients joined to a session's group.
- Plan/Build mode split with approval gating write-capable tools.
- Structured Q&A via `AskUserQuestion` tool call.
- Resume prior sessions using SDK's `--resume` mechanism.
- Mid-session mode and model switching.
- JSONL cache persistence surviving container recreation.
- Container reconciliation on server startup.

**Non-Goals:**
- Agent dispatch/orchestration (owned by `agent-dispatch`).
- Prompt CRUD (owned by `prompts`).
- Issues Agent specialized workflows (co-owned by `issues-agent`).

## Decisions

### D1: Façade + focused services

**Decision:** `ClaudeSessionService` is a thin façade; real work lives in `SessionLifecycleService`, `MessageProcessingService`, `ToolInteractionService`.

**Rationale:** Keeps individual services testable in isolation with dedicated test suites.

### D2: Docker-out-of-Docker execution

**Decision:** `DockerAgentExecutionService` runs worker containers out-of-process.

**Rationale:** Isolation + independent crash domain. Container status is authoritative over in-memory status when they disagree.

### D3: File-based message cache (JSONL)

**Decision:** JSONL on disk rather than SQLite for session message persistence.

**Rationale:** Aligns with Claude Agent SDK's own format (`--resume` is free). No schema churn.

### D4: AG-UI as the on-wire streaming vocabulary

**Decision:** SDK events are translated to AG-UI before fan-out so the web never depends on SDK-internal shapes.

**Rationale:** Decouples web client from SDK version changes.

### D5: Container status as ground truth

**Decision:** `ContainerDiscoveryService` beats `ClaudeSessionStore` when they disagree.

**Rationale:** Containers can crash asynchronously; in-memory state would otherwise drift.

### D6: Plan/Build modes as tool allow-lists

**Decision:** Mode is a concrete allow-list forwarded to the SDK. Plan restricts to `Read`, `Glob`, `Grep`, `WebFetch`, `WebSearch`.

**Rationale:** Safety rail — approval gates the transition from analysis to implementation.

## Risks / Trade-offs

- **[Gap 1]** No Playwright e2e coverage for session flows.
- **[Gap 2]** No Worker tests under `tests/Homespun.Worker/`.
- **[Gap 3]** Hardcoded mode/model on recovery — always restores as Build/"sonnet".
- **[Gap 5]** `ClaudeSessionStore` uses bare Dictionary without locking.
- **[Gap 7]** No automatic context management for long sessions.
- **[Trade-off]** In-memory session store means server restarts lose live handles (JSONL cache preserves history).
