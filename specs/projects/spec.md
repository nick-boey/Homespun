# Feature Specification: Projects

**Feature Branch**: n/a (pre-spec-kit; built on `main` over many PRs)
**Created**: 2026-04-14 (migrated)
**Status**: Migrated
**Input**: Reverse-engineered from existing implementation in `src/Homespun.Server/Features/Projects/`, `src/Homespun.Shared/Models/Projects/` + `src/Homespun.Shared/Requests/ProjectRequests.cs`, `src/Homespun.Web/src/features/projects/`, the project-scoped routes under `src/Homespun.Web/src/routes/`, and their tests in `tests/Homespun.Tests/Features/Projects/` and `tests/Homespun.Api.Tests/Features/ProjectsApiTests.cs`.

> **Migration note.** This spec was produced by `/speckit-brownfield-migrate` against an already-shipped feature. It documents *what exists*, not a future design. "Projects" here is the **lifecycle and layout shell** for a project entity: creating it (local or from GitHub), listing it, selecting it, and routing into its tabs. The tabs themselves (Issues, Pull Requests, Workflows, Branches, Clones, Prompts, Secrets, Settings) are owned by other feature slices and are out of scope. The cross-slice `PullSyncButton` living in `features/projects/` is documented here because it ships as part of the project toolbar, but its logic belongs to Fleece + PullRequests sync (see Assumptions §A-3).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - List and select projects (Priority: P1) 🎯 MVP

As a Homespun user I land on the home page and see every project I have registered with Homespun, sorted most-recently-updated first, so that I can pick one to work in.

**Why this priority**: Every other workflow in the app — issues, PRs, workflows, agent sessions, clones — is scoped to a project. Without the list/select loop no other tab is reachable.

**Independent Test**: `GET /api/projects` returns the stored projects in `UpdatedAt`-descending order; the root route `/` renders them via `<ProjectsList>`; clicking a card navigates to `/projects/{id}`. Covered by `ProjectsApiTests.GetAll_ReturnsOk_WithListOfProjects`, the service-level `GetAllAsync_ReturnsAllProjects`, and the co-located `projects-list.test.tsx` / `project-card.test.tsx` component tests.

**Acceptance Scenarios**:

1. **Given** the data store has one or more projects, **When** the user loads `/`, **Then** `<ProjectsList>` renders one `<ProjectCard>` per project and each card links to `/projects/{id}`.
2. **Given** the data store has zero projects, **When** the user loads `/`, **Then** `<ProjectsEmptyState>` renders with a "Create Project" call to action pointing at `/projects/new`.
3. **Given** the projects query is still loading, **When** the UI renders, **Then** three `<ProjectCardSkeleton>` placeholders are displayed.
4. **Given** `/api/projects` returns an error, **When** the UI renders, **Then** the error boundary surfaces an inline fallback with a retry action, and retry invokes `refetch()`.

---

### User Story 2 - Create a project from a GitHub repository (Priority: P1)

As a user I paste `owner/repo`, submit the form, and Homespun clones the repo locally and registers the project so I can immediately work on it.

**Why this priority**: GitHub-backed projects are the primary way Homespun is onboarded. Without this, users can only create empty local projects.

**Independent Test**: `POST /api/projects { ownerRepo: "owner/repo" }` returns `201 Created` with a `Project` whose `GitHubOwner`/`GitHubRepo` are set, `DefaultBranch` is whatever GitHub reports, and `LocalPath` points inside `~/.homespun/projects/{repo}/{branch}`. Covered by `ProjectServiceTests.CreateAsync_*` (9 cases) and the E2E-flavoured `projects.new.test.tsx`.

**Acceptance Scenarios**:

1. **Given** a reachable GitHub repo `owner/repo`, **When** the user submits the "GitHub Repository" tab of `/projects/new` with that value, **Then** the server calls `GitHubService.GetDefaultBranchAsync`, `git clone`s into `{HomespunBasePath}/{repo}/{branch}`, persists a `Project`, and the client navigates to `/projects/{id}`.
2. **Given** an `ownerRepo` string that does not match `owner/name` after splitting on `/`, **When** the user submits, **Then** the server returns a `400 Bad Request` with message containing "Invalid format" and the form shows the error inline.
3. **Given** GitHub returns `null` for the default branch (repo missing or token unconfigured), **When** the user submits, **Then** the server returns a `400` with a message that names the `owner/repo` and points the user at `GITHUB_TOKEN`.
4. **Given** `git clone` fails with an error, **When** the user submits, **Then** the server returns a `400` with the underlying git error. (Exception: if `localPath/.git` already exists the server treats the repo as already cloned and continues — allows re-adding a project whose directory survived an earlier run.)

