## ADDED Requirements

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
