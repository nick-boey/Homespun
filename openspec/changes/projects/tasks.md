---
description: "Retrospective task list for the migrated Projects feature"
---

# Tasks: Projects

**Input**: Design documents from `/specs/projects/`
**Status**: Migrated — all in-scope tasks reflect work that is already complete. Gaps are left **unchecked** and tracked in `follow-up-issues.md`.

> **Migration semantics.** `[x]` marks observed as-built work. Unchecked items are real, remediable gaps — do not delete them. Task groups mirror the user-story structure of `spec.md` so the backlog remains coherent with the SDD workflow going forward.

## Path Conventions (Homespun)

| Concern | Path |
|---------|------|
| Server slice | `src/Homespun.Server/Features/Projects/...` |
| Web slice | `src/Homespun.Web/src/features/projects/...` |
| Routes (route-per-file, consumer layer) | `src/Homespun.Web/src/routes/{index,projects.*}.tsx` |
| Shared contracts | `src/Homespun.Shared/{Models/Projects,Requests}/...` |
| Server unit tests | `tests/Homespun.Tests/Features/Projects/...` |
| Server API tests | `tests/Homespun.Api.Tests/Features/...` |
| Web unit tests | co-located `*.test.ts(x)` next to the source |
| Web e2e tests | `src/Homespun.Web/e2e/...` |

---

## Phase 1: Setup (Slice Scaffolding)

- [x] T001 Server slice scaffolding under `src/Homespun.Server/Features/Projects/` with `Controllers/` subfolder.
- [x] T002 Web slice scaffolding under `src/Homespun.Web/src/features/projects/` with `components/`, `hooks/`, `index.ts`.
- [x] T003 Test projects initialised: `tests/Homespun.Tests/Features/Projects/`, `tests/Homespun.Api.Tests/Features/`.

---

## Phase 2: Foundational (Blocking Prerequisites)

- [x] T004 [P] Shared DTO `src/Homespun.Shared/Models/Projects/Project.cs` — `Id`, `Name`, `LocalPath`, `GitHubOwner`/`Repo`, `DefaultBranch`, `DefaultModel`, `CreatedAt`/`UpdatedAt`, `[JsonIgnore] PullRequests`.
- [x] T005 [P] Shared envelope `src/Homespun.Shared/Models/Projects/CreateProjectResult.cs` with `Ok`/`Error` factories.
- [x] T006 [P] Shared request DTOs in `src/Homespun.Shared/Requests/ProjectRequests.cs` — `CreateProjectRequest`, `UpdateProjectRequest`.
- [x] T007 Service interface `src/Homespun.Server/Features/Projects/IProjectService.cs` — `GetAllAsync`, `GetByIdAsync`, `CreateLocalAsync`, `CreateAsync(ownerRepo)`, `UpdateAsync`, `DeleteAsync`.
- [x] T008 Swashbuckle annotations on `ProjectsController` so OpenAPI emits `Project`, `CreateProjectRequest`, `UpdateProjectRequest` correctly.
- [x] T009 Generated OpenAPI client refreshed under `src/Homespun.Web/src/api/generated/` covering `/api/projects*` endpoints.
- [ ] T010 Delete the duplicate `CreateProjectRequest` / `UpdateProjectRequest` classes at the bottom of `ProjectsController.cs` (authoritative copies live in `Homespun.Shared/Requests`). **DEFERRED → fleece:iuWY7t**
**Checkpoint**: Foundation landed — per-story work flows from here.

---

## Phase 3: US1 — List and select projects (P1) 🎯 MVP

**Goal**: Home page lists all projects, loading / empty / error states render, clicking a card navigates to the project layout.

### Tests

- [x] T011 [P] [US1] `tests/Homespun.Tests/Features/Projects/ProjectServiceTests.cs::GetAllAsync_ReturnsAllProjects`.
- [x] T012 [P] [US1] `tests/Homespun.Api.Tests/Features/ProjectsApiTests.cs::GetAll_ReturnsOk_WithListOfProjects`.
- [x] T013 [P] [US1] `tests/Homespun.Api.Tests/Features/ProjectsApiTests.cs::GetById_ReturnsProject_WhenExists` + `GetById_ReturnsNotFound_WhenDoesNotExist`.
- [x] T014 [P] [US1] Web component tests `features/projects/components/{projects-list,project-card,project-card-skeleton,projects-empty-state}.test.tsx`.
- [x] T015 [P] [US1] Web hook tests `features/projects/hooks/{use-projects,use-project}.test.ts`.
- [ ] T016 [US1] Add ordering test — two projects with distinct `UpdatedAt` assert `UpdatedAt desc` at the service and/or API layer. **DEFERRED → fleece:iuWY7t**
- [ ] T017 [US1] Playwright e2e in `src/Homespun.Web/e2e/projects/list-and-select.spec.ts` — seed a project, load `/`, click a card, land on `/projects/{id}/issues`. **DEFERRED → fleece:iuWY7t**
### Implementation