---

### User Story 3 - Create a local-only project (Priority: P2)

As a user without a GitHub remote I provide a project name and optional default branch, and Homespun initialises a fresh empty git repository so I can start tracking work in Homespun.

**Why this priority**: Local projects unlock offline / pre-GitHub use. Lower than GitHub-backed because most real users onboard via GitHub.

**Independent Test**: `POST /api/projects { name: "my-proj" }` returns `201 Created` with `GitHubOwner`/`GitHubRepo = null`, `DefaultBranch = "main"`, and `LocalPath` under `~/.homespun/projects/my-proj/main`. Covered by `ProjectsApiTests.Create_WithLocalName_ReturnsCreated`, `Create_WithCustomDefaultBranch_UsesProvidedBranch`, `Create_WithInvalidName_ReturnsBadRequest`.

**Acceptance Scenarios**:

1. **Given** a valid name matching `^[a-zA-Z0-9_-]+$`, **When** the user submits the "Local Project" tab, **Then** the server creates `{HomespunBasePath}/{name}/{branch}`, runs `git init`, renames to the requested branch, sets repo-local `user.email`/`user.name` defaults, and commits `--allow-empty -m "Initial commit"`.
2. **Given** a name containing whitespace, dots, or other disallowed characters, **When** the user submits, **Then** the server returns `400` with the "Invalid project name" message.
3. **Given** the target directory already exists on disk, **When** the user submits, **Then** the server returns `400` with `"Project already exists at {path}"`.
4. **Given** any step after directory creation fails (git init, initial commit, data-store add), **When** the server handles the error, **Then** the freshly-created directory is best-effort deleted before returning the error (cleanup is swallowed if it fails).
5. **Given** no `defaultBranch` is supplied or it is whitespace, **When** the server processes the request, **Then** it defaults to `main`.

---

### User Story 4 - Delete a project (Priority: P2)

As a user I remove a project I no longer need so it disappears from my list.

**Why this priority**: Needed for housekeeping; users create throwaway projects during exploration.

**Independent Test**: `DELETE /api/projects/{id}` returns `204 No Content` on success, `404` on unknown id. After delete, `GET /api/projects/{id}` returns `404`. Covered by `ProjectsApiTests.Delete_*` and `ProjectServiceTests.DeleteAsync_*`.

**Acceptance Scenarios**:

1. **Given** a project exists, **When** the user clicks the trash icon on its `<ProjectCard>` and confirms the alert dialog, **Then** the React client calls `useDeleteProject` → `DELETE /api/projects/{id}`, invalidates the `['projects']` query, and the card disappears.
2. **Given** deletion succeeds, **Then** a `project_deleted` telemetry event is emitted with `{ projectId }`; on error a `project_deletion_failed` event carries the error message.
3. **Given** a project with a local clone on disk, **When** the project is deleted, **Then** the on-disk clone at `LocalPath` is **not** removed — only the data-store record. (See Assumptions §A-2: documented behavior, not a bug.)

---

### User Story 5 - Update a project's default model (Priority: P3)

As a user I set which Claude model new agent sessions in this project should start with.

**Why this priority**: Per-project model default is a convenience that saves picking a model for each new session; nothing is blocked if the field is unset.

**Independent Test**: `PUT /api/projects/{id} { defaultModel: "sonnet" }` returns `200` with the updated project; the `DefaultModel` column persists. Covered by `ProjectsApiTests.Update_ReturnsUpdatedProject_WhenExists` / `Update_ReturnsNotFound_WhenDoesNotExist` and `ProjectServiceTests.UpdateAsync_*`.

**Acceptance Scenarios**:

