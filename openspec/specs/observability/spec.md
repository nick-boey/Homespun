# observability Specification

## Purpose
TBD - created by archiving change seq-replaces-plg. Update Purpose after archive.
## Requirements
### Requirement: Seq is the canonical long-lived observability sink

Homespun SHALL emit logs and traces to Seq in both dev and prod. Metrics SHALL continue to be exported to the Aspire dashboard only; no Seq metrics sink is configured.

#### Scenario: dev profile wires Seq via Aspire hosting integration
- **WHEN** the AppHost starts any dev profile
- **THEN** a Seq container resource named `seq` is present in the Aspire model
- **AND** its OTLP ingest endpoint accepts HTTP protobuf
- **AND** every project resource that emits OTel references Seq via `.WithReference(seq)`

#### Scenario: prod compose file defines a Seq service
- **WHEN** `docker compose up -d` runs from the repo root in prod
- **THEN** a `seq` service based on `datalust/seq:2024.3` is started
- **AND** `homespun` and `worker` services point at `http://seq:5341/ingest/otlp` via env vars
- **AND** a `SEQ_API_KEY` env gates ingestion auth (empty in dev, set via Komodo in prod)

### Requirement: Server and worker dual-export OTLP to Aspire dashboard and Seq

`Homespun.ServiceDefaults` SHALL register OTLP exporters for the Aspire dashboard via per-signal env vars AND for Seq via `AddSeqEndpoint`. The previous `UseOtlpExporter()` single-destination wiring SHALL NOT remain in the code path.

#### Scenario: server logs reach both Aspire dashboard and Seq
- **WHEN** the server emits a log entry while both sinks are running
- **THEN** the entry appears in `aspire otel logs server`
- **AND** the entry appears in Seq filtered by `service.name = homespun.server`

#### Scenario: server traces reach both Aspire dashboard and Seq
- **WHEN** the server starts a span
- **THEN** the span appears in the Aspire dashboard Traces view
- **AND** the span appears in Seq's Traces view

### Requirement: SignalR hub invocations emit native OTel spans

The `Microsoft.AspNetCore.SignalR.Server` ActivitySource SHALL be registered on the tracer provider. Each hub-method invocation SHALL appear as a separate span.

#### Scenario: a hub invocation is traced
- **WHEN** a client invokes `ClaudeCodeHub.JoinSession(…)` over SignalR
- **THEN** a span named `SignalR.ClaudeCodeHub/JoinSession` is visible in Seq and the Aspire dashboard

### Requirement: PLG stack is no longer part of the observability surface

No dev or prod artifact SHALL run, reference, or provision Loki, Promtail, or Grafana. The `logging=promtail` Docker label convention SHALL be removed.

#### Scenario: Aspire model carries no PLG resources
- **WHEN** the AppHost builds the distributed application model
- **THEN** no resource with image `grafana/loki`, `grafana/promtail`, or `grafana/grafana` is present

#### Scenario: docker-compose carries no PLG services
- **WHEN** a developer inspects `docker-compose.yml`
- **THEN** no service is defined for Loki, Promtail, or Grafana
- **AND** no named volume references `loki-data` or `grafana-data`

### Requirement: Server stdout no longer emits the custom Promtail-format JSON

`JsonConsoleFormatter` and its registration SHALL be removed. Stdout uses ASP.NET Core's default formatter for local diagnostics only; OTLP export is the authoritative sink.

#### Scenario: the custom formatter type no longer exists
- **WHEN** a developer searches `src/Homespun.Server` for `JsonConsoleFormatter`
- **THEN** no source-code references remain outside archived OpenSpec content

### Requirement: All emitted spans are documented in the trace dictionary

`docs/traces/dictionary.md` SHALL be the single source of truth for span names, tracer/ActivitySource names, and Homespun-namespaced attribute keys used by any tier.

#### Scenario: dictionary is present at the canonical location
- **WHEN** a developer opens `docs/traces/dictionary.md`
- **THEN** it contains sections for conventions, tracer registry, client traces, server traces, worker traces, and the drift-check description

#### Scenario: dictionary entries name originator and kind
- **WHEN** a developer reads any H3 entry naming a span
- **THEN** the entry names the originator tier (client / server / worker), span kind (SERVER / CLIENT / INTERNAL / PRODUCER / CONSUMER), and required + optional attributes

### Requirement: CI drift check enforces dictionary coverage

