# project-lifecycle

## Purpose

Homespun's Projects feature slice — CRUD over `/api/projects` for GitHub-backed and local-only projects, the home route that lists them, the layout shell for a selected project, and the React Query wiring that keeps the list fresh after every mutation. Projects are the top-level container for issues, pull requests, clones, and agent sessions.

## Requirements

### Requirement: Project CRUD API surface

The system SHALL expose CRUD operations over `/api/projects` with endpoints for list, get by id, create, update, and delete.

#### Scenario: List projects returns sorted results
- **WHEN** a client calls `GET /api/projects`
- **THEN** the response SHALL contain all projects ordered by `UpdatedAt` descending

#### Scenario: Get project by id succeeds
- **WHEN** a client calls `GET /api/projects/{id}` with a valid id
- **THEN** the response SHALL contain the matching project

#### Scenario: Get project by id returns 404 for unknown id
- **WHEN** a client calls `GET /api/projects/{id}` with an unknown id
- **THEN** the response SHALL be `404 Not Found`

#### Scenario: Delete project removes data-store record only
- **WHEN** a client calls `DELETE /api/projects/{id}` for an existing project
- **THEN** the data-store record SHALL be removed
- **AND** the on-disk clone at `LocalPath` SHALL NOT be deleted

#### Scenario: Delete unknown project returns 404
- **WHEN** a client calls `DELETE /api/projects/{id}` with an unknown id
- **THEN** the response SHALL be `404 Not Found`

### Requirement: GitHub-backed project creation

The system SHALL create a project from a GitHub repository by parsing `owner/repo`, querying the default branch, and cloning the repository.

#### Scenario: Valid owner/repo creates project
- **WHEN** a client POSTs `{ ownerRepo: "owner/repo" }` to `/api/projects`
- **THEN** the server SHALL query `IGitHubService.GetDefaultBranchAsync`
- **AND** SHALL clone into `{HomespunBasePath}/{repo}/{defaultBranch}`
- **AND** SHALL return `201 Created` with the new project

#### Scenario: Invalid owner/repo format returns 400
- **WHEN** a client POSTs an `ownerRepo` that does not match `owner/name` format
- **THEN** the response SHALL be `400 Bad Request` with message containing "Invalid format"

#### Scenario: GitHub returns null default branch
- **WHEN** GitHub returns `null` for the default branch
- **THEN** the response SHALL be `400 Bad Request` naming the `owner/repo`

#### Scenario: Already-cloned directory is reused
- **WHEN** `git clone` fails but `{localPath}/.git` already exists
- **THEN** the server SHALL treat the repo as already cloned and continue

### Requirement: Local project creation

The system SHALL create a local-only project by initializing a fresh git repository with an empty initial commit.

#### Scenario: Valid name creates local project
- **WHEN** a client POSTs `{ name: "my-proj" }` to `/api/projects`
- **THEN** the server SHALL create `{HomespunBasePath}/{name}/{branch}`
- **AND** SHALL run `git init`, rename to the requested branch, set repo-local user config, and commit `--allow-empty`
- **AND** SHALL return `201 Created`

#### Scenario: Invalid name returns 400
- **WHEN** a client POSTs a name not matching `^[a-zA-Z0-9_-]+$`
- **THEN** the response SHALL be `400 Bad Request` with "Invalid project name"

#### Scenario: Directory already exists returns 400
- **WHEN** the target directory already exists on disk
- **THEN** the response SHALL be `400 Bad Request` with "Project already exists at {path}"

#### Scenario: Default branch defaults to main
- **WHEN** no `defaultBranch` is supplied or it is whitespace
- **THEN** the server SHALL default to `main`

### Requirement: Project update limited to DefaultModel

The system SHALL support updating only the `DefaultModel` field on a project, stamping `UpdatedAt`.

#### Scenario: Update succeeds for existing project
- **WHEN** a client PUTs `{ defaultModel }` to `/api/projects/{id}` for an existing project
- **THEN** the server SHALL update `DefaultModel` and `UpdatedAt`
- **AND** SHALL return the updated project

#### Scenario: Update returns 404 for unknown id
- **WHEN** a client PUTs to `/api/projects/{id}` with an unknown id
- **THEN** the response SHALL be `404 Not Found`

### Requirement: Project routing shell

The home route SHALL render the projects list, with redirect rules for project navigation.

#### Scenario: Home route renders project list
- **WHEN** a user navigates to `/`
- **THEN** `<ProjectsList>` SHALL render

#### Scenario: Projects redirect to home
- **WHEN** a user navigates to `/projects`
- **THEN** the route SHALL redirect to `/`

#### Scenario: Project layout renders tabs
- **WHEN** a user navigates to `/projects/$projectId`
- **THEN** the layout SHALL render tabs for Issues, Pull Requests, Workflows, Branches, Clones, Prompts, Secrets, Settings

#### Scenario: Project index redirects to issues
- **WHEN** a user navigates to `/projects/$projectId/`
- **THEN** the route SHALL redirect to `/projects/$projectId/issues`

### Requirement: Mutation cache invalidation

Every project mutation SHALL invalidate the `['projects']` query key so the list re-sorts without manual refresh.

#### Scenario: Create invalidates project list
- **WHEN** a project is created successfully
- **THEN** the `['projects']` query key SHALL be invalidated
- **AND** a `project_created` telemetry event SHALL be emitted

#### Scenario: Delete invalidates project list
- **WHEN** a project is deleted successfully
- **THEN** the `['projects']` query key SHALL be invalidated
- **AND** a `project_deleted` telemetry event SHALL be emitted
