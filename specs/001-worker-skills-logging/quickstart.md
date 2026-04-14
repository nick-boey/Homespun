# Quickstart — Worker Skills & Plugins Logging

Audience: operators and developers who want to verify the feature works, or who are debugging a production session and need to know which skills/plugins it had.

---

## 1. What the log line looks like

One `info`-level line per session create / resume / worker boot:

```
[Info] 2026-04-15T10:12:47.391Z inventory event=create sessionId=e6f8c4b8-1a64-4c90-9f1a-8b7f0e1b38f9 payload={"event":"create","sessionId":"e6f8c4b8-1a64-4c90-9f1a-8b7f0e1b38f9","cwd":"/workdir","timestamp":"2026-04-15T10:12:47.391Z","sdkVersion":"1.7.3","model":"claude-opus-4-6","permissionMode":"bypassPermissions","settingSources":["user","project"],"skills":[...],"plugins":[...],"commands":[...],"agents":[...],"hooks":[...],"mcpServers":[...],"discoveryErrors":[]}
```

The prefix `inventory event=<create|resume|boot> sessionId=<id>` is stable and greppable. The JSON payload after `payload=` is validated against [`contracts/inventory-log-record.schema.json`](./contracts/inventory-log-record.schema.json).

---

## 2. LogQL queries

The worker logs flow to Loki at `http://homespun-loki:3100` per Constitution §XI. Use the `/logs` skill or a direct query.

### 2a. All inventory records for a specific session

```logql
{app="homespun-worker"} |~ "inventory event=" |~ "sessionId=e6f8c4b8-"
```

### 2b. "Did skill X load for session Y?" — SC-001 query

```logql
{app="homespun-worker"}
  |~ "inventory event="
  |~ "sessionId=e6f8c4b8-"
  | regexp "payload=(?P<payload>\\{.*\\})"
  | line_format "{{.payload}}"
  | json
  | skills_0_name = "superpowers:brainstorming"
```

(Use the Loki `| json` parser on the extracted payload. The `skills_0_name` pattern illustrates list access; in practice use the Loki "json" query with a path expression or run the extracted JSON through `jq` client-side after `logcli`.)

### 2c. Sessions where a specific plugin was unavailable

```logql
{app="homespun-worker"}
  |~ "inventory event="
  | regexp "payload=(?P<payload>\\{.*\\})"
  | line_format "{{.payload}}"
  | json
  |~ "\"status\":\"unavailable\""
```

### 2d. Boot inventory (Story 3)

```logql
{app="homespun-worker"} |~ "inventory event=boot"
```

---

## 3. Reproducing locally in mock mode

1. Start the mock stack (per `CLAUDE.md` — **do not kill the mock shell**):

   ```bash
   ./scripts/mock.sh
   ```

2. Tail the worker log:

   ```bash
   tail -f logs/mock-backend.log | grep "inventory event="
   ```

3. Trigger a session via the Homespun web UI. Expect one `inventory event=create` line per session, with all six category lists present (some possibly empty) and `discoveryErrors: []`.

4. Resume that session (reload the page, continue the conversation). Expect one `inventory event=resume` line.

5. Restart the worker (kill only the worker via `pkill -f "node.*Homespun.Worker"`; do **not** touch `mock.sh` or `dotnet`). Expect one `inventory event=boot` line shortly after worker ready.

---

## 4. Running the worker tests for this feature

```bash
cd src/Homespun.Worker
npm test -- session-inventory
npm test -- session-manager-logging
```

Expected: both suites green. The `session-inventory` suite also validates every synthesized record against the JSON Schema in `contracts/`.

---

## 5. Sanity checks before opening the PR

- [ ] Grep the log file for `GITHUB_TOKEN`, `GH_TOKEN`, `Bearer`, `password` across a test session — should return zero matches in any `inventory event=` line (FR-010 / INV-2).
- [ ] Session creation under an unreadable `~/.claude/hooks/` (simulate with `chmod 000`) still succeeds, and the inventory record lists the failure under `discoveryErrors` (FR-006).
- [ ] All six categories present — an empty `.claude/` directory still produces `"skills":[],"plugins":[],"commands":[],"agents":[],"hooks":[],"mcpServers":[]` (FR-009 / INV-1).
- [ ] Rename the branch from `001-worker-skills-logging` to a constitution-compliant prefix before PR — e.g. `feat/worker-skills-logging+<fleece-id>` (Constitution §VII).
- [ ] The matching Fleece issue exists, is linked via `--linked-pr`, and moved to `review` before PR creation (Constitution §VI).
