# Feature Specification: Worker Skills & Plugins Logging

**Feature Branch**: `001-worker-skills-logging`
**Created**: 2026-04-15
**Status**: Draft
**Input**: User description: "I want to add logging of the skills and plugins that are available to the worker. This will likely affect src/Homespun.Worker/src/services/session-manager.ts"

## Clarifications

### Session 2026-04-15

- Q: Log level for the new inventory records (session create/resume inventory, boot inventory, and the `canUseTool` origin field)? → A: `info` for all three. The `canUseTool` origin is added as an additional field on the existing `info` line, not a new line.
- Q: Which resource categories count as "skills and plugins" for the inventory? → A: All discoverable categories — skills, plugins, slash commands, sub-agents, hooks, MCP servers — each as its own named list in the log record.
- Q: Record format for the structured log line? → A: Human-readable prefix followed by one compact JSON object in the message (e.g. `inventory event=create sessionId=… payload={...json...}`); operators parse via LogQL `| json`. No logger rewrite.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Operator confirms which skills & plugins are active per session (Priority: P1)

As a Homespun operator debugging an agent run, I need to see in the worker logs the complete list of skills and plugins that were available to the agent at the moment a session was created, so I can reproduce behaviour, confirm a skill was actually loaded, and diagnose "my skill didn't fire" problems without attaching to a live session.

**Why this priority**: This is the core user value. Today, when an agent does (or fails to do) something that depends on a skill or plugin, there is no record of which skills/plugins the SDK actually had when the session started. Everything else in this feature is an extension of this visibility.

**Independent Test**: Start the worker against a known working directory that has at least one resource in each of the six categories (e.g. a user-level skill, a project-level skill, a project-level slash command, a sub-agent definition, a hook, a configured plugin, and the existing Playwright MCP server). Trigger a session via the normal Homespun flow. Fetch the worker log (via Loki or the local log file). Confirm that a single structured entry records session creation with six separately named lists (`skills`, `plugins`, `commands`, `agents`, `hooks`, `mcpServers`), each enumerating its entries by name and source scope.

**Acceptance Scenarios**:

1. **Given** a worker is about to create a new agent session, **When** `SessionManager.create(...)` is invoked, **Then** exactly one log entry is emitted that lists, under six separately named lists, every skill, plugin, slash command, sub-agent, hook, and MCP server currently discoverable by the SDK under the session's `cwd` and user-level settings sources, keyed by the new session id.
2. **Given** a session is resumed via `resumeSessionId`, **When** the SDK `query()` is reconstructed for that resume, **Then** the same enumeration is logged again, tagged as a resume, so operators can confirm the resumed session has the expected capabilities.
3. **Given** the working directory has zero project-level skills and no plugins configured, **When** a session is created, **Then** the log entry is still emitted and explicitly indicates an empty list for each category (not missing fields), so absence is itself observable.
4. **Given** skill discovery fails (e.g. unreadable directory, malformed manifest), **When** a session is created, **Then** the session still starts, and a warning-level log entry records the discovery failure with the offending path and error reason, without blocking the session.

---

### User Story 2 - Per-tool-call attribution to a source skill/plugin (Priority: P2)

As an operator investigating a specific tool invocation in the worker's event stream, I want each `canUseTool` log line to include (when determinable) which skill or plugin the tool originates from, so I can trace agent actions back to the capability that enabled them.

**Why this priority**: Useful for deeper debugging but not required for the primary "what did the session have" question. Depends on Story 1 being in place.

**Independent Test**: Run a session that invokes a tool provided by a plugin/MCP server (e.g. the Playwright MCP `browser_click`). In the worker log, find the corresponding `canUseTool` entry and verify it records the plugin/MCP server name alongside the tool name.

**Acceptance Scenarios**:

1. **Given** a plugin-provided tool is invoked, **When** the existing `canUseTool` log is written, **Then** it additionally records the originating plugin/MCP server name.
2. **Given** a built-in tool (e.g. `Read`, `Grep`) is invoked, **When** the log is written, **Then** the origin is recorded as `builtin` rather than omitted.

---

### User Story 3 - Inventory is queryable at worker startup (Priority: P3)

As an operator who has just deployed a new worker container, I want a one-shot log entry at worker boot that enumerates every skill and plugin the worker *would* expose to a session created in the default working directory, so I can verify the container image and mounts are correct before any user session runs.

**Why this priority**: A convenience on top of Story 1. If Story 1 is in place, operators can always trigger a session to see the inventory — this just saves that step for smoke-testing.

**Independent Test**: Start the worker with no sessions. Tail the startup log. Confirm a single boot-time entry lists the skills and plugins available in the default `cwd`.

**Acceptance Scenarios**:

1. **Given** the worker has just started, **When** the HTTP server reports ready, **Then** one inventory log entry has been emitted listing skills and plugins for the default working directory.
2. **Given** the default working directory is missing or unreadable, **When** the worker starts, **Then** a warning-level log entry records the condition and the worker still starts.

---

### Edge Cases

