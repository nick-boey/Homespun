# Implementation Plan: Prompts

**Branch**: n/a (pre-spec-kit; built on `main` over many PRs)  |  **Date**: 2026-04-14 (migrated)  |  **Spec**: [`./spec.md`](./spec.md)
**Status**: Migrated — describes the as-built implementation, not a future design.

## Summary

Maintain a two-tier catalogue of Claude prompt templates: 13 seeded globals (auto-loaded from `default-prompts.json` at startup) plus per-project prompts and explicit project-over-global overrides. Expose 13 HTTP endpoints under `/api/agent-prompts` for CRUD, overrides, issue-agent variants, and defaults management. Render templates with a minimal `{{placeholder}}` + `{{#if}}` engine consumed by the `agent-dispatch` pipeline. Ship a React UI with a per-card form plus a bulk JSON code-editor that computes a diff and applies operations one by one.

## Technical Context

**Language/Version**: C# / .NET 10 (server + shared), TypeScript 5.9 + React 19 (web).

**Primary Dependencies**:
- Server: ASP.NET Core controllers, `IHostedService` for default seeding, `JsonDataStore` (file-backed JSON persistence shared with the PullRequests slice), embedded resource loading for `default-prompts.json`.
- Web: TanStack Query (13 hooks — 5 queries, 8 mutations, one applicator), shadcn/ui form + dialog primitives, a Monaco/CodeMirror-style JSON editor for the bulk-edit flow, a small custom diff utility in `utils/prompt-diff.ts`.

**Storage**: JSON file — the `AgentPrompt*` methods live on `IDataStore` / `JsonDataStore` in `Features/PullRequests/Data/` (historical: one persistence file per process). No SQL schema; no migrations. The seed catalogue is an embedded JSON resource.

**Testing**: Vitest + RTL (web) with dense hook + component coverage (14 test files). **Server-side tests are absent** for both service and controller (GP-1); mock test doubles exist (`Features/Testing/MockDataStore.cs`, `MockDataSeederService.cs`) but no direct `AgentPromptService` unit tests and no API tests hit the controller.

**Target Platform**: Linux containers — no platform-specific code beyond file I/O on the JSON persistence path.

**Project Type**: Pure CRUD slice with a rendering primitive. No SignalR, no background dispatch, no long-running operations.

**Performance Goals**:
- `GET /api/agent-prompts/available-for-project/{id}` completes well under a single request budget even with ~100 total prompts — the merge is O(N) over the union and happens in-memory.
- `RenderTemplate` is O(k) over placeholder count, not over template length; no template is rendered more than twice per dispatch.

**Constraints**:
- Cross-process DTOs SHOULD live in `src/Homespun.Shared/Requests/` (Constitution §III). **They currently don't** — `CreateAgentPromptRequest` et al. are nested inside the controller file. See Complexity Tracking.
- Default prompt seed MUST stay stable: deploying a new version that changes the seed JSON affects every user on next restart (via `ensure-defaults`), and `restore-defaults` is explicitly destructive.
- Prompt mutations are not broadcast over SignalR; relying on TanStack Query invalidation in the initiating client only.

**Scale/Scope**:
- Server slice: 4 C# files (controller + service pair + init service + `DefaultPromptDefinition`) + 1 embedded resource, ~800 LOC.
- Web slice: 7 components + 13 hooks + 1 utility under `features/prompts/`, ~2,000 LOC production + ~2,000 LOC test.
- Shared models: `AgentPrompt`, `PromptContext`, + three enums in `src/Homespun.Shared/Models/Sessions/`.
- Routes: `routes/prompts.tsx` (global), `routes/projects.$projectId.prompts.tsx` (project-scoped).

## Constitution Check

*Retrospective check for the as-built feature. Any box unchecked is called out under **Complexity Tracking** with a remediation note.*