- [x] T018 [P] [US1] `ProjectService.GetAllAsync` (sorts `UpdatedAt desc`).
- [x] T019 [P] [US1] `ProjectService.GetByIdAsync`.
- [x] T020 [P] [US1] `ProjectsController.GetAll` + `GetById` (with `CreatedAtAction` using `GetById` for created responses).
- [x] T021 [P] [US1] Web `useProjects` + `useProject` hooks with `['projects']` / `['project', id]` query keys.
- [x] T022 [P] [US1] Web `<ProjectsList>` composing `<ProjectCard>` / `<ProjectCardSkeleton>` / `<ProjectsEmptyState>` / `<ErrorFallback>`.
- [x] T023 [P] [US1] Web `<ProjectCard>` — relative-time formatter, GitHub-vs-local badge, trash-icon confirm dialog.
- [x] T024 [US1] Route wiring: `routes/index.tsx` renders `<ProjectsList>`; `routes/projects.index.tsx` redirects to `/`; `routes/projects.$projectId.tsx` (layout shell with 8-tab nav); `routes/projects.$projectId.index.tsx` redirects to `/issues`.
- [ ] T025 [US1] Router test asserting `/projects/{id}/` redirects to `/projects/{id}/issues`. **DEFERRED → fleece:iuWY7t**
**Checkpoint**: US1 shippable independently — in production since before migration.

---

## Phase 4: US2 — Create a project from a GitHub repository (P1)

**Goal**: User submits `owner/repo`, server clones and persists, client navigates to the new project.

### Tests

- [x] T026 [P] [US2] `ProjectServiceTests.CreateAsync_ValidOwnerRepo_ReturnsSuccessWithProject`.
- [x] T027 [P] [US2] `ProjectServiceTests.CreateAsync_ValidOwnerRepo_SetsCorrectLocalPath`.
- [x] T028 [P] [US2] `ProjectServiceTests.CreateAsync_InvalidFormat_ReturnsError` + `CreateAsync_EmptyOwner_ReturnsError` + `CreateAsync_EmptyRepo_ReturnsError`.
- [x] T029 [P] [US2] `ProjectServiceTests.CreateAsync_GitHubReturnsNull_ReturnsError`.
- [x] T030 [P] [US2] `ProjectServiceTests.CreateAsync_CloneFails_ReturnsError`.
- [x] T031 [P] [US2] `ProjectServiceTests.CreateAsync_Success_AddsProjectToDataStore`.
- [x] T032 [P] [US2] `ProjectServiceTests.CreateAsync_WithNonMainDefaultBranch_UsesCorrectBranch`.
- [x] T033 [P] [US2] `features/projects/hooks/use-create-project.test.ts` + route test `routes/projects.new.test.tsx`.
- [ ] T034 [US2] Playwright e2e `src/Homespun.Web/e2e/projects/create-from-github.spec.ts`. **DEFERRED → fleece:iuWY7t**
- [ ] T035 [US2] Add a test asserting GitHub repos with names containing `.` (e.g. `foo.js`) still create a valid `Project` — locks in symmetric sanitisation. **DEFERRED → fleece:iuWY7t**
### Implementation

- [x] T036 [P] [US2] `ProjectService.CreateAsync(ownerRepo)` — parse, GitHub lookup, `git clone`, already-cloned handling, data-store add.
- [x] T037 [P] [US2] `ProjectsController.Create` — dispatches on `OwnerRepo` vs `Name`, maps `CreateProjectResult` to `201 CreatedAtAction(GetById)` / `400`.
- [x] T038 [P] [US2] Web `useCreateProject` with telemetry (`project_created` / `project_creation_failed`) and `['projects']` invalidation.
- [x] T039 [US2] Web route `routes/projects.new.tsx` — `react-hook-form` + `zod` schema, GitHub / Local tabs, error surface, navigate-on-success.

---

## Phase 5: US3 — Create a local-only project (P2)

