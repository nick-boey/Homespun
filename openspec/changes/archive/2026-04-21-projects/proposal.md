## Why

The project entity is the foundational building block of Homespun — every other workflow (issues, PRs, workflows, agent sessions, clones) is scoped to a project. This change documents the existing project lifecycle management surface: create (from GitHub or local), list, get, update (default model), and delete, plus the project layout shell that hosts per-project tabs.

## What Changes

- Project CRUD over `/api/projects` — list (sorted by `UpdatedAt` desc), get by id, create from GitHub repo or local name, update default model, delete (data-store only, filesystem untouched).
- GitHub-backed creation: parse `owner/repo`, query default branch, `git clone` into `{HomespunBasePath}/{repo}/{branch}`.
- Local creation: validate name (`^[a-zA-Z0-9_-]+$`), `git init` + initial commit, best-effort cleanup on failure.
- React routing shell: 8-tab layout (Issues, Pull Requests, Workflows, Branches, Clones, Prompts, Secrets, Settings) under `/projects/$projectId`.
- Home route renders project list; `/projects` redirects to `/`; `/projects/$projectId/` redirects to `/issues`.

## Capabilities

### New Capabilities
- `project-lifecycle`: Project CRUD, GitHub clone integration, local project initialization, routing shell.

### Modified Capabilities
<!-- None — this is a brownfield migration of an existing feature. -->

## Impact

- **Backend**: `Features/Projects/` — controller + service (~270 LOC), shared models in `Homespun.Shared`.
- **Frontend**: `features/projects/` — 5 components, 4 hooks, 7 co-located tests, plus 6 route files.
- **Testing**: 15 NUnit service tests, 12 WebApplicationFactory API tests, co-located Vitest component/hook tests.
- **Status**: Migrated — documents the as-built implementation, not a future design.
