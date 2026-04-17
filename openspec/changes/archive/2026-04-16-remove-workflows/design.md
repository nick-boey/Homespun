## Context

The workflow slice under `src/Homespun.Server/Features/Workflows/` provides an orchestration runtime for multi-step agent dispatches, with typed executors for Agent, ServerAction, CiMerge, and Gate steps. Execution state is persisted to `.fleece/workflows/*.jsonl` with a write-through cache. Template definitions live in `DefaultWorkflowTemplates.cs` and register into `WorkflowTemplateService`. Agent steps integrate with the session system via `WaitingForCallback()` semantics so a workflow can pause until a session ends.

In practice the slice is unused. The value it was intended to deliver — chaining agent work with manual review gates — is now adequately provided by OpenSpec's artefact-driven lifecycle (`propose` produces proposal/specs/design/tasks; user reviews; `apply` executes tasks with a gate before archive).

## Goals / Non-Goals

**Goals:**
- Delete the workflow slice in its entirety. Zero residual abstractions, zero "workflow" vocabulary remaining in the codebase.
- Delete associated UI (workflow tab, template management).

**Non-Goals:**
- Replace with anything. The `openspec-integration` change separately introduces an OpenSpec Stages panel in the run-agent UI that covers the useful ground.
- On-disk migration. Feature is confirmed unused — no existing `.fleece/workflows/` data to migrate.
- Backwards-compatibility shims. HTTP endpoints deleted cleanly; external callers are not expected.

## Decisions

### D1: Delete, don't deprecate

**Decision:** Remove the slice directly in one change. Do not ship a deprecation window.

**Rationale:** Feature is unused (user confirmed). Deprecation overhead (warnings, parallel paths) exceeds the value.

### D2: Action-queue repurposing is out of scope

**Decision:** The existing task-queueing surface (used for "dispatch agent on issue") remains. Repurposing it to support phase-dispatch and pre-flight readiness checks is tracked separately in Fleece issue `wA0N2U`.

**Rationale:** Keep this change focused on deletion. The action-queue work depends on `openspec-integration` having landed.

### D3: No migration of `.fleece/workflows/` directory

**Decision:** Leave any dormant `.fleece/workflows/` files on disk (none expected). Do not write a cleanup script.

**Rationale:** User confirmed the feature is not in use. If stray files exist in a repo, they are harmless once the server-side readers are removed.

## Risks / Trade-offs

- **[R1]** If any out-of-tree consumer (script, bookmark) hits `/api/workflows/*`, it will 404. Acceptable — no expected consumers.
- **[R2]** The `openspec-integration` change introduces the new "stages" concept in the UI. Brief window where users who remember the Workflow tab may look for it before finding the OpenSpec tab. Copy in release notes.
