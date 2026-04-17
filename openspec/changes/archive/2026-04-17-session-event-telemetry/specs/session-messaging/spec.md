## ADDED Requirements

### Requirement: Structured logging covers every pipeline hop

The system SHALL emit a structured log entry at each of six defined hops in the session event pipeline, under the `SessionEventLog` category, carrying correlation fields sufficient to reconstruct the end-to-end journey of a single A2A event from worker emission to client reducer application.

The six hops SHALL be identified by a `Hop` field taking exactly one of the following values:

- `worker.a2a.emit` — emitted in the worker immediately after an A2A event is formatted for SSE output
- `server.sse.rx` — emitted on the server immediately after a worker SSE event is successfully parsed
- `server.ingest.append` — emitted on the server immediately after the A2A event record is written to the event log with its server-assigned `seq` and `eventId`
- `server.agui.translate` — emitted on the server once per AG-UI envelope produced by translating the parent A2A event
- `server.signalr.tx` — emitted on the server immediately after the envelope is dispatched to the SignalR group for the session
- `client.signalr.rx` — emitted on the client immediately after an envelope is received from SignalR, before any dedup or reducer action
- `client.reducer.apply` — emitted on the client immediately after the reducer folds the envelope into state

#### Scenario: A message traverses every hop with a shared messageId

- **WHEN** the worker emits an A2A `Message` event with `messageId = M1` for a session `S1` and the client successfully renders it
- **THEN** the Loki log stream SHALL contain at least one entry per hop (`worker.a2a.emit`, `server.sse.rx`, `server.ingest.append`, at least one `server.agui.translate`, at least one `server.signalr.tx`, at least one `client.signalr.rx`, at least one `client.reducer.apply`)
- **AND** every such entry SHALL carry `SessionId = S1` and `MessageId = M1`

#### Scenario: Status update hops use status timestamp as correlation

- **WHEN** the worker emits an A2A `StatusUpdate` event for a session `S1` with `status.timestamp = T1`
- **THEN** every logged hop for that event SHALL carry `SessionId = S1` and `StatusTimestamp = T1`
- **AND** hops at `server.ingest.append` and later SHALL additionally carry the server-assigned `EventId`

#### Scenario: AG-UI hops reference parent A2A correlation

- **WHEN** a single A2A `Message` event translates to multiple AG-UI envelopes (for example a `TEXT_MESSAGE_START` + `TEXT_MESSAGE_CONTENT` + `TEXT_MESSAGE_END` triple)
- **THEN** every `server.agui.translate`, `server.signalr.tx`, `client.signalr.rx`, and `client.reducer.apply` entry for those envelopes SHALL carry the parent A2A event's `MessageId` and the same `EventId`
- **AND** each entry SHALL carry the specific `AGUIType` it represents

### Requirement: Log entries expose correlation fields at the top level

Every `SessionEventLog` entry SHALL expose its correlation fields as top-level JSON properties suitable for LogQL `| json` filtering without further parsing.

Required top-level fields on every entry:
- `Timestamp` — ISO 8601 with milliseconds
- `Level` — one of `Information`, `Warning`, `Error`
- `SourceContext` — `Homespun.SessionEvents` (server) or `Homespun.ClientSessionEvents` (client-originated via `/api/log/client`) or `Worker` (worker-emitted)
- `Component` — `Worker`, `Server`, or `Web`
- `Hop` — one of the values defined in the hops requirement above
- `SessionId` — the Homespun session id, equal to the A2A `contextId`

Conditional top-level fields (emitted when applicable):
- `TaskId` — the A2A `taskId`
- `MessageId` — present for entries whose parent A2A event is a `Message`
- `ArtifactId` — present for entries whose parent A2A event is a `TaskArtifactUpdateEvent`
- `StatusTimestamp` — present for entries whose parent A2A event is a `TaskStatusUpdateEvent`
- `EventId` — present from `server.ingest.append` onward
- `Seq` — present from `server.ingest.append` onward
- `A2AKind` — present on A2A-level hops (`worker.a2a.emit`, `server.sse.rx`, `server.ingest.append`)
- `AGUIType` — present on AG-UI-level hops (`server.agui.translate`, `server.signalr.tx`, `client.signalr.rx`, `client.reducer.apply`)
- `AGUICustomName` — present when `AGUIType = "CUSTOM"`
- `ContentPreview` — present when `SessionEventLog:ContentPreviewChars` is greater than zero AND the event carries text content

