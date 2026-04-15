## Why

When an agent does (or fails to do) something that depends on a skill or plugin, there is no record of which skills/plugins the SDK actually had when the session started. Operators debugging agent runs need visibility into the complete set of Claude Agent resources available per session — skills, plugins, slash commands, sub-agents, hooks, and MCP servers — to diagnose "my skill didn't fire" problems without attaching to a live session.

## What Changes

- Emit a single structured `info`-level log record per session-lifecycle event (create, resume, boot) enumerating six Claude Agent resource categories.
- New helper `session-inventory.ts` extracts inventory from SDK init message + hooks filesystem discovery.
- Log format: `inventory event=<create|resume|boot> sessionId=<id> payload={...json...}` — tail-friendly and LogQL-parseable.
- Per-tool-call origin attribution on existing `canUseTool` log line (builtin, mcp:server, or unknown).
- Boot-time inventory at worker startup for smoke-testing container image correctness.

## Capabilities

### New Capabilities
- `worker-skills-logging`: Per-session resource inventory logging, tool-call origin attribution, boot-time inventory.

### Modified Capabilities
<!-- None. -->

## Impact

- **Worker only**: `session-manager.ts` (modified), `session-inventory.ts` (new), `index.ts` (modified for boot log).
- **No server, web, shared, or infra changes.**
- **Testing**: New `session-inventory.test.ts`, extended `session-manager-logging.test.ts`.
- **Operational**: Adds one structured record per session create/resume + one at boot. Negligible log volume impact.
- **Status**: Draft — implementation complete, pending PR.
