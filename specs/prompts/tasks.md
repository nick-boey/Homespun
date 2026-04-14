---
description: "Retrospective task list for the migrated Prompts feature"
---

# Tasks: Prompts

**Input**: Design documents from `/specs/prompts/`
**Status**: Migrated — all in-scope tasks reflect work that is already complete. Gaps are left **unchecked** and tracked in `follow-up-issues.md`.

> **Migration semantics.** `[x]` marks observed as-built work. Unchecked items are real, remediable gaps — do not delete them. Task groups mirror the user-story structure of `spec.md` so the backlog remains coherent with the SDD workflow going forward.

## Path Conventions (Homespun)

| Concern | Path |
|---------|------|
| Server controller | `src/Homespun.Server/Features/ClaudeCode/Controllers/AgentPromptsController.cs` |
| Server services | `src/Homespun.Server/Features/ClaudeCode/Services/{IAgentPromptService,AgentPromptService,DefaultPromptsInitializationService,DefaultPromptDefinition}.cs` |
| Server seed data | `src/Homespun.Server/Features/ClaudeCode/Resources/default-prompts.json` |
| Server data access | `src/Homespun.Server/Features/PullRequests/Data/{IDataStore,JsonDataStore}.cs` (historical persistence root) |
| Shared models | `src/Homespun.Shared/Models/Sessions/{AgentPrompt,PromptContext}.cs` + enums |
| Shared request DTOs | **should be** `src/Homespun.Shared/Requests/` — currently nested in controller (GP-2) |
| Web slice | `src/Homespun.Web/src/features/prompts/{components,hooks,utils}/` |
| Web routes | `src/Homespun.Web/src/routes/{prompts,projects.$projectId.prompts}.tsx` |
| Web unit tests | co-located `*.test.ts(x)` next to the source |
| Server unit tests | **missing** — see GP-1 |

---

## Phase 1: Setup

- [x] T001 ClaudeCode slice already hosts prompts code — folders `Controllers/`, `Services/`, `Resources/` in place.
- [x] T002 Web slice scaffolding under `src/Homespun.Web/src/features/prompts/` with `components/`, `hooks/`, `utils/`, barrel `index.ts`.
- [x] T003 Routes registered: `routes/prompts.tsx` and `routes/projects.$projectId.prompts.tsx`.

---

## Phase 2: Foundational

- [x] T004 [P] Shared model `AgentPrompt` in `src/Homespun.Shared/Models/Sessions/AgentPrompt.cs` with `Name`, `InitialMessage`, `Mode`, `ProjectId`, `Category`, `SessionType`, `CreatedAt`, `UpdatedAt`, computed `IsOverride`.
- [x] T005 [P] Shared model `PromptContext` in `src/Homespun.Shared/Models/Sessions/PromptContext.cs` with `Title`, `Id`, `Description`, `Branch`, `Type`, `Context`, `SelectedIssueId`, `UserPrompt`.
- [x] T006 [P] Enums `SessionMode`, `SessionType`, `PromptCategory` in `src/Homespun.Shared/Models/Sessions/`.
- [x] T007 `AgentPrompt*` CRUD methods on `IDataStore` + `JsonDataStore` (in `Features/PullRequests/Data/`): `AddAgentPromptAsync`, `UpdateAgentPromptAsync`, `RemoveAgentPromptAsync`, `GetAgentPrompt`, `GetAgentPromptsByProject`.
- [x] T008 Seed catalogue `default-prompts.json` at `Features/ClaudeCode/Resources/` with 13 prompts (10 Standard + 3 IssueAgent).
- [ ] T009 **GP-2**: Move request DTOs (`CreateAgentPromptRequest`, `UpdateAgentPromptRequest`, `CreateOverrideRequest`) from the controller file to `src/Homespun.Shared/Requests/AgentPromptRequests.cs` — see FI-2.

---

## Phase 3: User Story 1 — List prompts for a project (P1) 🎯 MVP

