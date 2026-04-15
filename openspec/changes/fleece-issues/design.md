## Context

Integrate the external Fleece library for local, file-based issue tracking. `Fleece.Core` provides the JSONL storage model, the `Issue` / `TaskGraph` / `DependencyService` types, and cycle detection. Homespun adds an HTTP API (29 endpoints across 3 controllers), a project-aware cache, a git-backed sync layer, a specialised "Issues Agent" session flow, an interactive task-graph UI, and an undo/redo history layer.

## Goals / Non-Goals

**Goals:**
- Wrap `Fleece.Core` in a project-path-first HTTP API surface.
- Per-project in-memory write-through cache backed by disk serialization queue.
- Git-backed sync (commit, push, pull) for `.fleece/` JSONL files.
- Issues Agent: Claude sessions that modify issues, with diff/accept/cancel workflow.
- Interactive task-graph with SVG + Konva renderers, filtering, keyboard navigation.
- Undo/redo with ring-buffered history (100 entries).

**Non-Goals:**
- Re-implementing JSONL schema or dependency graph (owned by `Fleece.Core`).
- Session streaming (owned by `claude-agent-sessions`).
- Agent dispatch pipeline (owned by `agent-dispatch`).

## Decisions

### D1: Project-aware cache wrapping Fleece.Core

**Decision:** `IProjectFleeceService` maintains a per-project in-memory dictionary, written through to disk via `IIssueSerializationQueue`.

**Rationale:** Fleece.Core is file-based; caching avoids re-parsing JSONL on every request. Write-through ensures durability.

### D2: Three-controller API surface

**Decision:** `IssuesController` (CRUD + hierarchy + agent-run + history), `IssuesAgentController` (session lifecycle + diff/accept/cancel), `FleeceIssueSyncController` (sync/pull/branch-status).

**Rationale:** Separation by concern keeps each controller focused. Issues Agent is session-scoped, not issue-scoped.

### D3: SignalR broadcast on every write

**Decision:** Every write endpoint broadcasts `IssuesChanged(projectId, IssueChangeType, issueId?)` on `NotificationHub`.

**Rationale:** Enables real-time task-graph updates across multiple clients without polling.

### D4: Two render modes (SVG + Konva)

**Decision:** `TaskGraphView` (SVG) and `TaskGraphKonvaView` (Konva), selectable via toolbar, persisted in Zustand store.

**Rationale:** SVG is simpler for small graphs; Konva handles 500+ issues at 60fps with virtual nodes.

### D5: Issues Agent operates on session branches

**Decision:** `IssuesAgentController` uses `/api/issues-agent/{sessionId}/*` addressing (session-scoped, not project-scoped).

**Rationale:** Once a session is created, all operations are session-scoped. The project is discoverable from the session.

### D6: Conflict resolution on agent changes

**Decision:** `IFleeceChangeApplicationService.ApplyChangesAsync` with `ConflictResolutionStrategy` (default `Manual`), conflicts surfaced via `IssueConflictDto`.

**Rationale:** Agents may produce changes that conflict with concurrent edits. Users must review and resolve.

## Risks / Trade-offs

- **[Gap GP-1]** Filename/class mismatch: `FleeceService.cs` defines `ProjectFleeceService`.
- **[Gap GP-2]** `FleeceChangeApplicationService.cs` (881 lines) contains 4 public types across 2 service boundaries.
- **[Gap GP-7]** No e2e coverage for Fleece sync or undo/redo.
- **[Risk]** `Fleece.Core` ↔ `Fleece.Cli` version drift can cause production breakage (Constitution §IX enforces sync).

## Open Questions

- Should `MaxHistoryEntries` be configurable? **Working assumption: yes, via `IOptions<FleeceHistoryOptions>` with default 100.**