**Goal**: Name-only path creates an initialised git repo with an empty initial commit and registers the project.

### Tests

- [x] T040 [P] [US3] `ProjectsApiTests.Create_WithLocalName_ReturnsCreated`.
- [x] T041 [P] [US3] `ProjectsApiTests.Create_WithCustomDefaultBranch_UsesProvidedBranch`.
- [x] T042 [P] [US3] `ProjectsApiTests.Create_WithInvalidName_ReturnsBadRequest`.
- [x] T043 [P] [US3] `ProjectsApiTests.Create_WithNoOwnerRepoOrName_ReturnsBadRequest`.
- [x] T044 [P] [US3] Inline validation branch covered in the same `routes/projects.new.test.tsx` tests as US2.
- [ ] T045 [US3] Add explicit unit test for `CreateLocalAsync` covering (a) directory-already-exists, (b) cleanup-after-git-init-fails, (c) empty/whitespace `defaultBranch` defaults to `main`. **DEFERRED → fleece:iuWY7t**
- [ ] T046 [US3] Playwright e2e `src/Homespun.Web/e2e/projects/create-local.spec.ts`. **DEFERRED → fleece:iuWY7t**
### Implementation

- [x] T047 [P] [US3] `ProjectService.CreateLocalAsync` — name validation, path build, `git init` / `branch -M` / repo-local `user.email`+`user.name` / empty initial commit, try/catch cleanup.
- [x] T048 [P] [US3] `ProjectService.IsValidProjectName` regex helper (`^[a-zA-Z0-9_-]+$`).
- [x] T049 [P] [US3] Dispatch in `ProjectsController.Create` when `Name` (not `OwnerRepo`) is supplied.

---

## Phase 6: US4 — Delete a project (P2)

**Goal**: User confirms deletion on the home card → data-store row removed, list updates, filesystem untouched.

### Tests

- [x] T050 [P] [US4] `ProjectServiceTests.DeleteAsync_ExistingProject_ReturnsTrue` + `DeleteAsync_NonExistentProject_ReturnsFalse`.
- [x] T051 [P] [US4] `ProjectsApiTests.Delete_ReturnsNoContent_WhenExists` + `Delete_ReturnsNotFound_WhenDoesNotExist` + `Delete_ProjectIsGone_AfterDeletion`.
- [x] T052 [P] [US4] Component test `features/projects/components/project-card.test.tsx` (confirm dialog + `onDelete` callback wiring).
- [x] T053 [P] [US4] Hook test `features/projects/hooks/use-projects.test.ts` (delete mutation + telemetry + cache invalidation).
- [ ] T054 [US4] Add an explicit test locking in "delete leaves `LocalPath` on disk" behaviour (spec §A-2, FR-009) — either a service-layer test with a mocked filesystem or an integration test asserting the directory survives. **DEFERRED → fleece:iuWY7t**
- [ ] T055 [US4] Wire up the dead "Delete Project" item in `projects.$projectId.tsx`'s dropdown (AlertDialog + `useDeleteProject` + `navigate({ to: '/' })`) and cover with a route test. **DEFERRED → fleece:iuWY7t**
- [ ] T056 [US4] Playwright e2e `src/Homespun.Web/e2e/projects/delete-project.spec.ts`. **DEFERRED → fleece:iuWY7t**
### Implementation

- [x] T057 [P] [US4] `ProjectService.DeleteAsync` — data-store remove, returns `false` for unknown id. **Filesystem intentionally not touched.**
- [x] T058 [P] [US4] `ProjectsController.Delete` — `204` / `404`.
- [x] T059 [P] [US4] Web `useDeleteProject` — mutation, telemetry (`project_deleted` / `project_deletion_failed`), `['projects']` invalidation.
- [x] T060 [US4] `<ProjectCard>` alert-dialog delete confirmation.

---

## Phase 7: US5 — Update a project's default model (P3)

**Goal**: Narrow-update endpoint that stamps `DefaultModel` + `UpdatedAt`; all other `Project` fields are immutable after creation (spec §A-4).

### Tests

- [x] T061 [P] [US5] `ProjectServiceTests.UpdateAsync_ValidProject_UpdatesDefaultModel` + `UpdateAsync_NonExistentProject_ReturnsNull`.
- [x] T062 [P] [US5] `ProjectsApiTests.Update_ReturnsUpdatedProject_WhenExists` + `Update_ReturnsNotFound_WhenDoesNotExist`.
- [ ] T063 [US5] Add a test asserting `UpdatedAt` is bumped on update and that `DefaultModel = null` clears the previous value. **DEFERRED → fleece:iuWY7t**
### Implementation