**Goal**: The merged list of globals + project prompts (with overrides) is reachable and correct.

**Independent Test**: `GET /api/agent-prompts/available-for-project/{projectId}` returns merged list; `/projects/{id}/prompts` renders `<PromptsList>` via `useMergedProjectPrompts`; globals route `/prompts` renders via `useGlobalPrompts`.

### Backend

- [x] T010 `AgentPromptsController.GetAll` — `GET /api/agent-prompts` (globals only, Standard category).
- [x] T011 `AgentPromptsController.GetByProject` — `GET /api/agent-prompts/project/{projectId}`.
- [x] T012 `AgentPromptsController.GetAvailableForProject` — `GET /api/agent-prompts/available-for-project/{projectId}`.
- [x] T013 `AgentPromptsController.GetByName` — `GET /api/agent-prompts/by-name/{name}?projectId={id}`.
- [x] T014 `AgentPromptsController.GetIssueAgentPrompts` — `GET /api/agent-prompts/issue-agent-prompts`.
- [x] T015 `AgentPromptsController.GetIssueAgentPromptsForProject` — `GET /api/agent-prompts/issue-agent/available/{projectId}`.
- [x] T016 `AgentPromptService.GetAvailableForProjectAsync` — merge logic: project prompts win over globals by `Name`; `IsOverride` computed; `SessionType`-bearing prompts filtered out of standard lists.

### Web

- [x] T017 `<PromptsList>` component in `components/prompts-list.tsx` — supports `isGlobal` / `projectId` props, switches between list, edit-form, and code-editor modes.
- [x] T018 `<PromptCard>` component with edit/delete/override actions.
- [x] T019 `<PromptCardSkeleton>` loading placeholder.
- [x] T020 `<PromptsEmptyState>` with "Restore defaults" action.
- [x] T021 `<IssueAgentPromptsSection>` for the IssueAgent-category split.
- [x] T022 `useGlobalPrompts` hook — `GET /api/agent-prompts`.
- [x] T023 `useProjectPrompts` hook — `GET /api/agent-prompts/project/{projectId}`.
- [x] T024 `useMergedProjectPrompts` hook — `GET /api/agent-prompts/available-for-project/{projectId}`.
- [x] T025 `useIssueAgentPrompts` + `useIssueAgentProjectPrompts` hooks.

### Tests

- [x] T026 [P] `prompts-list.test.tsx`, `prompt-card.test.tsx`, `issue-agent-prompts-section.test.tsx`, `prompts-empty-state.test.tsx`.
- [x] T027 [P] `use-global-prompts.test.tsx`, `use-project-prompts.test.tsx`, `use-merged-project-prompts.test.tsx`, `use-issue-agent-project-prompts.test.tsx`.
- [ ] T028 **GP-1**: server-side unit tests for `AgentPromptService.GetAvailableForProjectAsync` covering override merge, `Name` dedupe, `SessionType` filtering, `IsOverride` flag — see FI-1.
- [ ] T029 **GP-1**: API tests for the four list endpoints via `HomespunWebApplicationFactory` — see FI-1.
- [ ] T030 **GP-7**: route-level component tests for `routes/prompts.tsx` and `routes/projects.$projectId.prompts.tsx` — see FI-7.

### Checkpoint

- [x] T031 US1 checkpoint: merged list renders correctly, overrides replace globals, skeletons + empty state reachable.

---

## Phase 4: User Story 2 — Create, edit, and delete a prompt (P1)

**Goal**: Full single-prompt CRUD through the per-card form.

### Backend

