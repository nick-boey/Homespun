# Implementation Plan: [FEATURE]

**Branch**: `[###-feature-name]` | **Date**: [DATE] | **Spec**: [link]
**Input**: Feature specification from `/specs/[###-feature-name]/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

[Extract from feature spec: primary requirement + technical approach from research]

## Technical Context

<!--
  Defaults below are the Homespun stack as of the constitution v1.0.0. Adjust ONLY
  the fields where this feature deviates (e.g. a new dependency, new scale target).
  Mark anything genuinely unknown as NEEDS CLARIFICATION.
-->

**Language/Version**: C# / .NET 10 (server, shared, AppHost), TypeScript 5.9 + React 19 (web), TypeScript + Node (worker)
**Primary Dependencies**:
- Server: ASP.NET Core, SignalR, Swashbuckle, Octokit, Markdig, `Microsoft.Data.Sqlite`, `Fleece.Core`, A2A, AGUI
- Web: Vite, TailwindCSS v4, TanStack Router/Query, Zustand, shadcn/ui (new-york / zinc) + Radix, prompt-kit, Konva, Mermaid, Shiki, Zod, react-hook-form, `@microsoft/signalr`, `@hey-api/openapi-ts`
- Worker: Hono, `@anthropic-ai/claude-agent-sdk`, `@a2a-js/sdk`
**Storage**: SQLite (`Microsoft.Data.Sqlite`) + Fleece JSONL files under `.fleece/`
**Testing**: NUnit + Moq (server unit), WebApplicationFactory (server API), Vitest + React Testing Library (web unit), Playwright (web e2e), Vitest (worker)
**Target Platform**: Linux containers on Azure Container Apps + an Azure VM, orchestrated via .NET Aspire locally and Komodo in production. Browser target: evergreen Chromium / Firefox / Safari.
**Project Type**: Multi-module monorepo — ASP.NET API + React SPA + Node worker + shared contracts + Aspire host + Bicep infra
**Performance Goals**: [domain-specific, e.g., chat token-stream latency, graph render fps — fill if relevant, otherwise N/A]
**Constraints**: [feature-specific — e.g. memory budget for worker container, SignalR fan-out, Loki query cost — fill if relevant]
**Scale/Scope**: [feature-specific — number of slices touched, expected projects/issues at steady state]

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

The plan MUST satisfy every rule in `.specify/memory/constitution.md` (v1.0.0).
Tick each gate explicitly; any "No" requires a justification under
*Complexity Tracking*.

| # | Gate | Pass? |
|---|------|-------|
| I    | Test-First — failing tests will be written before production code | [ ] |
| II   | Vertical Slice Architecture — change is scoped to identified slice(s) | [ ] |
| III  | Shared Contract Discipline — DTOs in `Homespun.Shared`; OpenAPI client regenerated, not hand-edited | [ ] |
| IV   | Pre-PR Quality Gate — `dotnet test`, `npm run lint:fix`, `npm run format:check`, `npm run generate:api:fetch`, `npm run typecheck`, `npm test`, `npm run test:e2e` will all pass | [ ] |
| V    | Coverage — delta ≥ 80% on changed lines AND overall module coverage does not decrease vs `main` (ratchet). On track for the dated targets: 60% by 2026-06-30, 80% by 2026-09-30 | [ ] |
| VI   | Fleece-Driven Workflow — issue exists, status will move open→progress→review→complete; `.fleece/` committed | [ ] |
| VII  | Conventional Commits + PR suffix; allowed branch prefix used | [ ] |
| VIII | Naming — PascalCase (C#) / kebab-case (web feature folders) / co-located tests | [ ] |
| IX   | Fleece.Core ↔ Fleece.Cli versions stay in sync (only flag if bumping `Fleece.Core`) | [ ] / N/A |
| X    | Container & mock-shell safety preserved (no killing `homespun*` containers or `mock.sh`/`dotnet` shells) | [ ] |
| XI   | Logs queried via Loki (`http://homespun-loki:3100`), not invented paths | [ ] / N/A |

## Project Structure

### Documentation (this feature)

```text
specs/[###-feature]/
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output (/speckit.plan command)
├── data-model.md        # Phase 1 output (/speckit.plan command)
├── quickstart.md        # Phase 1 output (/speckit.plan command)
├── contracts/           # Phase 1 output (/speckit.plan command)
└── tasks.md             # Phase 2 output (/speckit.tasks command - NOT created by /speckit.plan)
```

### Source Code (repository root)

Homespun is a fixed monorepo layout. Reference the real paths your feature
touches; delete the lines that don't apply.

```text
src/
├── Homespun.Server/             # ASP.NET API + SignalR
│   └── Features/<Slice>/        # vertical slice you add or change
├── Homespun.Web/                # React 19 + Vite SPA
│   ├── src/features/<slice>/    # vertical slice you add or change
│   ├── src/api/generated/       # OpenAPI-generated client (DO NOT hand-edit)
│   ├── src/components/ui/       # shadcn/ui components
│   └── e2e/                     # Playwright specs
├── Homespun.Worker/             # Hono + Claude Agent SDK
│   └── src/{routes,services,tools,types,utils}/
├── Homespun.Shared/             # Cross-process DTOs / hub interfaces
├── Homespun.AppHost/            # .NET Aspire orchestration
└── Homespun.ServiceDefaults/    # Aspire telemetry / health defaults

tests/
├── Homespun.Tests/              # NUnit + Moq (server unit)
├── Homespun.Api.Tests/          # WebApplicationFactory (server API)
├── Homespun.AppHost.Tests/      # Aspire host tests
└── Homespun.Worker/             # Vitest (worker)

infra/                            # Bicep modules + cloud-init
scripts/                          # mock.sh / mock.ps1 / run / deploy / komodo
docs/                             # markdown docs + LikeC4 architecture model
.fleece/                          # Fleece JSONL issues (committed)
```

**Structure Decision**: List the exact files and folders this feature will add
or change, grouped by the slice table from `spec.md` § *Affected Slices*.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| [e.g., 4th project] | [current need] | [why 3 projects insufficient] |
| [e.g., Repository pattern] | [specific problem] | [why direct DB access insufficient] |
