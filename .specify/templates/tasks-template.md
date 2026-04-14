---

description: "Task list template for Homespun feature implementation"
---

# Tasks: [FEATURE NAME]

**Input**: Design documents from `/specs/[###-feature-name]/`
**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/

**Tests are MANDATORY** for Homespun (Constitution §I — Test-First Development).
Every implementation task in this template has a paired test task that MUST be
written and observed failing first. Do not delete the test tasks.

**Organization**: Tasks are grouped by user story so each story can be
implemented, tested, and reviewed independently.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no shared dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths, using the Homespun monorepo layout below

## Path Conventions (Homespun)

| Concern | Path |
|---------|------|
| Server slice | `src/Homespun.Server/Features/<Slice>/...` |
| Web slice | `src/Homespun.Web/src/features/<slice>/...` |
| Web shared UI | `src/Homespun.Web/src/components/ui/` (shadcn/ui — never reinvent) |
| OpenAPI client | `src/Homespun.Web/src/api/generated/` (generated — never hand-edit) |
| Worker | `src/Homespun.Worker/src/{routes,services,tools,types,utils}/` |
| Shared contracts | `src/Homespun.Shared/...` |
| Server unit tests | `tests/Homespun.Tests/...` |
| Server API tests | `tests/Homespun.Api.Tests/...` |
| Web unit tests | co-located `*.test.ts(x)` next to the source |
| Web e2e tests | `src/Homespun.Web/e2e/...` |
| Worker tests | `tests/Homespun.Worker/...` |
| Infra | `infra/` (Bicep) and `Dockerfile*`, `docker-compose.yml` |
| Architecture diagram | `docs/architecture/` (LikeC4) |
| Fleece issues | `.fleece/` (JSONL — commit with related code) |

<!--
  ============================================================================
  IMPORTANT: The tasks below are SAMPLE TASKS for illustration only.
  /speckit.tasks MUST replace them with concrete tasks derived from:
  - User stories from spec.md (with priorities P1, P2, P3...)
  - Affected slices from spec.md
  - API & Contract Impact, Realtime Impact, Persistence Impact, Worker Impact
  - Constitution Check from plan.md
  Tasks MUST stay grouped by user story so each story can ship as an MVP slice.
  Delete this comment from the generated tasks.md.
  ============================================================================
-->

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Repo-level prep that every story needs

- [ ] T001 Move the Fleece issue to `progress`: `fleece edit <id> -s progress`
- [ ] T002 Create branch using an allowed prefix (`feat/`, `feature/`, `fix/`, `task/`, `bug/`, `chore/`, `docs/`, `verify/`, `revert/`); keep the Fleece `+<id>` suffix if applicable
- [ ] T003 [P] Confirm Aspire/mock environment runs: `./scripts/mock.sh` (do **not** kill `homespun*` containers or `mock.sh` shells — Constitution §X)
- [ ] T004 [P] Add or update `.env.example` for any new env var introduced by this feature

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Cross-slice scaffolding that MUST land before per-story work

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

Examples (keep only what applies):

- [ ] T005 [P] Add or update shared DTOs / hub interfaces in `src/Homespun.Shared/`
- [ ] T006 [P] Add or update SQLite migration / schema for new persistence
- [ ] T007 Update server endpoint contract surface (Swashbuckle annotations) so OpenAPI regeneration produces the right client shape
- [ ] T008 Regenerate the typed web client: `cd src/Homespun.Web && npm run generate:api:fetch` (commit the diff under `src/api/generated/`)
- [ ] T009 [P] If `Fleece.Core` is being bumped, also bump `Fleece.Cli` in `Dockerfile.base` to the matching version (Constitution §IX)

**Checkpoint**: Foundation ready — user stories may now proceed in parallel

---

## Phase 3: User Story 1 — [Title] (Priority: P1) 🎯 MVP

**Goal**: [What this story delivers end-to-end]

**Independent Test**: [How to verify this story works on its own]

### Tests for User Story 1 (write FIRST, observe failing) ⚠️

- [ ] T010 [P] [US1] Server unit test in `tests/Homespun.Tests/Features/<Slice>/<Name>Tests.cs`
- [ ] T011 [P] [US1] Server API integration test in `tests/Homespun.Api.Tests/Features/<Slice>/<Name>EndpointTests.cs`
- [ ] T012 [P] [US1] Web unit test co-located: `src/Homespun.Web/src/features/<slice>/<Component>.test.tsx`
- [ ] T013 [P] [US1] Worker test in `tests/Homespun.Worker/<area>/<name>.test.ts` (only if worker is touched)
- [ ] T014 [US1] Playwright e2e in `src/Homespun.Web/e2e/<slice>/<flow>.spec.ts` covering the user journey

### Implementation for User Story 1

- [ ] T015 [P] [US1] Server slice changes in `src/Homespun.Server/Features/<Slice>/...`
- [ ] T016 [P] [US1] Web slice changes in `src/Homespun.Web/src/features/<slice>/...` (use existing shadcn/ui + prompt-kit components — do not reinvent)
- [ ] T017 [US1] Wire SignalR / AG-UI events if the spec's *Realtime Impact* requires them
- [ ] T018 [US1] Surface the slice via its `index.ts` and add to navigation/routes if user-facing
- [ ] T019 [US1] Update LikeC4 model under `docs/architecture/` if the topology changed

**Checkpoint**: User Story 1 is fully functional and independently shippable

---

## Phase 4: User Story 2 — [Title] (Priority: P2)

