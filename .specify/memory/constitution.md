# Homespun Constitution

> Homespun is a web application for managing development features and AI agents.
> It provides project and feature management with hierarchical tree visualization,
> Git clone integration for isolated feature development, GitHub PR synchronization,
> and Claude Code agent orchestration.
>
> The system is composed of: an ASP.NET Core API + SignalR backend (`src/Homespun.Server`),
> a React 19 + Vite SPA (`src/Homespun.Web`), a Node + Hono Claude Agent SDK worker
> (`src/Homespun.Worker`), shared DTO/contract assemblies (`src/Homespun.Shared`),
> and .NET Aspire orchestration (`src/Homespun.AppHost`, `src/Homespun.ServiceDefaults`).
> Persistence is SQLite plus Fleece JSONL files. Infrastructure is Bicep on Azure
> Container Apps and an Azure VM, with Komodo for redeploys.

## Core Principles

### I. Test-First Development (NON-NEGOTIABLE)

TDD is mandatory for every change — feature, bugfix, or refactor. The Red → Green →
Refactor cycle is enforced:

1. Write a failing test that names the scenario and expected outcome.
2. Write the minimum production code to make it pass.
3. Refactor with tests green.

Test names MUST describe scenario + expected outcome
(e.g. `it('returns error when project not found')`). Backend tests live in
`tests/Homespun.Tests` (NUnit + Moq) and `tests/Homespun.Api.Tests`
(WebApplicationFactory). Frontend unit tests are co-located as `*.test.ts(x)` next
to the source they cover. Worker tests live in `tests/Homespun.Worker`. End-to-end
tests are Playwright specs under `src/Homespun.Web/e2e/`.

### II. Vertical Slice Architecture

Code is organised by feature, not by technical layer.

- Server slices live under `src/Homespun.Server/Features/<SliceName>/` (e.g.
  `AgentOrchestration`, `ClaudeCode`, `Commands`, `Containers`, `Fleece`, `Git`,
  `GitHub`, `Gitgraph`, `Navigation`, `Notifications`, `Observability`, `Plans`,
  `Projects`, `PullRequests`, `Search`, `Secrets`, `Settings`, `Shared`, `SignalR`,
  `Testing`, `Workflows`).
- Web slices live under `src/Homespun.Web/src/features/<slice-name>/` and expose
  their public surface via the slice's `index.ts`.
- A new feature SHOULD add or extend a slice rather than scatter logic across
  unrelated layers. Cross-slice utilities go in `Features/Shared` (server) or
  `src/components`, `src/hooks`, `src/lib`, `src/stores` (web).

### III. Shared Contract Discipline

Cross-process types (server ↔ web ↔ worker) MUST NOT be hand-duplicated.

- .NET DTOs and SignalR hub interfaces shared between projects belong in
  `src/Homespun.Shared`.
- The web client's API surface is generated from the server's OpenAPI spec into
  `src/Homespun.Web/src/api/generated/`. This directory is generated output —
  never hand-edit it. After any server API change, run
  `npm run generate:api:fetch` and commit the regenerated client.

### IV. Pre-PR Quality Gate (NON-NEGOTIABLE)

Before opening a pull request, all of the following MUST pass locally, in this
order:

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

CI runs the same checks; a failing local gate means a failing PR.

### V. Test Coverage — Delta Floor + Ratchet

Current baseline coverage is low, so absolute floors would penalise work in the
most test-starved areas. Coverage is instead enforced two ways:

1. **Delta floor (per PR).** The PR's own added and changed lines MUST reach at
   least **80%** line coverage (patch coverage / `diff-cover`-style
   measurement), computed per module against the merge base:
   - `src/Homespun.Server` — Coverlet via `dotnet test --collect:"XPlat Code Coverage"` → Cobertura → `diff-cover`
   - `src/Homespun.Web` — Vitest LCOV from `npm run test:coverage` → `diff-cover`
   - `src/Homespun.Worker` — Vitest LCOV → `diff-cover`

2. **Ratchet (no regression).** Overall module coverage MUST NOT decrease
   relative to `main`. Coverage trends up monotonically; once a module reaches
   a new high, that number becomes the new floor.

**Dated absolute targets.** The ratchet has a destination. Overall line
coverage per module MUST reach:

| Deadline         | Target |
|------------------|--------|
| 30 June 2026     | **60%** |
| 30 September 2026 | **80%** |

Missing a dated target is a release blocker that MUST be addressed before any
non-trivial feature work continues in the affected module. Once 80% is reached
per module, the rule collapses to "delta ≥ 80% + no regression" in perpetuity.

