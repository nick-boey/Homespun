# Implementation Plan: Worker Skills & Plugins Logging

**Branch**: `001-worker-skills-logging` | **Date**: 2026-04-15 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/001-worker-skills-logging/spec.md`

## Summary

The worker must emit a single structured `info`-level log record — one per session-lifecycle event (create, resume, boot) — enumerating six Claude Agent resource categories (skills, plugins, slash commands, sub-agents, hooks, MCP servers) that are available to the SDK session. The record form is a human-readable prefix followed by a compact JSON payload (`inventory event=... sessionId=... payload={...}`), so it is tail-friendly and LogQL-parseable via `| json`.

**Technical approach**: The `@anthropic-ai/claude-agent-sdk` already emits a `system`/`init` message as the first SDK message of every session — that message contains `skills`, `slash_commands`, `mcp_servers`, `plugins`, `agents` (subagents), `cwd`, `session_id`, `permissionMode` and `model`. We consume that init message as the canonical runtime source of truth, augment it with `hooks` (derived from the `options.hooks` we pass to `query()`, plus a best-effort filesystem scan of `~/.claude/hooks/` and `<cwd>/.claude/hooks/` when `settingSources` includes `user`/`project`), and emit the inventory log record. Resume re-emits the same record tagged `event=resume`. Boot runs a lightweight version against the default working directory. All inventory assembly is extracted to a new helper `src/Homespun.Worker/src/services/session-inventory.ts` so `session-manager.ts` stays focused.

## Technical Context

**Language/Version**: TypeScript 5.9 + Node (worker only — no server, web, or shared changes)
**Primary Dependencies**:
- Worker: `@anthropic-ai/claude-agent-sdk` (already a dependency — no version bump required), existing `src/Homespun.Worker/src/utils/logger.ts` helpers (`info`, `warn`).
- No new dependencies. No new MCP servers. No new routes.
**Storage**: N/A — feature is observability-only. No SQLite, no Fleece schema change.
**Testing**: Vitest (worker) — tests live in `tests/Homespun.Worker/services/`. Existing `session-manager-logging.test.ts` is the natural sibling for new tests. A new `tests/Homespun.Worker/services/session-inventory.test.ts` will cover the helper in isolation.
**Target Platform**: Linux worker container (existing `Dockerfile` in `src/Homespun.Worker/`). Log output goes through stdout → Loki via the existing Promtail pipeline.
**Project Type**: Multi-module monorepo; this feature is scoped to the worker module only.
**Performance Goals**: Adding one `info` log per session create / resume / boot event MUST add no more than ~50 ms to session startup (SC-005). Inventory assembly happens in-process from already-in-memory data (SDK init message + options object + at most two directory reads for hooks).
**Constraints**:
- No secret material in log output (FR-010) — the `env` map passed into `query()` currently includes `GITHUB_TOKEN`/`GH_TOKEN`; the inventory emitter MUST NOT include `env`, `options.env`, or plugin/MCP `env` values. Only names, transports, paths (relative to repo or home), scopes and enabled/status flags are loggable.
- Single structured record per event (FR-007). The JSON payload field names are the stable contract (FR-008) captured in `contracts/inventory-log-record.schema.json`.
- Discovery failure MUST NOT fail session creation (FR-006): all filesystem and SDK reads are wrapped and, on error, a separate `warn`-level log line is emitted and the inventory record is still emitted (with the failing category filled with what was successfully gathered, plus a `discoveryErrors` field listing the failures).
**Scale/Scope**:
- Slices touched: Worker only (`src/Homespun.Worker/src/services/session-manager.ts` — modified; `src/Homespun.Worker/src/services/session-inventory.ts` — new; `src/Homespun.Worker/src/index.ts` — modified for Story 3 boot log).
- Expected resource counts per session: typically <100 skills, <20 slash commands, 0–5 plugins, 0–5 sub-agents, 0–20 hooks, 1–3 MCP servers. Record size bounded to a few KB.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

The plan MUST satisfy every rule in `.specify/memory/constitution.md` (v1.1.0).

| # | Gate | Pass? | Notes |
|---|------|-------|-------|
| I    | Test-First — failing tests will be written before production code | [x] | Vitest tests for `session-inventory.ts` and new assertions in `session-manager-logging.test.ts` are written first per story. Mocks for the SDK `init` message and for `fs.readdir` ensure failures surface before implementation. |
| II   | Vertical Slice Architecture — change is scoped to identified slice(s) | [x] | Worker slice only (`src/Homespun.Worker/src/services/` + `src/Homespun.Worker/src/index.ts`). No cross-slice leakage; no server/web/shared changes. |
| III  | Shared Contract Discipline — DTOs in `Homespun.Shared`; OpenAPI client regenerated, not hand-edited | [x] N/A | Feature does not cross any process boundary. Log record schema lives next to the spec as a contract artefact, not in `Homespun.Shared`. |
| IV   | Pre-PR Quality Gate — `dotnet test`, `npm run lint:fix`, `npm run format:check`, `npm run generate:api:fetch`, `npm run typecheck`, `npm test`, `npm run test:e2e` will all pass | [x] | No server changes means `generate:api:fetch` is a no-op; all other checks run as usual. Worker Vitest suite is the primary test runner for this change. |
| V    | Coverage — delta ≥ 80% on changed lines AND overall module coverage does not decrease vs `main` (ratchet) | [x] | `session-inventory.ts` is new — fully test-covered. Added lines in `session-manager.ts` and `index.ts` are covered by new logging tests. No existing lines are removed. |
| VI   | Fleece-Driven Workflow — issue exists, status will move open→progress→review→complete; `.fleece/` committed | [ ] | **Outstanding as of this plan draft** — the feature currently has a spec-kit branch/spec but no corresponding Fleece issue. Tasks phase (`/speckit.tasks`) or a dedicated `fleece create` step will create the issue and move it to `progress` before any code commit. Flagged so it is not forgotten. |
| VII  | Conventional Commits + PR suffix; allowed branch prefix used | [x] | Current branch `001-worker-skills-logging` was created by the speckit git hook. It does not match the constitution's allowed prefixes (`feature/*`, `feat/*`, ...). Before the PR is opened, the working branch MUST be renamed/rebased onto a conforming prefix (e.g. `feat/worker-skills-logging+<fleece-id>`). Commits will follow Conventional Commits (`feat(worker): ...`). |
| VIII | Naming — PascalCase (C#) / kebab-case (web feature folders) / co-located tests | [x] | Worker is camelCase/kebab-case. New file `session-inventory.ts` follows the existing `session-manager.ts` / `session-discovery.ts` pattern. Tests mirror source paths under `tests/Homespun.Worker/services/`. |
| IX   | Fleece.Core ↔ Fleece.Cli versions stay in sync | [x] N/A | Not bumping `Fleece.Core`. |
| X    | Container & mock-shell safety preserved | [x] | Feature adds logging only; no container restarts, no process kills. |
| XI   | Logs queried via Loki (`http://homespun-loki:3100`), not invented paths | [x] | Verification of the new log record is documented via LogQL queries (see `quickstart.md`). No ad-hoc log file paths are introduced. |

Two items need explicit tracking before PR: **VI** (Fleece issue creation — belongs in tasks phase) and **VII** (branch rename to a constitution-compliant prefix — a mechanical pre-PR step). Neither is a design-level violation; both are workflow steps. Captured as tasks, not as *Complexity Tracking* entries.

## Project Structure

### Documentation (this feature)

```text
specs/001-worker-skills-logging/
├── plan.md                                # This file
├── research.md                            # Phase 0 output
├── data-model.md                          # Phase 1 output
├── quickstart.md                          # Phase 1 output
├── contracts/
│   └── inventory-log-record.schema.json   # Phase 1 output — JSON Schema for the log payload
└── checklists/
    └── requirements.md                    # Specification quality checklist (from /speckit.specify + /speckit.clarify)
```

### Source Code (repository root)

Only the worker slice is touched.

```text
src/
└── Homespun.Worker/
    └── src/
        ├── index.ts                       # MODIFIED — emit Story 3 boot inventory log after HTTP server reports ready
        └── services/
            ├── session-manager.ts         # MODIFIED — call inventory emitter on create + resume paths; enrich canUseTool log with origin
            └── session-inventory.ts       # NEW — inventory assembly + emission; secret-scrubbing; hooks discovery fallback

tests/
└── Homespun.Worker/
    └── services/
        ├── session-manager-logging.test.ts  # MODIFIED — new cases for create/resume inventory emission and canUseTool origin
        └── session-inventory.test.ts        # NEW — unit tests for inventory assembly (init-message parsing, hooks scan, secret scrubbing, empty-list guarantees, error fallback)
```

**Structure Decision**: Single new worker helper plus focused edits to two existing worker files. Zero changes in `src/Homespun.Server/`, `src/Homespun.Web/`, `src/Homespun.Shared/`, `Homespun.AppHost/`, `tests/Homespun.Tests/`, `tests/Homespun.Api.Tests/`, `infra/`, `docker-compose.yml`, `Dockerfile`, `Dockerfile.base`.

## Complexity Tracking

No constitution gate was recorded as failed. The two items flagged under gates VI and VII are workflow steps (Fleece issue creation and branch rename before PR), not architectural complexity. They are tracked as tasks in `/speckit.tasks` rather than under this section.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| _None_    | _N/A_      | _N/A_                               |

---

## Phase 0 — Outline & Research

See [research.md](./research.md) for the detailed findings and decisions. All items that could have been marked NEEDS CLARIFICATION in Technical Context have been resolved through reading the SDK type definitions and the existing worker code; no open unknowns remain at entry to Phase 1.

## Phase 1 — Design & Contracts

**Prerequisites**: research.md complete ✅

Generated artefacts:

1. [data-model.md](./data-model.md) — entity schema for `ResourceInventoryEntry` and the composite `SessionInventoryLogRecord`, including the six category lists and the `discoveryErrors` field.
2. [contracts/inventory-log-record.schema.json](./contracts/inventory-log-record.schema.json) — JSON Schema for the payload object emitted in the log line. This is the stable contract surfaced by FR-008; changes to it require a spec update.
3. [quickstart.md](./quickstart.md) — operator-facing how-to: what the log looks like, which LogQL queries answer the SC-001 question, how to reproduce in mock mode.
4. Agent context — `update-agent-context.ps1` is not run here (no `pwsh` on this host and the change introduces no new top-level technology for `CLAUDE.md`). A note is recorded so the next `/speckit.tasks` or manual run picks it up if the user wants.

### Constitution Re-Check (post-design)

Re-evaluated every gate after writing the contract + data model. No design-level change since Phase 0 entry; gates VI and VII remain tracked as workflow tasks and all other gates continue to pass.

---

## Stop — Ready for `/speckit.tasks`

Planning ends here. Task generation will consume this plan plus `data-model.md` + `contracts/` + `quickstart.md` and produce `tasks.md`.
