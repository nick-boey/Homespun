# Phase 0 — Research: Worker Skills & Plugins Logging

Scope: resolve every unknown in the plan's Technical Context that would otherwise block Phase 1 contract/data-model work. All items below are closed; nothing remains as `NEEDS CLARIFICATION`.

---

## R1 — Can the Claude Agent SDK enumerate skills/plugins/etc. programmatically?

**Decision**: Yes — and the SDK emits the enumeration for us as the first message of every session. We consume the `system`/`init` SDK message rather than scanning directories ourselves.

**Rationale**: Inspecting the installed SDK types at `src/Homespun.Worker/node_modules/@anthropic-ai/claude-agent-sdk/entrypoints/sdk/coreTypes.d.ts` reveals `SDKSystemMessage` with `subtype: 'init'` (lines 475–499) which already carries:

```ts
agents?: string[];                                    // subagents
tools: string[];                                      // built-in + provided tools
mcp_servers: { name: string; status: string }[];      // MCP servers w/ live status
slash_commands: string[];                             // commands
skills: string[];                                     // skills
plugins: { name: string; path: string }[];            // plugins
cwd: string;
session_id: string;
permissionMode, model, ...
```

The SDK `Query` interface additionally exposes `supportedCommands(): Promise<SlashCommand[]>` and `mcpServerStatus(): Promise<McpServerStatus[]>` (see `runtimeTypes.d.ts` ~lines 125–141) for on-demand queries, but the init message is sufficient for five of six categories and is free (we already receive it). Using it is both canonical and avoids duplicating SDK discovery logic.

**Alternatives considered**:
- *Filesystem scan of `.claude/skills/` ourselves.* Rejected — would diverge from what the SDK actually loads (precedence rules, plugin-provided overlays), and would need to re-implement SDK semantics.
- *Call `supportedCommands()` + `mcpServerStatus()` after query creation.* Rejected as primary source — it requires awaiting a session-scoped promise chain and duplicates what the init message already carries. Will be retained only as a supplementary source for the resume path (see R3).

---

## R2 — Hooks are NOT in the init message. How do we enumerate them?

**Decision**: For v1, emit two sources for hooks:
1. **Programmatic hooks** — whatever we passed in `options.hooks` to `query()`. Today the worker passes none, so this is typically an empty list.
2. **Filesystem-discovered hooks** — best-effort scan of `~/.claude/hooks/` and `<cwd>/.claude/hooks/` (only for the scopes listed in `settingSources`). Each entry records its scope (`user` or `project`) and filename; non-existent or unreadable directories are recorded to `discoveryErrors` and do not fail emission (FR-006).

**Rationale**: The init message does not list hooks (confirmed by reading `SDKSystemMessage`). The SDK's `settingSources` contract is a documented integration point — scanning the well-known hook directories under those scopes is the minimum work required to make the "did my hook fire" debugging use case possible. Precedence is recorded by scope tag, not resolved.

**Alternatives considered**:
- *Do not report hooks at all.* Rejected — clarification Q2 explicitly expanded the inventory to all six categories including hooks.
- *Parse Claude Code's config files to infer hooks.* Rejected as brittle for v1 — the FS scan is sufficient and mirrors Claude Code's own discovery convention. If the SDK later exposes hooks in init we switch to that source.

---

## R3 — Create vs resume: is the inventory guaranteed to be the same?

**Decision**: Always re-emit on resume against the resumed session's own init message. Do not cache or copy from the create-time record.

**Rationale**: Mounted volumes, `.claude/skills/` edits, and plugin version changes between create and resume are exactly the drift operators need to see (edge case captured in spec). The resumed `query()` call also emits a fresh `system`/`init` message, so the capture mechanism is identical to the create path and there is no correctness reason to cache.

**Alternatives considered**:
- *Cache the create-time record and tag resumes with the cached snapshot.* Rejected — hides drift, violates FR-005 intent.

---

## R4 — How do we capture the init message without disrupting the existing event stream?

**Decision**: Add a small pass-through in the async generator path that already feeds `OutputChannel.push(event)`. When the consumer sees the first `SDKMessage` with `type === 'system' && subtype === 'init'`, it is additionally forwarded to the inventory emitter; the event itself continues into `OutputChannel` unchanged.

**Rationale**: The worker already processes SDK messages in a generator loop inside `session-manager.ts` (`for await (const event of q) { ... outputChannel.push(event) ... }`). Sniffing the first init message adds one `if` per session; it does not re-order or consume messages. This preserves the existing catch-up/replay behaviour of `MessageHistoryEntry`.

**Alternatives considered**:
- *Call `supportedCommands()` / `mcpServerStatus()` proactively after `query()` returns.* Rejected — introduces extra awaits before the user's first assistant turn, may race with session initialization, and duplicates the init message content.
- *Monkey-patch the SDK.* Rejected — brittle and unnecessary given the init message is public API.

