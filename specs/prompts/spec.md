# Feature Specification: Prompts

**Feature Branch**: n/a (pre-spec-kit; built on `main` over many PRs)
**Created**: 2026-04-14 (migrated)
**Status**: Migrated
**Input**: Reverse-engineered from existing implementation in `src/Homespun.Server/Features/ClaudeCode/Controllers/AgentPromptsController.cs`, `src/Homespun.Server/Features/ClaudeCode/Services/{IAgentPromptService,AgentPromptService,DefaultPromptsInitializationService,DefaultPromptDefinition}.cs`, `src/Homespun.Server/Features/ClaudeCode/Resources/default-prompts.json`, the `AgentPrompt*` methods on `IDataStore` + `JsonDataStore` (in `Features/PullRequests/Data/`), `src/Homespun.Shared/Models/Sessions/{AgentPrompt,PromptContext}.cs` + the related enums, `src/Homespun.Web/src/features/prompts/`, and the routes `src/Homespun.Web/src/routes/{prompts,projects.$projectId.prompts}.tsx`.

> **Migration note.** This spec documents *what exists*, not a future design. "Prompts" is the **catalogue of agent prompt templates** — global defaults plus per-project overrides plus user-authored project prompts — and the rendering primitive that turns a template + issue context into the initial message sent to a Claude Code session. It deliberately does NOT own: session streaming (→ `claude-agent-sessions`), the dispatch pipeline that *calls* `RenderTemplate` (→ `agent-dispatch`), or the similarly-named `MiniPromptService` (lives in `Features/AgentOrchestration/` and backs branch-id generation — unrelated). The `AgentPrompt*` data-store methods physically live inside the `PullRequests` slice's `JsonDataStore` because that file is the process's single persistence root; logically they are this slice's property.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - List prompts for a project (Priority: P1) 🎯 MVP

As a Homespun user on `/projects/{id}/prompts` I see every prompt available to this project — global prompts merged with any project-specific overrides and project-only prompts — so I can pick one from `RunAgentDialog` later or edit it here.

**Why this priority**: Every downstream flow (dispatch, issue-agent sessions, workflow runs) depends on a prompt being *discoverable*. Without the list nothing else in this feature has a surface.

**Independent Test**: `GET /api/agent-prompts/available-for-project/{projectId}` returns global prompts unioned with project prompts, with overrides replacing globals of the same `Name`; the route `/projects/{id}/prompts` renders `<PromptsList projectId={projectId} />`, which fetches via `useMergedProjectPrompts` and shows one `<PromptCard>` per prompt. `GET /api/agent-prompts` returns globals only; `/prompts` renders `<PromptsList isGlobal />` via `useGlobalPrompts`. Covered by `use-merged-project-prompts.test.tsx`, `use-global-prompts.test.tsx`, `use-project-prompts.test.tsx`, and `prompts-list.test.tsx`.

**Acceptance Scenarios**:

1. **Given** the catalogue has 13 seeded global prompts and no project prompts, **When** the user loads `/projects/{id}/prompts`, **Then** the list shows 13 cards all flagged non-override; loading renders `<PromptCardSkeleton>` placeholders.
2. **Given** a project has a prompt named `"Build"` that overrides a global prompt of the same name, **When** the list renders, **Then** the override replaces the global in the merged list and the card displays an "override" badge (`IsOverride = true`).
3. **Given** both Standard and IssueAgent prompts exist, **When** the list renders, **Then** Standard prompts appear in the main section and IssueAgent prompts appear under `<IssueAgentPromptsSection>` (backed by `useIssueAgentProjectPrompts`), each filtered by `Category`.
4. **Given** no prompts at all (after `delete-all` without restore), **When** the list renders, **Then** `<PromptsEmptyState>` is shown with a "Restore defaults" action that calls `useRestoreDefaultPrompts`.

---

### User Story 2 - Create, edit, and delete a prompt (Priority: P1)