| # | Gate | Pass? | Notes |
|---|------|-------|-------|
| I    | Test-First — failing tests written before production code | [~] | Strong web-side coverage (14 co-located tests). **Zero direct server tests** for `AgentPromptService` / `AgentPromptsController` (GP-1). Historic TDD cannot be reconstructed; forward work on this slice MUST follow the rule. |
| II   | Vertical Slice Architecture — change scoped to identified slice(s) | [~] | Server code lives under `Features/ClaudeCode/` (controller, services, resources) which is the correct slice today; the data-store methods live in `Features/PullRequests/Data/` for historical persistence-root reasons (assumption A-2). Pragmatic but not ideal. |
| III  | Shared Contract Discipline — DTOs in `Homespun.Shared`; OpenAPI client regenerated, not hand-edited | [~] | **FAIL**: `CreateAgentPromptRequest`, `UpdateAgentPromptRequest`, `CreateOverrideRequest` are nested inside the controller file. The web client consumes them via the generated OpenAPI (which is fine because the generator picks them up), but any direct C# consumer would need to reference `Homespun.Server`. GP-2. |
| IV   | Pre-PR Quality Gate — `dotnet test`, `npm run lint:fix`, `format:check`, `generate:api:fetch`, `typecheck`, `test`, `test:e2e` all pass | [~] | Historic PRs ran the gate. `test:e2e` has no prompt-specific coverage. `dotnet test` doesn't cover this slice directly (GP-1). |
| V    | Coverage — delta ≥ 80% on changed lines AND no regression vs `main`; on track for 60%/2026-06-30 and 80%/2026-09-30 | [~] | Web side: production-to-test LOC ratio ≈ 1:1. Server side: 0% direct coverage — relies on in-memory tests via `JsonDataStore` covered by other slices. Drags the weighted coverage average down. |
| VI   | Fleece-Driven Workflow — issue exists, status transitions, `.fleece/` committed | [n/a] | Slice predates the workflow. Follow-ups drafted in `follow-up-issues.md`. |
| VII  | Conventional Commits + PR suffix; allowed branch prefix | [x] | Observed in history. |
| VIII | Naming — PascalCase (C#) / kebab-case (web feature folders) / co-located tests | [x] | Observed. One exception: `use-remove-override.ts` lacks a co-located test (GP-7). |
| IX   | Fleece.Core ↔ Fleece.Cli version sync | [n/a] | Feature does not touch Fleece. |
| X    | Container & mock-shell safety preserved | [x] | Pure CRUD; does not target any container. |
| XI   | Logs queried via Loki | [x] | Default logging pipeline → Loki. |

## Project Structure

```
src/Homespun.Server/Features/ClaudeCode/
├── Controllers/
│   └── AgentPromptsController.cs           # 13 endpoints under /api/agent-prompts
├── Services/
│   ├── IAgentPromptService.cs
│   ├── AgentPromptService.cs               # CRUD + merge + RenderTemplate + overrides
│   ├── DefaultPromptDefinition.cs          # POCO for seed JSON rows
│   └── DefaultPromptsInitializationService.cs   # IHostedService; runs ensure-defaults at boot
└── Resources/
    └── default-prompts.json                # 13 seed prompts (10 Standard, 3 IssueAgent)

src/Homespun.Server/Features/PullRequests/Data/        # ← persistence root (historical)
├── IDataStore.cs                           # AgentPrompt* methods (lines ~96–128)
└── JsonDataStore.cs                        # AgentPrompt* implementations

src/Homespun.Web/src/features/prompts/
├── components/
│   ├── prompts-list.tsx                    # main container (list / edit / code-view)
│   ├── prompt-card.tsx                     # card + edit/delete actions
│   ├── prompt-card-skeleton.tsx
│   ├── prompt-form.tsx                     # create/edit form
│   ├── prompts-code-editor.tsx             # bulk JSON editor w/ diff
│   ├── issue-agent-prompts-section.tsx     # IssueAgent-category section
│   ├── prompts-empty-state.tsx
│   └── index.ts
├── hooks/
│   ├── use-global-prompts.ts
│   ├── use-project-prompts.ts
│   ├── use-merged-project-prompts.ts
│   ├── use-issue-agent-prompts.ts
│   ├── use-issue-agent-project-prompts.ts
│   ├── use-create-prompt.ts
│   ├── use-update-prompt.ts
│   ├── use-delete-prompt.ts
│   ├── use-create-override.ts
│   ├── use-remove-override.ts              # no co-located test (GP-7)
│   ├── use-restore-default-prompts.ts
│   ├── use-delete-all-project-prompts.ts
│   ├── use-apply-prompt-changes.ts         # applies bulk diff ops
│   └── index.ts
├── utils/
│   ├── prompt-diff.ts                      # serialize / parse / calculate diff
│   └── index.ts
└── index.ts

src/Homespun.Web/src/routes/
├── prompts.tsx                             # /prompts — global list
└── projects.$projectId.prompts.tsx         # /projects/{id}/prompts

src/Homespun.Shared/Models/Sessions/
├── AgentPrompt.cs                          # Name, InitialMessage, Mode, ProjectId, Category, SessionType, dates, IsOverride (computed)
├── PromptContext.cs                        # render context: Title, Id, Description, Branch, Type, Context, SelectedIssueId, UserPrompt
├── (enums: SessionMode, SessionType, PromptCategory)
```

## Architecture Notes

### Merge logic (US1)

```
GET /api/agent-prompts/available-for-project/{projectId}
  └── AgentPromptService.GetAvailableForProjectAsync
        ├── load globals (projectId == null)
        ├── load project prompts (projectId == {id})
        ├── for each project prompt, set IsOverride = (global with matching Name exists)
        ├── build merged dict keyed by Name: project prompt wins
        └── return merged values filtered by Category.Standard (non-null SessionType excluded)
```

### Template rendering (consumed by agent-dispatch)

```
AgentPromptService.RenderTemplate(template, PromptContext)
  1. pass 1 — conditionals:
       regex /\{\{#if (\w+)\}\}([\s\S]*?)\{\{\/if\}\}/
       for each match: if context[name] is null/whitespace → remove match; else → replace match with inner
  2. pass 2 — simple placeholders:
       regex /\{\{(\w+)\}\}/
       for each match: replace with context[name] (case-insensitive lookup)
```

### Bulk code-editor flow (US4)

```
<PromptsCodeEditor> → prompt-diff.ts:
    serializePrompts(list)          → JSON text
    user edits                       → new JSON text
    parsePrompts(newText)           → new list
    calculateDiff(old, new)         → [{ op: 'create' | 'update' | 'delete', prompt }]
useApplyPromptChanges(ops) → dispatches useCreatePrompt / useUpdatePrompt / useDeletePrompt sequentially,
                             returning the first failure or { ok: true } if all succeed.
```

### Initialization sequence

```
Program.cs → AddHostedService<DefaultPromptsInitializationService>()
  Startup: read Resources/default-prompts.json via Assembly.GetManifestResourceStream (or IFileProvider)
         → deserialize to DefaultPromptDefinition[]
         → for each, call AgentPromptService.CreateAsync if missing
         → log summary
```

## Complexity Tracking

| Area | Why it's complex | Current mitigation |
|------|------------------|--------------------|
| DTOs nested in controller file (GP-2) | Historic — the contract grew alongside the controller before the shared-contracts discipline was formalised. Moving now is straightforward but touches OpenAPI output. | Documented as GP-2 / FI-2. Web uses the generated client, which masks the issue. |
| Data-store methods in a different slice (PullRequests) | The JSON persistence file is global; one slice has to own it. | Documented in spec assumption A-2. Move when persistence is split. |
| 13 endpoints on one controller | CRUD + overrides + defaults + issue-agent variants need distinct endpoints for the web client's ergonomics. | No plan to consolidate — each endpoint has a specific caller. |
| Seed-vs-edit tension (`ensure-defaults` vs `restore-defaults`) | Needed both "idempotent repair" and "force reset" flavours; the latter is destructive by design. | Documented in FR-009, FR-010, and A-3. |

## Identified Gaps (mirror of `spec.md` → Identified Gaps)

- **GP-1**: No server-side unit or API tests for the service/controller — FI-1.
- **GP-2**: Request DTOs live in the controller file instead of `Homespun.Shared/Requests/` — FI-2.
- **GP-3**: `UpdateAsync` silently drops `Category` — FI-3.
- **GP-4**: No input-sanitisation review of `InitialMessage` — FI-4 (document decision, not code change).
- **GP-5**: No SignalR invalidation for prompt mutations — FI-5.
- **GP-6**: Hard delete only; no undo / audit trail — FI-6.
- **GP-7**: Missing route-level tests + `use-remove-override.ts` test — FI-7.

See `follow-up-issues.md` for draft Fleece stubs.
