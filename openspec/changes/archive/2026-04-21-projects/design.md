## Context

Own the project entity (create, list, get, update, delete) plus the project layout shell that hosts every per-project tab. Backing store is `IDataStore` (app-wide data-store abstraction); GitHub discovery goes through `IGitHubService`; git plumbing goes through `ICommandRunner`. The slice is deliberately thin — its job is to give every other slice a stable `Project` reference and a place to mount its route.

## Goals / Non-Goals

**Goals:**
- Full CRUD for projects over a REST API.
- GitHub-backed project creation via clone.
- Local-only project initialization via `git init`.
- React routing shell hosting per-project tabs.
- Shared DTOs in `Homespun.Shared` consumed by generated OpenAPI client.

**Non-Goals:**
- Tab content (Issues, PRs, etc.) — owned by respective feature slices.
- On-disk cleanup on delete — `LocalPath` is intentionally preserved.
- `DefaultModel` validation — handled by the Sessions slice.

## Decisions

### D1: Thin CRUD slice with immutable identity fields

**Decision:** `Name`, `DefaultBranch`, `LocalPath`, `GitHubOwner`, `GitHubRepo` are immutable after creation. Only `DefaultModel` is updatable.

**Rationale:** Changing identity fields would invalidate clones, worktrees, and cached PRs across dependent slices.

### D2: Filesystem preserved on delete

**Decision:** `DeleteAsync` removes only the data-store record; the on-disk clone at `LocalPath` is untouched.

**Rationale:** The clone may contain uncommitted work or be referenced by Git-slice worktrees. Data-loss avoidance is the default.

### D3: Dual create paths (GitHub vs Local)

**Decision:** `POST /api/projects` discriminates on whether `OwnerRepo` or `Name` is supplied, routing to `CreateAsync` or `CreateLocalAsync`.

**Rationale:** GitHub-backed is the primary onboarding path; local unlocks offline/pre-GitHub use. Single endpoint keeps the API surface small.

### D4: Cross-slice PullSyncButton co-located in projects

**Decision:** `<PullSyncButton>` and `use-fleece-sync` hooks live in `features/projects/` despite their Fleece+PR sync behaviour.

**Rationale:** UI adjacency — the button renders in the project toolbar. Documented as a boundary question, not a violation.

## Risks / Trade-offs

- **[Gap GP-1]** No Playwright e2e coverage for project CRUD.
- **[Gap GP-6]** Duplicate request DTOs in controller file (should be in `Homespun.Shared`).
- **[Gap GP-4]** Dead "Delete Project" dropdown item in project layout (no click handler).
- **[Trade-off]** `PullSyncButton` lives in wrong slice by strict vertical-slice reading — accepted for UI adjacency.

## Open Questions

- Should `DeleteAsync` offer optional filesystem cleanup? **Working assumption: no — too risky without explicit user confirmation.**
