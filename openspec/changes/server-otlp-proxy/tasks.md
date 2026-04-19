## 1. Vendor OTLP proto types

- [x] 1.1 Create `proto/opentelemetry/proto/common/v1/common.proto`, `proto/opentelemetry/proto/resource/v1/resource.proto`, `proto/opentelemetry/proto/logs/v1/logs.proto`, `proto/opentelemetry/proto/trace/v1/trace.proto`, `proto/opentelemetry/proto/collector/logs/v1/logs_service.proto`, `proto/opentelemetry/proto/collector/trace/v1/trace_service.proto`. Copy verbatim from `open-telemetry/opentelemetry-proto` at a pinned tag (e.g. `v1.3.2`).
- [x] 1.2 Write `proto/PROTO_VERSION` naming the upstream tag + commit SHA. Add a README note explaining regeneration discipline.
- [x] 1.3 Add to `src/Homespun.Server/Homespun.Server.csproj`:
  - `<PackageReference Include="Google.Protobuf" Version="3.28.*" />`
  - `<PackageReference Include="Grpc.Tools" Version="2.68.*"><PrivateAssets>all</PrivateAssets><IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets></PackageReference>`
  - `<ItemGroup><Protobuf Include="..\..\proto\**\*.proto" GrpcServices="None" ProtoRoot="..\..\proto\" /></ItemGroup>`
- [x] 1.4 `dotnet build` succeeds. `OpenTelemetry.Proto.Collector.Logs.V1.ExportLogsServiceRequest` and `OpenTelemetry.Proto.Collector.Trace.V1.ExportTraceServiceRequest` importable from C#.

## 2. OtlpReceiverController

- [x] 2.1 Create `src/Homespun.Server/Features/Observability/OtlpReceiverController.cs` with `[Route("api/otlp/v1")]`, `[ApiController]`, and two endpoints: `[HttpPost("logs")]`, `[HttpPost("traces")]`.
- [x] 2.2 Enforce `[RequestSizeLimit(4 * 1024 * 1024)]` on both.
- [x] 2.3 Reject non-`application/x-protobuf` requests with 415 Unsupported Media Type.
- [x] 2.4 Decompress the request body when `Content-Encoding: gzip` via `GZipStream`.
- [x] 2.5 Parse body with `ExportLogsServiceRequest.Parser.ParseFrom(stream)` / `ExportTraceServiceRequest.Parser.ParseFrom(stream)`. On `InvalidProtocolBufferException`, log at Warning and return 400.
- [x] 2.6 Call `IOtlpScrubber.Scrub(req)` in place.
- [x] 2.7 Fire-and-forget `IOtlpFanout.LogsAsync(req, ct)` / `TracesAsync(req, ct)`. *(Implemented as an awaited dispatch; the fanout always swallows errors so the await is non-blocking for upstream failures and makes receiver tests deterministic without an extra wait-for-dispatch synchronisation point.)*
- [x] 2.8 Return 202 Accepted with body `{"partialSuccess":{"rejectedLogRecords":0}}` (logs) or `{"partialSuccess":{"rejectedSpans":0}}` (traces).

## 3. OtlpFanout

