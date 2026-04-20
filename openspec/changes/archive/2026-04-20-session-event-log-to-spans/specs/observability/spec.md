## ADDED Requirements

### Requirement: A2A → AG-UI pipeline is observable as spans, not hops

The server SHALL represent the A2A → AG-UI ingestion pipeline as OpenTelemetry spans on `Homespun.SessionPipeline`. The former `SessionEventLog` hop logger SHALL be removed.

#### Scenario: each ingested A2A event produces one ingest span
- **WHEN** the server receives an A2A event from a worker
- **THEN** exactly one span named `homespun.session.ingest` is emitted on `Homespun.SessionPipeline`
- **AND** it carries attributes `homespun.session.id`, `homespun.a2a.kind`, `homespun.seq`
- **AND** it contains span events `sse.rx`, `ingest.append`, `signalr.tx` in that order

#### Scenario: translate step is a child span
- **WHEN** the server translates an A2A event to AG-UI
- **THEN** a child span named `homespun.agui.translate` is emitted inside the ingest span
- **AND** it carries attributes `homespun.a2a.kind`, `homespun.agui.type`

### Requirement: SignalR hub lifecycle is observable as spans

Hub connect / disconnect / join / leave events SHALL be represented as spans on `Homespun.Signalr`. The former `SessionEventLog.LogHubHop` logger SHALL be removed.

#### Scenario: hub connection span spans the connection lifetime
- **WHEN** a client connects to a hub and later disconnects
- **THEN** exactly one span named `homespun.signalr.connect` covers the interval
- **AND** it contains a `connected` event at start and a `disconnected` event at end

#### Scenario: join emits a discrete span
- **WHEN** a client calls `ClaudeCodeHub.JoinSession(…)`
- **THEN** one span named `homespun.signalr.join` is emitted
- **AND** it carries `homespun.session.id` and `signalr.connection.id`

### Requirement: Worker a2a emits are spans

The worker SHALL emit `homespun.a2a.emit` spans (kind PRODUCER) for each A2A event written upstream. The former `sessionEventLog('worker.a2a.emit', …)` call SHALL be removed.

#### Scenario: every worker a2a emission is a span
- **WHEN** the worker writes an A2A event to the SSE response stream
- **THEN** a span named `homespun.a2a.emit` is emitted on `homespun.worker`
- **AND** it carries `homespun.session.id`, `homespun.a2a.kind`, and — when applicable — `homespun.task.id`, `homespun.message.id`, `homespun.artifact.id`

### Requirement: Client hub-lifecycle hops become span events

The React client SHALL represent SignalR connection lifecycle as span events on a long-lived `homespun.signalr.client.connect` span.

#### Scenario: lifecycle events group under one client span
- **WHEN** a hub connection undergoes connect → reconnect → disconnect
- **THEN** one `homespun.signalr.client.connect` span covers the lifetime
- **AND** it contains events `connected`, `reconnecting`, `reconnected`, `disconnected` as they occur

### Requirement: Content-preview gating is preserved under the new span model

Attribute `homespun.content.preview` on any span SHALL be gated through an `IContentPreviewGate` backed by `SessionEventContent:ContentPreviewChars`. Behaviour parity with the former `SessionEventLog.TruncatePreview` is required.

#### Scenario: preview removed when chars zero
- **WHEN** `SessionEventContent:ContentPreviewChars = 0`
- **THEN** no span carries a `homespun.content.preview` attribute

#### Scenario: preview truncated when chars positive
- **WHEN** `SessionEventContent:ContentPreviewChars = 80` and a preview text of 120 chars is supplied
- **THEN** the attribute value is 80 chars followed by an ellipsis

#### Scenario: legacy config section is honoured for one release
- **WHEN** `SessionEventLog:ContentPreviewChars` is set and `SessionEventContent:ContentPreviewChars` is not
- **THEN** the legacy value is used
- **AND** a deprecation warning is logged at startup
