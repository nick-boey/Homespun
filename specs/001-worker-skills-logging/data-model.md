# Phase 1 — Data Model: Worker Skills & Plugins Logging

This document defines the in-memory and on-the-wire shapes used by the feature. Because the feature does not cross any process boundary, "on the wire" here means the JSON payload embedded in the worker's log line. The authoritative schema for that payload is [`contracts/inventory-log-record.schema.json`](./contracts/inventory-log-record.schema.json); this document explains and justifies the same shape in prose.

There is no SQLite schema change, no Fleece schema change, no shared DTO. No `src/Homespun.Shared/` types are added.

---

## Entity: `ResourceInventoryEntry`

A single discovered Claude Agent resource. Emitted inside the category lists of a `SessionInventoryLogRecord`.

| Field | Type | Required | Description | Source |
|-------|------|----------|-------------|--------|
| `category` | `"skill" \| "plugin" \| "command" \| "agent" \| "hook" \| "mcpServer"` | yes | Which of the six categories this entry belongs to. Redundant with the enclosing list name, but kept so entries are self-describing when extracted via LogQL. | Derived from the list the entry is placed into. |
| `name` | `string` | yes | Resource name as the SDK exposes it (e.g. skill name, command name without leading `/`, sub-agent key, plugin name, MCP server name, hook filename stem). | SDK init message / hooks FS scan. |
| `scope` | `"user" \| "project" \| "plugin" \| "inline" \| "unknown"` | yes | Where the resource was sourced from. `plugin` means it was contributed by a plugin (combine with `providedByPlugin`). `inline` applies to `mcpServer` entries configured programmatically via `options.mcpServers`. | Derived per R1/R2 of research. |
| `sourcePath` | `string` | no | Relative or home-relative path (`~/.claude/...` or `.claude/...`) — never absolute with user-identifying segments beyond `~`. Omitted for `mcpServer` inline entries and for any entry the SDK did not expose a path for. | SDK init (plugins) / FS scan (hooks). |
| `providedByPlugin` | `string` | no | When `scope === "plugin"`, the name of the providing plugin. Omitted otherwise. | Derived from init message plugins list crossed with skill/command/agent/hook origin, when determinable; else omitted. |
| `enabled` | `boolean` | no | Present when the SDK reports enablement explicitly (e.g. MCP server `status`). Absent when not reported. | SDK init (`mcp_servers[].status`). |
| `transport` | `"stdio" \| "sse" \| "http" \| "sdk"` | no | Only present for `mcpServer` entries. | `options.mcpServers[name].type`. |
| `status` | `"configured" \| "unavailable" \| "enabled"` | no | Only present for `mcpServer` entries. `configured` = wired in options; `enabled` = actively running per SDK; `unavailable` = wired but SDK reported an error state. | SDK init `mcp_servers[].status` mapped to one of these three values; original string retained in `statusDetail`. |
| `statusDetail` | `string` | no | Short free-form reason, only present when `status === "unavailable"`. Must not contain credentials. | SDK-provided status string, verbatim. |

**Additional-properties policy**: `additionalProperties: false`. If a future SDK version adds new fields we want to surface, bump the schema deliberately (per FR-008).

**Forbidden substrings anywhere in the entry**: `env`, `apiKey`, `token`, `authorization`, `headers`, `Bearer`, `password`, `secret`. Enforced by a unit test that stringifies the entry and asserts none of these substrings appear (see `session-inventory.test.ts`).

### Example entries

```jsonc
// A user-level skill
{
  "category": "skill",
  "name": "superpowers:brainstorming",
  "scope": "user",
  "sourcePath": "~/.claude/skills/superpowers/brainstorming/SKILL.md"
}

// A plugin-provided slash command
{
  "category": "command",
  "name": "subframe:design",
  "scope": "plugin",
  "providedByPlugin": "subframe"
}

// The Playwright MCP server, running
{
  "category": "mcpServer",
  "name": "playwright",
  "scope": "inline",
  "transport": "stdio",
  "status": "enabled"
}

// A project-level hook
{
  "category": "hook",
  "name": "PreToolUse",
  "scope": "project",
  "sourcePath": ".claude/hooks/PreToolUse"
}
```

---

## Entity: `SessionInventoryLogRecord`

