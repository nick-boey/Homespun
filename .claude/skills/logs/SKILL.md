---
name: logs
description: Log analysis with Seq. Use this skill when debugging errors, investigating failures, or troubleshooting issues. Query application logs + traces from Seq via its HTTP API.
---

# Log Analysis with Seq

Query application logs + traces from Seq via its HTTP API and curl. Seq is
an events-first store that natively ingests OTLP and exposes every log
attribute as an addressable property — no label-cardinality tax.

## Seq API Endpoint

- **Dev base URL (host):** `http://localhost:5341`
- **Prod base URL (container network):** `http://homespun-seq:5341`
- **OTLP ingest:** `/ingest/otlp` (emitters) — NOT what you query.
- **Event query endpoint:** `/api/events/signal`
- **Auth:** `X-Seq-ApiKey: <SEQ_API_KEY>` header when `SEQ_API_KEY` is set
  (required in prod, empty in dev).

## Verify Connectivity

```bash
curl -s 'http://localhost:5341/api/events/signal?count=1'
```

## Query Syntax

Seq uses property-based filtering. Every OTLP log attribute becomes a
top-level property on the event. Pass filters via the `filter` query-string
parameter, in Seq's filter language (C#-ish expressions).

### Common properties

| Property | Meaning | Example values |
|----------|---------|----------------|
| `@Level` | Log level | `Error`, `Warning`, `Information` |
| `@Message` | Rendered message | any string |
| `service.name` | OTel service name | `homespun.server`, `homespun.worker` |
| `SourceContext` | .NET logger category | `Homespun.Features.…`, `ClientTelemetry` |
| `Component` | Tier | `Server`, `Worker`, `Client` |
| `homespun.issue.id` | Fleece issue id | `abc123` |
| `homespun.session.id` | Claude session id | UUID |

### Time windows

Pass `fromDateUtc` / `toDateUtc` as ISO-8601, or omit to use defaults.

## Common Query Patterns

### Recent server logs

```bash
curl -sG 'http://localhost:5341/api/events/signal' \
  --data-urlencode 'filter=service.name = "homespun.server"' \
  --data-urlencode 'count=100' \
  --data-urlencode 'render=true'
```

### Errors only

```bash
curl -sG 'http://localhost:5341/api/events/signal' \
  --data-urlencode 'filter=@Level = "Error" and service.name = "homespun.server"' \
  --data-urlencode 'count=100' \
  --data-urlencode 'render=true'
```

### Filter by component

```bash
curl -sG 'http://localhost:5341/api/events/signal' \
  --data-urlencode 'filter=Component = "Worker"' \
  --data-urlencode 'count=200' \
  --data-urlencode 'render=true'
```

### Filter by issue id

```bash
ISSUE_ID=abc123
curl -sG 'http://localhost:5341/api/events/signal' \
  --data-urlencode "filter=homespun.issue.id = \"$ISSUE_ID\"" \
  --data-urlencode 'count=500' \
  --data-urlencode 'render=true'
```

### Search for text

```bash
curl -sG 'http://localhost:5341/api/events/signal' \
  --data-urlencode 'filter=@Message like "%exception%"' \
  --data-urlencode 'count=100' \
  --data-urlencode 'render=true'
```

### Errors for a specific issue

```bash
ISSUE_ID=abc123
curl -sG 'http://localhost:5341/api/events/signal' \
  --data-urlencode "filter=@Level = \"Error\" and homespun.issue.id = \"$ISSUE_ID\"" \
  --data-urlencode 'count=100' \
  --data-urlencode 'render=true'
```

## Output Parsing with jq

### Raw messages

```bash
… | jq -r '.Events[].RenderedMessage'
```

### Structured

```bash
… | jq '.Events[] | {ts: .Timestamp, level: .Level, msg: .RenderedMessage, props: .Properties}'
```

### Count

```bash
… | jq '.Events | length'
```

## Troubleshooting

### No results

1. **Time range** — use `fromDateUtc=...` to widen if needed.
2. **Check Seq is up** — `curl -s 'http://localhost:5341/api/events/signal?count=1'`.
3. **Verify auth** — in prod, include `-H "X-Seq-ApiKey: $SEQ_API_KEY"`.

### List known properties

Seq's UI (`http://localhost:5341`) surfaces all discovered properties in the
query bar. Programmatically, inspect a sample event's `Properties` via the
`/api/events/signal` endpoint.

## Retention

Seq free tier retains **7 days** of events. Older events age out.

## Notes

- Seq is always on in every dev profile; in prod it's started by
  `docker-compose.yml`.
- Workers on the `homespun-net` network reach Seq via `http://homespun-seq:5341`.
- Traces land in Seq alongside logs via the same OTLP endpoint.