#### Scenario: LogQL query by messageId returns flat fields

- **WHEN** a query `{app="homespun"} | json | MessageId="M1"` is issued against Loki
- **THEN** every matching entry SHALL expose `Hop`, `SessionId`, `MessageId` as directly-addressable fields without requiring nested JSON extraction

#### Scenario: Non-Message events omit MessageId

- **WHEN** a `StatusUpdate` is logged
- **THEN** the entry SHALL NOT include a `MessageId` field
- **AND** the entry SHALL include `StatusTimestamp` instead

### Requirement: Content preview is configurable and defaults to safe values

The system SHALL provide a single configuration key `SessionEventLog:ContentPreviewChars` that governs truncation of the `ContentPreview` field. A value of `0` SHALL disable the `ContentPreview` field entirely.

Defaults:
- Development environment: `80`
- Production environment: `0`

#### Scenario: Default in Development includes a short preview

- **WHEN** the server runs with `ASPNETCORE_ENVIRONMENT=Development` and `SessionEventLog:ContentPreviewChars` is unset
- **THEN** log entries for text-bearing events SHALL include a `ContentPreview` field truncated to at most 80 characters

#### Scenario: Default in Production omits preview

- **WHEN** the server runs with `ASPNETCORE_ENVIRONMENT=Production` and `SessionEventLog:ContentPreviewChars` is unset
- **THEN** log entries SHALL NOT include a `ContentPreview` field

#### Scenario: Explicit preview length in Production emits a startup warning

- **WHEN** the server starts in Production with `SessionEventLog:ContentPreviewChars > 0`
- **THEN** a warning SHALL be logged at startup indicating that content previews will be shipped to logs
- **AND** the configured value SHALL still take effect

#### Scenario: Preview truncation respects the configured length

- **WHEN** an event's content text length exceeds `ContentPreviewChars`
- **THEN** the emitted `ContentPreview` SHALL be exactly `ContentPreviewChars` characters long followed by an ellipsis character

### Requirement: Client telemetry is forwarded via a server endpoint

The server SHALL expose `POST /api/log/client` which accepts a batched array of client log entries and forwards each entry to the server's Serilog pipeline under `SourceContext = "Homespun.ClientSessionEvents"`.

#### Scenario: Batch accepted and forwarded

- **WHEN** a client posts a batch of up to 100 well-formed entries to `/api/log/client`
- **THEN** the endpoint SHALL respond `202 Accepted`
- **AND** each entry SHALL be forwarded to Serilog at its self-reported `Level`
- **AND** each forwarded entry SHALL have `SourceContext = "Homespun.ClientSessionEvents"` applied regardless of what the client reported

#### Scenario: Oversized batch is rejected

- **WHEN** a client posts a batch of more than 100 entries
- **THEN** the endpoint SHALL respond `413 Payload Too Large`
- **AND** no entries from the batch SHALL be forwarded to Serilog

#### Scenario: Malformed entry rejects the whole batch

- **WHEN** a client posts a batch in which any entry fails JSON schema validation
- **THEN** the endpoint SHALL respond `400 Bad Request` with an error body naming the first invalid entry's index
- **AND** no entries from the batch SHALL be forwarded to Serilog

### Requirement: Client batcher is self-defensive

The client-side `sessionEventLog` batcher SHALL buffer entries in memory and flush them to `/api/log/client` on a bounded schedule without ever causing cascading failures during outages.

#### Scenario: Flush on entry count threshold

- **WHEN** the batcher's buffer contains 50 entries
- **THEN** the batcher SHALL flush the buffer to `/api/log/client` within the next tick

#### Scenario: Flush on age threshold

- **WHEN** the oldest buffered entry is more than 500 milliseconds old
- **THEN** the batcher SHALL flush the buffer

#### Scenario: Flush on unload

- **WHEN** the browser emits `beforeunload` or `pagehide`
- **THEN** the batcher SHALL attempt a best-effort flush using `navigator.sendBeacon`

#### Scenario: Server outage does not cascade

- **WHEN** `/api/log/client` responds with a non-2xx status or the fetch rejects
- **THEN** the batcher SHALL drop the failed batch without re-queueing
- **AND** the batcher SHALL emit at most one `console.warn` per flush failure
- **AND** the batcher SHALL NOT route its own failure through `sessionEventLog`

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
