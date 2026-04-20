## ADDED Requirements

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
