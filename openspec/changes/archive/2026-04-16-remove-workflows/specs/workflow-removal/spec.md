## REMOVED Requirements

### Requirement: Workflow execution engine

**Reason**: Replaced by OpenSpec's schema-driven artefact lifecycle. The workflow system (imperative step-based execution with Agent, ServerAction, CiMerge, and Gate step types) is unused and the equivalent orchestration value is delivered by OpenSpec's propose → apply → archive progression with manual review gates.

**Migration**: No migration needed — feature confirmed unused. OpenSpec changes + Fleece issue dispatch cover all use cases previously intended for workflows.

### Requirement: Workflow template management

**Reason**: Templates (`DefaultWorkflowTemplates.cs`, `WorkflowTemplateService`) defined step sequences that are now modelled as OpenSpec schema artefacts. Custom schemas replace custom workflow templates.

**Migration**: No migration needed. Users who want custom workflows use OpenSpec's `openspec schema fork` to create custom schemas.

### Requirement: Workflow context interpolation

**Reason**: The context accumulation system (`WorkflowContextStore`, `{{steps.implement.output.prNumber}}` interpolation) is replaced by agents reading OpenSpec artefacts directly. Each agent session reads `proposal.md`, `design.md`, `tasks.md` etc. for context rather than receiving interpolated variables from prior steps.

**Migration**: No migration needed — no stored workflow contexts exist.

### Requirement: Workflow SignalR hub

**Reason**: `WorkflowHub` broadcast step-progress events to the UI. Replaced by the post-session snapshot contract in `openspec-integration` which pushes change state to the server, which then broadcasts via existing notification infrastructure.

**Migration**: No migration needed. Frontend workflow listeners removed with the workflow UI slice.

### Requirement: Workflow HTTP endpoints

**Reason**: `WorkflowsController` and `WorkflowTemplateController` endpoints (`/api/workflows/*`, `/api/workflow-templates/*`) are removed. **BREAKING** for any external consumers (none expected).

**Migration**: No external consumers identified. Endpoints return 404 after removal.
