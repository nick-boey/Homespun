## Why

Fleece Issues is the integration with the external Fleece library for local, file-based issue tracking. `Fleece.Core` provides the JSONL storage + in-process model; Homespun wraps it in an HTTP API, a project-aware cache, a sync layer over git, an "Issues Agent" specialised session flow, and a React UI centered on an interactive task-graph. The slice is large (6 kLOC server, ~23 kLOC web incl. tests) but has a clear surface: everything flows through three controllers — `IssuesController`, `IssuesAgentController`, `FleeceIssueSyncController`.

## What Changes

- Issue CRUD surface over `/api` with filtering, hierarchy, and agent-run endpoints.
- Interactive task-graph UI with SVG and Konva renderers, rich filter query language, inline editing, and keyboard navigation.
- Issues Agent session flow: Claude sessions that modify `.fleece/` files, reviewed via diff UI before applying to main.
- Git-backed sync layer: commit-and-push `.fleece/` paths, pull, discard-non-fleece-and-pull, branch status.
- Undo/redo history layer: ring-buffered snapshots (100 entries) applied through a serialization queue.
- SignalR `IssuesChanged` broadcasts for real-time cache invalidation.

## Capabilities

### New Capabilities
- `fleece-issue-tracking`: Issue CRUD, hierarchy management, task graph visualization, agent integration, git sync, and undo/redo history.

### Modified Capabilities
<!-- None — brownfield migration. -->

## Impact

- **Backend**: `Features/Fleece/` — 3 controllers, 11+ services (~6,088 LOC). Shared contracts: 8 files (~800 LOC).
- **Frontend**: `features/issues/` (~23,000 LOC incl. tests), `features/issues-agent/` (~400 LOC), 4 route files.
- **Testing**: 22 backend test files (~10,757 LOC), co-located Vitest tests, 3 Playwright e2e specs.
- **Dependencies**: `Fleece.Core` 2.1.1 NuGet + `Fleece.Cli` 2.1.1 in Dockerfile.base.
- **Status**: Migrated — documents the as-built implementation.
