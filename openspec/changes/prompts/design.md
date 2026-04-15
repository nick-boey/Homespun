## Context

Maintain a two-tier catalogue of Claude prompt templates: 13 seeded globals plus per-project prompts and explicit overrides. Expose 13 HTTP endpoints for CRUD, overrides, issue-agent variants, and defaults management. Render templates with a minimal `{{placeholder}}` + `{{#if}}` engine consumed by the `agent-dispatch` pipeline.

## Goals / Non-Goals

**Goals:**
- Global prompt seed from embedded `default-prompts.json` at startup.
- Per-project prompts with first-class override mechanism.
- Template rendering with `{{placeholder}}` substitution and `{{#if}}` conditionals.
- Bulk editing via JSON code-editor with diff-based apply.
- Defaults management: ensure (idempotent), restore (destructive), delete-all-project.

**Non-Goals:**
- Session streaming (owned by `claude-agent-sessions`).
- Dispatch pipeline (owned by `agent-dispatch` — this slice provides `RenderTemplate`, dispatch consumes it).
- SignalR real-time invalidation (gap — edits don't broadcast to other tabs).

## Decisions

### D1: Two-tier merge with override detection

**Decision:** `GetAvailableForProjectAsync` returns globals unioned with project prompts; project prompts of matching `Name` replace globals and are flagged `IsOverride = true`.

**Rationale:** One query returns the full resolved catalogue. Override detection enables UI distinction.

### D2: Immutable Category after creation

**Decision:** `UpdateAsync` mutates only `InitialMessage`, `Mode`, and `UpdatedAt`. `Category` and `SessionType` are immutable after create.

**Rationale:** Category determines list filtering and endpoint routing. Changing it post-creation would break assumptions.

**Open concern:** `UpdateAgentPromptRequest` accepts `Category` but it's silently dropped (gap GP-3).

### D3: Two-pass template rendering

**Decision:** Pass 1 handles `{{#if x}}…{{/if}}` conditionals (remove when empty). Pass 2 handles `{{x}}` simple substitution. Case-insensitive.

**Rationale:** Minimal engine that covers all current prompt patterns. O(k) over placeholder count.

### D4: Startup seeding via IHostedService

**Decision:** `DefaultPromptsInitializationService` runs `EnsureDefaultsAsync` once at startup, creating missing seeds without overwriting existing ones.

**Rationale:** Idempotent — safe across upgrades. `RestoreDefaults` exists for explicit destructive reset.

### D5: Override creation seeds from global

**Decision:** `CreateOverrideAsync` copies `InitialMessage`, `Mode`, `Category`, `SessionType` from the global when request body omits `initialMessage`.

**Rationale:** Users typically want to tweak the global, not start from scratch.

## Risks / Trade-offs

- **[Gap GP-1]** Zero server-side tests for `AgentPromptService` or `AgentPromptsController`.
- **[Gap GP-2]** Request DTOs live in controller file, not `Homespun.Shared/Requests/` (Constitution §III violation).
- **[Gap GP-5]** No SignalR broadcast for prompt mutations.
- **[Gap GP-6]** Hard delete only; no soft-delete or audit trail.
- **[Trade-off]** Data-store methods physically live in PullRequests slice's `JsonDataStore` — historical persistence-root coupling.