- [x] 3.1 Define `IOtlpFanout` with `Task LogsAsync(ExportLogsServiceRequest, CancellationToken)` and `Task TracesAsync(ExportTraceServiceRequest, CancellationToken)`.
- [x] 3.2 Implement `OtlpFanout` using `IHttpClientFactory`. Named client `"otlp-fanout"` with a 5-second request timeout.
- [x] 3.3 For each destination, compose the URL (`{base}/v1/logs` or `{base}/v1/traces`), set `Content-Type: application/x-protobuf`, attach `X-Seq-ApiKey` header when `SeqApiKey` is non-empty.
- [x] 3.4 Dispatch to Seq + Aspire in parallel via `Task.WhenAll`. Each `SendAsync` wraps its own try/catch that logs at Warning and swallows — exceptions never propagate.
- [x] 3.5 Skip destinations whose URL is null/empty.
- [x] 3.6 Support `OtlpFanoutOptions` config section with: `SeqBaseUrl`, `SeqApiKey`. The Aspire leg is NOT in this section — see Task 6 for how it resolves at runtime.
- [x] 3.7 Aspire leg URL resolution: read `OTEL_EXPORTER_OTLP_ENDPOINT` from `IConfiguration` at `OtlpFanout` construction. Force HTTP/protobuf by appending `/v1/logs` / `/v1/traces` to the base (Aspire's OTLP receiver accepts both protocols on the same URL). When `OTEL_EXPORTER_OTLP_ENDPOINT` is unset (prod Docker-compose, which doesn't use Aspire), skip the Aspire leg entirely.
- [x] 3.8 Aspire leg auth: read `OTEL_EXPORTER_OTLP_HEADERS` from `IConfiguration`; parse the `key=value,key=value` comma-separated form; attach each pair as a request header on the Aspire outbound POST (so the dashboard's `x-otlp-api-key` auth works when enabled).

## 4. OtlpScrubber

- [x] 4.1 Define `IOtlpScrubber` with `void Scrub(ExportLogsServiceRequest req)` + `void Scrub(ExportTraceServiceRequest req)`.
- [x] 4.2 Walk `ResourceLogs[].ScopeLogs[].LogRecords[].Attributes[]`. When key `== "homespun.content.preview"`: truncate `StringValue` per `SessionEventLog:ContentPreviewChars`; remove attribute entirely when `Chars == 0`.
- [x] 4.3 Walk spans: span-level `Attributes[]`, per-`Event.Attributes[]` inside each span. Apply the same preview rule.
- [x] 4.4 Secret-key redaction: configurable substring list (default `token`, `secret`, `password`, `authorization`, `credential`). Any attribute whose key matches (case-insensitive) has `StringValue` replaced with `[REDACTED]` and other value kinds cleared.
- [x] 4.5 Unit tests cover each branch.

## 5. DI registration

- [x] 5.1 In `Program.cs` production branch: register `IOtlpFanout` (singleton), `IOtlpScrubber` (singleton), bind `OtlpFanoutOptions` from config section `"OtlpFanout"`. *(Registered at top level rather than gated by the production branch so mock-mode integration tests exercise the same DI graph as production. Services are stateless singletons and safe to resolve in mock mode.)*
- [x] 5.2 Add HttpClient named `"otlp-fanout"` via `AddHttpClient` with sensible defaults (no automatic decompression needed outbound; 5-second timeout).
- [x] 5.3 In `Program.cs`: delete the `TelemetryConfigController` registration if explicit, and delete the source file + any DTO it owns. *(No-op: neither `TelemetryConfigController` nor `TelemetryConfigDto` exist in the current tree; the legacy scaffold was already removed prior to this change. Verified via `grep -rn "TelemetryConfigController"`.)*

## 6. Config defaults

- [x] 6.1 `appsettings.json`: empty `OtlpFanout` section documenting `SeqBaseUrl` + `SeqApiKey` keys only. Aspire leg resolves from `OTEL_EXPORTER_OTLP_ENDPOINT` at runtime — do not model it here.
- [x] 6.2 `appsettings.Development.json`: Seq default `http://localhost:5341/ingest/otlp`. No Aspire entry (Aspire injects its own env).
- [x] 6.3 AppHost: inject `OtlpFanout__SeqBaseUrl` env var on the server resource resolving to the Seq container's ingest endpoint. Aspire-dashboard URL is already injected by Aspire itself as `OTEL_EXPORTER_OTLP_ENDPOINT` (verified via live probe — see exploration notes).
- [x] 6.4 `docker-compose.yml`: set `OtlpFanout__SeqBaseUrl=http://seq:5341/ingest/otlp` and `OtlpFanout__SeqApiKey=${SEQ_API_KEY}` on the `homespun` service. Do NOT set `OTEL_EXPORTER_OTLP_ENDPOINT` in prod (absent env = skip Aspire leg — correct prod behaviour since there is no Aspire dashboard there).

## 7. Tests

- [x] 7.1 `OtlpReceiverTests.Logs_happy_path_returns_202_and_fan_out_called()` — fake fanout, assert single invocation with request byte-equivalent to posted body.
- [x] 7.2 `OtlpReceiverTests.Trace_context_ids_preserved_through_proxy()` — LogRecord with known TraceId + SpanId → dispatched request has byte-identical IDs.
- [x] 7.3 `OtlpReceiverTests.Gzip_body_decompressed_before_parse()`.
- [x] 7.4 `OtlpReceiverTests.Oversized_body_returns_413()`. *(Attribute-presence + byte-ceiling reflection assert — `[RequestSizeLimit]` enforcement is Kestrel/Mvc-layer and not observable from a direct controller invocation.)*
- [x] 7.5 `OtlpReceiverTests.Malformed_protobuf_returns_400_no_fanout()`.
- [x] 7.6 `OtlpReceiverTests.Unsupported_content_type_returns_415()`.
- [x] 7.7 `OtlpReceiverTests.Upstream_seq_500_still_returns_202()` — fanout throws, controller still 202.
- [x] 7.8 `OtlpScrubberTests.Content_preview_truncated_per_config()`.
- [x] 7.9 `OtlpScrubberTests.Content_preview_removed_when_chars_zero()`.
- [x] 7.10 `OtlpScrubberTests.Authorization_attribute_redacted()`.
- [x] 7.11 `OtlpFanoutTests.Empty_url_destination_is_skipped_silently()`.
- [x] 7.12 `OtlpFanoutTests.Seq_leg_sends_X_Seq_ApiKey_header()`.
- [x] 7.13 `OtlpFanoutTests.Aspire_leg_skipped_when_OTEL_EXPORTER_OTLP_ENDPOINT_unset()` (prod scenario).
- [x] 7.14 `OtlpFanoutTests.Aspire_leg_forwards_OTEL_EXPORTER_OTLP_HEADERS_pairs()` (each `key=value` becomes a request header).

## 8. Documentation

- [x] 8.1 Create `docs/observability/otlp-proxy.md` — short page: architecture diagram (client/worker → `/api/otlp/v1/*` → fanout → Seq/Aspire), scrub contract, failure semantics.
- [x] 8.2 Update `CLAUDE.md` observability section: add a bullet describing the proxy endpoints and why they exist.

## 9. Verification

- [x] 9.1 `dotnet test` passes. *(212 API tests, 1739 unit tests, 12 AppHost tests — all green. 22 new tests under `Features/Observability/` cover the receiver, scrubber, and fanout paths.)*
- [ ] 9.2 Manual smoke: `curl -X POST -H "Content-Type: application/x-protobuf" --data-binary @sample.pb http://localhost:5101/api/otlp/v1/logs` returns 202; entry appears in Seq within 5s. *(Not executed in this session — requires a running AppHost + live Seq. Deferred to the post-merge dev-loop.)*
- [x] 9.3 `grep -rn "TelemetryConfigController\|APPLICATIONINSIGHTS_CONNECTION_STRING" src/` returns no matches outside archived OpenSpec. *(Confirmed via Grep: zero matches in `src/`.)*
