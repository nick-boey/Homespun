# session-messaging Specification

## Purpose

This capability defines the worker-to-server-to-client messaging pipeline for Claude Code sessions. A2A events emitted by the worker are persisted verbatim in a per-session append-only log, translated once into AG-UI envelopes, and delivered to clients via both live SignalR broadcast and a replay endpoint that share a single translator so live and replay streams are equivalent.
## Requirements
### Requirement: A2A events are stored verbatim with per-session monotonic sequence numbers

The server SHALL persist every A2A event received from the worker into an append-only per-session log, assigning a strictly monotonic `seq` number starting at 1 for each new session. The stored record SHALL include the original A2A event payload, a server-assigned UUID `eventId`, the session id, the `seq`, and the receive timestamp.

#### Scenario: First event in a new session receives seq=1

- **WHEN** the server receives the first A2A event for a previously-unknown session
- **THEN** the event SHALL be appended to the session's event log with `seq = 1`
- **AND** the event SHALL be assigned a stable UUID `eventId`

#### Scenario: Subsequent events increment seq monotonically

- **WHEN** the server receives additional A2A events for an existing session
- **THEN** each event SHALL be appended with `seq` equal to one greater than the previous event's `seq`

#### Scenario: Concurrent appends to the same session remain monotonic

- **WHEN** two A2A events arrive concurrently for the same session
- **THEN** the store SHALL serialize the appends such that resulting `seq` values are strictly monotonic and line-ordered in the underlying file

#### Scenario: Events persist before broadcast

- **WHEN** an A2A event is received
- **THEN** the event SHALL be written to the on-disk event log before any live SignalR broadcast for that event is dispatched

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

### Requirement: Replay endpoint serves AG-UI envelopes from the event log

The system SHALL expose `GET /api/sessions/{sessionId}/events?since={long?}&mode={incremental|full}` which returns stored events translated through the same translator used for live broadcast, in `seq`-ascending order.

#### Scenario: Incremental mode returns only events after since

- **WHEN** a client requests `?since=N&mode=incremental`
- **THEN** the response SHALL contain envelopes with `seq > N` in ascending order

#### Scenario: Full mode ignores since

- **WHEN** a client requests `?mode=full`
- **THEN** the response SHALL contain all envelopes starting from `seq = 1`

#### Scenario: Default mode comes from server config

- **WHEN** `mode` is absent and `SessionEvents:ReplayMode` is set to `Incremental` (default)
- **THEN** the endpoint SHALL behave as incremental
- **WHEN** `mode` is absent and `SessionEvents:ReplayMode` is set to `Full`
- **THEN** the endpoint SHALL behave as full

#### Scenario: Since beyond the end returns empty

- **WHEN** a client requests `?since=N` where `N` is greater than or equal to the current highest seq
- **THEN** the response SHALL be an empty array with HTTP 200
- **AND** the response SHALL NOT be treated as an error

#### Scenario: Unknown session returns 404

- **WHEN** a client requests events for a session id that has no event log
- **THEN** the response SHALL be HTTP 404

### Requirement: Live AG-UI broadcasts carry stable envelope metadata

The server SHALL broadcast every AG-UI event to SignalR clients wrapped in a `SessionEventEnvelope` carrying `seq`, `sessionId`, `eventId`, and the AG-UI event itself.

#### Scenario: Live envelope matches replay envelope

- **WHEN** a client observes an envelope live and subsequently fetches the same event via replay
- **THEN** both envelopes SHALL have the same `eventId` and `seq`

#### Scenario: Hub exposes one broadcast method for AG-UI events

- **WHEN** the server broadcasts any AG-UI event
- **THEN** it SHALL use a single hub method `ReceiveSessionEvent(sessionId, envelope)`
- **AND** per-type `BroadcastAGUI*` hub methods SHALL NOT be used

### Requirement: Client reconciles live and replay via eventId dedup

Client consumers SHALL track the last-seen `seq` per session and SHALL deduplicate envelopes by `eventId` when merging live and replay streams.

#### Scenario: Duplicate eventIds are ignored

- **WHEN** a client receives the same envelope (same `eventId`) from both live broadcast and a replay response
- **THEN** the client SHALL apply the envelope exactly once to its render state

