# interactive-tool-calls Specification

## Purpose
TBD - created by archiving change questions-plans-as-tools. Update Purpose after archive.
## Requirements
### Requirement: Questions render via the ask_user_question Toolkit entry

The web client SHALL register a `Toolkit` entry named `ask_user_question` of `type: "frontend"` that renders using a Tool UI interaction component (`@tool-ui/question-flow` for multi-step / branching questions; `@tool-ui/option-list` for single-choice cases). The `render` callback SHALL commit the user's selection via `addResult` when the user confirms.

#### Scenario: Pending question renders the interaction component

- **WHEN** the message stream contains a tool-call part with `toolName = "ask_user_question"` and no result
- **THEN** the DOM SHALL render the interaction component configured from the tool-call args
- **AND** the component SHALL be interactive (confirm action enabled)

#### Scenario: User confirm submits a tool result

- **WHEN** the user commits a selection via the interaction component
- **THEN** the Toolkit `render` callback SHALL call `addResult(selection)`
- **AND** the client SHALL dispatch the selection to the server so that a corresponding `TOOL_CALL_RESULT` envelope is appended to the session log

#### Scenario: Answered question renders in receipt mode

- **WHEN** the message stream contains a tool-call part with `toolName = "ask_user_question"` and a committed `result`
- **THEN** the DOM SHALL render the interaction component in receipt mode (read-only, `choice` prop set to the committed selection)
- **AND** no interactive action SHALL be available

### Requirement: Plans render via the propose_plan Toolkit entry

The web client SHALL register a `Toolkit` entry named `propose_plan` of `type: "frontend"` that renders using `@tool-ui/plan` for the plan presentation and `@tool-ui/approval-card` for the approve / reject action surface. The `render` callback SHALL commit `{ approved, keepContext, feedback? }` via `addResult` when the user confirms.

#### Scenario: Pending plan renders plan body and approval card

- **WHEN** the message stream contains a tool-call part with `toolName = "propose_plan"` and no result
- **THEN** the DOM SHALL render the plan content from the tool-call args
- **AND** the DOM SHALL render an approval card with Approve / Reject actions
- **AND** the approval card SHALL include the `keepContext` toggle and the optional rejection-feedback text field

#### Scenario: Approve commits a plan-approval result

- **WHEN** the user clicks Approve
- **THEN** the Toolkit `render` callback SHALL call `addResult({ approved: true, keepContext, feedback: undefined })`

#### Scenario: Reject with feedback commits a plan-rejection result

- **WHEN** the user clicks Reject with optional feedback text
- **THEN** the Toolkit `render` callback SHALL call `addResult({ approved: false, keepContext, feedback })`

#### Scenario: Resolved plan renders in receipt mode

- **WHEN** the message stream contains a tool-call part with `toolName = "propose_plan"` and a committed `result`
- **THEN** the DOM SHALL render the plan in receipt mode with the committed approval state
- **AND** the approval card SHALL display the outcome without interactive controls

### Requirement: Unknown interactive tools fall back to a safe default

The web client SHALL render a safe fallback presentation for tool-call parts whose `toolName` is not registered in the Toolkit and whose status is `requires-action`, so that a stale client receiving a newer interactive tool does not crash the message list.

#### Scenario: Unknown requires-action tool renders the fallback

- **WHEN** the message stream contains a tool-call part with an unregistered `toolName` and no result
- **THEN** the DOM SHALL render a fallback panel that displays the tool name and a disabled "Unsupported" indicator
- **AND** the rendering SHALL NOT throw

### Requirement: Pending-state reducer fields are removed

The AG-UI reducer (`agui-reducer.ts`) SHALL NOT expose `pendingQuestion` or `pendingPlan` fields on `AGUISessionState`, and SHALL NOT apply `CustomEvent` envelopes with `name` in `{"question.pending", "plan.pending", "status.resumed"}` — these are retired.

#### Scenario: Reducer state has no pending fields

- **WHEN** a session has received any sequence of envelopes
- **THEN** `AGUISessionState` SHALL NOT contain keys named `pendingQuestion` or `pendingPlan`

#### Scenario: Retired custom event names are ignored

- **WHEN** the reducer applies a `CustomEvent` with `name` in `{"question.pending", "plan.pending", "status.resumed"}`
- **THEN** the reducer SHALL add the event to `unknownEvents` for diagnostics
- **AND** the reducer SHALL NOT mutate any other state field as a side effect

### Requirement: Legacy PlanApprovalPanel and QuestionPanel components are removed

`features/sessions/components/plan-approval-panel.tsx`, `features/questions/QuestionPanel.tsx`, and their tests SHALL be deleted once the tool-call Toolkit entries are in place.

#### Scenario: Panel components are absent from the bundle

- **WHEN** the client bundle is produced
- **THEN** no module at `features/sessions/components/plan-approval-panel.tsx` or `features/questions/QuestionPanel.tsx` SHALL be present
- **AND** the session detail page SHALL NOT import those components

