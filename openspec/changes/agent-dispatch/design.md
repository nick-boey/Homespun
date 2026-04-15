## Context

Accept an HTTP dispatch request for an issue (or a tree of issues), atomically claim the issue, resolve its base branch, ensure a clone exists, create a Claude Code session, send the rendered initial message fire-and-forget, and broadcast the outcome over SignalR — all inside a 5-minute background budget. Orchestrate multiple issues through series/parallel queues with topological execution. Generate kebab-case branch ids via a tiny AI sidecar.

## Goals / Non-Goals

**Goals:**
- `POST /api/issues/{id}/run` returns `202` in p95 < 250 ms.
- At-most-one concurrent dispatch per `(projectId, issueId)`.
- Base-branch resolution with blocking checks and stacked-PR support.
- Queue orchestration with series/parallel semantics.
- AI-generated kebab-case branch ids with deterministic fallback.

**Non-Goals:**
- Session streaming loop (owned by `claude-agent-sessions`).
- Issue/graph data or sync (owned by `fleece-issues`).
- Prompt catalogue (owned by `prompts` — this slice only consumes prompts).
- Cross-server coordination (single-server deployment assumed).

## Decisions

### D1: In-memory tracker + queue state

**Decision:** `IAgentStartupTracker` and `IQueueCoordinator` hold state in-memory. No persistence layer.

**Rationale:** Avoids a persistence layer, keeps dispatch fast, fits single-server deployment. A server restart drops queue state (documented assumption).

### D2: 5-minute fixed timeout

**Decision:** Hard-coded 5-minute wall-clock timeout on the entire dispatch pipeline.

**Rationale:** Pragmatic ceiling on clone + session startup. Moving to options is cheap but no pressure yet.

### D3: Base-branch resolution order

**Decision:** (a) explicit `BaseBranch` in request; (b) PR branch of first open prior series sibling with open PR; (c) project default branch.

**Rationale:** Explicit override takes precedence; stacked-PR auto-detection enables clean PR chains; default is the safe fallback.

### D4: AI branch-id with deterministic fallback

**Decision:** Sidecar generates kebab-case ids ≤50 chars. Background path falls back to deterministic slug on sidecar failure. Sync endpoint has no fallback.

**Rationale:** Fast path preserves meaningful names; fallback ensures no deadlock on sidecar outage.

### D5: Workflow callback seam

**Decision:** `QueueCoordinator` registers sessions with `IWorkflowSessionCallback` when `WorkflowMappings` match — a seam to the separate workflows slice.

**Rationale:** Keeps this slice from directly depending on the workflows slice.

## Risks / Trade-offs

- **[Gap GP-1]** No API test covers the 202 happy path of `POST /api/issues/{issueId}/run`.
- **[Gap GP-2]** `AgentStartFailed` has no consistent UI handler.
- **[Gap GP-4]** `MiniPromptService` has no startup health check + no sync fallback.
- **[Gap GP-5]** `QueueCoordinator.RegisterSession` workflow callback unwrapped in try/catch.
- **[Risk]** Server restart mid-dispatch loses background task and queue state.
- **[Trade-off]** Two execution patterns (single dispatch + queue) share infrastructure but have distinct error surfaces.
