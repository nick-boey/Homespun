## ADDED Requirements

### Requirement: Full-body session message logging gated by a single env var

The system SHALL provide a single environment variable `HOMESPUN_DEBUG_FULL_MESSAGES` that, when set to `true`, causes the worker, server, and web client to emit full A2A, AG-UI, and Claude Agent SDK message bodies as OpenTelemetry log events. The flag SHALL default to off in every launch profile (`dev-mock`, `dev-live`, `dev-windows`, `dev-container`, `prod`). When off, behaviour SHALL be identical to today: only the existing `homespun.content.preview` span attribute (truncated per `SessionEventContent:ContentPreviewChars`) and the worker's `DEBUG_AGENT_SDK` boundary log SHALL carry message-body data.

Setting `HOMESPUN_DEBUG_FULL_MESSAGES=true` SHALL imply, on the worker container env, `DEBUG_AGENT_SDK=true` and `CONTENT_PREVIEW_CHARS=-1` when those variables are otherwise unset; SHALL imply, on the server env, `SessionEventContent__ContentPreviewChars=-1` when otherwise unset; and SHALL set `VITE_HOMESPUN_DEBUG_FULL_MESSAGES=true` for the web build. Implications SHALL NOT override values explicitly set by the user.

#### Scenario: umbrella flag emits A2A bodies on the server

- **WHEN** the server is started with `HOMESPUN_DEBUG_FULL_MESSAGES=true` and ingests an A2A event
- **THEN** an OTel log entry SHALL be emitted on logger `Homespun.SessionPipeline` with the rendered message including the full A2A JSON payload as the `Body` property
- **AND** the entry SHALL carry `homespun.session.id`, `homespun.a2a.kind`, `homespun.seq` attributes
- **AND** the entry SHALL appear in both Seq and the Aspire dashboard

#### Scenario: umbrella flag emits AG-UI bodies on the server translator

- **WHEN** the server is started with `HOMESPUN_DEBUG_FULL_MESSAGES=true` and the A2A → AG-UI translator emits an AG-UI event
- **THEN** an OTel log entry SHALL be emitted on logger `Homespun.SessionPipeline` with the rendered message including the full AG-UI event JSON as `Body`
- **AND** the entry SHALL carry `homespun.agui.type`, `homespun.session.id`, `homespun.seq` attributes

#### Scenario: umbrella flag emits SignalR broadcast bodies on the server

- **WHEN** the server is started with `HOMESPUN_DEBUG_FULL_MESSAGES=true` and broadcasts a `SessionEventEnvelope`
- **THEN** an OTel log entry SHALL be emitted on logger `Homespun.Signalr` with the full envelope JSON as `Body`
- **AND** the entry SHALL carry `homespun.session.id`, `homespun.seq`, and the envelope's `Traceparent` (when present)

#### Scenario: umbrella flag implies DEBUG_AGENT_SDK on the worker

- **WHEN** the AppHost wires a worker container under `HOMESPUN_DEBUG_FULL_MESSAGES=true` without an explicit `DEBUG_AGENT_SDK` value
- **THEN** the worker container env SHALL contain `DEBUG_AGENT_SDK=true`
- **AND** the worker container env SHALL contain `CONTENT_PREVIEW_CHARS=-1`

#### Scenario: explicit per-tier values are not overridden

- **WHEN** the AppHost is started with both `HOMESPUN_DEBUG_FULL_MESSAGES=true` AND `DEBUG_AGENT_SDK=false`
- **THEN** the worker container env SHALL contain `DEBUG_AGENT_SDK=false`

#### Scenario: replay endpoint tags its log entries to enable filtering

- **WHEN** the server is started with `HOMESPUN_DEBUG_FULL_MESSAGES=true` and a client calls `GET /api/sessions/{id}/events`
- **THEN** every log entry emitted by the replay path SHALL carry attribute `homespun.replay=true`
- **AND** log entries from the live path SHALL NOT carry that attribute

#### Scenario: web client emits envelope-receive logs through the OTLP proxy

- **WHEN** the web bundle is built with `VITE_HOMESPUN_DEBUG_FULL_MESSAGES=true` and the running client receives a `SessionEventEnvelope` over SignalR
- **THEN** an OTel log entry SHALL be emitted on logger `homespun.web` with the full envelope JSON as `Body`
- **AND** the entry SHALL be exported through `POST /api/otlp/v1/logs`
- **AND** the entry SHALL appear in Seq