1. **Given** a project exists, **When** a `PUT /api/projects/{id}` is submitted with `{ defaultModel }`, **Then** the server updates the project's `DefaultModel`, stamps `UpdatedAt`, and returns the mutated project.
2. **Given** an unknown id, **When** `PUT` is called, **Then** the server returns `404`.
3. `UpdateAsync` intentionally accepts only `DefaultModel` today — see Assumptions §A-4.

---

### Edge Cases

- **Concurrent create of the same GitHub repo** — no unique constraint on `(GitHubOwner, GitHubRepo)`; two POSTs racing on the same repo can both succeed (second one sees `.git` existing and re-registers). Not asserted by any test.
- **`HOMESPUN_BASE_PATH` / `HOMESPUN_DATA_PATH` overrides** — `ProjectService.HomespunBasePath` prefers explicit `HOMESPUN_BASE_PATH`, falls back to deriving a `projects/` sibling of `HOMESPUN_DATA_PATH`, and finally `~/.homespun/projects`. Verified implicitly by the integration tests (they rely on `HOMESPUN_DATA_PATH` being set by `HomespunWebApplicationFactory`).
- **Project loaded when SignalR or any other service is down** — `useProject` throws if the response has `error` or no `data`; the `$projectId` layout renders a "Project Not Found" card with Try Again / Go Home actions.
- **Dead-stub "Delete Project" menu item** — the dropdown in `projects.$projectId.tsx` has no click handler (see Assumptions §A-5). Users must delete from the card on the home page.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Expose CRUD over `/api/projects` — `GET /api/projects`, `GET /api/projects/{id}`, `POST /api/projects`, `PUT /api/projects/{id}`, `DELETE /api/projects/{id}`, with the request/response DTOs defined in `Homespun.Shared.Models.Projects` and `Homespun.Shared.Requests`.
- **FR-002**: `GetAllAsync` MUST return projects ordered by `UpdatedAt` descending.
- **FR-003**: `GetByIdAsync` MUST return `null` (and the controller `404`) for unknown ids.
- **FR-004**: `CreateAsync(ownerRepo)` MUST split on the first `/`, reject empty halves with "Invalid format", query `IGitHubService.GetDefaultBranchAsync`, and fail fast with a user-readable message if the default branch is null.
- **FR-005**: `CreateAsync` MUST compute the local path as `{HomespunBasePath}/{repo}/{defaultBranch}`, create the parent directory, and `git clone` the `https://github.com/{owner}/{repo}.git` into it. If clone fails but `{localPath}/.git` already exists, treat the repo as already cloned.
- **FR-006**: `CreateLocalAsync(name, defaultBranch="main")` MUST validate `name` against `^[a-zA-Z0-9_-]+$`, reject existing directories, run `git init` + `branch -M {defaultBranch}` + repo-local `user.email`/`user.name` + `commit --allow-empty -m "Initial commit"`, and best-effort clean up the directory on any failure.
- **FR-007**: Both create paths MUST persist the `Project` via `IDataStore.AddProjectAsync` before the controller returns `201 Created` with the new id.
- **FR-008**: `UpdateAsync(id, defaultModel)` MUST mutate only `DefaultModel` + `UpdatedAt` and return the project (or null for unknown id).
- **FR-009**: `DeleteAsync(id)` MUST remove the data-store record (returning `true`) and MUST NOT touch the on-disk clone.
- **FR-010**: `POST /api/projects` MUST return `400 Bad Request` when neither `ownerRepo` nor `name` is supplied and pass the service-level error message verbatim when the service returns failure.
- **FR-011**: The home route `/` MUST render the projects list; `/projects` MUST redirect to `/`; `/projects/$projectId/` MUST redirect to `/projects/$projectId/issues`.
- **FR-012**: The `/projects/$projectId` layout MUST render tabs for Issues, Pull Requests, Workflows, Branches, Clones, Prompts, Secrets, Settings (in that order) and highlight the tab matching the current URL prefix.
- **FR-013**: `useCreateProject` MUST invalidate `['projects']` on success and emit `project_created` / `project_creation_failed` telemetry events; `useDeleteProject` MUST invalidate the same key and emit `project_deleted` / `project_deletion_failed`.
- **FR-014**: The project card delete flow MUST require user confirmation via an alert dialog before calling the mutation.

### Key Entities

