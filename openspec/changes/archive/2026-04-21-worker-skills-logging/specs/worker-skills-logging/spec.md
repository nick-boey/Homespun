## ADDED Requirements

### Requirement: Per-session resource inventory logging

The worker SHALL log the full set of Claude Agent resources available per session at creation and resume time.

#### Scenario: Session create emits inventory
- **WHEN** `SessionManager.create(...)` is invoked
- **THEN** exactly one `info`-level log entry SHALL be emitted listing skills, plugins, commands, agents, hooks, and mcpServers
- **AND** the entry SHALL be correlated by `sessionId`

#### Scenario: Session resume emits inventory
- **WHEN** a session is resumed
- **THEN** the same enumeration SHALL be logged tagged as `event=resume`
- **AND** SHALL reflect the inventory at resume time, not a cached copy

#### Scenario: Empty categories produce empty lists
- **WHEN** the working directory has zero project-level skills and no plugins
- **THEN** the log entry SHALL contain explicit empty lists for each category (not missing fields)

#### Scenario: Discovery failure does not block session
- **WHEN** skill discovery fails (e.g. unreadable directory)
- **THEN** the session SHALL still start
- **AND** a warning-level log entry SHALL record the failure
- **AND** the inventory record SHALL still be emitted with `discoveryErrors`

### Requirement: Structured log record format

The log entry SHALL be a single structured record per session event.

#### Scenario: Log format is tail-friendly and parseable
- **WHEN** an inventory record is emitted
- **THEN** it SHALL follow the format `inventory event=<type> sessionId=<id> payload={...json...}`

#### Scenario: All six category lists always present
- **WHEN** an inventory record is emitted
- **THEN** the JSON payload SHALL contain `skills`, `plugins`, `commands`, `agents`, `hooks`, `mcpServers`
- **AND** each SHALL be present even if empty

#### Scenario: No secret material in log entries
- **WHEN** an inventory record is emitted
- **THEN** it SHALL NOT contain token values, credentials, `env` dumps, or API keys
- **AND** entries SHALL list only names, transports, paths, scopes, and status flags

#### Scenario: Stable field names across releases
- **WHEN** the JSON payload field names change
- **THEN** the change SHALL be documented as a spec update, not made ad-hoc

### Requirement: Per-tool-call origin attribution

The existing `canUseTool` log line SHALL include the originating skill/plugin name.

#### Scenario: Built-in tool shows builtin origin
- **WHEN** a built-in tool (e.g. Read, Grep) is invoked
- **THEN** the `canUseTool` log line SHALL include `origin=builtin`

#### Scenario: MCP tool shows server origin
- **WHEN** a plugin-provided tool (e.g. `mcp__playwright__browser_click`) is invoked
- **THEN** the `canUseTool` log line SHALL include `origin=mcp:playwright`

#### Scenario: Unknown tool shows unknown origin
- **WHEN** a tool's origin cannot be determined
- **THEN** the `canUseTool` log line SHALL include `origin=unknown`

### Requirement: Boot-time inventory

The worker SHALL emit a one-shot inventory at startup for smoke-testing.

#### Scenario: Boot inventory emitted at startup
- **WHEN** the worker starts and the HTTP server reports ready
- **THEN** one inventory log entry SHALL be emitted with `event=boot` and `sessionId=boot`

#### Scenario: Missing working directory at boot
- **WHEN** the default working directory is missing or unreadable
- **THEN** a warning-level log entry SHALL record the condition
- **AND** the worker SHALL still start