- A skill with the same name exists at both user scope and project scope — both MUST appear in the log with their scope so the precedence is visible.
- A plugin/MCP server is configured but fails to start (e.g. Playwright `npx` fetch fails) — the log MUST still list it as *configured* with a status indicating unavailable; this is distinct from "not configured".
- The list of skills is very large (hundreds) — the log entry MUST remain a single structured record (not per-skill lines) and MUST NOT be truncated silently; if truncation is unavoidable, the record MUST include a count and a truncation flag.
- Session created without a working directory (falls back to `WORKING_DIRECTORY` env or `/workdir`) — the resolved `cwd` used for discovery MUST appear in the log so operators know which scope was scanned.
- Skill/plugin inventory changes between the original session and a resume — the resume log entry MUST reflect the inventory *at resume time*, not a cached copy from the original create, so drift is observable.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Worker MUST log, at session creation time, the full set of Claude Agent resources available to that session, covering **all six categories**: skills, plugins, slash commands, sub-agents, hooks, and MCP servers. Each category MUST appear in the log record as its own named list (never merged into a single "capabilities" list), so operators can query any one category independently. Each entry MUST record source scope (user-level vs project-level vs plugin-provided vs any other source the SDK exposes through `settingSources`). The record MUST be emitted at `info` level so it is present in the default log stream without a config change.
- **FR-002**: For MCP servers and plugins specifically, the record MUST additionally include the server/plugin name, transport type (for MCP servers), and whether it is enabled for the session. Plugin entries MUST identify which of the other five categories they contributed (so a skill or command provided by a plugin can be traced back to its plugin of origin). The record MUST be emitted at `info` level (see FR-001).
- **FR-003**: The log entry from FR-001 and FR-002 MUST be correlated to the session by its `sessionId`, consistent with existing worker log conventions.
- **FR-004**: The log entry MUST include the resolved working directory (`cwd`) used for skill discovery.
- **FR-005**: Worker MUST emit the same enumeration on session *resume* paths (not only on initial create), tagged as a resume so operators can distinguish.
- **FR-006**: If skill or plugin discovery fails for any reason, the worker MUST NOT fail session creation; instead it MUST emit a warning-level log entry describing the failure and continue.
- **FR-007**: The log entry MUST be a single structured record per session event, not one line per resource, to keep Loki queries cheap. The record MUST be emitted as a single-line message consisting of a short human-readable prefix (including the event type and `sessionId`) followed by one compact JSON payload, in the form `inventory event=<create|resume|boot> sessionId=<id> payload={...json...}` — so the line is still readable when tailing and parseable via LogQL `| json`.
- **FR-008**: The JSON payload's top-level field names (`event`, `sessionId`, `cwd`, `skills`, `plugins`, `commands`, `agents`, `hooks`, `mcpServers`) and the inner Resource Inventory Entry field names MUST be stable across releases so LogQL queries do not break; any change MUST be documented as part of a spec update, not made ad-hoc.
- **FR-009**: When a given category has no discoverable entries, the log entry MUST record an explicit empty collection for that category rather than omitting the field, so absence is observable. All six category lists (`skills`, `plugins`, `commands`, `agents`, `hooks`, `mcpServers`) MUST always be present.
- **FR-010**: The logging MUST NOT include secret material (e.g. `env` values like `GITHUB_TOKEN` that currently flow into `buildCommonOptions`). Plugin/MCP server entries MUST list names and transports only, not credentials.
- **FR-011**: The existing `canUseTool` log line SHOULD (Story 2) include the origin plugin/MCP server name when the tool is not a built-in SDK tool; where origin cannot be determined it MUST be recorded as `unknown` rather than omitted. The origin MUST be an additional field on the existing `info`-level `canUseTool` line, not a new separate log line.
- **FR-012**: (Story 3) Worker SHOULD emit a one-shot inventory log entry at startup, listing skills/plugins resolvable for the default working directory. The record MUST be emitted at `info` level.

### Key Entities

- **Resource Inventory Entry**: A record of one discovered Claude Agent resource. Fields: `category` (one of `skill` / `plugin` / `command` / `agent` / `hook` / `mcpServer`), `name`, `scope` (`user` / `project` / `plugin:<pluginName>` / `inline` / other), `sourcePath` (relative, no secrets — omitted when not file-backed, e.g. MCP servers configured inline), and whether the SDK considers it enabled. MCP server entries additionally carry `transport` (e.g. `stdio`) and `status` (`configured` / `unavailable` / `enabled`) with a short reason when unavailable. No env values or tokens.
- **Session Inventory Log Record**: The composite structured log entry emitted per session-lifecycle event (create / resume / boot). Fields: `sessionId` (or `boot`), `event` (`create` / `resume` / `boot`), resolved `cwd`, timestamp, and six separate named lists — `skills`, `plugins`, `commands`, `agents`, `hooks`, `mcpServers` — each populated with its respective Resource Inventory Entries. Every list MUST appear even if empty (see FR-009).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For 100% of sessions created or resumed by the worker, an operator can retrieve the full list of skills and plugins that were available to that session using a single LogQL query filtered by sessionId.
- **SC-002**: Median time for an operator to answer the question "did skill X load for session Y?" drops from "must re-run the session" to under 30 seconds (single log lookup).
- **SC-003**: Zero secret values (tokens, credentials, full `env` dumps) appear in any of the new log entries across a full test run.
- **SC-004**: A failed skill/plugin discovery (e.g. unreadable directory) never prevents a session from starting — session creation success rate is unchanged vs. the pre-feature baseline.
- **SC-005**: The added logging adds no more than a negligible amount of per-session startup latency (target: under 50 ms added on a typical worker container with a few dozen skills).