As a project owner I add a prompt template with a name, a mode (Plan or Build), a category (Standard or IssueAgent), and the initial-message template body; I can also edit or delete any prompt I own.

**Why this priority**: Project-specific tuning is the primary reason prompts are editable at all — globals are the floor, not the ceiling. Delete + edit are table-stakes for a CRUD UI.

**Independent Test**: `POST /api/agent-prompts` with `{ name, initialMessage?, mode, projectId?, category }` creates a prompt (`201`); duplicates by `(name, projectId)` return `409`. `PUT /api/agent-prompts/by-name/{name}?projectId={id}` updates `initialMessage` and `mode`. `DELETE /api/agent-prompts/by-name/{name}?projectId={id}` removes it. All three flow through `useCreatePrompt`, `useUpdatePrompt`, `useDeletePrompt` hooks.

**Acceptance Scenarios**:

1. **Given** the user submits `<PromptForm>` with a unique `(name, projectId)`, **When** `POST /api/agent-prompts` returns `201`, **Then** the list view invalidates the relevant query key (`['prompts', 'project', projectId]` or `['prompts', 'global']`) and the new card appears without a manual refresh.
2. **Given** the user submits `<PromptForm>` with a name that already exists for the scope, **When** the server returns `409 Conflict`, **Then** the form surfaces the duplicate error inline without losing the user's input.
3. **Given** the user edits `initialMessage` and changes the mode from Plan to Build, **When** the `PUT` returns `200`, **Then** the card reflects the updated mode and the template change persists through a page reload.
4. **Given** the user clicks delete on a card, **When** the confirmation dialog is accepted, **Then** `useDeletePrompt` calls `DELETE /api/agent-prompts/by-name/{name}?projectId={id}` and the card disappears; on server error the card remains and the error surfaces via the notifications slice.

---

### User Story 3 - Override a global prompt in a project (Priority: P2)

As a project owner I clone a global prompt into my project so I can tweak its body without affecting other projects — and remove that override later if I change my mind.

**Why this priority**: Without overrides, a user who wants "Build, but for *this* repo only" has to delete the global (affecting everyone) or copy-paste into a project prompt with the same name (legal, but awkward). The explicit `create-override` endpoint exists to make that intent first-class.

**Independent Test**: `POST /api/agent-prompts/create-override { globalPromptName, projectId, initialMessage? }` creates a project prompt whose `Name` matches a global and whose `InitialMessage` starts seeded from the global (overridable by the request body). `DELETE /api/agent-prompts/by-name/{name}/override?projectId={id}` removes the override (NOT the underlying global). Covered by `use-create-override.test.tsx`, `use-remove-override.ts` + its test (note: `use-remove-override` has no test file — GP-7).

**Acceptance Scenarios**:

1. **Given** a global prompt `"Rebase"` exists and the project has no override, **When** the user clicks "Override" on the card and submits, **Then** `POST /create-override` is called and the merged list replaces the global with the new override (`IsOverride = true`).
2. **Given** an override already exists for `(name, projectId)`, **When** a second `POST /create-override` is attempted, **Then** the server returns `409 Conflict`.
3. **Given** an override exists, **When** the user clicks "Remove override", **Then** the project prompt is deleted and the merged list falls back to the global; the global itself is untouched.

---

### User Story 4 - Bulk-edit prompts via a code editor with diff (Priority: P2)

As a power user I want to paste or hand-edit a JSON blob of prompts and apply the resulting diff in one shot — without clicking through the per-card form for each of 13 built-ins.

**Why this priority**: Faster than form-by-form editing, especially for syncing prompt libraries across projects or catching up after upstream changes.

**Independent Test**: `<PromptsCodeEditor>` serialises the current list through `prompt-diff.ts:serializePrompts`, accepts edits, then `parsePrompts` + `calculateDiff` produce a set of create/update/delete operations that `useApplyPromptChanges` executes in order. Covered by `prompts-code-editor.test.tsx` and `prompt-diff.test.ts`.