- [x] T064 [P] [US5] `ProjectService.UpdateAsync(id, defaultModel)` — sets `DefaultModel`, stamps `UpdatedAt`, persists.
- [x] T065 [P] [US5] `ProjectsController.Update` — `200` / `404`.

**Note**: no web UI currently mutates `DefaultModel` from this slice; the value is consumed by the Sessions slice at session-create time. `routes/projects.$projectId.settings.tsx` today exposes PR "Full Refresh" only (spec §A-4).

---

## Phase 8: Cross-slice toolbar (out-of-slice code hosted here)

*The `<PullSyncButton>` ships in the project toolbar on `/projects/$projectId` but its logic is Fleece + PullRequests sync. Tracked here for inventory; see plan GP-3 / GP-5.*

- [x] T066 [P] [polish] `features/projects/components/pull-sync-button.tsx` + `.test.tsx` — inline mutation pipelines for pull, discard-and-pull, sync; toast feedback; conflict alert dialog.
- [x] T067 [P] [polish] `features/projects/hooks/use-fleece-sync.ts` — `useFleecePull`, `useFleeceSync`, `usePullAndSync` (+ tests) — **exported but currently unused**.
- [ ] T068 [polish] Decide the home for sync UI: (a) refactor `<PullSyncButton>` to consume `use-fleece-sync` hooks then keep slice-local, or (b) move both to a `features/sync` (or `features/pull-requests`) slice and re-export. **DEFERRED → fleece:iuWY7t**
---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup. Blocks all user-story phases.
- **US1 (Phase 3)**: depends on Foundational.
- **US2 / US3 (Phases 4–5)**: each depends on Foundational; independent of each other and of US1 at the service layer (the UI happens to live in the same `projects.new.tsx` form).
- **US4 (Phase 6)**: depends on US1 (needs a list to delete from) for the primary UX path; the API endpoint itself depends only on Foundational.
- **US5 (Phase 7)**: depends on Foundational; orthogonal to the other stories.
- **Cross-slice toolbar (Phase 8)**: depends on US1 layout and on the Fleece + PullRequests slices' sync endpoints.

### User Story Dependencies

- US1 → MVP; blocks everything else in the UI because no other route is reachable.
- US2, US3, US4, US5 are mutually independent at the API level; at the UI level US4 depends on US1 (list-to-delete-from).

---

## Parallel Execution Examples

Because the slice is already built, the main value of the `[P]` markers is for any **re-run** of TDD on a gap item:

- `fleece:nqPkp8` (FI-1, e2e coverage) is embarrassingly parallel — each acceptance scenario can be written as its own spec by a different agent.
- `fleece:F4hpfr` (FI-7, CreateLocal edge tests), `fleece:5JYd5t` (FI-8, ordering test), and `fleece:QXdnMZ` (FI-9, redirect test) are all isolated file additions and can run in parallel.
- `fleece:wPpKCH` (FI-6, delete duplicate DTO) is a single-file deletion; must precede re-running `npm run generate:api:fetch` to confirm the OpenAPI spec still emits the Shared types.

---

## Implementation Strategy

### MVP First (already achieved)

1. ✅ Phase 1–2: Setup + Foundational (shared contracts, service interface, OpenAPI).
2. ✅ Phase 3: US1 (list + select) — this alone unblocks every other slice in the app.
3. ✅ Phase 4–7: GitHub-create, local-create, delete, update — add in priority order.

### Incremental Hardening (gaps to close)

1. Close the e2e gap (`fleece:nqPkp8` / FI-1) — highest value, lowest risk.
2. Remove the duplicate DTOs (`fleece:wPpKCH` / FI-6).
3. Wire up the dead Delete-Project dropdown item (`fleece:Y5k5Cr` / FI-4) OR remove it.
4. Decide `<PullSyncButton>` home (`fleece:Ia7rXU` / FI-3, `fleece:uvAnqB` / FI-5).
5. Backfill ordering / redirect / edge-case tests (`fleece:uvAnqB`, `F4hpfr`, `5JYd5t`, `QXdnMZ` — FI-5/7/8/9).
6. Decide and test the `DeleteAsync` filesystem contract (`fleece:MEAD2E` / FI-2).