- [x] T032 `AgentPromptsController.Create` — `POST /api/agent-prompts`; service returns `Conflict` on duplicate `(Name, ProjectId)`.
- [x] T033 `AgentPromptsController.Update` — `PUT /api/agent-prompts/by-name/{name}?projectId={id}`.
- [x] T034 `AgentPromptsController.Delete` — `DELETE /api/agent-prompts/by-name/{name}?projectId={id}`.
- [x] T035 `AgentPromptService.CreateAsync` — validation, dedupe on `(Name, ProjectId)`, persist via `IDataStore.AddAgentPromptAsync`.
- [x] T036 `AgentPromptService.UpdateAsync` — mutate `InitialMessage`, `Mode`, `UpdatedAt`.
- [x] T037 `AgentPromptService.DeleteAsync` — remove via `IDataStore.RemoveAgentPromptAsync`; returns false if missing.
- [ ] T038 **GP-3**: `UpdateAsync` currently ignores `Category` — persist it (or reject updates that change it, with `400`) — see FI-3.

### Web

- [x] T039 `<PromptForm>` component with name/mode/category/template fields.
- [x] T040 `useCreatePrompt` mutation — invalidates `['prompts', ...]` keys on success, surfaces `409` inline.
- [x] T041 `useUpdatePrompt` mutation.
- [x] T042 `useDeletePrompt` mutation — card confirmation dialog enforces user intent.

### Tests

- [x] T043 [P] `prompt-form.test.tsx`.
- [x] T044 [P] `use-create-prompt.test.tsx`, `use-update-prompt.test.tsx`, `use-delete-prompt.test.tsx`.
- [ ] T045 **GP-1**: server-side service + controller tests for CreateAsync / UpdateAsync / DeleteAsync paths (happy + 404 + 409 + category-silent-drop) — see FI-1.

### Checkpoint

- [x] T046 US2 checkpoint: single-prompt CRUD is fully usable through the UI, invalidation works, duplicates surface inline.

---

## Phase 5: User Story 3 — Override a global prompt in a project (P2)

**Goal**: First-class project overrides via explicit endpoints.

### Backend

- [x] T047 `AgentPromptsController.CreateOverride` — `POST /api/agent-prompts/create-override`; body `{ globalPromptName, projectId, initialMessage? }`.
- [x] T048 `AgentPromptsController.RemoveOverride` — `DELETE /api/agent-prompts/by-name/{name}/override?projectId={id}`.
- [x] T049 `AgentPromptService.CreateOverrideAsync` — require matching global, reject duplicate override, seed `InitialMessage` from global when omitted, copy `Mode` + `Category` + `SessionType`.

### Web

- [x] T050 `useCreateOverride` mutation.
- [x] T051 `useRemoveOverride` mutation.
- [x] T052 Override UI wired into `<PromptCard>` (Override / Remove override actions).

### Tests

- [x] T053 [P] `use-create-override.test.tsx`.
- [ ] T054 **GP-7**: `use-remove-override.test.tsx` — currently absent; see FI-7.
- [ ] T055 **GP-1**: server-side tests for `CreateOverrideAsync` covering missing-global, duplicate-override, inheritance of Mode/Category/SessionType — see FI-1.

### Checkpoint

- [x] T056 US3 checkpoint: overrides replace globals in merged list, remove-override restores global, no cross-project leakage.

---

## Phase 6: User Story 4 — Bulk-edit prompts via code-editor with diff (P2)

**Goal**: Power-user JSON round-trip with diff-based apply.

### Web

- [x] T057 `<PromptsCodeEditor>` component — JSON editor with apply/reset.
- [x] T058 `utils/prompt-diff.ts` — `serializePrompts(list)`, `parsePrompts(text)`, `calculateDiff(old, new)`.
- [x] T059 `useApplyPromptChanges` — sequential execution of create/update/delete ops, returning the first failure.

### Tests

- [x] T060 [P] `prompts-code-editor.test.tsx`.
- [x] T061 [P] `prompt-diff.test.ts`.
- [x] T062 [P] `use-apply-prompt-changes.test.tsx`.

### Checkpoint

- [x] T063 US4 checkpoint: round-trip serialize → no edits → apply is a zero-op (no network calls).

---

## Phase 7: User Story 5 — Defaults, restore, delete-all (P3)

**Goal**: Operational bulk ops for seed management.

### Backend