**Acceptance Scenarios**:

1. **Given** the user edits the JSON to add a new prompt, **When** "Apply" is clicked, **Then** `calculateDiff` emits one `create` op and `useApplyPromptChanges` calls `useCreatePrompt` for it.
2. **Given** the user modifies `initialMessage` of an existing prompt, **When** applying, **Then** `calculateDiff` emits an `update` op; the scope (global vs project) is preserved.
3. **Given** the user removes a prompt from the JSON, **When** applying, **Then** `calculateDiff` emits a `delete` op; the user is warned that default global prompts deleted this way can only be restored via `restore-defaults`.
4. **Given** the JSON is malformed, **When** "Apply" is clicked, **Then** the editor shows a parse error inline and no network call is made.

---

### User Story 5 - Manage defaults: ensure, restore, delete-all-project-prompts (Priority: P3)

As an operator I can (a) guarantee the global default prompts exist after an upgrade, (b) restore globals that were deleted, and (c) nuke all prompts for a specific project without touching globals.

**Why this priority**: Recovery and bulk ops. Not needed on the happy path but essential for operational hygiene.

**Independent Test**: `POST /api/agent-prompts/ensure-defaults` is idempotent — creates any missing default global but does not overwrite edited ones. `POST /api/agent-prompts/restore-defaults` replaces all default globals with the seed `default-prompts.json` content, overwriting local edits. `DELETE /api/agent-prompts/project/{projectId}/all` removes every project prompt for a project. `DefaultPromptsInitializationService` runs `ensure-defaults` automatically on app startup. Covered by `use-restore-default-prompts.test.tsx`, `use-delete-all-project-prompts.test.tsx`.

**Acceptance Scenarios**:

1. **Given** a fresh install, **When** the server starts, **Then** `DefaultPromptsInitializationService` runs and all 13 seed prompts from `default-prompts.json` exist as globals.
2. **Given** the user deletes the global "Build" prompt and then calls `ensure-defaults`, **When** the request runs, **Then** "Build" is recreated with its original seed content.
3. **Given** the user has locally edited the global "Plan" prompt and then calls `restore-defaults`, **When** the request runs, **Then** "Plan" is overwritten back to its seed content — the local edit is lost.
4. **Given** a project has 5 project-scoped prompts, **When** the user calls `DELETE /project/{projectId}/all`, **Then** all 5 project prompts are removed and the merged list for that project falls back to pure globals.

---

### Edge Cases