Each tier's test suite SHALL include a drift check that fails when span or tracer names in source code are missing from the dictionary, or vice-versa.

#### Scenario: undocumented span name fails CI
- **WHEN** a contributor adds `tracer.startSpan("homespun.new.span")` without adding the corresponding H3 entry to the dictionary
- **THEN** the tier's drift-check test fails with a message identifying the undocumented name and its source location

#### Scenario: orphan dictionary entry fails CI
- **WHEN** the dictionary lists an H3 entry for `homespun.defunct.span` that no code path in the corresponding tier emits
- **THEN** the tier's drift-check test fails with a message identifying the orphan

#### Scenario: dynamic span names are allowlisted
- **WHEN** a contributor uses an interpolated span name (e.g. `SignalR.{hub}/{method}`) that cannot be matched statically
- **THEN** an allowlist entry with a justifying comment exempts it from the drift check

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

### Requirement: Worker emits OpenTelemetry logs and traces

The worker SHALL use `@opentelemetry/sdk-node` to emit logs and traces to the server's OTLP proxy. The worker SHALL NOT emit custom JSON stdout for ingestion; stdout is for local-debugging fallback only.

#### Scenario: worker boot wires the OTel SDK
- **WHEN** the worker process starts
- **THEN** `src/instrumentation.ts` is imported before any other module
- **AND** `NodeSDK.start()` is called before the Hono app begins listening

#### Scenario: worker attaches identity resource attributes
- **WHEN** the worker is spawned with env `HOMESPUN_SESSION_ID`, `HOMESPUN_ISSUE_ID`, `HOMESPUN_PROJECT_NAME`, `HOMESPUN_AGENT_MODE`
- **THEN** every emitted log and span carries resource attributes `service.name = homespun.worker`, `homespun.session.id`, `homespun.issue.id`, `homespun.project.name`, `homespun.agent.mode`

#### Scenario: worker exports reach Seq via the server proxy
- **WHEN** the worker emits a log
- **THEN** the request targets `${OTLP_PROXY_URL}/logs` with `Content-Type: application/x-protobuf`
- **AND** the record arrives in Seq with the worker's resource attributes intact

### Requirement: Worker trace context is inherited from the inbound HTTP request

Trace context propagation from server to worker SHALL use the `traceparent` HTTP header handled by `@opentelemetry/instrumentation-http` auto-instrumentation. The worker SHALL NOT require a custom `TRACEPARENT` environment variable shim.

#### Scenario: worker spans are linked to the server's outgoing span
- **WHEN** the server POSTs to the worker with a traceparent header
- **THEN** every span the worker emits while handling that request carries the same TraceId
- **AND** the worker's root span's parent is the server's outgoing span

### Requirement: Worker flushes batched telemetry on graceful shutdown

The worker SHALL register SIGTERM and SIGINT handlers that `await sdk.shutdown()` before exiting. The server SHALL use `docker stop --time 3` (not `docker kill`) so workers receive SIGTERM.

#### Scenario: server-initiated stop flushes worker telemetry
- **WHEN** the server calls `StopContainerAsync` for a worker container
- **THEN** the worker receives SIGTERM
- **AND** at least one final OTLP batch is dispatched before the container exits

#### Scenario: batch delay is short enough to minimise loss
- **WHEN** the worker's log record processor is configured
- **THEN** its `scheduledDelayMillis` is at most 1000 ms

### Requirement: Server spawns worker containers with OTLP proxy env

`DockerAgentExecutionService` SHALL inject `OTLP_PROXY_URL` into every `docker run` invocation. The URL SHALL resolve to the server's `/api/otlp/v1` endpoint from within the worker's container network view.

#### Scenario: host-mode server resolves proxy URL to host.docker.internal
- **WHEN** the server runs on the host (not in a container) and spawns a worker via DooD
- **THEN** the worker receives `OTLP_PROXY_URL=http://host.docker.internal:5101/api/otlp/v1`

#### Scenario: container-mode server resolves proxy URL to its container hostname
- **WHEN** the server runs inside a container and spawns a sibling worker
- **THEN** the worker receives `OTLP_PROXY_URL=http://server:8080/api/otlp/v1`

### Requirement: Legacy worker logger is retired

`src/Homespun.Worker/src/utils/logger.ts` and its tests SHALL be removed. All worker log call sites SHALL use `logs.getLogger('homespun.worker').emit(...)`.

