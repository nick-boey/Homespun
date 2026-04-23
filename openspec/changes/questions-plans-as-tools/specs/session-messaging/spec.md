## MODIFIED Requirements

### Requirement: A2A events are translated to AG-UI envelopes by a single translator

The server SHALL translate every stored A2A event to an AG-UI event envelope using a single pure translation function that is used for both live broadcast and replay.

#### Scenario: Live broadcast and replay use the same translator

- **WHEN** the same A2A event is broadcast live and later fetched via replay
- **THEN** the resulting AG-UI envelopes SHALL be equal by value (same `seq`, `eventId`, AG-UI event type, and payload)

#### Scenario: Canonical A2A events map to canonical AG-UI events

- **WHEN** an A2A `Message` with an agent text block is translated
- **THEN** the result SHALL be an AG-UI `TextMessageStart` + `TextMessageContent` + `TextMessageEnd` sequence for that block
- **WHEN** an A2A `Message` with an agent tool_use block is translated
- **THEN** the result SHALL be an AG-UI `ToolCallStart` + `ToolCallArgs` + `ToolCallEnd` sequence
- **WHEN** an A2A `StatusUpdate` with state `completed` is translated
- **THEN** the result SHALL be an AG-UI `RunFinished` event
- **WHEN** an A2A `StatusUpdate` with state `input-required` and `inputType = question` is translated
- **THEN** the result SHALL be an AG-UI `ToolCallStart` + `ToolCallArgs` + `ToolCallEnd` sequence with `toolName = "ask_user_question"` and the question payload serialised into the args
- **WHEN** an A2A `StatusUpdate` with state `input-required` and `inputType = plan-approval` is translated
- **THEN** the result SHALL be an AG-UI `ToolCallStart` + `ToolCallArgs` + `ToolCallEnd` sequence with `toolName = "propose_plan"` and the plan payload (including `planContent` and optional `planFilePath`) serialised into the args

#### Scenario: Non-canonical concerns map to AG-UI Custom events

- **WHEN** an A2A `Message` system-init, system-hook_started, system-hook_response, thinking block, or `StatusUpdate` workflow_complete is translated
- **THEN** the result SHALL be an AG-UI `Custom` event with a Homespun-namespaced `name` (`system.init`, `hook.started`, `hook.response`, `thinking`, `workflow.complete`) and the source payload carried in `data`

#### Scenario: Input-required does NOT emit a Custom event

- **WHEN** an A2A `StatusUpdate` with state `input-required` is translated
- **THEN** the translator SHALL NOT emit a `Custom` event with `name` in `{"question.pending", "plan.pending", "status.resumed"}`
- **AND** the emitted envelopes SHALL be `ToolCallStart` / `ToolCallArgs` / `ToolCallEnd` only

#### Scenario: Unknown A2A variants never break translation

- **WHEN** the translator receives an A2A event whose shape is unknown
- **THEN** the translator SHALL emit an AG-UI `Custom` event with `name = "raw"` and the original payload under `data.original`
- **AND** the translator SHALL NOT throw

### Requirement: Answering an input-required tool call appends a TOOL_CALL_RESULT envelope

When the user submits an answer to an `ask_user_question` tool call, or approves/rejects a `propose_plan` tool call, the server SHALL append a `TOOL_CALL_RESULT` envelope to the session's event log after the worker confirms the submission.

#### Scenario: Answered question emits TOOL_CALL_RESULT

- **WHEN** the user submits a question answer and the worker confirms resolution
- **THEN** the server SHALL append a `TOOL_CALL_RESULT` envelope with the `toolCallId` of the original `ToolCallStart` and the answer payload serialised as the result
- **AND** the envelope SHALL be broadcast live to SignalR clients AND available via replay

#### Scenario: Approved plan emits TOOL_CALL_RESULT

- **WHEN** the user approves or rejects a plan and the worker confirms
- **THEN** the server SHALL append a `TOOL_CALL_RESULT` envelope with the `toolCallId` of the original plan `ToolCallStart` and a payload of `{ approved: boolean, keepContext: boolean, feedback?: string }`

#### Scenario: Tool-call id is stable across the input-required round-trip

- **WHEN** an input-required `TOOL_CALL_START` is emitted with `toolCallId = T` and later a `TOOL_CALL_RESULT` is appended for the same submission
- **THEN** the `TOOL_CALL_RESULT.toolCallId` SHALL equal `T`
- **AND** no intervening `TOOL_CALL_*` envelopes for the same `toolCallId` SHALL be emitted between start and result

## REMOVED Requirements

<!-- The question.pending / plan.pending / status.resumed Custom event names are retired.
     These were never a stand-alone requirement in session-messaging; they appeared as
     examples inside the "Non-canonical concerns" scenario above, which is being MODIFIED
     rather than removed. This section exists only to state the retirement explicitly so
     downstream consumers (reducer, client) can plan their removals. -->

### Requirement: Input-required emits question.pending / plan.pending Custom events

**Reason**: These names caused the client to model question/plan state as modal ghost-state instead of conversation content. Tool-call events model the same interaction more faithfully and benefit from standard AG-UI replay and idempotence semantics.

**Migration**: Server translator change lands together with the client's Tool UI toolkit entries (`ask_user_question`, `propose_plan`) per the `questions-plans-as-tools` change. Stale browser tabs that connect post-deploy SHALL gracefully render unknown tool calls via the fallback path; they will not see `question.pending` / `plan.pending` Custom events again.
