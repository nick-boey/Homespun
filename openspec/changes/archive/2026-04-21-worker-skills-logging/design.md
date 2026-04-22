## Context

The `@anthropic-ai/claude-agent-sdk` already emits a `system`/`init` message as the first SDK message of every session — that message contains `skills`, `slash_commands`, `mcp_servers`, `plugins`, `agents` (subagents), `cwd`, `session_id`, `permissionMode` and `model`. We consume that init message as the canonical source of truth, augment it with `hooks` (derived from the options + filesystem scan), and emit the inventory log record.

## Goals / Non-Goals

**Goals:**
- Log all six resource categories (skills, plugins, commands, agents, hooks, mcpServers) per session create/resume/boot.
- Each category as its own named list with source scope, name, and enabled status.
- Correlated to session by `sessionId`.
- No secret material in logs.
- Discovery failure does not block session creation.
- Per-tool-call origin attribution on existing `canUseTool` log line.

**Non-Goals:**
- Changing which skills or plugins are loaded.
- UI display of inventory (operators use Loki).
- Cross-wire DTO changes (feature is observability-only).

## Decisions

### D1: SDK init message as canonical source

**Decision:** Consume the `system/init` message emitted by the SDK rather than independently scanning the filesystem.

**Rationale:** The SDK is the authority on what it loaded. Re-scanning would risk divergence.

### D2: Hooks augmented from filesystem

**Decision:** Hooks are discovered from `options.hooks` + best-effort scan of `~/.claude/hooks/` and `<cwd>/.claude/hooks/`.

**Rationale:** The SDK init message doesn't enumerate hooks; they're configured outside the SDK.

### D3: Single structured record per event

**Decision:** One JSON payload per session event (not per-resource lines).

**Rationale:** Keeps Loki queries cheap. A single LogQL query filtered by sessionId returns the full inventory.

### D4: Stable field names as contract

**Decision:** JSON payload field names are documented in `inventory-log-record.schema.json` and treated as stable across releases.

**Rationale:** Operators write LogQL queries against these fields. Breaking changes require spec updates.

### D5: Secret scrubbing

**Decision:** The inventory emitter never reads `env`, `options.env`, or plugin/MCP `env` values. Only names, transports, paths, scopes, and status flags are logged.

**Rationale:** `GITHUB_TOKEN` and similar secrets flow through `buildCommonOptions`. They must never appear in logs.

### D6: Tool origin resolution

**Decision:** `resolveToolOrigin` returns `builtin` for known SDK tools, `mcp:<server>` for `mcp__<server>__<tool>` patterns, otherwise `unknown`.

**Rationale:** Enables tracing tool invocations back to their source skill/plugin without complex runtime instrumentation.

## Risks / Trade-offs

- **[Risk]** SDK init message format may change across versions — mitigated by treating it as runtime data, not compile-time types.
- **[Trade-off]** Boot-time inventory requires a dry SDK query that is immediately aborted — acceptable overhead for smoke-testing value.
- No complexity tracking violations identified.