## Assumptions

- The Claude Agent SDK (`@anthropic-ai/claude-agent-sdk`) exposes, or allows the worker to resolve, the list of skills and plugins/MCP servers applicable to a session's `settingSources` and `cwd`. If the SDK does not expose this directly, the worker will discover skills by scanning the standard `.claude/skills/` / `~/.claude/skills/` locations that match the `settingSources` configuration and will enumerate plugins/MCP servers from the options object passed to `query()`.
- Structured logging is emitted via the existing `info` / `warn` helpers in `src/Homespun.Worker/src/utils/logger.ts`; no new logging framework is introduced.
- Logs continue to flow to Loki via the existing collection path (stdout capture); no changes to the log transport are in scope.
- "Available to the worker" means "in scope for a session created right now under a given `cwd`" — not a historical or cross-tenant catalogue.
- The feature is observability-only: it does not change which skills or plugins are loaded, or how permissions are enforced.
- Resume-time re-enumeration (FR-005) is acceptable even if the inventory is usually identical to create-time, because drift (mounted volumes, file edits) is exactly what operators need to see.

## Affected Slices *(mandatory)*

| Side   | Slice path                                        | New / Existing | Why this slice is touched                                                                                  |
|--------|---------------------------------------------------|----------------|------------------------------------------------------------------------------------------------------------|
| Server | `src/Homespun.Server/Features/*`                  | N/A            | No server changes — this is a worker-side observability feature.                                           |
| Web    | `src/Homespun.Web/src/features/*`                 | N/A            | No UI changes — operators consume the new logs via Loki, not the web app.                                  |
| Worker | `src/Homespun.Worker/src/services/session-manager.ts` | Existing       | `SessionManager.create` and the resume path build the SDK options and are the correct point to enumerate and log skills/plugins. |
| Worker | `src/Homespun.Worker/src/services/` (possible new `inventory.ts`) | New (optional) | A small helper that discovers skills from `settingSources` + `cwd` and formats the structured record, to keep `session-manager.ts` focused. |
| Worker | `src/Homespun.Worker/src/index.ts` (boot log)     | Existing       | Story 3 boot-time inventory log is emitted from the worker entrypoint, not from `SessionManager`.          |
| Shared | `src/Homespun.Shared/`                            | No             | No DTO or hub contract changes — this feature does not cross the wire.                                     |

## API & Contract Impact *(mandatory)*

- [ ] **Server API changes?** No.
- [ ] **OpenAPI regeneration required?** No.
- [ ] **Shared DTO / hub interface changes?** No.
- [ ] **Breaking change for existing clients?** No.

Logs are a human/operator interface, not a typed contract. Field names in the structured log record will be documented in the plan and treated as stable, but they are not part of `Homespun.Shared`.

## Realtime Impact *(SignalR / AG-UI)*

- [x] N/A — feature has no realtime surface. The inventory is recorded to the worker log only; it is not streamed over SignalR or AG-UI to the UI in this feature.

## Persistence Impact *(SQLite + Fleece)*

- [x] N/A — feature has no persistence impact. Log retention is handled by the existing Loki pipeline.

## Worker Impact *(Claude Agent SDK)*

- [x] **Changes to session construction in `session-manager.ts`**: `SessionManager.create` and the resume path will each call a new inventory helper and emit one structured log record per call, before or immediately after the SDK `query()` is instantiated. No changes to permissions, tool allowlists, prompts, or A2A/AG-UI flow.
- [x] **Possible new worker helper**: a discovery helper that enumerates skills from the SDK's configured `settingSources` + `cwd` and formats the structured record. No new worker HTTP route.
- [ ] **New agent prompt / tool / permission?** No.
- [ ] **New worker route in `src/Homespun.Worker/src/routes/`?** No.
- [ ] **Changes to A2A / AG-UI message flow?** No.

## Operational Impact

- [ ] **New env var?** No.
- [ ] **New container or compose change?** No.
- [ ] **Fleece.Core version bump?** No.
- [ ] **Bicep / infra change?** No.
- [ ] **Architecture diagram update?** No (topology unchanged).
- [x] **Loki / log volume**: Adds one structured record per session create and per session resume, plus (Story 3) one at worker boot. Volume impact is expected to be negligible relative to the existing per-event stream; the plan should confirm LogQL field names with the ops conventions used elsewhere in the worker.