The composite JSON payload embedded in a single log line. One record per session-lifecycle event.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `event` | `"create" \| "resume" \| "boot"` | yes | Which lifecycle event triggered emission. `boot` is the Story 3 one-shot. |
| `sessionId` | `string` | yes (when `event !== "boot"`) | The worker session id (`randomUUID()` assigned in `SessionManager.create`). For `boot` the field MUST be the literal string `"boot"`. |
| `cwd` | `string` | yes | Resolved working directory used for SDK discovery. Home-relative (`~/...`) or repo-relative where possible; otherwise the absolute path as the SDK resolved it. No trailing slash. |
| `timestamp` | `string` (RFC 3339) | yes | Emission time, ISO 8601 / RFC 3339. |
| `sdkVersion` | `string` | no | `claude_code_version` from the SDK init message, when available. Useful for correlating behaviour with SDK versions. |
| `model` | `string` | no | Model string the SDK reports for the session. Not present on `boot` when the dry query uses a default. |
| `permissionMode` | `"default" \| "acceptEdits" \| "plan" \| "bypassPermissions"` | no | Effective permission mode. Omitted on `boot`. |
| `settingSources` | `string[]` | yes | The `settingSources` the SDK was configured with (e.g. `["user", "project"]`). |
| `skills` | `ResourceInventoryEntry[]` | yes (MUST be present; may be empty) | All discovered skills. |
| `plugins` | `ResourceInventoryEntry[]` | yes | All configured plugins. |
| `commands` | `ResourceInventoryEntry[]` | yes | All discovered slash commands. |
| `agents` | `ResourceInventoryEntry[]` | yes | All discovered sub-agents. |
| `hooks` | `ResourceInventoryEntry[]` | yes | All hooks (programmatic + FS-discovered). |
| `mcpServers` | `ResourceInventoryEntry[]` | yes | All MCP servers (inline + plugin-provided). |
| `discoveryErrors` | `{ category: string; source: string; reason: string }[]` | yes (MUST be present; may be empty) | Any per-source discovery failures that did not block emission. `reason` is a short sanitized error description. |
| `truncated` | `boolean` | no | Set to `true` only when at least one category list was truncated because the payload would otherwise exceed a soft size budget. Omitted when false. |
| `truncationCounts` | `Record<string, { emitted: number; total: number }>` | no | Only present when `truncated === true`. Gives the per-category before/after counts so operators can see what was clipped. |

### Invariants

- **INV-1** (FR-009): All six category list fields (`skills`, `plugins`, `commands`, `agents`, `hooks`, `mcpServers`) MUST always appear. Missing category → the implementation is incorrect.
- **INV-2** (FR-010): No value in any field may be, or contain as a substring, any secret from `process.env` or `options.env`. Enforced by unit test.
- **INV-3** (FR-005): Records where `event === "resume"` MUST be produced by the same code path as `create` — no cache read. Enforced by test that resumes a session after mutating the mocked init message and asserts the resume record reflects the mutation.
- **INV-4** (FR-007): Exactly one `info`-level log line per session-lifecycle event. Verified by asserting `info.mock.calls` count in tests.
- **INV-5** (schema stability, FR-008): The JSON Schema file is the stable contract. Adding a field requires bumping schema `$id` via a spec change. Tests load the schema and validate every emitted record against it.

### Example payload

```jsonc
{
  "event": "create",
  "sessionId": "e6f8c4b8-1a64-4c90-9f1a-8b7f0e1b38f9",
  "cwd": "/workdir",
  "timestamp": "2026-04-15T10:12:47.391Z",
  "sdkVersion": "1.7.3",
  "model": "claude-opus-4-6",
  "permissionMode": "bypassPermissions",
  "settingSources": ["user", "project"],
  "skills": [
    { "category": "skill", "name": "superpowers:brainstorming", "scope": "user", "sourcePath": "~/.claude/skills/superpowers/brainstorming/SKILL.md" },
    { "category": "skill", "name": "update-config", "scope": "user" }
  ],
  "plugins": [
    { "category": "plugin", "name": "ralph-loop", "scope": "user", "sourcePath": "~/.claude/plugins/ralph-loop" }
  ],
  "commands": [
    { "category": "command", "name": "speckit-specify", "scope": "project", "sourcePath": ".claude/skills/speckit-specify" }
  ],
  "agents": [
    { "category": "agent", "name": "code-reviewer", "scope": "user" }
  ],
  "hooks": [
    { "category": "hook", "name": "SessionStart", "scope": "project", "sourcePath": ".claude/hooks/SessionStart" }
  ],
  "mcpServers": [
    { "category": "mcpServer", "name": "playwright", "scope": "inline", "transport": "stdio", "status": "enabled" }
  ],
  "discoveryErrors": []
}
```

### Emitted log line

```
[Info] 2026-04-15T10:12:47.391Z inventory event=create sessionId=e6f8c4b8-1a64-4c90-9f1a-8b7f0e1b38f9 payload={"event":"create","sessionId":"e6f8c4b8-1a64-4c90-9f1a-8b7f0e1b38f9","cwd":"/workdir","timestamp":"2026-04-15T10:12:47.391Z", ... }
```

---

## State transitions

This feature has no long-lived state of its own. For completeness, the emission events map to the existing `WorkerSession.status` transitions as follows:

- New session → record emitted with `event: "create"` immediately after the SDK `query()` emits its `system`/`init` message and before the session transitions `idle → streaming`.
- Resumed session → record emitted with `event: "resume"` on the equivalent point in the resume code path.
- Worker process start → single record with `event: "boot"` after Hono logs "ready" and before the first HTTP request is accepted.

No additional states are introduced on `WorkerSession` itself.

---

## Validation rules (summary)

| Rule | Source | Enforcement |
|------|--------|-------------|
| All six category lists present | FR-009, INV-1 | `session-inventory.test.ts` asserts keys after every emit. |
| No secrets leak | FR-010, INV-2 | `session-inventory.test.ts` runs record through a forbidden-substring scanner. |
| Resume re-discovers, not caches | FR-005, INV-3 | Test flips init mock between create and resume; asserts divergent records. |
| One record per event | FR-007, INV-4 | Test counts `info` mock calls tagged `inventory event=`. |
| Schema stability | FR-008, INV-5 | Test validates every record against `contracts/inventory-log-record.schema.json`. |
| Discovery failure does not block session | FR-006 | Test forces `fs.readdir` to throw on the hooks scan and asserts session still creates. |
