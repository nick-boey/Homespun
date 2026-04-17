## Why

The workflow system (`Features/Workflows/` — ~15 files across services, executors, controllers, hubs, storage) implements a step-based execution engine with typed steps (Agent, ServerAction, CiMerge, Gate), context interpolation (`{{steps.implement.output.prNumber}}`), JSONL persistence under `.fleece/workflows/`, and SignalR broadcast. In practice it is unused and brittle; the equivalent orchestration value is now delivered by OpenSpec's schema-driven artefact lifecycle (propose → apply → archive with manual review gates before apply and before archive). Removing the slice deletes a meaningful maintenance burden with no known user-visible regression.

## What Changes

- **Remove** the entire `src/Homespun.Server/Features/Workflows/` directory.
- **Remove** `WorkflowHub` (SignalR) and its interface in `Homespun.Shared`.
- **Remove** workflow-related DTOs and enums from `Homespun.Shared/Models/`.
- **Remove** `DefaultWorkflowTemplates.cs` and template registration.
- **Remove** the `workflowTemplateService` / `workflowService` registrations from DI in `Program.cs`.
- **Remove** any workflow UI surfaces (workflow tab, template management) from `src/Homespun.Web/`.
- **Delete** `.fleece/workflows/` content path from documentation; no on-disk cleanup required because the feature is not in use.
- **BREAKING:** HTTP endpoints under `/api/workflows/*` and `/api/workflow-templates/*` removed.

## Capabilities

### Removed Capabilities
- Workflow system (not previously formalised as an OpenSpec capability, so removal is by deletion of the slice directory only).

## Impact

- **Backend**: ~15 C# files deleted (~2,500 LOC). Entire `Features/Workflows/` slice and its DI registration.
- **Shared**: workflow DTOs, enums, and hub interface removed.
- **Frontend**: workflow tab component and any hooks/API clients deleted.
- **Generated API client**: regenerated, removing workflow endpoints.
- **Tests**: remove any workflow-related backend tests.
- **Migration**: none — feature confirmed unused. No on-disk `.fleece/workflows/` cleanup task required.
- **Dependencies**: this change stands alone; it does not depend on skills-catalogue or openspec-integration. Sequence: can ship first.
