## ADDED Requirements

### Requirement: Chat rendering runtime is backed by Assistant UI's ExternalStoreRuntime

The web client SHALL render session chat using `@assistant-ui/react`'s `useExternalStoreRuntime`, with messages sourced from the existing AG-UI reducer state.

#### Scenario: Rendered messages come from reducer output
- **WHEN** the session detail page mounts with an `AGUISessionState` containing messages
- **THEN** the rendered thread SHALL contain one DOM message per `AGUIMessage` in `state.messages`
- **AND** the order of rendered messages SHALL match the order of `state.messages`

#### Scenario: No parallel message store exists on the client
- **WHEN** the session detail page is mounted
- **THEN** the only client-side source of truth for chat message content SHALL be the AG-UI reducer
- **AND** Assistant UI's runtime SHALL consume the reducer output through `ExternalStoreRuntime` without duplicating state

### Requirement: AG-UI message blocks map to Assistant UI message parts

The web client SHALL provide a pure `convertAGUIMessage` function that maps `AGUIMessage` to `ThreadMessageLike` using the following correspondences: text block → text part; thinking block → reasoning part; toolUse block → tool-call part carrying `toolCallId`, `toolName`, `argsText`, and optional `result`.

#### Scenario: Text block becomes a text part
- **WHEN** an `AGUIMessage` contains a text block
- **THEN** the converted message SHALL contain a content part of type `text` carrying that text

#### Scenario: Thinking block becomes a reasoning part
- **WHEN** an `AGUIMessage` contains a thinking block
- **THEN** the converted message SHALL contain a content part of type `reasoning` carrying that text

#### Scenario: ToolUse block becomes a tool-call part
- **WHEN** an `AGUIMessage` contains a toolUse block with a `toolCallId`, `toolName`, `input`, and optional `result`
- **THEN** the converted message SHALL contain a content part of type `tool-call` with `toolCallId`, `toolName`, `argsText = input`, and `result` when present

#### Scenario: Multi-block assistant message preserves block order
- **WHEN** an `AGUIMessage` contains multiple blocks
- **THEN** the converted message's content parts SHALL appear in the same order as the source blocks

#### Scenario: Conversion is pure
- **WHEN** `convertAGUIMessage` is invoked with the same `AGUIMessage` instance twice
- **THEN** the two results SHALL be deep-equal
- **AND** the function SHALL not read from React context, global state, or time-varying sources

### Requirement: Tool-call rendering is wired through the Tool UI `Toolkit` API

The web client SHALL register one `Toolkit` entry per known built-in tool name (Bash, Read, Grep, Write), each declared `type: "backend"` with a `render({ result })` callback producing the tool-specific presentation. The toolkit SHALL be supplied to `useAui({ tools: Tools({ toolkit }) })` and the resulting `aui` SHALL be attached to `AssistantRuntimeProvider`. Unknown tool names SHALL render a generic fallback presentation.

#### Scenario: Known tool renders its Toolkit `render` output
- **WHEN** a message contains a tool-call part with a `toolName` present in the Toolkit
- **THEN** the DOM SHALL render the output of that Toolkit entry's `render` callback
- **AND** the unknown-tool fallback SHALL NOT be used

#### Scenario: Unknown tool renders the fallback
- **WHEN** a message contains a tool-call part with a `toolName` not present in the Toolkit
- **THEN** the DOM SHALL render a generic unknown-tool fallback

#### Scenario: Runtime and Toolkit compose under one provider
- **WHEN** the session page mounts
- **THEN** a single `AssistantRuntimeProvider` SHALL receive both `runtime` (from `useExternalStoreRuntime`) and `aui` (from `useAui({ tools: Tools({ toolkit }) })`)
- **AND** a tool-call content part SHALL route to the Toolkit `render` callback without requiring the `useChatRuntime` path

### Requirement: Plan approval and pending-question surfaces render outside the message stream

`PlanApprovalPanel` and `QuestionPanel` SHALL render as siblings of the Assistant UI thread viewport, reading `pendingPlan` and `pendingQuestion` from the AG-UI reducer state. They SHALL NOT be injected as message content parts or custom message parts.

#### Scenario: Panels are siblings of the thread viewport
- **WHEN** the session has a non-null `pendingPlan` or `pendingQuestion`
- **THEN** the corresponding panel SHALL be rendered as a sibling of `<ThreadPrimitive.Root>`
- **AND** the panel SHALL NOT appear inside the message list

#### Scenario: Panels are pinned, not scrollable
- **WHEN** the thread scroll position changes
- **THEN** an active plan-approval or question panel SHALL remain visible
- **AND** it SHALL NOT scroll with the message history

### Requirement: Composer preserves session-control affordances

The Assistant UI composer SHALL present, as siblings of the input field, the session mode selector (Plan/Build), the model selector (Opus/Sonnet/Haiku), and the `@`-mention search popup trigger, preserving the behaviour of the prior `ChatInput` component.

#### Scenario: Mode selector toggles session mode
- **WHEN** the user selects a new session mode from the composer
- **THEN** the session-mode store SHALL be updated
- **AND** the next message sent SHALL carry the new mode

#### Scenario: Model selector toggles session model
- **WHEN** the user selects a new model from the composer
- **THEN** the session-model store SHALL be updated
- **AND** the next message sent SHALL carry the new model selection

#### Scenario: `@`-mention triggers the search popup
- **WHEN** the user types `@` followed by partial text in the composer
- **THEN** the mention search popup SHALL appear at the caret
- **AND** selecting a result SHALL insert the referenced entity into the composer value

### Requirement: Live and replay streams produce identical rendered output

The client's chat rendering SHALL produce identical DOM for a given session event history regardless of whether the events arrived live via SignalR or via replay through `GET /api/sessions/{sessionId}/events`.

#### Scenario: Live and replay produce identical DOM
- **WHEN** a session's events are first received live and the page is later reloaded to trigger a replay
- **THEN** the rendered message count, per-message content parts, tool-call result bindings, and plan/question panel visibility SHALL match between the two renders

#### Scenario: Idempotent envelope application
- **WHEN** the same envelope is applied to the reducer twice
- **THEN** the rendered DOM SHALL NOT change between the first and second application
- **AND** no message, content part, or tool-call result SHALL be duplicated

### Requirement: Chat surface state space is covered by Storybook fixtures

The web client SHALL author Storybook stories driven by scripted `SessionEventEnvelope[]` fixtures that exercise the chat surface's principal states: simple text turn, tool-call lifecycle, thinking block, multi-block turn, plan pending, question pending, run error, and streaming interruption.

#### Scenario: Fixture story builds
- **WHEN** `npm run build-storybook` runs
- **THEN** each named fixture story SHALL build without warnings
- **AND** each story SHALL render the session chat surface as configured by the fixture envelope sequence

#### Scenario: Interactive composer story
- **WHEN** the composer interaction story's `play` function runs
- **THEN** it SHALL type a message, submit it, and assert the runtime's `onNew` callback received the expected content