#### Scenario: legacy logger file absent
- **WHEN** a developer searches the worker source tree
- **THEN** no file at `src/utils/logger.ts` exists
- **AND** no import of `./utils/logger` remains

### Requirement: Client log controller is retired

`/api/log/client` and its controller SHALL be removed once the worker no longer calls it. `ClientLogController.cs` and `ClientLogEntry.cs` SHALL be deleted.

#### Scenario: legacy endpoint returns 404
- **WHEN** any client POSTs to `/api/log/client`
- **THEN** the server returns 404 Not Found

### Requirement: React client emits OpenTelemetry traces and logs

The React client SHALL use `@opentelemetry/sdk-trace-web` + `@opentelemetry/sdk-logs` to emit signals through the server OTLP proxy. No custom telemetry schema SHALL remain.

#### Scenario: client boot registers OTel providers
- **WHEN** `main.tsx` begins executing
- **THEN** `./instrumentation` is the first import resolved
- **AND** a `WebTracerProvider` is registered with a `CompositePropagator` of W3C trace-context and baggage
- **AND** a `LoggerProvider` is set as the global logger provider

#### Scenario: fetch requests propagate traceparent
- **WHEN** application code calls `fetch('/api/projects')` under an active span
- **THEN** the outbound request carries `traceparent` and `tracestate` headers derived from the active context
- **AND** the response's corresponding server-side span shares the client span's TraceId

#### Scenario: OTLP exporter endpoints are excluded from auto-instrumentation
- **WHEN** the client's own exporter POSTs to `/api/otlp/v1/traces` or `/logs`
- **THEN** those requests are NOT themselves traced (preventing trace loops)

### Requirement: SignalR client-to-server invocations propagate traceparent as the first argument

Every hub method exposed to JavaScript clients SHALL accept a `traceparent` string as its first parameter. The client SHALL wrap `invoke` with a helper that injects the active context as traceparent. The server SHALL extract via a `TraceparentHubFilter` and parent its span to the client's context.

#### Scenario: hub invocation carries client traceparent
- **WHEN** a client calls `connection.invoke('JoinSession', sessionId)` under an active span
- **THEN** the serialised invocation arguments begin with a traceparent string followed by `sessionId`
- **AND** the server-side span named `SignalR.ClaudeCodeHub/JoinSession` has that traceparent's `span_id` as its `parent_span_id`

#### Scenario: missing traceparent falls through gracefully
- **WHEN** a hub method is invoked without a well-formed traceparent first argument
- **THEN** the filter does not create a child span
- **AND** the method executes normally (no error)

### Requirement: SignalR server-to-client broadcasts propagate traceparent via the envelope

`SessionEventEnvelope` SHALL carry an optional `Traceparent` string field. The server SHALL populate it from `Activity.Current` before broadcast. The client SHALL restore context before dispatching the envelope to its reducer.

#### Scenario: client envelope span is parented to server ingest span
- **WHEN** the server ingests an A2A event and broadcasts an envelope
- **THEN** the envelope's `Traceparent` field is set to the current activity's context
- **AND** the client's `homespun.envelope.rx` span uses that traceparent as its parent

### Requirement: The custom client telemetry stack is retired

All files under `src/Homespun.Web/src/lib/telemetry/`, `src/lib/session-event-log.ts`, the `TelemetryProvider`, `useTelemetry` hook, and `ClientTelemetryController.cs` SHALL be removed. No alternative custom telemetry schema SHALL be introduced.

#### Scenario: legacy paths no longer exist
- **WHEN** a developer greps the repo for `TelemetryService`, `TelemetryProvider`, `useTelemetry`, `sessionEventLog`, or `ClientTelemetryController`
- **THEN** no matches appear outside archived OpenSpec content

### Requirement: Native SignalR ActivitySource is disabled in favour of the Homespun filter

`tracing.AddSource("Microsoft.AspNetCore.SignalR.Server")` SHALL NOT be registered when `TraceparentHubFilter` is installed. The filter is the authoritative SignalR span source for this application.

#### Scenario: only the filter span appears
- **WHEN** a client invokes a hub method with a valid traceparent
- **THEN** exactly one SignalR-named span appears in Seq for that invocation (emitted by `Homespun.Signalr`)
- **AND** no span named `Microsoft.AspNetCore.SignalR.Server.*` appears for that invocation

