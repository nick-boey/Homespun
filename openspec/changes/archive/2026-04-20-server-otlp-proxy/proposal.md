## Why

Worker and client need to ship OTel signals into the same Seq + Aspire sinks the server uses. Routing through the server (rather than direct to sinks) is a firm user requirement: the server owns the auth boundary, PII scrubbing, and abstracts the sink topology from clients. The existing `/api/client-telemetry` and `/api/log/client` endpoints are custom JSON schemas that predate OTLP adoption — they re-invent OTLP shape, lose TraceId/SpanId fidelity, and would need per-attribute shim code to join worker logs onto client-originated traces.

Accept standard OTLP at the server instead, re-export byte-identical to downstream sinks. Trace context survives the proxy. Client and worker SDKs use off-the-shelf OTel exporters — no custom serialiser. Server acts as a mini-collector: parse → scrub → fan out.

## What Changes

- **Add `POST /api/otlp/v1/logs` and `POST /api/otlp/v1/traces`** to the server, accepting `application/x-protobuf` bodies shaped as `ExportLogsServiceRequest` and `ExportTraceServiceRequest` respectively. Per earlier exploration, protobuf is the on-wire format end-to-end; JSON was rejected in favour of using standard off-the-shelf JS SDK exporters.
- **Vendor OTLP `.proto` files** under `proto/opentelemetry/proto/{common,resource,logs,trace,collector/logs,collector/trace}/v1/` from `open-telemetry/opentelemetry-proto`. Wire `Grpc.Tools` codegen in `Homespun.Server.csproj` so the generated `OpenTelemetry.Proto.*` types compile as first-party.
- **Introduce `IOtlpFanout`** — given a parsed request, re-serialise to protobuf and POST to (a) Seq at `/ingest/otlp/v1/{logs,traces}` with `X-Seq-ApiKey`, (b) the Aspire dashboard OTLP HTTP endpoint. Upstream failures are logged and swallowed — the proxy always returns 202 so clients never retry-amplify. Aspire leg URL + auth headers are read from the Aspire-injected env vars `OTEL_EXPORTER_OTLP_ENDPOINT` and `OTEL_EXPORTER_OTLP_HEADERS` at construction time (confirmed via live probe of a running AppHost); the Aspire leg is skipped when those env vars are absent (prod behaviour — no dashboard in prod).
- **Introduce `IOtlpScrubber`** — walks requests and enforces the `SessionEventLog:ContentPreviewChars` gate on attribute key `homespun.content.preview`. Redacts attribute values whose key matches a configurable secret-substring list (`token`, `secret`, `password`, `authorization`).
- **Delete `TelemetryConfigController`** and its Application Insights shim DTO — dead code.
- **Retain `ClientTelemetryController` and `ClientLogController`** in this change; their deletion is scoped to `client-otel` (#4) and `worker-otel` (#3) respectively, once those changes cut over the callers.

## Capabilities

### Modified Capabilities
- `observability` — adds the OTLP receiver surface (this change) to the sink topology established by `seq-replaces-plg`.

## Impact

- **Files touched:**
  - `proto/opentelemetry/proto/**/*.proto` — vendored (new folder at repo root or under `src/Homespun.Server/proto/`).
  - `proto/PROTO_VERSION` — marker file recording upstream commit/tag.
  - `src/Homespun.Server/Homespun.Server.csproj` — `<Protobuf>` items + `Google.Protobuf` + `Grpc.Tools` (build-only).
  - `src/Homespun.Server/Features/Observability/OtlpReceiverController.cs` — new.
  - `src/Homespun.Server/Features/Observability/OtlpFanout.cs` + `IOtlpFanout.cs` — new.
  - `src/Homespun.Server/Features/Observability/OtlpFanoutOptions.cs` — new.
  - `src/Homespun.Server/Features/Observability/OtlpScrubber.cs` + `IOtlpScrubber.cs` — new.
  - `src/Homespun.Server/Program.cs` — register new services; delete `TelemetryConfigController` registration if applicable.
  - `src/Homespun.Server/Features/Observability/TelemetryConfigController.cs` + related DTO — DELETE.
  - `tests/Homespun.Api.Tests/Features/Observability/OtlpReceiverTests.cs` — new.
  - `docs/observability/otlp-proxy.md` — new (brief).

- **Dependencies:** `Google.Protobuf` 3.28.*, `Grpc.Tools` 2.68.* (private build-time asset).

- **Risk surface:**
  - Mis-scrubbed PII lands in Seq/Aspire. Tests must cover content-preview gating + secret-key redaction.
  - OTLP proto drift: upstream schema changes can break the generated types. Pin `proto/PROTO_VERSION` and bump deliberately.
  - Body-size abuse: enforce a 4 MiB `[RequestSizeLimit]` + gzip handling; return 413 beyond that.
  - Upstream-sink unavailability must NOT cause 5xx to callers (would cause OTLP client retry storms).

- **Rollback:** revert. Existing `ClientTelemetryController` + `ClientLogController` still in place (this change doesn't delete them) — no caller impact. Worker and client changes (#3, #4) would need to roll back too if they landed on top.
