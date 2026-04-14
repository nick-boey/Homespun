# Implementation Plan: Projects

**Branch**: n/a (pre-spec-kit; built on `main` over many PRs)  |  **Date**: 2026-04-14 (migrated)  |  **Spec**: [`./spec.md`](./spec.md)
**Status**: Migrated — describes the as-built implementation, not a future design.

## Summary

Own the **project entity** (create, list, get, update, delete) plus the **project layout shell** that hosts every per-project tab. Backing store is `IDataStore` (the app-wide data-store abstraction used across slices); GitHub discovery goes through `IGitHubService`; git plumbing goes through `ICommandRunner` shelling out to the local `git` binary. The slice is deliberately thin — its job is to give every other slice a stable `Project` reference and a place to mount its route — so most of its complexity is in create-path validation, filesystem layout, and the React routing shell.

## Technical Context

**Language/Version**: C# / .NET 10 (server, shared), TypeScript 5.9 + React 19 (web)

**Primary Dependencies**:
- Server: ASP.NET Core, Swashbuckle (OpenAPI), `IDataStore` (app-wide persistence — SQLite underneath), `IGitHubService` (Octokit-backed), `ICommandRunner` (process-exec abstraction).
- Web: TanStack Query (`@tanstack/react-query`), TanStack Router file-based routes, `react-hook-form` + `zod` + `@hookform/resolvers`, shadcn/ui primitives (`Card`, `Button`, `AlertDialog`, `Tabs`, `Input`, `Label`, `DropdownMenu`, `Skeleton`, `Badge`), `lucide-react` icons, `sonner` toasts, generated OpenAPI client in `src/api/generated/`.

**Storage**:
- Project rows live in the application's global data store (`IDataStore.Projects`, accessed via `AddProjectAsync`, `UpdateProjectAsync`, `RemoveProjectAsync`, `GetProject`). No table is owned by this slice exclusively.
- Filesystem layout: `{HomespunBasePath}/{projectName-or-repo}/{defaultBranch}/` holds the cloned/initialised repo, where `HomespunBasePath = $HOMESPUN_BASE_PATH ?? dirname($HOMESPUN_DATA_PATH)/projects ?? ~/.homespun/projects`.
- `DeleteAsync` intentionally leaves the filesystem in place (spec §A-2).

**Testing**: NUnit + Moq (`ProjectServiceTests`), WebApplicationFactory (`ProjectsApiTests`), Vitest + React Testing Library (co-located `*.test.tsx` for every component and three of four hooks), Playwright (**gap** — no e2e spec covers project CRUD).

**Target Platform**: Linux containers in production (Azure Container Apps); Windows/macOS/Linux locally via `dotnet run` and `./scripts/mock.{sh,ps1}`.

**Project Type**: Multi-module monorepo — ASP.NET API + React SPA + shared contracts.

**Performance Goals**:
- List-projects query is a single in-memory sort; no pagination (typical N is well under 100 per user).
- Create-project wall time is dominated by `git clone` (GitHub) or `git init + initial commit` (local). No Homespun-side concurrency or caching is added; the UI shows a disabled "Creating..." button during the mutation.

**Constraints**:
- Cross-process DTOs MUST originate in `src/Homespun.Shared` (Constitution §III). `Project`, `CreateProjectResult`, `CreateProjectRequest`, `UpdateProjectRequest` all live there.
- The OpenAPI client is regenerated from the server's spec; never hand-edited.
- `Project.Name`, `DefaultBranch`, `LocalPath`, `GitHubOwner`, `GitHubRepo` are immutable after creation by design (spec §A-4).

**Scale/Scope**:
- Server slice: 3 files under `Features/Projects/` (~270 LOC); shared models 3 files (~50 LOC).
- Web slice: 20 files under `features/projects/` (5 components + 4 hooks + 7 colocated tests + indexes).
- Routes that consume the slice: `src/Homespun.Web/src/routes/{index,projects.new,projects.$projectId,projects.$projectId.index,projects.$projectId.settings}.tsx`.
- Backend tests: `ProjectServiceTests.cs` (15 cases) + `ProjectsApiTests.cs` (12 cases).

