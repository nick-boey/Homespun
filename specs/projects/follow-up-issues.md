# Follow-Up Issues — Projects (migrated)

**Status**: Created in Fleece on 2026-04-14. The `FI-N` labels below map to real Fleece IDs per the table. Use `fleece show <id> --json` for live details; this file is a historical record of the migration-time scope.

| Label | Fleece ID | Type | Title |
|---|---|---|---|
| FI-1 | `nqPkp8` | task | Add Playwright e2e coverage for Projects CRUD |
| FI-2 | `MEAD2E` | task | Decide and test the Projects DeleteAsync filesystem contract |
| FI-3 | `Ia7rXU` | chore | Eliminate use-fleece-sync / PullSyncButton duplication in Projects slice |
| FI-4 | `Y5k5Cr` | bug  | Wire up the dead Delete Project dropdown item on the project layout |
| FI-5 | `uvAnqB` | chore | Backfill UpdateAsync UpdatedAt/null-clearing tests and decide PullSyncButton slice placement |
| FI-6 | `wPpKCH` | chore | Delete duplicate CreateProjectRequest/UpdateProjectRequest DTOs from ProjectsController.cs |
| FI-7 | `F4hpfr` | task | Symmetric name validation for Projects CreateAsync + edge tests for CreateLocalAsync |
| FI-8 | `5JYd5t` | chore | Add ordering test for ProjectService.GetAllAsync |
| FI-9 | `QXdnMZ` | chore | Add test for /projects/:projectId/ to /issues redirect route |

---

## FI-1 — Add Playwright e2e coverage for Projects CRUD

- **Fleece ID**: `nqPkp8`
- **Type**: `task` (or break into multiple sub-tasks with a `verify` parent)
- **Referenced by**: `tasks.md` T017, T034, T046, T056 ; `plan.md` GP-1
- **Why**: Constitution §IV requires the full Pre-PR Quality Gate including `npm run test:e2e`. The Projects slice has zero project-prefixed e2e coverage beyond prompts.
- **Suggested scope**: `src/Homespun.Web/e2e/projects/`
  - `list-and-select.spec.ts` — seed a project, load `/`, click a card, land on `/projects/{id}/issues`.
  - `create-from-github.spec.ts` — mocked GitHub service, happy-path GitHub create.
  - `create-local.spec.ts` — local-create happy path, invalid-name 400 surfacing in the form.
  - `delete-project.spec.ts` — trash icon → confirm dialog → card disappears.
- **Suggested create command**:
  ```bash
  fleece create -t "Add Playwright e2e coverage for Projects CRUD" -y task \
    -d "Add e2e specs under src/Homespun.Web/e2e/projects/ covering list-and-select, create-from-github, create-local, delete-project. Referenced by specs/projects/tasks.md T017/T034/T046/T056 and plan.md GP-1."
  ```

---

## FI-2 — Decide and test the `DeleteAsync` filesystem contract

- **Fleece ID**: `MEAD2E`
- **Type**: `task`
- **Referenced by**: `tasks.md` T054 ; `plan.md` GP-2 ; `spec.md` §A-2 / FR-009
- **Why**: Current behaviour — "delete the row, keep the directory" — is implicit. Either lock it in with a test, or design an explicit opt-in cleanup flow. Silently leaving orphaned directories on disk is surprising.
- **Deliverables**:
  - Decision recorded in `specs/projects/spec.md` §A-2 (keep or change).
  - If keeping: add a test in `ProjectServiceTests` (or `ProjectsApiTests`) that creates a project, deletes it via API, and asserts the `LocalPath` directory still exists.
  - If changing: design an endpoint like `DELETE /api/projects/{id}?purge=true` with a UI confirmation listing what will be deleted; write tests both for purge and non-purge paths. Do NOT merge without design review.

---

## FI-3 — Eliminate the `use-fleece-sync` / `<PullSyncButton>` duplication