- **Update silently drops `Category`** — `UpdateAgentPromptRequest` accepts a `Category` value that `AgentPromptService.UpdateAsync` ignores; category can only be set at create time. Gap GP-3.
- **Unknown `(name, projectId)` on update/delete** — service returns `null`/false; controller returns `404`. Covered by controller happy-path tests only (no service unit tests — GP-1).
- **Category field lookup is `Standard`-biased** — `GetByName` ignores `Category`; two prompts with the same `Name` but different categories would conflict. The current design assumes `Name` is globally unique across categories; no enforcement at the service level.
- **`SessionType` prompts are excluded from standard lists** — any prompt whose `SessionType` is `IssueAgentModification` or `IssueAgentSystem` is filtered out of `GetAll` / `GetByProject` and only appears in `issue-agent-prompts` endpoints. Implicit coupling with `Category.IssueAgent`.
- **No real-time invalidation** — editing a prompt in Browser A does not invalidate the query in Browser B until focus-refetch kicks in. Gap GP-5.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Expose `GET /api/agent-prompts`, `GET /api/agent-prompts/by-name/{name}?projectId={id}`, `GET /api/agent-prompts/project/{projectId}`, `GET /api/agent-prompts/available-for-project/{projectId}`, `POST /api/agent-prompts`, `PUT /api/agent-prompts/by-name/{name}?projectId={id}`, `DELETE /api/agent-prompts/by-name/{name}?projectId={id}` for the core CRUD + list surface.
- **FR-002**: Expose `POST /api/agent-prompts/ensure-defaults`, `POST /api/agent-prompts/restore-defaults`, `DELETE /api/agent-prompts/project/{projectId}/all` for the defaults / bulk management surface.
- **FR-003**: Expose `GET /api/agent-prompts/issue-agent-prompts`, `GET /api/agent-prompts/issue-agent/available/{projectId}` for IssueAgent-category prompts (filtered out of the standard lists).
- **FR-004**: Expose `POST /api/agent-prompts/create-override`, `DELETE /api/agent-prompts/by-name/{name}/override?projectId={id}` for the project-override flow.
- **FR-005**: `AgentPromptService.GetAvailableForProjectAsync(projectId)` MUST return the global prompts unioned with project prompts, with project prompts of matching `Name` replacing their global counterpart and flagged `IsOverride = true`.
- **FR-006**: `AgentPromptService.CreateAsync` MUST return `Conflict` when `(Name, ProjectId)` already exists; `Name` validation is case-sensitive.
- **FR-007**: `AgentPromptService.UpdateAsync` MUST mutate `InitialMessage`, `Mode`, and `UpdatedAt` only; `Category`, `SessionType`, and identity fields are immutable after create (documented drift — see GP-3).
- **FR-008**: `AgentPromptService.RenderTemplate(string? template, PromptContext context)` MUST support two patterns: `{{placeholder}}` (case-insensitive simple substitution) and `{{#if placeholder}}…{{/if}}` (conditional block, removed entirely when the placeholder is empty or whitespace). Placeholders: `title`, `id`, `description`, `branch`, `type`, `context`, `selectedissueid`, `userprompt`.
- **FR-009**: `DefaultPromptsInitializationService` MUST run `EnsureDefaultsAsync` exactly once per process start, loading `default-prompts.json` from the ClaudeCode `Resources/` folder. The service MUST be idempotent — creating only missing prompts, never overwriting existing ones.
- **FR-010**: `RestoreDefaultsAsync` MUST unconditionally overwrite the 13 seed globals from `default-prompts.json`, preserving any non-seed globals the user added.
- **FR-011**: `CreateOverrideAsync(globalPromptName, projectId, initialMessage?)` MUST (a) require that a global prompt of the given name exists, (b) reject if an override already exists for `(globalPromptName, projectId)`, (c) seed the override's `InitialMessage` from the global when the request omits it, and (d) copy `Mode`, `Category`, and `SessionType` from the global.
- **FR-012**: `GET /api/agent-prompts` and `GET /api/agent-prompts/project/{id}` MUST filter out prompts whose `SessionType` is non-null (those are only returned via `issue-agent-*` endpoints).

### Key Entities

- **`AgentPrompt`** (`src/Homespun.Shared/Models/Sessions/AgentPrompt.cs`)
  - `Name: string` — composite key with `ProjectId`.
  - `InitialMessage: string?` — template body supporting `{{placeholder}}` + `{{#if}}` syntax.
  - `Mode: SessionMode` — `Plan` (read-only tools) or `Build` (full write access).
  - `ProjectId: string?` — null for globals, set for project-scoped or override prompts.
  - `Category: PromptCategory` — `Standard` or `IssueAgent`; immutable after create.
  - `SessionType: SessionType?` — `Standard` | `IssueAgentModification` | `IssueAgentSystem`.
  - `CreatedAt: DateTime`, `UpdatedAt: DateTime` — UTC.
  - `IsOverride: bool` — *computed* at query time by `GetAvailableForProjectAsync`, not persisted.
- **`PromptContext`** (`src/Homespun.Shared/Models/Sessions/PromptContext.cs`)
  - Rendering context: `Title`, `Id`, `Description`, `Branch`, `Type`, `Context`, `SelectedIssueId`, `UserPrompt`.