- **`Project`** (`src/Homespun.Shared/Models/Projects/Project.cs`)
  - `Id: string` — GUID, assigned server-side.
  - `Name: string` — for GitHub-backed projects this is the GitHub repo name; for local projects it is the user-supplied sanitised name.
  - `LocalPath: string` — absolute path to the default-branch clone/worktree.
  - `GitHubOwner: string?`, `GitHubRepo: string?` — both null together for local-only projects.
  - `DefaultBranch: string` — required.
  - `DefaultModel: string?` — consumed by the Sessions slice, not by Projects itself.
  - `CreatedAt`, `UpdatedAt: DateTime` — server-maintained.
  - `PullRequests: ICollection<PullRequest>` — navigation, `[JsonIgnore]`, populated by the PullRequests slice.
- **`CreateProjectResult`** (`src/Homespun.Shared/Models/Projects/CreateProjectResult.cs`) — service-internal result envelope with `Ok(Project)` / `Error(string)` factories.
- **`CreateProjectRequest`** (`src/Homespun.Shared/Requests/ProjectRequests.cs`) — `{ OwnerRepo?, Name?, DefaultBranch? }`; discriminated at the controller by which field is set. *Note: an identical class also exists at `src/Homespun.Server/Features/Projects/Controllers/ProjectsController.cs` (end of file); both names resolve for historical reasons, only the one the controller actually binds is used.*
- **`UpdateProjectRequest`** (`src/Homespun.Shared/Requests/ProjectRequests.cs`) — `{ DefaultModel? }`.

### Assumptions

- **A-1**: `HomespunBasePath` defaults to `~/.homespun/projects` but is overridable via `HOMESPUN_BASE_PATH` or derived from `HOMESPUN_DATA_PATH`. Container environments set one of these; local dev relies on the user profile path.
- **A-2**: `DeleteAsync` deliberately does not remove the on-disk clone. The reasoning is not documented in code but is consistent with data-loss-avoidance: the clone may contain uncommitted work or be referenced by worktrees from the Git slice. This is an **implicit contract** — future work that starts deleting directories MUST update this spec.
- **A-3**: `<PullSyncButton>` lives in `features/projects/` because it appears in the project toolbar, but its behaviour (Fleece pull/sync + PR sync) is entirely cross-slice. The exported `useFleecePull` / `useFleeceSync` / `usePullAndSync` hooks duplicate what the button inlines; see gaps in `plan.md`.
- **A-4**: `UpdateAsync` supports only `DefaultModel` by design — `Name`, `DefaultBranch`, `LocalPath`, `GitHubOwner`, and `GitHubRepo` are immutable after creation because changing them would invalidate clones, worktrees, and cached PRs. The current `/projects/{id}/settings` route surfaces GitHub sync actions rather than project-field edits.
- **A-5**: The "Delete Project" item in `projects.$projectId.tsx`'s `<DropdownMenu>` is a dead stub (no `onClick`). Deletion is only reachable via the card on the home page. Treated as a bug — see gap GP-4 in `plan.md`.
- **A-6**: `Project.DefaultModel` accepts any string the UI sends; validation against the live model catalogue lives in the Sessions slice, not here.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Core CRUD is discoverable from test files in a single grep — `ProjectsApiTests` and `ProjectServiceTests` together cover `GET`, `GET/{id}`, `POST` (both paths), `PUT`, `DELETE`, including 404 and 400 branches. No controller branch is uncovered at the API level.
- **SC-002**: New GitHub-backed project goes from "submit form" to "navigated to `/projects/{id}`" without any intermediate user input — one mutation, one navigation.
- **SC-003**: New local project is immediately committable: `git log` in the created directory shows one empty "Initial commit" so downstream slices (Fleece issue tracking, Git worktrees) have a valid ref to branch from.
- **SC-004**: `DELETE /api/projects/{id}` is idempotent from the client's perspective — a second `DELETE` returns `404` without side effects; the user's list view reaches the deleted state after exactly one round-trip.
- **SC-005**: The home route, `/projects` redirect, `/projects/$projectId/` redirect, and the layout shell all respond within a single React render cycle after the `useProject` query resolves — verified implicitly by the absence of loading spinners layered over one another in the routes' component code.
- **SC-006**: The projects list re-sorts after any mutation (create / delete / update) without a manual refresh, because every mutation invalidates the `['projects']` query key.
