## Why

Prompts is the catalogue of agent prompt templates — global defaults plus per-project overrides plus user-authored project prompts — and the rendering primitive that turns a template + issue context into the initial message sent to a Claude Code session. Every downstream flow (dispatch, issue-agent sessions, workflow runs) depends on a prompt being discoverable and renderable.

## What Changes

- Two-tier prompt catalogue: 13 seeded globals (auto-loaded from `default-prompts.json` at startup) plus per-project prompts and explicit project-over-global overrides.
- 13 HTTP endpoints under `/api/agent-prompts` for CRUD, overrides, issue-agent variants, and defaults management.
- Template rendering with `{{placeholder}}` + `{{#if}}` syntax consumed by the agent-dispatch pipeline.
- React UI: per-card form plus bulk JSON code-editor with diff-based apply.
- Defaults management: ensure-defaults (idempotent), restore-defaults (destructive overwrite), delete-all-project-prompts.

## Capabilities

### New Capabilities
- `prompt-catalogue`: Prompt CRUD, merge logic, override system, template rendering, defaults management, bulk editing.

### Modified Capabilities
<!-- None — brownfield migration. -->

## Impact

- **Backend**: `Features/ClaudeCode/` — 4 C# files (controller + service + init service + definition) + 1 seed resource, ~800 LOC.
- **Frontend**: `features/prompts/` — 7 components, 13 hooks, 1 utility, ~4,000 LOC (production + test).
- **Shared**: `AgentPrompt`, `PromptContext`, 3 enums in `Models/Sessions/`.
- **Testing**: 14 web test files. Zero server tests (gap GP-1).
- **Status**: Migrated — documents the as-built implementation.