#### Scenario: web client logging code is absent from non-debug builds

- **WHEN** the web bundle is built without `VITE_HOMESPUN_DEBUG_FULL_MESSAGES=true`
- **THEN** the envelope-receive debug log call site SHALL NOT appear in the production bundle (tree-shaken away)

### Requirement: Session-pipeline log sites use Seq-friendly message templates

Every new or modified log site introduced by the full-body logging path SHALL use a Serilog-style message template that includes the message-body placeholder, so Seq's log list view renders the value inline without requiring per-row expansion.

#### Scenario: a2a.rx template renders body inline in Seq

- **WHEN** the server emits the `a2a.rx` log entry under full-body logging
- **THEN** the message template SHALL match the form `"a2a.rx kind={Kind} seq={Seq} body={Body}"` (placeholder names may differ; the template SHALL include all three)
- **AND** Seq's log list view SHALL display the rendered text including the `body` value without an expansion click

#### Scenario: agui.translate template renders body inline in Seq

- **WHEN** the server emits the `agui.translate` log entry under full-body logging
- **THEN** the message template SHALL include the AG-UI event type and the body placeholder

#### Scenario: agui.tx template renders envelope inline in Seq

- **WHEN** the server emits the `agui.tx` log entry under full-body logging
- **THEN** the message template SHALL include the seq number and the envelope body placeholder

## MODIFIED Requirements

### Requirement: Content preview and secret attributes are scrubbed in the proxy

The receiver SHALL enforce `SessionEventContent:ContentPreviewChars` against the attribute key `homespun.content.preview` and SHALL redact attribute values whose key (case-insensitive) contains any configured secret substring. The configuration value `-1` SHALL be a sentinel meaning "no truncation": when set, the scrubber SHALL pass the `homespun.content.preview` attribute through unchanged regardless of length.

#### Scenario: content preview removed when ContentPreviewChars is zero

- **WHEN** `SessionEventContent:ContentPreviewChars = 0` and an incoming log record attribute has key `homespun.content.preview`
- **THEN** the scrubber removes that attribute from the request before fan-out

#### Scenario: content preview truncated when ContentPreviewChars is positive

- **WHEN** `SessionEventContent:ContentPreviewChars = 80` and an incoming attribute value is longer than 80 characters
- **THEN** the scrubber truncates the value to 80 characters followed by an ellipsis

#### Scenario: content preview passes through unchanged when ContentPreviewChars is -1

- **WHEN** `SessionEventContent:ContentPreviewChars = -1` and an incoming attribute value is 5000 characters long
- **THEN** the scrubber leaves the attribute value unchanged at 5000 characters
- **AND** the attribute reaches every fan-out destination unmodified

#### Scenario: authorization-bearing attribute is redacted

- **WHEN** a log or span record contains an attribute with key matching `authorization` (case-insensitive)
- **THEN** the scrubber replaces its string value with `[REDACTED]` before fan-out

### Requirement: Content-preview gating is preserved under the new span model

Attribute `homespun.content.preview` on any span SHALL be gated through an `IContentPreviewGate` backed by `SessionEventContent:ContentPreviewChars`. Behaviour parity with the former `SessionEventLog.TruncatePreview` is required. The gate SHALL additionally honour the `-1` sentinel as "no truncation": when set, the full preview text SHALL be returned unchanged.

#### Scenario: preview removed when chars zero

- **WHEN** `SessionEventContent:ContentPreviewChars = 0`
- **THEN** no span carries a `homespun.content.preview` attribute

#### Scenario: preview truncated when chars positive

- **WHEN** `SessionEventContent:ContentPreviewChars = 80` and a preview text of 120 chars is supplied
- **THEN** the attribute value is 80 chars followed by an ellipsis

#### Scenario: preview unbounded when chars is -1

- **WHEN** `SessionEventContent:ContentPreviewChars = -1` and a preview text of 5000 chars is supplied
- **THEN** the attribute value is the full 5000 chars unchanged

#### Scenario: legacy config section is honoured for one release

- **WHEN** `SessionEventLog:ContentPreviewChars` is set and `SessionEventContent:ContentPreviewChars` is not
- **THEN** the legacy value is used
- **AND** a deprecation warning is logged at startup