- **Enums** — `SessionMode`, `SessionType`, `PromptCategory` (all in `Models/Sessions/`).
- **Request DTOs** — `CreateAgentPromptRequest`, `UpdateAgentPromptRequest`, `CreateOverrideRequest`. **Currently nested inside `AgentPromptsController.cs`**, not in `Homespun.Shared/Requests/` — this violates Constitution §III (see GP-2).

### Assumptions

- **A-1**: `Name` is unique across categories — i.e. no two prompts may share a `Name` even if one is `Standard` and the other is `IssueAgent`. Not explicitly enforced by the service; relies on the fact that seed prompts and the UI both avoid name collisions.
- **A-2**: The `AgentPrompt*` methods on `IDataStore` / `JsonDataStore` physically live in the `PullRequests` data-store file for historical reasons (one JSON persistence root). Logically they belong to this slice and MUST move if the persistence layer is split.
- **A-3**: `DefaultPromptsInitializationService` reads from an embedded resource path (`Features/ClaudeCode/Resources/default-prompts.json`). Deployment MUST keep the JSON next to the binary; missing-file behaviour is log + skip.
- **A-4**: `RenderTemplate` is called from `AgentStartBackgroundService.StartAgentAsync` (in the `agent-dispatch` slice). Template rendering itself is owned here; template *consumption* is owned there.
- **A-5**: Hot-edits to an already-created session's prompt do NOT affect that session — prompts are rendered into an initial message once, then frozen. There is no "re-render on prompt change" behaviour.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On a fresh install the 13 seed prompts are present as globals within one `DefaultPromptsInitializationService` tick — visible via `GET /api/agent-prompts` before any user request.
- **SC-002**: `GET /api/agent-prompts/available-for-project/{id}` returns exactly one row per distinct `Name`, with override rows replacing globals; no duplicates are observed under any override configuration.
- **SC-003**: Template rendering completes in O(k) over placeholder count — `RenderTemplate` never iterates the template more than twice regardless of context size (asserted by `prompt-diff.test.ts` indirectly through template round-tripping; no dedicated bench).
- **SC-004**: A project's prompt list is isolated from every other project — `DELETE /project/{id}/all` on project A does not affect project B's prompts or globals, and this is asserted by `use-delete-all-project-prompts.test.tsx`.
- **SC-005**: The bulk code-editor round-trips: serialising a list, applying zero edits, and re-submitting is a no-op (zero network calls) — asserted by `prompts-code-editor.test.tsx`.

## Identified Gaps

Detailed remediation lives in `plan.md` → Complexity Tracking and `follow-up-issues.md`.

- **GP-1**: No server-side unit or API tests for `AgentPromptService` or `AgentPromptsController` — the largest shipped CRUD surface in the app without direct test coverage.
- **GP-2**: Request DTOs (`CreateAgentPromptRequest`, `UpdateAgentPromptRequest`, `CreateOverrideRequest`) live inside the controller file instead of `src/Homespun.Shared/Requests/`, violating Constitution §III (Shared Contract Discipline).
- **GP-3**: `UpdateAgentPromptRequest.Category` is accepted by the request body but ignored by `UpdateAsync` — silent data drop on category change attempts.
- **GP-4**: No input sanitisation on `InitialMessage`. Low risk today (the body is not rendered as HTML in the browser — it is sent as a Claude message) but worth documenting as a reviewed decision, not an oversight.
- **GP-5**: No SignalR hub events for prompt catalogue mutations; edits in one browser do not invalidate the query in another.
- **GP-6**: Hard delete only. No soft-delete, audit trail, or "recently deleted" undo — `DELETE /project/{id}/all` is immediately destructive and `restore-defaults` only covers globals.
- **GP-7**: No route-level component tests for `routes/prompts.tsx` or `routes/projects.$projectId.prompts.tsx`; `use-remove-override.ts` also lacks a co-located test file.