## Constitution Check

*Retrospective check for the as-built feature. Any box unchecked is called out under **Complexity Tracking** with a remediation note.*

| # | Gate | Pass? | Notes |
|---|------|-------|-------|
| I    | Test-First — failing tests written before production code | [~] | Dense backend coverage (service + API); frontend unit tests on every component and three of four hooks. No Playwright e2e for project CRUD (gap GP-1). Retrospective TDD cannot be reconstructed; forward work on this slice MUST follow the rule. |
| II   | Vertical Slice Architecture — change scoped to identified slice(s) | [x] | All server code under `Features/Projects/`, paired web code under `features/projects/`. Cross-slice `<PullSyncButton>` and `use-fleece-sync` hooks live here for UI-adjacency reasons (spec §A-3); flagged as a boundary question in gap GP-5 but not a violation today. |
| III  | Shared Contract Discipline — DTOs in `Homespun.Shared`; OpenAPI client regenerated, not hand-edited | [~] | `Project`, `CreateProjectResult`, `CreateProjectRequest`, `UpdateProjectRequest` all live in `src/Homespun.Shared`. **But**: `ProjectsController.cs` redeclares identical `CreateProjectRequest` / `UpdateProjectRequest` classes at file end — a duplicate that predates the Shared move (gap GP-6). |
| IV   | Pre-PR Quality Gate — `dotnet test`, `npm run lint:fix`, `npm run format:check`, `npm run generate:api:fetch`, `npm run typecheck`, `npm test`, `npm run test:e2e` all pass | [~] | Historic PRs run the gate. `npm run test:e2e` passes but does not exercise this slice (gap GP-1). |
| V    | Coverage — delta ≥ 80% on changed lines AND no regression vs `main`; on track for 60%/2026-06-30 and 80%/2026-09-30 | [~] | Service has near 1:1 production:tests ratio. Controller branches covered via API tests. UI components and hooks have co-located tests. `useFleeceSync` / `usePullAndSync` are exported but uncovered and unused (gap GP-3). `UpdateAsync` has no test covering `UpdatedAt` bump or `defaultModel = null` clearing. |
| VI   | Fleece-Driven Workflow — issue exists, status transitions, `.fleece/` committed | [n/a] | The slice predates the current workflow. Follow-up issues are drafted in `follow-up-issues.md` to backfill governance for the gaps. |
| VII  | Conventional Commits + PR suffix; allowed branch prefix | [x] | Historic commits follow the convention. No branches currently outstanding for this slice. |
| VIII | Naming — PascalCase (C#) / kebab-case (web feature folder) / co-located tests | [x] | `Features/Projects/`, `features/projects/`, `ProjectsController.cs`, `project-card.tsx` + `project-card.test.tsx` — all conforming. |
| IX   | Fleece.Core ↔ Fleece.Cli version sync | [n/a] | Feature does not depend on Fleece.Core. |
| X    | Container & mock-shell safety preserved | [x] | Slice never targets the `homespun` / `homespun-prod` containers; `git clone` is the only process spawn and runs under the user, not in one of the protected containers. |
| XI   | Logs queried via Loki | [x] | `ProjectService` logs via `ILogger<ProjectService>` which feeds the standard pipeline → Loki; no ad-hoc file sinks. |

## Project Structure

### Documentation (this feature)

```text
specs/projects/
├── spec.md                # User-visible feature description (migrated)
├── plan.md                # This file
├── tasks.md               # Retrospective task list, all completed except gaps
└── follow-up-issues.md    # Draft Fleece issue stubs for gaps (non-authoritative — create via fleece CLI)
```

### Source Code (repository root — as-built)

```text
src/
├── Homespun.Server/
│   └── Features/Projects/
│       ├── Controllers/
│       │   └── ProjectsController.cs            # /api/projects surface (GET, GET/{id}, POST, PUT/{id}, DELETE/{id})
│       ├── IProjectService.cs                   # Service interface — List, GetById, CreateLocal, Create(owner/repo), Update, Delete
│       └── ProjectService.cs                    # Implementation — path resolution, git init/clone, data-store mutations
│
├── Homespun.Shared/
│   ├── Models/Projects/
│   │   ├── Project.cs                           # Canonical Project DTO (incl. navigation prop to PullRequest)
│   │   └── CreateProjectResult.cs               # Ok/Error envelope for service-level returns
│   └── Requests/ProjectRequests.cs              # CreateProjectRequest + UpdateProjectRequest (authoritative copy)
│
└── Homespun.Web/src/
    ├── features/projects/
    │   ├── components/
    │   │   ├── project-card.tsx           (+ .test.tsx)   # Card + delete-confirm alert dialog + relative-time formatter
    │   │   ├── project-card-skeleton.tsx  (+ .test.tsx)   # Loading placeholder
    │   │   ├── projects-empty-state.tsx   (+ .test.tsx)   # Zero-state with "Create Project" CTA
    │   │   ├── projects-list.tsx          (+ .test.tsx)   # Query binding, loading/error/empty branches
    │   │   └── pull-sync-button.tsx       (+ .test.tsx)   # Cross-slice Fleece+PR sync button (spec §A-3)
    │   ├── hooks/
    │   │   ├── index.ts
    │   │   ├── use-create-project.ts      (+ .test.ts)    # Mutation + telemetry
    │   │   ├── use-fleece-sync.ts         (+ .test.ts)    # useFleecePull / useFleeceSync / usePullAndSync (exported but unused — gap GP-3)
    │   │   ├── use-project.ts             (+ .test.ts)    # Single-project query
    │   │   └── use-projects.ts            (+ .test.ts)    # List query + useDeleteProject mutation
    │   └── index.ts                                       # Public surface
    │
    └── routes/
        ├── index.tsx                                      # Home → <ProjectsList>
        ├── projects.index.tsx                             # /projects → redirect to /
        ├── projects.new.tsx            (+ .test.tsx)      # Create form with GitHub / Local tabs (Zod + react-hook-form)
        ├── projects.$projectId.tsx     (+ .test.tsx)      # Layout shell: 8-tab nav + PullSyncButton + actions dropdown
        ├── projects.$projectId.index.tsx                  # Redirect to /issues
        └── projects.$projectId.settings.tsx  (+ .test.tsx)# "Full Refresh" action (PR sync) — no project-field edits today (spec §A-4)

tests/
├── Homespun.Tests/
│   └── Features/Projects/
│       └── ProjectServiceTests.cs                 # 15 NUnit cases across Create/Update/Delete/GetAll
└── Homespun.Api.Tests/
    └── Features/ProjectsApiTests.cs               # 12 WebApplicationFactory cases across all routes
```

**Structure Decision**: Single vertical slice per side (`Features/Projects` + `features/projects`) with shared DTOs in `Homespun.Shared`. Routes that orchestrate the slice live in the global `src/routes/` tree because TanStack Router is file-based, not per-feature. Cross-slice sync UI (`<PullSyncButton>`) co-locates with the project toolbar for UI adjacency (spec §A-3).

## Complexity Tracking

*Gaps and boundary issues that did not meet the constitution; each is tracked as a proposed Fleece follow-up in `follow-up-issues.md`.*

| # | Finding | Why it exists | Remediation |
|---|---------|---------------|-------------|
| GP-1 | **No e2e coverage for project CRUD.** The only project-prefixed Playwright spec is `project-prompt-override.spec.ts` (prompts feature). `critical-journeys.spec.ts` does not create or delete a project. | Slice was built before the e2e bar tightened; backend + unit tests were deemed sufficient at the time. | Add `src/Homespun.Web/e2e/projects/` specs covering: create-from-GitHub happy path, create-local happy path, delete flow from home card, invalid-name 400 surfacing in the form. |
| GP-2 | **`DeleteAsync` does not clean up the on-disk clone.** The directory at `LocalPath` is orphaned after delete. Behaviour is in the spec (§A-2, FR-009) and is probably intentional, but there is no test either asserting or challenging it. | Data-loss-avoidance is a reasonable default — the clone may hold uncommitted work, Git-slice worktrees, or Fleece JSONL. | Either (a) add a test locking in the current behaviour, or (b) design an explicit cleanup flow (with a confirmation step that lists what would be deleted) — NOT merged until design-reviewed. |
| GP-3 | **Exported sync hooks are unused.** `useFleecePull`, `useFleeceSync`, `usePullAndSync` are exported from `features/projects` and tested in `use-fleece-sync.test.ts`, but no component consumes them — `<PullSyncButton>` inlines its own `useMutation`s. | Hooks were written first; the button was later reimplemented directly against the API. The refactor to consume the hooks never happened. | Either refactor `<PullSyncButton>` to use the hooks, or delete the hooks + their tests. |
| GP-4 | **"Delete Project" dropdown item is a dead stub.** `projects.$projectId.tsx:133-136` renders the menu item with no `onClick`; clicking it does nothing. Deletion is only reachable via the card on `/`. | Incremental UI scaffolding; the destructive-action wiring (confirmation + re-navigation after delete) was never finished. | Wire up an `AlertDialog` + `useDeleteProject`, then `navigate({ to: '/' })` on success. Add a route-level test asserting the destructive action is available from the layout. |
| GP-5 | **`<PullSyncButton>` and `use-fleece-sync` live in the wrong slice by strict reading.** Their behaviour is Fleece + PullRequests sync; they sit in `features/projects/` only because they render in the project toolbar. | UI adjacency won against slice discipline when the button was first added. | Consider moving to a new `features/sync/` slice or to `features/pull-requests/` (since "sync" in this codebase already blends PR + Fleece). Not a blocker — document the current home as intentional if moving is deferred. |
| GP-6 | **Duplicate request DTOs.** `CreateProjectRequest` and `UpdateProjectRequest` exist both in `src/Homespun.Shared/Requests/ProjectRequests.cs` (authoritative) and at the bottom of `src/Homespun.Server/Features/Projects/Controllers/ProjectsController.cs`. The controller binds one of them (via `using` resolution), but both compile and the duplicate drifts unseen. Constitution §III says cross-process DTOs MUST live in `Shared` and not be duplicated. | Pre-dates the move to `Homespun.Shared`. Deleting the in-controller copy was missed. | Delete the duplicates at the bottom of `ProjectsController.cs` and rerun the OpenAPI generator to confirm the Shared types are what the spec emits. |
| GP-7 | **Asymmetric name sanitisation.** `CreateLocalAsync` enforces `^[a-zA-Z0-9_-]+$` on its `name` parameter; `CreateAsync` trusts the `repo` segment from the user's `ownerRepo` string and uses it verbatim as `Project.Name`. Practically low-risk (GitHub repo names are a subset of what we allow), but asymmetric. | Two code paths, two authors, no explicit symmetry requirement. | Share a sanitisation/validation helper between both creates, or at minimum add a regex-check to `CreateAsync` mirroring the local path. Add a test asserting a GitHub repo containing a `.` (common, e.g. `foo.js`) still round-trips correctly. |
| GP-8 | **No test for `GetAllAsync` ordering.** The service sorts by `UpdatedAt desc` but no test exercises two projects with distinct timestamps to lock in the order. | Incidental — the original test only asserted count. | Add an ordering test in `ProjectServiceTests` and/or an API test that inserts two projects with staggered updates. |
| GP-9 | **No test for the `/projects/$projectId/` → `/issues` redirect.** The route contract (FR-011) is currently unverified. | Route was added as trivial redirect. | Add a Vitest router test or a Playwright spec that navigates to `/projects/{id}` and asserts the final URL ends with `/issues`. |