- **Fleece ID**: `Ia7rXU`
- **Type**: `chore`
- **Referenced by**: `tasks.md` T068 ; `plan.md` GP-3
- **Why**: `useFleecePull`, `useFleeceSync`, `usePullAndSync` are exported + tested but no component consumes them; `<PullSyncButton>` inlines equivalent mutation logic. Both cannot be right.
- **Suggested approach**: Refactor `<PullSyncButton>` to consume the hooks (and delete any behaviour that genuinely can't be expressed through them). Preserve all current toast + alert-dialog UX. Update the hook tests to cover the new paths exercised via the button.

---

## FI-4 — Wire up the dead "Delete Project" dropdown item

- **Fleece ID**: `Y5k5Cr`
- **Type**: `bug`
- **Referenced by**: `tasks.md` T055 ; `plan.md` GP-4 ; `spec.md` §A-5
- **Why**: `src/Homespun.Web/src/routes/projects.$projectId.tsx:133-136` renders the "Delete Project" menu item with no `onClick`. Clicking does nothing. Destruction of the currently-open project must be reachable from the project shell, not only from the home card.
- **Suggested scope**:
  - Add an `AlertDialog` and call `useDeleteProject` from the dropdown handler.
  - On success, `navigate({ to: '/' })` and show a success toast.
  - Add a Vitest/RTL test in `projects.$projectId.test.tsx` asserting the menu item triggers the confirmation and the delete mutation.

---

## FI-5 — Backfill narrow tests (UpdatedAt bump, DefaultModel clear, and PullSyncButton slice placement decision)

- **Fleece ID**: `uvAnqB`
- **Type**: `chore`
- **Referenced by**: `tasks.md` T063 ; `plan.md` GP-5 (placement) / GP-8 (no `UpdatedAt` assertion on update)
- **Why**: Two small, independent gaps.
  1. `UpdateAsync` bumps `UpdatedAt` — no test asserts this, and no test asserts `DefaultModel = null` clears a previously set value.
  2. `<PullSyncButton>` / `use-fleece-sync` arguably belong in a non-projects slice. If a decision is made to move them, the move is mechanical + requires updating imports + adding re-export stubs or deletion.
- **Deliverables**:
  - Add `UpdateAsync_StampsUpdatedAt` and `UpdateAsync_NullDefaultModel_ClearsPreviousValue` tests in `ProjectServiceTests`.
  - Record the final placement decision in `spec.md` §A-3 (either "stays here because UI-adjacent" or "move to `features/sync/`"); if moving, do so in the same PR.

---

## FI-6 — Delete duplicate request DTOs from `ProjectsController.cs`

- **Fleece ID**: `wPpKCH`
- **Type**: `chore`
- **Referenced by**: `tasks.md` T010 ; `plan.md` GP-6
- **Why**: Constitution §III — cross-process DTOs MUST live in `Homespun.Shared` and not be duplicated. `ProjectsController.cs` (end of file) re-declares `CreateProjectRequest` / `UpdateProjectRequest` that are authoritative in `Homespun.Shared.Requests`.
- **Deliverables**:
  - Delete the in-controller classes; fix `using` imports.
  - Re-run `npm run generate:api:fetch` and verify the generated client still emits the correct types.
  - Assert all backend + API tests still pass.

---

## FI-7 — Symmetric validation for `CreateAsync` + edge tests for `CreateLocalAsync`

- **Fleece ID**: `F4hpfr`
- **Type**: `task`
- **Referenced by**: `tasks.md` T035, T045 ; `plan.md` GP-7
- **Why**:
  - `CreateAsync` trusts the `repo` half of `ownerRepo` verbatim; `CreateLocalAsync` enforces `^[a-zA-Z0-9_-]+$`. Asymmetric. Low practical risk but a real audit finding.
  - `CreateLocalAsync` has branches (directory-already-exists, cleanup-after-failure, empty/whitespace `defaultBranch`) that aren't covered by a unit test.
- **Deliverables**:
  - Extract name validation into a shared helper used by both creates (or make `CreateAsync` enforce the same rule).
  - Add `ProjectServiceTests` cases: local create when directory exists; local create when `git init` fails (mock failure) asserts cleanup; default-branch defaulting when null/whitespace.
  - Add a GitHub-side test that a repo name containing `.` (e.g. `foo.js`) round-trips (update the regex if needed — current regex rejects dots).

---

## FI-8 — Add ordering test for `GetAllAsync`

- **Fleece ID**: `5JYd5t`
- **Type**: `chore`
- **Referenced by**: `tasks.md` T016 ; `plan.md` GP-8
- **Why**: The service promises `UpdatedAt desc`. No test enforces this. Trivial to add.
- **Deliverables**:
  - Add `GetAllAsync_ReturnsProjectsOrderedByUpdatedAtDescending` to `ProjectServiceTests`, using two projects with deliberately staggered `UpdatedAt` values.

---

## FI-9 — Add test for `/projects/$projectId/` → `/issues` redirect

- **Fleece ID**: `QXdnMZ`
- **Type**: `chore`
- **Referenced by**: `tasks.md` T025 ; `plan.md` GP-9 ; `spec.md` FR-011
- **Why**: The `projects.$projectId.index.tsx` file exists only to redirect; nothing asserts the route contract.
- **Deliverables**:
  - Vitest/RTL router test (or a Playwright spec as part of FI-1) that navigates to `/projects/{id}` and asserts the final URL ends with `/issues`.

---

## Notes for the humans running `fleece create`

- Prefer `verify`-type parent issues when bundling FI-1 (four sub-tasks) or FI-7 (symmetric validation + multiple edge tests). Use `fleece create ... --parent-issues <parent-id>:<lex-order>` for sub-tasks.
- `FI-6` is a prerequisite for any future `/api/projects` schema change — do it first.
- `FI-4` and `FI-1/delete-project.spec.ts` are best scheduled together; they share the delete-flow wiring.
- Cross-link each real Fleece id back into `tasks.md` and `plan.md` when created.
