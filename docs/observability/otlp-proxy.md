# Server-side OTLP proxy

The worker and web client do not speak OTLP directly to Seq or the Aspire
dashboard. Both ship their signals through two server endpoints that parse,
scrub, and fan out to the downstream sinks.

- `POST /api/otlp/v1/logs` — body: `ExportLogsServiceRequest` protobuf
- `POST /api/otlp/v1/traces` — body: `ExportTraceServiceRequest` protobuf

The server acts as a mini-collector. Trace context (`traceId` / `spanId`)
survives byte-for-byte end-to-end.

## Flow

```
┌─────────┐       POST /api/otlp/v1/{logs,traces}
│ worker  │─────────┐
└─────────┘         │         ┌─────────────────────┐
                    ├────────▶│ OtlpReceiver (4 MiB │
┌─────────┐         │         │  cap, gzip, 415/400)│
│ client  │─────────┘         └──────────┬──────────┘
└─────────┘                              │
                                   Scrub │  (SessionEventContent:ContentPreviewChars,
                                         │   secret-substring → [REDACTED])
                                         ▼
                                 ┌──────────────┐
                                 │ OtlpFanout   │  parallel, 5-sec timeout
                                 └──┬────────┬──┘
                                    │        │
                        Seq /ingest │        │ Aspire dashboard
                        /otlp/v1/*  │        │ (OTEL_EXPORTER_OTLP_ENDPOINT)
                                    ▼        ▼
```

## Scrub contract

Two mutations happen before fan-out (see
`Homespun.Features.Observability.OtlpScrubber`):

1. Attribute key `homespun.content.preview`
    - When `SessionEventContent:ContentPreviewChars == 0` → attribute removed.
    - Otherwise string value truncated to `Chars` chars + `…`.
2. Any attribute whose key contains a configured secret substring
   (`token`, `secret`, `password`, `authorization`, `credential` by default,
   case-insensitive) → value replaced with `[REDACTED]`. Non-string value
   kinds on the same attribute are cleared.

Override the substring list under the `OtlpScrubber:SecretSubstrings`
config section.

## Failure semantics

- 4 MiB body ceiling, rejected at ingress with 413.
- Non-`application/x-protobuf` → 415 Unsupported Media Type.
- `Content-Encoding: gzip` decompressed before parse.
- Malformed protobuf → 400 Bad Request, no fan-out.
- **Upstream sink failures never propagate.** Each leg's `SendAsync` has a
  private try/catch that logs at Warning and swallows. The receiver always
  returns `202 Accepted` with the expected partial-success body, so OTLP
  client SDKs never trigger retry storms against the proxy.

## Destinations

| Leg    | URL source                              | Protocol                                      | Auth header              |
|--------|------------------------------------------|-----------------------------------------------|--------------------------|
| Seq    | `OtlpFanout:SeqBaseUrl` (config)         | HTTP/1.1 + `application/x-protobuf`           | `X-Seq-ApiKey` when `OtlpFanout:SeqApiKey` set |
| Aspire | `OTEL_EXPORTER_OTLP_ENDPOINT` (Aspire-injected env) | Driven by `OTEL_EXPORTER_OTLP_PROTOCOL` — `grpc` (default when Aspire injects it) → HTTP/2 + `application/grpc+proto` to `{endpoint}/opentelemetry.proto.collector.{trace,logs}.v1.{TraceService,LogsService}/Export`. Anything else → HTTP/1.1 POST to `{endpoint}/v1/{logs,traces}`. | `OTEL_EXPORTER_OTLP_HEADERS` (`k=v,k=v`) |

Either leg whose URL resolves to null/empty is skipped silently. In
production (docker-compose), Aspire is absent so the Aspire leg is
automatically skipped.

The gRPC path reuses the already-serialised protobuf bytes, wraps them in
the 5-byte gRPC length-prefix frame (compression flag + big-endian length),
and POSTs over HTTP/2. Non-zero `grpc-status` trailers are logged at
Warning and swallowed — same contract as the HTTP/protobuf path.