#### Scenario: Refresh resumes from lastSeenSeq

- **WHEN** a client mounts or its SignalR connection reconnects with a persisted `lastSeenSeq = N`
- **THEN** the client SHALL fetch `/api/sessions/{id}/events?since=N` before rendering
- **AND** the client SHALL merge the replay response with any buffered live envelopes before updating render state

#### Scenario: Client mode override takes precedence over server default

- **WHEN** a client appends `mode=full` to its replay request
- **THEN** the endpoint SHALL return all events regardless of `SessionEvents:ReplayMode`

### Requirement: Cache purge on upgrade

On first startup after an upgrade that introduces the A2A event log format, the system SHALL delete any pre-existing session cache files under the cache directory and log a warning indicating the count of deleted files.

#### Scenario: Pre-existing ClaudeMessage cache files are removed

- **WHEN** the server starts and finds `*.jsonl` files under the cache directory
- **THEN** those files SHALL be deleted before the server begins serving requests
- **AND** a warning SHALL be logged naming the count of files removed

#### Scenario: Environment opt-out preserves files

- **WHEN** `HOMESPUN_SKIP_CACHE_PURGE=true` is set in the process environment
- **THEN** the purge SHALL be skipped and a warning SHALL be logged that the cache is intact

### Requirement: Pipeline observability is span-based

The system SHALL represent the A2A → AG-UI pipeline as OpenTelemetry spans emitted on `Homespun.SessionPipeline` (server) and `homespun.worker` (worker), carrying correlation attributes sufficient to reconstruct the end-to-end journey of a single A2A event in Seq's trace view.

#### Scenario: Every ingested A2A event produces an ingest span with ordered events

- **WHEN** the server receives an A2A event from a worker
- **THEN** exactly one span named `homespun.session.ingest` is emitted on `Homespun.SessionPipeline`
- **AND** it carries attributes `homespun.session.id`, `homespun.a2a.kind`, `homespun.seq`, `homespun.event.id`
- **AND** it contains span events `sse.rx`, `ingest.append`, `signalr.tx` in that order

#### Scenario: Translate step is a child span

- **WHEN** the server translates an A2A event to AG-UI
- **THEN** a child span named `homespun.agui.translate` is emitted inside the ingest span
- **AND** it carries attributes `homespun.a2a.kind` and `homespun.session.id`

#### Scenario: Worker emissions are PRODUCER spans

- **WHEN** the worker writes an A2A event to the SSE response stream
- **THEN** a span named `homespun.a2a.emit` is emitted on the `homespun.worker` tracer with kind `PRODUCER`
- **AND** it carries `homespun.session.id`, `homespun.a2a.kind`, and — when applicable — `homespun.task.id`, `homespun.message.id`, `homespun.artifact.id`

#### Scenario: Hub lifecycle is observable as spans

- **WHEN** a client connects to `ClaudeCodeHub` and later disconnects
- **THEN** exactly one span named `homespun.signalr.connect` covers the interval
- **AND** it contains a `connected` event at start and a `disconnected` event at end

#### Scenario: Join and leave are discrete spans

- **WHEN** a client calls `ClaudeCodeHub.JoinSession(…)`
- **THEN** one span named `homespun.signalr.join` is emitted carrying `homespun.session.id` and `signalr.connection.id`

### Requirement: Content preview is gated and defaults to safe values

The system SHALL provide a single configuration key `SessionEventContent:ContentPreviewChars` that governs truncation of the optional `homespun.content.preview` span attribute. A value of `0` SHALL suppress the attribute entirely. The legacy key `SessionEventLog:ContentPreviewChars` is honoured as a fallback for one release and logs a deprecation warning at startup when consulted.

Defaults:
- Development environment: `80`
- Production environment: `0`

#### Scenario: Default in Development includes a short preview

- **WHEN** the server runs with `ASPNETCORE_ENVIRONMENT=Development` and `SessionEventContent:ContentPreviewChars` is unset
- **THEN** spans for text-bearing events SHALL carry a `homespun.content.preview` attribute truncated to at most 80 characters

#### Scenario: Default in Production omits preview

