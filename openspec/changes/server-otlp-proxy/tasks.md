## 1. Vendor OTLP proto types

- [ ] 1.1 Create `proto/opentelemetry/proto/common/v1/common.proto`, `proto/opentelemetry/proto/resource/v1/resource.proto`, `proto/opentelemetry/proto/logs/v1/logs.proto`, `proto/opentelemetry/proto/trace/v1/trace.proto`, `proto/opentelemetry/proto/collector/logs/v1/logs_service.proto`, `proto/opentelemetry/proto/collector/trace/v1/trace_service.proto`. Copy verbatim from `open-telemetry/opentelemetry-proto` at a pinned tag (e.g. `v1.3.2`).
- [ ] 1.2 Write `proto/PROTO_VERSION` naming the upstream tag + commit SHA. Add a README note explaining regeneration discipline.
- [ ] 1.3 Add to `src/Homespun.Server/Homespun.Server.csproj`:
  - `<PackageReference Include="Google.Protobuf" Version="3.28.*" />`
  - `<PackageReference Include="Grpc.Tools" Version="2.68.*"><PrivateAssets>all</PrivateAssets><IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets></PackageReference>`
  - `<ItemGroup><Protobuf Include="..\..\proto\**\*.proto" GrpcServices="None" ProtoRoot="..\..\proto\" /></ItemGroup>`
- [ ] 1.4 `dotnet build` succeeds. `OpenTelemetry.Proto.Collector.Logs.V1.ExportLogsServiceRequest` and `OpenTelemetry.Proto.Collector.Trace.V1.ExportTraceServiceRequest` importable from C#.

## 2. OtlpReceiverController

- [ ] 2.1 Create `src/Homespun.Server/Features/Observability/OtlpReceiverController.cs` with `[Route("api/otlp/v1")]`, `[ApiController]`, and two endpoints: `[HttpPost("logs")]`, `[HttpPost("traces")]`.
- [ ] 2.2 Enforce `[RequestSizeLimit(4 * 1024 * 1024)]` on both.
- [ ] 2.3 Reject non-`application/x-protobuf` requests with 415 Unsupported Media Type.
- [ ] 2.4 Decompress the request body when `Content-Encoding: gzip` via `GZipStream`.
- [ ] 2.5 Parse body with `ExportLogsServiceRequest.Parser.ParseFrom(stream)` / `ExportTraceServiceRequest.Parser.ParseFrom(stream)`. On `InvalidProtocolBufferException`, log at Warning and return 400.
- [ ] 2.6 Call `IOtlpScrubber.Scrub(req)` in place.
- [ ] 2.7 Fire-and-forget `IOtlpFanout.LogsAsync(req, ct)` / `TracesAsync(req, ct)`.
- [ ] 2.8 Return 202 Accepted with body `{"partialSuccess":{"rejectedLogRecords":0}}` (logs) or `{"partialSuccess":{"rejectedSpans":0}}` (traces).

## 3. OtlpFanout

