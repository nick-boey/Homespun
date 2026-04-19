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