- [x] T064 `AgentPromptsController.EnsureDefaults` — `POST /api/agent-prompts/ensure-defaults` — idempotent; creates missing seeds.
- [x] T065 `AgentPromptsController.RestoreDefaults` — `POST /api/agent-prompts/restore-defaults` — overwrites seed globals with `default-prompts.json`.
- [x] T066 `AgentPromptsController.DeleteAllProjectPrompts` — `DELETE /api/agent-prompts/project/{projectId}/all`.
- [x] T067 `AgentPromptService.EnsureDefaultsAsync` / `RestoreDefaultsAsync` / `DeleteAllProjectPromptsAsync`.
- [x] T068 `DefaultPromptsInitializationService` (IHostedService) — runs `EnsureDefaultsAsync` once at startup; logs summary.
- [x] T069 `DefaultPromptDefinition` POCO for deserialising seed rows.

### Web

- [x] T070 `useRestoreDefaultPrompts` mutation wired into empty-state action.
- [x] T071 `useDeleteAllProjectPrompts` mutation.

### Tests

- [x] T072 [P] `use-restore-default-prompts.test.tsx`, `use-delete-all-project-prompts.test.tsx`.
- [ ] T073 **GP-1**: server tests for `EnsureDefaultsAsync` (idempotent) vs `RestoreDefaultsAsync` (destructive) vs `DeleteAllProjectPromptsAsync` (project-isolated) — see FI-1.

### Checkpoint

- [x] T074 US5 checkpoint: startup seeding is idempotent, explicit restore overwrites, delete-all scopes correctly.

---

## Phase 8: Template Rendering (Internal Primitive)

**Goal**: `RenderTemplate` is a pure function consumed by `agent-dispatch`; lives here because it owns the template syntax.

- [x] T075 `AgentPromptService.RenderTemplate(string? template, PromptContext context)` — two-pass regex: `{{#if x}}…{{/if}}` removal, then `{{x}}` simple substitution; case-insensitive placeholder lookup.
- [x] T076 Consumers: `AgentStartBackgroundService.StartAgentAsync` (in `agent-dispatch` slice) calls `RenderTemplate` with the issue-derived `PromptContext`.
- [ ] T077 **GP-1**: server-side tests for `RenderTemplate` covering each placeholder, conditional removal, nested template scenarios, empty-string vs null — see FI-1.

---

## Phase 9: Polish & Cross-Cutting Concerns

- [x] T078 OpenAPI surface: all 13 endpoints appear in `src/Homespun.Web/src/api/generated/`.
- [x] T079 Generated-client consumers: 13 hooks under `features/prompts/hooks/` call the typed OpenAPI client, not hand-written fetches.
- [ ] T080 **GP-4**: Document the "no sanitisation on `InitialMessage`" decision (reviewed — Claude does not render it as HTML) in code comment + `follow-up-issues.md` (informational only) — FI-4.
- [ ] T081 **GP-5**: SignalR hub events on prompt catalogue mutations so cross-tab edits invalidate queries — FI-5.
- [ ] T082 **GP-6**: Soft-delete / audit trail for destructive ops (`delete`, `delete-all`, `restore-defaults`) — FI-6.

---

## Summary

| Phase | Tasks | Complete | Gaps |
|-------|-------|----------|------|
| Setup | T001–T003 | 3/3 | — |
| Foundational | T004–T009 | 5/6 | GP-2 |
| US1 (P1 🎯) | T010–T031 | 19/22 | GP-1 (×2), GP-7 |
| US2 (P1) | T032–T046 | 13/15 | GP-1, GP-3 |
| US3 (P2) | T047–T056 | 7/10 | GP-1, GP-7 |
| US4 (P2) | T057–T063 | 7/7 | — |
| US5 (P3) | T064–T074 | 10/11 | GP-1 |
| Template | T075–T077 | 2/3 | GP-1 |
| Polish | T078–T082 | 2/5 | GP-4, GP-5, GP-6 |
| **Total** | **82** | **68/82** | **7 gaps** |
