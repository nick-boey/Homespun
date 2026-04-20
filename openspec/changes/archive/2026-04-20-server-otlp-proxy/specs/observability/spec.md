## ADDED Requirements

### Requirement: Server hosts an OTLP receiver for worker and client telemetry

The server SHALL accept OTLP/HTTP protobuf at `POST /api/otlp/v1/logs` and `POST /api/otlp/v1/traces`. The receiver SHALL preserve `traceId` and `spanId` byte-for-byte when re-exporting to downstream sinks.

#### Scenario: client ships logs through the server proxy
- **WHEN** a client POSTs an `ExportLogsServiceRequest` protobuf body to `/api/otlp/v1/logs` with `Content-Type: application/x-protobuf`
- **THEN** the server returns 202 Accepted with body `{"partialSuccess":{"rejectedLogRecords":0}}`
- **AND** the log record's `traceId` and `spanId` reach Seq unchanged
- **AND** the log record reaches the Aspire dashboard unchanged

#### Scenario: malformed protobuf is rejected without fan-out
- **WHEN** a POST body cannot be parsed as the expected `Export*ServiceRequest`
- **THEN** the server returns 400 Bad Request
- **AND** no upstream sink receives a dispatched request

#### Scenario: unsupported Content-Type is rejected
- **WHEN** a POST arrives with `Content-Type: application/json` (JSON-OTLP is not accepted)
- **THEN** the server returns 415 Unsupported Media Type

#### Scenario: gzip-encoded body is decompressed before parse
- **WHEN** a POST arrives with `Content-Encoding: gzip`
- **THEN** the server decompresses before parsing
- **AND** downstream behaviour matches an uncompressed equivalent body

#### Scenario: upstream sink failure does not propagate
- **WHEN** the server parses a valid request but both Seq and the Aspire dashboard return 500
- **THEN** the server still returns 202 to the client
- **AND** a Warning log is emitted naming each failing destination

#### Scenario: body size beyond 4 MiB is rejected
- **WHEN** a POST body exceeds 4 MiB
- **THEN** the server returns 413 Payload Too Large

### Requirement: Content preview and secret attributes are scrubbed in the proxy

The receiver SHALL enforce `SessionEventLog:ContentPreviewChars` against the attribute key `homespun.content.preview` and SHALL redact attribute values whose key (case-insensitive) contains any configured secret substring.

#### Scenario: content preview removed when ContentPreviewChars is zero
- **WHEN** `SessionEventLog:ContentPreviewChars = 0` and an incoming log record attribute has key `homespun.content.preview`
- **THEN** the scrubber removes that attribute from the request before fan-out

#### Scenario: content preview truncated when ContentPreviewChars is positive
- **WHEN** `SessionEventLog:ContentPreviewChars = 80` and an incoming attribute value is longer than 80 characters
- **THEN** the scrubber truncates the value to 80 characters followed by an ellipsis

#### Scenario: authorization-bearing attribute is redacted
- **WHEN** a log or span record contains an attribute with key matching `authorization` (case-insensitive)
- **THEN** the scrubber replaces its string value with `[REDACTED]` before fan-out

### Requirement: Receiver fans out to Seq and Aspire dashboard in parallel

The proxy SHALL dispatch each accepted request to every configured destination concurrently. Destinations whose URL cannot be resolved SHALL be skipped silently without affecting others. The Seq leg is driven by the `OtlpFanout:SeqBaseUrl` config value; the Aspire leg is driven by the Aspire-injected env var `OTEL_EXPORTER_OTLP_ENDPOINT`.

#### Scenario: both destinations resolvable
- **WHEN** `OtlpFanout:SeqBaseUrl` is set AND `OTEL_EXPORTER_OTLP_ENDPOINT` is set
- **THEN** each accepted request triggers two outbound POSTs in parallel
- **AND** each outbound body is byte-identical to the scrubbed request

#### Scenario: Seq leg attaches the API key header
- **WHEN** `OtlpFanout:SeqApiKey` is non-empty
- **THEN** the outbound Seq POST includes `X-Seq-ApiKey: {value}`

#### Scenario: Aspire leg forwards the dashboard auth headers
- **WHEN** `OTEL_EXPORTER_OTLP_HEADERS` contains `key=value` pairs
- **THEN** the outbound Aspire POST includes each pair as a request header

#### Scenario: Aspire leg skipped when dashboard env absent
- **WHEN** `OTEL_EXPORTER_OTLP_ENDPOINT` is unset (e.g. production without Aspire)
- **THEN** no outbound POST is made to the Aspire leg
- **AND** the Seq leg still dispatches normally if configured

### Requirement: Legacy telemetry-config endpoint is retired

`TelemetryConfigController` and the `TelemetryConfigDto` SHALL be removed. No runtime API SHALL expose the legacy Application Insights connection string.

#### Scenario: the endpoint no longer responds
- **WHEN** a client requests `GET /api/telemetry-config`
- **THEN** the server returns 404 Not Found