**Goal**: [What this story delivers]

**Independent Test**: [How to verify this story works on its own]

### Tests for User Story 2 (write FIRST, observe failing) ⚠️

- [ ] T020 [P] [US2] Server unit / API tests as in US1
- [ ] T021 [P] [US2] Web unit + e2e tests as in US1

### Implementation for User Story 2

- [ ] T022 [P] [US2] Server slice changes
- [ ] T023 [P] [US2] Web slice changes
- [ ] T024 [US2] Integrate with US1 surface where required

**Checkpoint**: User Stories 1 AND 2 both work independently

---

## Phase 5: User Story 3 — [Title] (Priority: P3)

[Repeat the US1/US2 structure as needed for additional stories.]

---

## Phase N: Polish & Cross-Cutting Concerns

- [ ] TXXX [P] Documentation updates in `docs/` and any LikeC4 view changes under `docs/architecture/`
- [ ] TXXX [P] Refactor any code duplicated across slices into `Features/Shared` (server) or `src/{components,hooks,lib,stores}` (web)
- [ ] TXXX Verify coverage per Constitution §V — **delta ≥ 80% on changed lines AND ratchet (no regression vs `main`)**:
      - server: `dotnet test --collect:"XPlat Code Coverage"` → Cobertura → `diff-cover` vs merge base
      - web: `cd src/Homespun.Web && npm run test:coverage` (LCOV) → `diff-cover`
      - worker: `cd src/Homespun.Worker && npm run test -- --coverage` (LCOV) → `diff-cover`
      - Confirm the per-module absolute number is on track for 60% by 2026-06-30 and 80% by 2026-09-30; if this PR regresses overall coverage, restore it before requesting review
- [ ] TXXX Run the **Pre-PR Quality Gate** (Constitution §IV) end-to-end:
      ```bash
      dotnet test
      cd src/Homespun.Web
      npm run lint:fix
      npm run format:check
      npm run generate:api:fetch
      npm run typecheck
      npm test
      npm run test:e2e
      ```
- [ ] TXXX Move Fleece issue to `review` and link the PR:
      `fleece edit <id> -s review --linked-pr <pr-number>`
- [ ] TXXX Commit `.fleece/` changes alongside code (or `fleece commit --ci`)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately
- **Foundational (Phase 2)**: Depends on Setup; BLOCKS all user stories. Includes any `Homespun.Shared` / OpenAPI regeneration.
- **User Stories (Phase 3+)**: All depend on Foundational completion. Parallel-safe across stories once OpenAPI client is regenerated.
- **Polish (Final Phase)**: Depends on all in-scope user stories being complete

### Within Each User Story

- Tests MUST be written and observed failing BEFORE implementation (Constitution §I)
- Server contract changes precede web client changes (so OpenAPI regeneration happens once)
- DTOs in `Homespun.Shared` precede consumers
- Slice surface (`index.ts` exports, route registration) is the last implementation step

### Parallel Opportunities

- All `[P]` Setup tasks can run together
- All `[P]` Foundational tasks can run together within Phase 2
- Once Foundational is done, separate user stories can be picked up by different developers
- Within a story, server-slice and web-slice changes marked `[P]` can proceed once their shared contracts have landed

---

## Parallel Example: User Story 1

```bash
# Tests first (all parallel; observe each failing before writing implementation):
Task: "Server unit test in tests/Homespun.Tests/Features/<Slice>/<Name>Tests.cs"
Task: "Server API integration test in tests/Homespun.Api.Tests/Features/<Slice>/<Name>EndpointTests.cs"
Task: "Web unit test in src/Homespun.Web/src/features/<slice>/<Component>.test.tsx"

# Implementation (parallel after foundational + OpenAPI regen):
Task: "Server slice in src/Homespun.Server/Features/<Slice>/..."
Task: "Web slice in src/Homespun.Web/src/features/<slice>/..."
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Phase 1 — Setup (Fleece status → progress, branch, env)
2. Phase 2 — Foundational (shared contracts, OpenAPI regen, schema)
3. Phase 3 — User Story 1 (tests first, then implementation)
4. **STOP and VALIDATE**: run the Pre-PR Quality Gate; verify 80% coverage on touched modules
5. Open PR, move Fleece issue to `review`

### Incremental Delivery

1. Setup + Foundational → foundation ready
2. US1 → validate → ship
3. US2 → validate → ship
4. US3 → validate → ship
5. Each story passes the Pre-PR gate independently

### Parallel Team Strategy

1. Whole team completes Setup + Foundational together (so OpenAPI regen lands once)
2. Then split: one dev per user story; server-slice and web-slice tasks may be split within a story when shared contracts are stable

---

## Notes

- `[P]` = different files, no dependencies — safe to parallelise
- `[Story]` label maps each task to its user story for traceability
- Tests fail before implementation — every time (Constitution §I)
- Coverage: delta ≥ 80% on changed lines + ratchet (no regression vs `main`); dated targets 60%/2026-06-30 and 80%/2026-09-30 (Constitution §V)
- Never hand-edit `src/Homespun.Web/src/api/generated/` (Constitution §III)
- Never stop `homespun` / `homespun-prod` containers or `KillShell` a `mock.sh`/`dotnet` shell (Constitution §X)
- Commit `.fleece/` changes with related code; resolve `.fleece/` conflicts with `fleece merge` only
- Conventional Commits with PR-number suffix: `type(scope?): summary (#NN)`
- Stop at any checkpoint to validate a story independently
