---

description: "Task list for worker skills & plugins logging"
---

# Tasks: Worker Skills & Plugins Logging

**Input**: Design documents from `/specs/001-worker-skills-logging/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/inventory-log-record.schema.json, quickstart.md

**Tests are MANDATORY** for Homespun (Constitution §I — Test-First Development).
Every implementation task below has a paired test task that MUST be written and observed failing first.

**Organization**: Tasks are grouped by user story so each story can be implemented, tested, and reviewed independently.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no shared dependencies)
- **[Story]**: Which user story this task belongs to (US1 / US2 / US3)
- File paths are absolute-from-repo-root using the Homespun monorepo layout

## Path Conventions (this feature — worker only)

| Concern | Path |
|---------|------|
| Worker production code | `src/Homespun.Worker/src/services/`, `src/Homespun.Worker/src/index.ts` |
| Worker tests | `tests/Homespun.Worker/services/`, `tests/Homespun.Worker/helpers/` |
| JSON Schema contract | `specs/001-worker-skills-logging/contracts/inventory-log-record.schema.json` |
| Fleece issues | `.fleece/` (JSONL — commit with related code) |

No server, web, shared, or infra paths are touched by this feature (confirmed in spec's *Affected Slices* and plan's *Structure Decision*).

---

## Phase 1: Setup (Workflow Prep)

**Purpose**: Constitution §VI / §VII compliance — Fleece issue and branch prep. No production code yet.

- [X] T001 Create the Fleece issue backing this feature: `fleece create -t "Worker skills & plugins logging" -y feature -d "Emit info-level structured inventory log per session create/resume/boot, listing skills, plugins, commands, agents, hooks, mcpServers; see specs/001-worker-skills-logging/"` and capture the returned id → **SFq3pB**
- [X] T002 Move the Fleece issue to `progress`: `fleece edit SFq3pB -s progress`
- [X] T003 Rename the working branch to a Constitution §VII-compliant prefix that preserves the Fleece id suffix: `git branch -m 001-worker-skills-logging feat/worker-skills-logging+SFq3pB`
- [X] T004 [P] Confirm the mock stack is usable for manual verification later — **superseded**: verified directly against the live worker process (see T038). `mock.sh` spawns no TypeScript worker; all three inventory events (create/resume/boot) confirmed by running `npm run dev` with a real Claude SDK backend.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Land the shared helper module skeleton and the JSON Schema wiring so every user story's tests can depend on them. No user-visible behaviour yet.

**⚠️ CRITICAL**: No US1/US2/US3 tasks can begin until this phase is complete.

- [X] T005 [P] Create the helper module skeleton with typed exports (no logic yet) in `src/Homespun.Worker/src/services/session-inventory.ts` — declare and export the TypeScript types `ResourceInventoryEntry` and `SessionInventoryLogRecord` exactly matching `specs/001-worker-skills-logging/data-model.md`, plus empty function signatures `buildInventoryFromInit(...)`, `emitInventoryLog(...)`, and `discoverHooksFromFilesystem(...)` that throw `new Error("not implemented")`. This lets dependent test files import without TypeScript errors while still failing at runtime.
- [X] T006 [P] Add a test helper `tests/Homespun.Worker/helpers/inventory-schema.ts` that loads `specs/001-worker-skills-logging/contracts/inventory-log-record.schema.json` and exports a validator function using `ajv` (already transitively available — if not, add `ajv` + `ajv-formats` as a worker dev-dep); the function MUST throw on invalid records with the Ajv error list so assertions surface schema drift
- [X] T007 [P] Add a shared fixture builder `tests/Homespun.Worker/helpers/sdk-init-fixture.ts` that produces a realistic `SDKSystemMessage` with `subtype: 'init'` populated per the shape in `research.md` §R1 (all six fields: `agents`, `mcp_servers`, `slash_commands`, `skills`, `plugins`, and `cwd`, `session_id`, `claude_code_version`, `model`, `permissionMode`) with sensible defaults and overrides

**Checkpoint**: Helper module compiles, test schema validator loads the schema, fixture builder available — user stories may now proceed.

---

## Phase 3: User Story 1 — Operator confirms which skills & plugins are active per session (Priority: P1) 🎯 MVP

**Goal**: Emit one `info`-level structured log record per `SessionManager.create(...)` AND per resume path, listing all six resource categories, correlated by `sessionId`, with empty-list guarantees and discovery-error resilience. No `canUseTool` or boot changes in this story.

**Independent Test**: Run the worker tests below and confirm they fail before T014/T015/T016/T017 land and pass after. Separately: start mock mode (`./scripts/mock.sh`), trigger a session from the UI, `grep "inventory event=" logs/mock-backend.log` — expect exactly one matching line per session create, one per resume, schema-valid payload, `sessionId` present, all six category lists present, no `GITHUB_TOKEN`/`env` substring anywhere on the line.

### Tests for User Story 1 (write FIRST, observe failing) ⚠️

- [X] T008 [P] [US1] Worker unit test — "emits one inventory record with all six category lists on session create" — in `tests/Homespun.Worker/services/session-inventory.test.ts`. Arranges a fake init message via the fixture builder, calls `buildInventoryFromInit(...)`, asserts every one of `skills`, `plugins`, `commands`, `agents`, `hooks`, `mcpServers`, `discoveryErrors` is present (INV-1 / INV-4).
- [X] T009 [P] [US1] Worker unit test — "records validate against the JSON Schema contract" — in the same file; uses the schema validator from T006 against records produced with several permutations of the fixture (INV-5 / FR-008).
- [X] T010 [P] [US1] Worker unit test — "contains no secret material" — in the same file; stringifies the record and asserts it does NOT contain any of `GITHUB_TOKEN`, `GH_TOKEN`, `Bearer`, `password`, `apiKey`, `authorization`, `env=` (INV-2 / FR-010).
- [X] T011 [P] [US1] Worker unit test — "hook FS discovery failure does not block emission" — in the same file; mocks `fs.promises.readdir` to reject with `EACCES`, calls the full emit path, asserts the record is still produced and `discoveryErrors` contains one entry with `category: "hook"` and a sanitized `reason` (FR-006).
- [X] T012 [P] [US1] Worker unit test — "resume re-enumerates instead of caching" — in the same file; runs the build twice with two different fixture init messages, asserts the second output differs from the first in the category that was mutated (INV-3 / FR-005).
- [X] T013 [P] [US1] Worker unit test — "empty `.claude/` still produces six empty lists, not missing fields" — in the same file; fixture has no resources, asserts every category field is `[]` and present as own-key (FR-009).
- [X] T014 [P] [US1] Worker integration-style test in `tests/Homespun.Worker/services/session-manager-logging.test.ts` — extend the existing suite with a case "emits `inventory event=create sessionId=<id>` info log exactly once when the SDK yields its system/init message". Uses the existing `setMockQueryMessages` helper to prepend a mocked init message; asserts `info.mock.calls` contains exactly one call whose argument starts with `inventory event=create sessionId=test-session-123 payload={`.
- [X] T015 [P] [US1] Worker integration-style test in the same file — "emits `inventory event=resume` on the resume code path". Drives the `SessionManager` resume branch (per the existing resume test scaffolding around `session-manager.ts:1048-1110`); asserts one matching log line, sessionId matches the resumed session.

### Implementation for User Story 1

- [X] T016 [US1] Implement `discoverHooksFromFilesystem(cwd, settingSources)` in `src/Homespun.Worker/src/services/session-inventory.ts` — uses `node:fs/promises.readdir` against `~/.claude/hooks/` and `<cwd>/.claude/hooks/` (only the scopes present in `settingSources`); returns `{ entries: ResourceInventoryEntry[]; errors: {category,source,reason}[] }`; all errors caught and sanitized; never throws (FR-006).
- [X] T017 [US1] Implement `buildInventoryFromInit(initMessage, options, event, sessionId)` in `src/Homespun.Worker/src/services/session-inventory.ts` — maps the SDK `SDKSystemMessage` (subtype `'init'`) + the worker's options bag to a `SessionInventoryLogRecord`. Sources per research R1/R2/R6: skills / commands / agents / plugins / mcpServers from init; hooks from `options.hooks` merged with `discoverHooksFromFilesystem(...)`; `cwd` home-relative where possible; no `env` data ever read; adds a `truncated: true` flag + `truncationCounts` only if the JSON-stringified payload would exceed 16 KB (clip per-category proportionally).
- [X] T018 [US1] Implement `emitInventoryLog(record)` in `src/Homespun.Worker/src/services/session-inventory.ts` — `JSON.stringify(record)` (compact), then `info(\`inventory event=${record.event} sessionId=${record.sessionId} payload=${json}\`)` via the existing `#src/utils/logger.js` `info` import. Exactly one call. No new log line on the secondary path.
- [X] T019 [US1] Wire the emitter into `SessionManager.create` in `src/Homespun.Worker/src/services/session-manager.ts` — within the existing async generator loop that pushes events into `OutputChannel` (around the `for await (const event of q)` body in the create path), detect the first message where `event.type === 'system' && (event as any).subtype === 'init'` for that session, call `buildInventoryFromInit(event, sessionOptions, 'create', id)` → `emitInventoryLog(...)`, and continue forwarding the event unchanged to `OutputChannel.push(event)`. Guard against double-emit if the SDK ever replays init.
- [X] T020 [US1] Wire the emitter into the resume path in the same file — the equivalent async generator around the `newQuery` construction (circa line 1103 of `session-manager.ts`); use `event: 'resume'` and the resumed session's `sessionId` (`ws.id`). Same init-message sniff; same single emission guarantee.
- [X] T021 [US1] Remove the two workarounds already implied by the spec (none — sanity pass): re-scan the diff to confirm no dead code, no commented-out legacy prints, no `console.log` leaked.

**Checkpoint**: US1 is fully functional and independently shippable as the MVP. Operators can answer SC-001 via one LogQL query.

---

## Phase 4: User Story 2 — Per-tool-call attribution to a source skill/plugin (Priority: P2)

**Goal**: Enrich the existing `canUseTool` `info` log line with an additional origin field — `builtin`, an MCP server name, a plugin name, or `unknown` — resolved from the first-seen init message for the session.

**Independent Test**: In the mock stack, invoke a Playwright MCP tool (e.g. via a planned prompt that clicks in the browser); `grep "canUseTool" logs/mock-backend.log` and confirm the matching line now includes `origin=mcp:playwright`. Built-in tools (e.g. `Read`) show `origin=builtin`. Unmapped names show `origin=unknown`. No new log line; the existing one is just enriched.

### Tests for User Story 2 (write FIRST, observe failing) ⚠️

- [X] T022 [P] [US2] Worker unit test in `tests/Homespun.Worker/services/session-inventory.test.ts` — "resolveToolOrigin returns `builtin` for known SDK built-ins (Read, Write, Edit, Bash, Glob, Grep, Task, WebFetch, WebSearch, AskUserQuestion, ExitPlanMode, TodoWrite, NotebookEdit)". Asserts each one maps to `builtin` regardless of init contents.
- [X] T023 [P] [US2] Worker unit test — "resolveToolOrigin parses `mcp__<server>__<tool>` to `mcp:<server>`" — in the same file; asserts `mcp__playwright__browser_click` → `mcp:playwright` and matches a server present in `init.mcp_servers`.
- [X] T024 [P] [US2] Worker unit test — "resolveToolOrigin falls back to `unknown` when the tool name doesn't match any known pattern or server".
- [X] T025 [P] [US2] Worker integration test in `tests/Homespun.Worker/services/session-manager-logging.test.ts` — "canUseTool info log includes `origin=` field". Drives the captured `canUseTool` callback (already available as `getCapturedCanUseTool()` in the existing test scaffold) with tool name `Read`, then with `mcp__playwright__browser_click`; asserts each `info` call's message ends with ` origin=builtin` / ` origin=mcp:playwright` respectively, as a field on the existing line (not a new line — FR-011).

### Implementation for User Story 2

- [X] T026 [US2] Add `resolveToolOrigin(toolName, init): string` to `src/Homespun.Worker/src/services/session-inventory.ts` — enumerates the constant `BUILTIN_TOOL_NAMES` (the 13 SDK built-ins listed in R8), returns `builtin` for those, parses `mcp__<server>__...` and validates `<server>` against `init.mcp_servers[].name` returning `mcp:<server>`, otherwise returns `unknown`.
- [X] T027 [US2] In `src/Homespun.Worker/src/services/session-manager.ts`, store the init message on the session record when first observed (extend `WorkerSession` with an `init?: SDKSystemMessage` field — internal to the worker, not part of any shared DTO), so `createCanUseToolCallback` can read it at tool-invocation time.
- [X] T028 [US2] Enrich the existing `canUseTool` `info` call at `session-manager.ts:514` — compute `origin = resolveToolOrigin(toolName, session.init)` and append ` origin=${origin}` to the existing message string. No new log line. Preserve every other field.

**Checkpoint**: US1 + US2 both work independently. If US2 is regressed, US1 still satisfies SC-001.

---

## Phase 5: User Story 3 — Inventory is queryable at worker startup (Priority: P3)

**Goal**: At worker boot, after Hono reports ready, run one dry SDK query against the default working directory, capture the init message, emit a single `inventory event=boot sessionId=boot` record, and abort the dry query immediately.

**Independent Test**: Cold-start the worker process. Tail the log. Expect exactly one `inventory event=boot` line within ~2 s of the "ready" message and before any real session is created. Kill the worker and restart — expect exactly one additional boot line.

### Tests for User Story 3 (write FIRST, observe failing) ⚠️

- [X] T029 [P] [US3] Worker unit test in `tests/Homespun.Worker/services/session-inventory.test.ts` — "emitBootInventory(query) captures the first init message and emits one `event=boot` record with `sessionId === 'boot'`". Uses a mocked `Query` that yields an init message then completes; asserts schema-valid, one `info` call, `event` is `boot`.
- [X] T030 [P] [US3] Worker unit test — "emitBootInventory aborts the dry query after capturing init, even if the query would otherwise stream further messages". Verifies the `Query.interrupt()` / abort path is called.
- [X] T031 [P] [US3] Worker integration test in `tests/Homespun.Worker/routes/index.boot-inventory.test.ts` (new file) — stubs `@anthropic-ai/claude-agent-sdk.query` to return a mock yielding a canned init, boots the Hono app via the existing entrypoint wrapper, and asserts exactly one `info` log with `inventory event=boot sessionId=boot payload={` was emitted before the first HTTP request.

### Implementation for User Story 3

- [X] T032 [US3] Add `emitBootInventory(workingDirectory?)` to `src/Homespun.Worker/src/services/session-inventory.ts` — builds options identical to `buildCommonOptions(...)` for the default cwd, calls `query({ prompt: async function*(){}, options })`, consumes messages until the first init, calls `buildInventoryFromInit(init, options, 'boot', 'boot')` → `emitInventoryLog(...)`, invokes the returned query's abort/close, and wraps the entire function in a try/catch that degrades to a single `warn(...)` without crashing the worker (FR-006).
- [X] T033 [US3] Wire `emitBootInventory()` into `src/Homespun.Worker/src/index.ts` — invoke immediately after the existing "worker ready" log and before the first request handler completes (fire-and-forget `void emitBootInventory()`; do NOT await — we must not delay readiness).

**Checkpoint**: All three stories shippable independently. US3 is a convenience on top of US1/US2.

---

## Phase N: Polish & Cross-Cutting Concerns

- [X] T034 [P] Re-read the diff against `specs/001-worker-skills-logging/data-model.md` invariants INV-1 through INV-5 and confirm each has a dedicated green test (spot-check test names)
- [X] T035 [P] Re-read the diff against `specs/001-worker-skills-logging/contracts/inventory-log-record.schema.json` and confirm every emitted field type aligns (run the schema validator test)
- [X] T036 Run worker coverage and confirm Constitution §V delta floor: `cd src/Homespun.Worker && npm run test -- --coverage` → LCOV → `diff-cover` vs merge base. Delta ≥ 80% on changed lines in `session-inventory.ts`, `session-manager.ts`, `index.ts`. Overall `Homespun.Worker` module coverage must not regress vs `main`.
- [X] T037 Run the **Pre-PR Quality Gate** (Constitution §IV) end-to-end — note: `dotnet test`, `lint:fix`, `format:check`, `generate:api:fetch`, `typecheck` all run unchanged and should be no-ops except for worker lint/typecheck:
      ```bash
      dotnet test
      cd src/Homespun.Web
      npm run lint:fix
      npm run format:check
      npm run generate:api:fetch   # should produce zero diff — no server API changed
      npm run typecheck
      npm test
      npm run test:e2e
      cd ../Homespun.Worker
      npm run typecheck
      npm test
      ```
- [X] T038 Verify operationally in mock mode per `specs/001-worker-skills-logging/quickstart.md` §3 — trigger create, resume, boot; confirm one `inventory event=...` line per event; confirm no secrets (`grep -E "GITHUB_TOKEN|GH_TOKEN|Bearer" logs/mock-backend.log` against inventory lines should return zero rows)
- [ ] T039 Move the Fleece issue to `review` and link the PR: `fleece edit <id> -s review --linked-pr <pr-number>`; commit `.fleece/` alongside code (or `fleece commit --ci`)
- [ ] T040 Open the PR using Conventional Commits with the PR number suffix (e.g. `feat(worker): log available skills and plugins per session (#NN)`), base against `main`, include the `specs/001-worker-skills-logging/` directory in the PR
- [ ] T041 After merge: `fleece edit <id> -s complete`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — start immediately.
- **Phase 2 (Foundational)**: Depends on Setup. Blocks all user stories.
- **Phase 3 (US1 — MVP)**: Depends on Phase 2. Independent of US2 / US3.
- **Phase 4 (US2)**: Depends on Phase 2. Depends on the `init` message capture from US1 (T019 adds `session.init` state) — so **US2 implementation requires US1 to have landed**. US2's **tests** can be written against mocks in parallel with US1's tests.
- **Phase 5 (US3)**: Depends on Phase 2 and on `buildInventoryFromInit` + `emitInventoryLog` from US1 (T017 / T018). US3's tests can be written in parallel with US1's tests (against mocked queries).
- **Phase N (Polish)**: Depends on whichever stories ship.

### Within Each User Story

- Every `Tests for User Story X` subsection MUST be written and observed failing before any task in that story's `Implementation for User Story X` subsection is started (Constitution §I).
- Within each implementation subsection, tasks are ordered by dependency: helper functions (T017/T026/T032) before the wiring into `session-manager.ts` / `index.ts` (T019/T028/T033).

### Parallel Opportunities

- Phase 2: T005, T006, T007 are all `[P]` — different files, no ordering between them.
- Phase 3 tests: T008–T015 are all `[P]` — same test files but different test cases / different files entirely; Vitest runs them in parallel.
- Phase 4 tests: T022–T025 are all `[P]`.
- Phase 5 tests: T029–T031 are all `[P]`.
- Polish: T034 and T035 are `[P]` (read-only audits).
- Across stories: once US1 (MVP) has landed, US2 and US3 can be split across two developers; they touch different wiring points and their unit tests are in separate test cases.

---

## Parallel Example: User Story 1 (MVP)

```bash
# All US1 tests in parallel (write, run, observe failing):
Task: "tests/Homespun.Worker/services/session-inventory.test.ts  — 'emits one record with six category lists'"
Task: "tests/Homespun.Worker/services/session-inventory.test.ts  — 'record validates against JSON Schema'"
Task: "tests/Homespun.Worker/services/session-inventory.test.ts  — 'no secret material'"
Task: "tests/Homespun.Worker/services/session-inventory.test.ts  — 'hook FS failure does not block'"
Task: "tests/Homespun.Worker/services/session-inventory.test.ts  — 'resume re-enumerates'"
Task: "tests/Homespun.Worker/services/session-inventory.test.ts  — 'empty .claude produces six empty lists'"
Task: "tests/Homespun.Worker/services/session-manager-logging.test.ts — 'emits inventory event=create exactly once'"
Task: "tests/Homespun.Worker/services/session-manager-logging.test.ts — 'emits inventory event=resume on resume'"

# Then sequential implementation (no [P] — same files):
Task: "Implement discoverHooksFromFilesystem() in session-inventory.ts"
Task: "Implement buildInventoryFromInit() in session-inventory.ts"
Task: "Implement emitInventoryLog() in session-inventory.ts"
Task: "Wire into SessionManager.create in session-manager.ts"
Task: "Wire into resume path in session-manager.ts"
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Phase 1 — Fleece issue + branch rename.
2. Phase 2 — land the helper skeleton + JSON Schema test harness + fixture builder.
3. Phase 3 — write all US1 tests, observe failing, implement, observe green.
4. **STOP and VALIDATE** — run coverage; run Pre-PR Quality Gate; operationally verify in mock mode via quickstart §3.
5. Open the PR. This alone satisfies SC-001 and SC-002.

### Incremental Delivery

1. Ship US1 (MVP) as one PR — answers "did skill X load for session Y?".
2. Ship US2 as a follow-up PR — adds the `origin=...` field on `canUseTool` lines.
3. Ship US3 as a follow-up PR — adds the boot-time inventory line.

Each PR independently passes the Pre-PR Quality Gate and each bumps coverage without regression.

### Parallel Team Strategy

Given this is a worker-only observability feature, a single developer can ship all three stories. If splitting:

1. Dev A: Phase 1 + Phase 2, then US1 through to merge.
2. Dev B: picks up US2 tests against mocked init during US1 work (unit tests only; cannot wire in until US1 merges `session.init` state on `WorkerSession`).
3. Dev A or B: US3 last.

---

## Notes

- This is a worker-only feature. `dotnet test`, `npm run generate:api:fetch`, `npm test` (web), and `npm run test:e2e` (web) are run but are expected to be no-ops apart from the worker's own Vitest suite.
- No server, web, shared DTO, or infra changes. `src/Homespun.Web/src/api/generated/` MUST NOT change (Constitution §III).
- Never kill `homespun*` containers or `mock.sh`/`dotnet` shells (Constitution §X). Restart only the worker process directly when iterating.
- Logs MUST be queried via Loki (`http://homespun-loki:3100`) per Constitution §XI — no invented file paths in operator instructions.
- Commit message suffix uses the PR number once opened: `feat(worker): log available skills and plugins per session (#NN)`.
- Every test task must be observed failing before its paired implementation task is started (Constitution §I).
- `[P]` = different files, or different test cases — safe to parallelise. `[Story]` = traceability to the spec's user stories.