- **WHEN** the server runs with `ASPNETCORE_ENVIRONMENT=Production` and `SessionEventContent:ContentPreviewChars` is unset
- **THEN** spans SHALL NOT carry a `homespun.content.preview` attribute

#### Scenario: Explicit preview length in Production emits a startup warning

- **WHEN** the server starts in Production with `SessionEventContent:ContentPreviewChars > 0`
- **THEN** a warning SHALL be logged at startup indicating that content previews will be shipped to Seq
- **AND** the configured value SHALL still take effect

#### Scenario: Preview truncation respects the configured length

- **WHEN** an event's content text length exceeds `ContentPreviewChars`
- **THEN** the emitted `homespun.content.preview` attribute SHALL be exactly `ContentPreviewChars` characters long followed by an ellipsis character

#### Scenario: Legacy config section is honoured for one release

- **WHEN** `SessionEventLog:ContentPreviewChars` is set and `SessionEventContent:ContentPreviewChars` is not
- **THEN** the legacy value SHALL be applied
- **AND** a deprecation warning SHALL be logged at startup

### Requirement: SingleContainer agent execution mode is gated to Development

The system SHALL support an `AgentExecution:Mode = SingleContainer` configuration that points all agent sessions at a pre-running worker reachable at `AgentExecution:SingleContainer:WorkerUrl`. This mode SHALL only be permitted when `ASPNETCORE_ENVIRONMENT = Development`.

#### Scenario: SingleContainer mode in Development succeeds

- **WHEN** the server starts with `ASPNETCORE_ENVIRONMENT=Development` and `AgentExecution:Mode=SingleContainer` and a valid `AgentExecution:SingleContainer:WorkerUrl`
- **THEN** the server SHALL register `SingleContainerAgentExecutionService` as `IAgentExecutionService`
- **AND** the server SHALL NOT register `DockerAgentExecutionService`

#### Scenario: SingleContainer mode in Production fails at startup

- **WHEN** the server starts with `ASPNETCORE_ENVIRONMENT=Production` and `AgentExecution:Mode=SingleContainer`
- **THEN** the server SHALL throw `InvalidOperationException` at startup with a message naming the offending configuration key
- **AND** the process SHALL NOT begin serving requests

#### Scenario: Missing worker URL fails at startup

- **WHEN** the server starts with `AgentExecution:Mode=SingleContainer` and no `AgentExecution:SingleContainer:WorkerUrl`
- **THEN** the server SHALL throw at startup naming the missing configuration key

### Requirement: SingleContainer mode enforces a single active session

When running under `SingleContainerAgentExecutionService`, the system SHALL permit at most one concurrent agent session and SHALL surface a user-visible error when a second is attempted.

#### Scenario: First session starts normally

- **WHEN** no agent session is active and a new session is started
- **THEN** the service SHALL forward the start request to the configured worker URL
- **AND** the service SHALL record the session as the current active session

#### Scenario: Second concurrent session throws SingleContainerBusyException

- **WHEN** an agent session is already active and a second session start is attempted
- **THEN** the service SHALL throw `SingleContainerBusyException` carrying the requested session id and the currently-active session id
- **AND** the service SHALL NOT forward the request to the worker

#### Scenario: Second session surfaces a user-visible error toast

- **WHEN** `SingleContainerBusyException` is thrown by the service
- **THEN** the calling code SHALL transition the requested session to an error state with a user-friendly message
- **AND** a SignalR `BroadcastSessionError` SHALL be dispatched to the client naming the busy condition
- **AND** a log entry at `Error` level SHALL be emitted naming both session ids

#### Scenario: Stopping the active session clears the slot

- **WHEN** the active session is stopped
- **THEN** a subsequent start request SHALL succeed as if no session had been active

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

<!-- The question.pending / plan.pending / status.resumed Custom event names are retired.
     They were never a stand-alone requirement in session-messaging; they appeared as
     examples inside the "Non-canonical concerns" scenario, which the MODIFIED block above
     rewrites to drop them. No top-level REMOVED Requirements block is therefore needed —
     downstream consumers (reducer, client) drop them via the questions-plans-as-tools
     change. -->