---

## R5 — Log record format: JSON-in-message vs structured logger

**Decision**: Emit a single-line string of the form `inventory event=<create|resume|boot> sessionId=<id> payload={...json...}` via the existing `info(...)` helper, where `{...json...}` is `JSON.stringify(record)` with no pretty-printing. Operators parse the line in Loki with `| json` (using Loki's line-format / JSON parser stages on the bracketed payload) or a simple regex extractor to grab the JSON substring.

**Rationale**: The worker's current `logger.ts` emits `[Level] <timestamp> <message>` plain-text lines. Replacing that pipeline with a full structured logger is out of scope. Embedding one compact JSON object per record preserves tail readability, is trivially parseable, and matches the clarified decision (Q3). Payload size is bounded (a few KB at most) — well under typical Loki line length limits.

**Alternatives considered**:
- *Pure JSON lines.* Rejected by clarification Q3 — diverges from every other worker log line and breaks human tails.
- *Logfmt key=value.* Rejected — the six nested category lists don't express cleanly without collapsing to encoded strings.

---

## R6 — Secret scrubbing strategy

**Decision**: The inventory emitter operates only on names, transports, paths (relative where possible), scopes, and enabled/status flags. It has no `env` or credential inputs at all — by construction, not by filter. The contract JSON Schema (Phase 1) explicitly disallows `env`, `token`, `apiKey`, `headers`, `authorization` at every nesting level via `additionalProperties: false` in object definitions.

**Rationale**: FR-010 demands no secrets leak, and the worker currently flows `GITHUB_TOKEN`, `GH_TOKEN`, and the entire `process.env` through `buildCommonOptions`. A "build the record only from approved fields" approach is strictly safer than "build from everything and redact", and is verifiable by unit test (assert the record contains no substring `GITHUB_TOKEN` / `env`).

**Alternatives considered**:
- *Allowlist-then-deny-key scrubbing at emit time.* Rejected — easy to miss nested fields; the constructive approach is leak-proof.

---

## R7 — Boot-time inventory (Story 3): which query do we use?

**Decision**: At worker boot (after Hono reports ready in `src/Homespun.Worker/src/index.ts`), launch a *dry* SDK `query()` against the default working directory with the same `settingSources` the real sessions use, read its first `system`/`init` message, emit the boot inventory log, and then immediately abort/close the query. No user prompt is sent.

**Rationale**: The only way to get the SDK's view of available resources for a given `cwd` is through a `query()` call. The SDK emits the init message before any user turn; aborting right after capture costs effectively nothing. This guarantees Story 3's output is identical in format to the per-session records, fulfilling SC-001 across boot too.

**Alternatives considered**:
- *Filesystem-only enumeration at boot.* Rejected — would diverge from what actual sessions see (plugins + precedence).
- *Skip Story 3.* It's P3, so optional; but since the capture cost is tiny and reuses the same helper, there's no saving from skipping.

---

## R8 — `canUseTool` origin attribution (Story 2)

**Decision**: Maintain an in-process map `toolName → origin` populated from the init message (`mcp_servers`, `plugins`) and from built-in tool names. When `canUseTool` logs, look up the origin; default to `builtin` for known SDK tools, `unknown` for anything not mapped.

**Rationale**: Init gives us `mcp_servers[].name` and `plugins[].name` but not per-tool provenance. Known SDK built-ins (`Read`, `Write`, `Edit`, `Bash`, `Glob`, `Grep`, `Task`, `WebFetch`, `WebSearch`, `AskUserQuestion`, `ExitPlanMode`, `TodoWrite`, `NotebookEdit`) are a small stable list we can enumerate. MCP-provided tools follow the SDK's `mcp__<serverName>__<toolName>` naming convention (visible in this conversation's own tool list — e.g. `mcp__plugin_playwright_playwright__browser_click`) — we split on `__` to identify origin. When that parse fails, record `unknown` (FR-011).

**Alternatives considered**:
- *Add a new SDK call to resolve origin per tool.* Rejected — no such API; name-prefix parsing is sufficient and documented.
- *Skip origin for MCP tools.* Rejected — that's the exact use case Story 2 targets.

---

## R9 — Performance: will the helper add appreciable latency?

**Decision**: Target under 5 ms per session create on a warm cache; well under the 50 ms budget in SC-005.

**Rationale**: The init message is already in memory when we emit. The only I/O is the hooks FS scan — two `readdir` calls on small directories. `JSON.stringify` of a few-KB object is sub-millisecond. The dry boot query (R7) is one-off and non-blocking relative to session creation.

**Alternatives considered**: Not applicable — no tighter implementation is warranted for an observability-only feature.

---

## Summary

All eight technical unknowns closed. No `NEEDS CLARIFICATION` markers remain. Proceeding to Phase 1.
