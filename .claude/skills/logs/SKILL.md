---
name: logs
description: Log Analysis with Loki. Use this skill when debugging errors, investigating failures, or troubleshooting issues. Query application logs from Loki using LogQL.
---

# Log Analysis with Loki

Query application logs from Loki using LogQL and curl.

## Loki API Endpoint

- **Base URL:** `http://homespun-loki:3100`
- **Query endpoint:** `/loki/api/v1/query_range`
- **Auth:** None required

## Verify Connectivity

```bash
curl -s 'http://homespun-loki:3100/ready'
# Expected: "ready"
```

## LogQL Syntax

### Label Matchers

| Operator | Description | Example |
|----------|-------------|---------|
| `=` | Exact match | `{container="homespun"}` |
| `!=` | Not equal | `{container!="promtail"}` |
| `=~` | Regex match | `{container=~"homespun.*"}` |
| `!~` | Regex not match | `{container!~"loki.*"}` |

### Line Filters

| Operator | Description | Example |
|----------|-------------|---------|
| `\|=` | Contains | `{container="homespun"} \|= "error"` |
| `!=` | Does not contain | `{container="homespun"} != "health"` |
| `\|~` | Regex match | `{container="homespun"} \|~ "fail(ed\|ure)"` |
| `!~` | Regex not match | `{container="homespun"} !~ "debug"` |

### JSON Parsing

Parse structured logs with `| json`:

```logql
{container="homespun"} | json | Level="Error"
```

Extract specific fields:

```logql
{container="homespun"} | json | line_format "{{.Level}}: {{.Message}}"
```

## Available Labels

| Label | Description | Example Values |
|-------|-------------|----------------|
| `container` | Container name | `homespun`, `homespun-worker-*` |
| `component` | Log component | `Server`, `Worker`, `Client` |
| `level` | Log level | `Error`, `Warning`, `Information`, `Debug` |
| `issue_id` | Fleece issue ID | `abc123` |
| `project_name` | Project name | `MyProject` |

## Time Range Formatting

Use Go duration format for relative times:
- `1m` = 1 minute
- `1h` = 1 hour
- `24h` = 24 hours
- `7d` = 7 days

RFC3339 for absolute times:
- `2024-01-15T10:00:00Z`

## Common Query Patterns

### Query Recent Logs (Last 15 Minutes)

```bash
curl -sG 'http://homespun-loki:3100/loki/api/v1/query_range' \
  --data-urlencode 'query={container="homespun"}' \
  --data-urlencode 'start='$(date -d '15 minutes ago' +%s)000000000 \
  --data-urlencode 'end='$(date +%s)000000000 \
  --data-urlencode 'limit=100' | jq '.data.result[].values[][1]'
```

### Query Last Hour

```bash
curl -sG 'http://homespun-loki:3100/loki/api/v1/query_range' \
  --data-urlencode 'query={container="homespun"}' \
  --data-urlencode 'start='$(date -d '1 hour ago' +%s)000000000 \
  --data-urlencode 'end='$(date +%s)000000000 \
  --data-urlencode 'limit=200' | jq '.data.result[].values[][1]'
```

### Filter by Log Level (Errors Only)

```bash
curl -sG 'http://homespun-loki:3100/loki/api/v1/query_range' \
  --data-urlencode 'query={container="homespun"} | json | Level="Error"' \
  --data-urlencode 'start='$(date -d '1 hour ago' +%s)000000000 \
  --data-urlencode 'end='$(date +%s)000000000 \
  --data-urlencode 'limit=100' | jq '.data.result[].values[][1]'
```

### Filter by Component

```bash
curl -sG 'http://homespun-loki:3100/loki/api/v1/query_range' \
  --data-urlencode 'query={container="homespun"} | json | Component="AgentOrchestration"' \
  --data-urlencode 'start='$(date -d '1 hour ago' +%s)000000000 \
  --data-urlencode 'end='$(date +%s)000000000 \
  --data-urlencode 'limit=100' | jq '.data.result[].values[][1]'
```

### Filter by Issue ID

```bash
ISSUE_ID="abc123"
curl -sG 'http://homespun-loki:3100/loki/api/v1/query_range' \
  --data-urlencode "query={container=\"homespun\"} | json | IssueId=\"$ISSUE_ID\"" \
  --data-urlencode 'start='$(date -d '24 hours ago' +%s)000000000 \
  --data-urlencode 'end='$(date +%s)000000000 \
  --data-urlencode 'limit=500' | jq '.data.result[].values[][1]'
```

### Search for Text Pattern

```bash
curl -sG 'http://homespun-loki:3100/loki/api/v1/query_range' \
  --data-urlencode 'query={container="homespun"} |= "exception"' \
  --data-urlencode 'start='$(date -d '1 hour ago' +%s)000000000 \
  --data-urlencode 'end='$(date +%s)000000000 \
  --data-urlencode 'limit=100' | jq '.data.result[].values[][1]'
```

### Query Worker Logs

```bash
curl -sG 'http://homespun-loki:3100/loki/api/v1/query_range' \
  --data-urlencode 'query={container=~"homespun-worker.*"}' \
  --data-urlencode 'start='$(date -d '30 minutes ago' +%s)000000000 \
  --data-urlencode 'end='$(date +%s)000000000 \
  --data-urlencode 'limit=200' | jq '.data.result[].values[][1]'
```

### Combined Filters (Errors for Specific Issue)

```bash
ISSUE_ID="abc123"
curl -sG 'http://homespun-loki:3100/loki/api/v1/query_range' \
  --data-urlencode "query={container=\"homespun\"} | json | Level=\"Error\" | IssueId=\"$ISSUE_ID\"" \
  --data-urlencode 'start='$(date -d '24 hours ago' +%s)000000000 \
  --data-urlencode 'end='$(date +%s)000000000 \
  --data-urlencode 'limit=100' | jq '.data.result[].values[][1]'
```

## Output Parsing with jq

### Get Raw Log Lines

```bash
... | jq -r '.data.result[].values[][1]'
```

### Parse as JSON and Format

```bash
... | jq -r '.data.result[].values[][1]' | while read line; do echo "$line" | jq -r '"\(.Timestamp) [\(.Level)] \(.Message)"'; done
```

### Get Structured Data

```bash
... | jq '.data.result[].values[] | {timestamp: .[0], log: (.[1] | fromjson)}'
```

### Count Results

```bash
... | jq '.data.result[].values | length'
```

## Troubleshooting

### No Results Returned

1. **Check time range** - Logs may be outside the queried window
2. **Verify label exists** - Use `{container=~".+"}` to see all labels
3. **Check Loki status** - `curl -s 'http://homespun-loki:3100/ready'`

### List Available Labels

```bash
curl -s 'http://homespun-loki:3100/loki/api/v1/labels' | jq
```

### List Label Values

```bash
curl -s 'http://homespun-loki:3100/loki/api/v1/label/container/values' | jq
```

### Check Loki Metrics

```bash
curl -s 'http://homespun-loki:3100/metrics' | grep -E '^loki_ingester'
```

## Retention

Logs are retained for **7 days**. Queries beyond this window will return empty results.

## Notes

- The PLG stack must be running (`docker compose --profile plg up -d`) for log access
- Workers spawned on `homespun-net` network can access Loki directly
- All timestamps in Loki are nanoseconds since Unix epoch