- [ ] 3.1 Define `IOtlpFanout` with `Task LogsAsync(ExportLogsServiceRequest, CancellationToken)` and `Task TracesAsync(ExportTraceServiceRequest, CancellationToken)`.
- [ ] 3.2 Implement `OtlpFanout` using `IHttpClientFactory`. Named client `"otlp-fanout"` with a 5-second request timeout.
- [ ] 3.3 For each destination, compose the URL (`{base}/v1/logs` or `{base}/v1/traces`), set `Content-Type: application/x-protobuf`, attach `X-Seq-ApiKey` header when `SeqApiKey` is non-empty.
- [ ] 3.4 Dispatch to Seq + Aspire in parallel via `Task.WhenAll`. Each `SendAsync` wraps its own try/catch that logs at Warning and swallows — exceptions never propagate.
- [ ] 3.5 Skip destinations whose URL is null/empty.
- [ ] 3.6 Support `OtlpFanoutOptions` config section with: `SeqBaseUrl`, `SeqApiKey`. The Aspire leg is NOT in this section — see Task 6 for how it resolves at runtime.
- [ ] 3.7 Aspire leg URL resolution: read `OTEL_EXPORTER_OTLP_ENDPOINT` from `IConfiguration` at `OtlpFanout` construction. Force HTTP/protobuf by appending `/v1/logs` / `/v1/traces` to the base (Aspire's OTLP receiver accepts both protocols on the same URL). When `OTEL_EXPORTER_OTLP_ENDPOINT` is unset (prod Docker-compose, which doesn't use Aspire), skip the Aspire leg entirely.
- [ ] 3.8 Aspire leg auth: read `OTEL_EXPORTER_OTLP_HEADERS` from `IConfiguration`; parse the `key=value,key=value` comma-separated form; attach each pair as a request header on the Aspire outbound POST (so the dashboard's `x-otlp-api-key` auth works when enabled).

## 4. OtlpScrubber

- [ ] 4.1 Define `IOtlpScrubber` with `void Scrub(ExportLogsServiceRequest req)` + `void Scrub(ExportTraceServiceRequest req)`.
- [ ] 4.2 Walk `ResourceLogs[].ScopeLogs[].LogRecords[].Attributes[]`. When key `== "homespun.content.preview"`: truncate `StringValue` per `SessionEventLog:ContentPreviewChars`; remove attribute entirely when `Chars == 0`.
- [ ] 4.3 Walk spans: span-level `Attributes[]`, per-`Event.Attributes[]` inside each span. Apply the same preview rule.
- [ ] 4.4 Secret-key redaction: configurable substring list (default `token`, `secret`, `password`, `authorization`, `credential`). Any attribute whose key matches (case-insensitive) has `StringValue` replaced with `[REDACTED]` and other value kinds cleared.
- [ ] 4.5 Unit tests cover each branch.

## 5. DI registration

- [ ] 5.1 In `Program.cs` production branch: register `IOtlpFanout` (singleton), `IOtlpScrubber` (singleton), bind `OtlpFanoutOptions` from config section `"OtlpFanout"`.
- [ ] 5.2 Add HttpClient named `"otlp-fanout"` via `AddHttpClient` with sensible defaults (no automatic decompression needed outbound; 5-second timeout).
- [ ] 5.3 In `Program.cs`: delete the `TelemetryConfigController` registration if explicit, and delete the source file + any DTO it owns.

## 6. Config defaults

- [ ] 6.1 `appsettings.json`: empty `OtlpFanout` section documenting `SeqBaseUrl` + `SeqApiKey` keys only. Aspire leg resolves from `OTEL_EXPORTER_OTLP_ENDPOINT` at runtime — do not model it here.
- [ ] 6.2 `appsettings.Development.json`: Seq default `http://localhost:5341/ingest/otlp`. No Aspire entry (Aspire injects its own env).
- [ ] 6.3 AppHost: inject `OtlpFanout__SeqBaseUrl` env var on the server resource resolving to the Seq container's ingest endpoint. Aspire-dashboard URL is already injected by Aspire itself as `OTEL_EXPORTER_OTLP_ENDPOINT` (verified via live probe — see exploration notes).
- [ ] 6.4 `docker-compose.yml`: set `OtlpFanout__SeqBaseUrl=http://seq:5341/ingest/otlp` and `OtlpFanout__SeqApiKey=${SEQ_API_KEY}` on the `homespun` service. Do NOT set `OTEL_EXPORTER_OTLP_ENDPOINT` in prod (absent env = skip Aspire leg — correct prod behaviour since there is no Aspire dashboard there).

## 7. Tests

- [ ] 7.1 `OtlpReceiverTests.Logs_happy_path_returns_202_and_fan_out_called()` — fake fanout, assert single invocation with request byte-equivalent to posted body.
- [ ] 7.2 `OtlpReceiverTests.Trace_context_ids_preserved_through_proxy()` — LogRecord with known TraceId + SpanId → dispatched request has byte-identical IDs.
- [ ] 7.3 `OtlpReceiverTests.Gzip_body_decompressed_before_parse()`.
- [ ] 7.4 `OtlpReceiverTests.Oversized_body_returns_413()`.
- [ ] 7.5 `OtlpReceiverTests.Malformed_protobuf_returns_400_no_fanout()`.
- [ ] 7.6 `OtlpReceiverTests.Unsupported_content_type_returns_415()`.
- [ ] 7.7 `OtlpReceiverTests.Upstream_seq_500_still_returns_202()` — fanout throws, controller still 202.
- [ ] 7.8 `OtlpScrubberTests.Content_preview_truncated_per_config()`.
- [ ] 7.9 `OtlpScrubberTests.Content_preview_removed_when_chars_zero()`.
- [ ] 7.10 `OtlpScrubberTests.Authorization_attribute_redacted()`.
- [ ] 7.11 `OtlpFanoutTests.Empty_url_destination_is_skipped_silently()`.
- [ ] 7.12 `OtlpFanoutTests.Seq_leg_sends_X_Seq_ApiKey_header()`.
- [ ] 7.13 `OtlpFanoutTests.Aspire_leg_skipped_when_OTEL_EXPORTER_OTLP_ENDPOINT_unset()` (prod scenario).
- [ ] 7.14 `OtlpFanoutTests.Aspire_leg_forwards_OTEL_EXPORTER_OTLP_HEADERS_pairs()` (each `key=value` becomes a request header).

## 8. Documentation

- [ ] 8.1 Create `docs/observability/otlp-proxy.md` — short page: architecture diagram (client/worker → `/api/otlp/v1/*` → fanout → Seq/Aspire), scrub contract, failure semantics.
- [ ] 8.2 Update `CLAUDE.md` observability section: add a bullet describing the proxy endpoints and why they exist.

## 9. Verification

- [ ] 9.1 `dotnet test` passes.
- [ ] 9.2 Manual smoke: `curl -X POST -H "Content-Type: application/x-protobuf" --data-binary @sample.pb http://localhost:5101/api/otlp/v1/logs` returns 202; entry appears in Seq within 5s.
- [ ] 9.3 `grep -rn "TelemetryConfigController\|APPLICATIONINSIGHTS_CONNECTION_STRING" src/` returns no matches outside archived OpenSpec.
