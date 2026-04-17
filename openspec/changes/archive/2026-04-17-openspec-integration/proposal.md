## Why

Homespun today treats each Fleece issue as a single-session unit of work: one issue → one agent dispatch → one PR. OpenSpec provides a richer lifecycle (propose → apply → archive) with artefact-driven state that users want to see and drive from the Homespun issue graph. The server needs to look across branches to surface change progress, because changes live only in the branch that owns them. This change wires Fleece issues to OpenSpec changes via branch-name-based linking, scans branches for change state, and adds an "OpenSpec" tab to the run-agent panel that auto-selects the next stage based on artefact presence.

## What Changes

- **Add** `.homespun.yaml` sidecar file format for OpenSpec changes: lives at `openspec/changes/<name>/.homespun.yaml`, carries `fleeceId: <issue-id>` and `createdBy: server|agent`. This is the source of truth for issue↔change linkage.
- **Add** branch scanner service: for each relevant branch, enumerates `openspec/changes/*`, reads sidecars, matches the branch's embedded fleece-id (from the existing `+abc123` branch-name suffix) to determine the linked change. Falls back to reading `openspec/changes/archive/*` when the live change has been archived.
- **Add** snapshot contract from worker to server: at session end, the worker POSTs a summary (`{branch, fleeceId, changes[], nextIncomplete, phaseState}`) so the server can update its cache without polling.
- **Add** on-demand scan endpoint: when the UI requests graph data, fall back to live disk scan of on-disk clones if cache is cold or stale.
- **Add** virtual sub-issue rendering in the issue graph: parse `tasks.md` on linked changes, roll up to phase-level nodes (`## 1. Design`, `## 2. Implement`). Click the phase badge to show a modal with leaf tasks and their checkbox state.
- **Add** branch + change status indicators on each issue row: branch symbol (gray = none, white = branch exists, amber = branch with change) and change symbol mapped to auto-selection tiers (none / exploring ○ / change created but incomplete (red) ◐ / ready-to-apply (amber) / ready-to-archive (green) ● / archived (blue) ✓).
- **Add** "OpenSpec" tab to the run-agent panel. Lists the 8 OpenSpec skills that are available in the repository's skills (identified by `metadata.author == "openspec"` in frontmatter). Auto-selects the default based on change state: `explore` if artifacts incomplete, `apply` if all artifacts created, `archive` if all tasks complete. Dispatches by reading the selected skill's SKILL.md body + appending the change name as args. Schema override injected into system prompt when non-default.
- **Add** orphan-change handling: changes with no sidecar that were newly introduced on a branch surface in the graph under that branch's issue with [link-to-issue] / [create-sub-issue] actions. Changes in main with no sidecar surface at the bottom of the graph as orphans with [create-issue] action.
- **Add** archive handling: when a linked change moves to `openspec/changes/archive/<dated>-<name>/`, the scanner re-links via the preserved sidecar, transitions the fleece issue to `complete`, and renders the node with an "archived" indicator.
- **Modify** the run-agent panel: keep "Task Agent" and "Issue Agent" tabs as-is; replace the existing "Workflow" tab with an "OpenSpec" tab (this depends on `remove-workflows`).
- **Modify** the Fleece issue node visual: round = no change, square = has change (linked).
- **Multi-change per branch**: permitted. Each change carries its own sidecar; the scanner links each to its own fleece issue. Invariant: one sidecar's `fleeceId` maps to one issue.

## Capabilities

### New Capabilities
- `openspec-integration`: change sidecars, branch scanning, post-session snapshot contract, virtual sub-issues, stage panel, orphan surfacing, archive handling, graph indicators.

### Modified Capabilities
- `fleece-issue-tracking`: no schema changes, but rendering shifts to round/square + branch/change indicators.
- `agent-dispatch`: gains OpenSpec tab; skill-based invocation already introduced by `skills-catalogue`.

## Impact

- **Backend**: new `Features/OpenSpec/` slice with `SidecarService`, `ChangeScannerService`, `ChangeSnapshotController`, DTOs for stage status. Extend `IssueGraphService` to merge change state into the graph response (~1,500 LOC new).
- **Worker**: post-session hook that scans the branch's `openspec/changes/`, composes a snapshot JSON, POSTs to the server (~200 LOC).
- **Frontend**: new "OpenSpec" tab component in run-agent panel. Stage panel with auto-next highlight. Graph node component updates for round/square + two-symbol indicator. Phase-tasks modal. Orphan lane in graph (~2,500 LOC).
- **Scanning dependencies**: Homespun shells out to `openspec status --change X --json` for stage readiness and `openspec schemas`/`openspec schema which` for the active schema name. No YAML parsing on the Homespun side.
- **Tests**: unit tests for scanner (with fixture branches), snapshot-contract integration test, graph-rendering tests for new indicators, end-to-end test covering promotion flow.
- **Dependencies**: depends on `skills-catalogue` (need skill dispatch to drive stages) and `remove-workflows` (need the Workflow tab slot). Sequence: third of three.