A PR that fails the delta floor or breaks the ratchet MUST either fix coverage
in the same PR or be justified explicitly under the plan's *Complexity
Tracking* section.

### VI. Fleece-Driven Workflow

Every unit of work has a Fleece issue. Status transitions track real progress:

```text
open → progress → review → complete
                         ↘ archived | closed
```

Required practice:

- Start work: `fleece edit <id> -s progress`.
- Before opening a PR: `fleece edit <id> -s review --linked-pr <pr-number>`,
  then commit `.fleece/` changes alongside the code (or use `fleece commit --ci`).
- Complete: `fleece edit <id> -s complete`.
- Resolve `.fleece/` merge conflicts with `fleece merge` — never delete them
  manually.

### VII. Conventional Commits + PR Suffix

Commit messages MUST follow Conventional Commits, with the merging PR number
appended:

```text
type(scope?): short imperative summary (#NN)
```

Permitted types: `feat`, `fix`, `refactor`, `test`, `chore`, `docs`, `revert`.
Branch names MUST match one of: `feature/*`, `feat/*`, `fix/*`, `bug/*`,
`task/*`, `chore/*`, `docs/*`. Fleece-linked branches keep the trailing
`+<fleece-id>` slug.

### VIII. Naming Conventions

- C# files, types, namespaces: PascalCase. Project root namespace is `Homespun`.
- Web feature folders: kebab-case (e.g. `pull-requests`, `issues-agent`).
- Web source files follow ecosystem norms (`PascalCase.tsx` for components,
  `camelCase.ts` for modules, `kebab-case` for routes).
- Tests: backend mirrors source project layout; frontend co-locates
  `Foo.test.tsx` next to `Foo.tsx`.
- Line endings: LF, with a final newline (enforced by `.editorconfig`).

### IX. Fleece.Core ↔ Fleece.Cli Version Sync

When the `Fleece.Core` NuGet package version is changed in
`src/Homespun.Server/Homespun.Server.csproj` and
`src/Homespun.Shared/Homespun.Shared.csproj`, the `Fleece.Cli` version installed
in `Dockerfile.base` MUST be bumped to match in the same PR. A drift between
runtime and CLI versions has previously caused production breakage and is a
release blocker.

### X. Container and Mock-Shell Safety

The following actions are forbidden during automated or interactive work:

- Stopping or removing the `homespun` or `homespun-prod` containers.
- `KillShell` (or equivalent) on a shell running `mock.sh` / `mock.ps1` or any
  `dotnet` process — doing so can terminate the entire session.

To restart a stuck mock server: stop the dotnet process directly with
`pkill -f "dotnet.*mock"`, then start a fresh `./scripts/mock.sh &`.

### XI. Logs via Loki

Application logs MUST be queried from the project's Loki instance at
`http://homespun-loki:3100` (use the `/logs` skill for LogQL queries). Ad-hoc
log file paths must not be invented or assumed; if the required log isn't in
Loki, fix the logging pipeline rather than working around it.

## Development Workflow

1. Pick or create a Fleece issue (`fleece next` for the work queue).
2. Move the issue to `progress` and create a branch using the naming rule above.
3. Write failing tests for the change (Principle I).
4. Implement until tests pass; refactor with tests green.
5. Run the full Pre-PR Quality Gate (Principle IV).
6. Verify coverage stays ≥ 80% for the affected module (Principle V).
7. Commit using Conventional Commits (Principle VII), including `.fleece/`
   changes.
8. Move the issue to `review`, link the PR, open the PR.
9. After merge, move the issue to `complete`.

Detailed runtime guidance for agents (mock servers, Playwright MCP, log skill,
shadcn/ui usage) lives in the root `CLAUDE.md` and `src/Homespun.Web/CLAUDE.md`.
This constitution governs *what* must be true; `CLAUDE.md` documents *how* to
operate the local toolchain.

## Governance

This constitution supersedes ad-hoc practice. All PRs and reviews MUST verify
compliance with the principles above. Violations require either a fix in the
same PR or an explicit justification recorded under *Complexity Tracking* in
the relevant `plan.md`.

Amendments require:

1. A PR that updates this file with the proposed change.
2. A `MAJOR.MINOR.PATCH` version bump:
   - **MAJOR** — backward-incompatible removal or redefinition of a principle.
   - **MINOR** — new principle or materially expanded guidance.
   - **PATCH** — clarifications, typo fixes, non-semantic edits.
3. Updates to dependent templates in `.specify/templates/` where the change
   affects required sections or gates.

**Version**: 1.0.0 | **Ratified**: 2026-04-14 | **Last Amended**: 2026-04-14
