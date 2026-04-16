# claude-agent-sessions Specification

## Purpose

This capability manages Claude Code SDK agent sessions, including live event streaming to clients, session resume from persisted event logs, and turn completion semantics.

## Requirements

### Requirement: AG-UI event streaming via SignalR

The system SHALL stream SDK output to clients as AG-UI events wrapped in `SessionEventEnvelope` records, broadcast via the `ClaudeCodeHub` SignalR hub using a single `ReceiveSessionEvent(sessionId, envelope)` method. Every envelope SHALL carry a per-session monotonic `seq`, a stable `eventId`, and the AG-UI event payload. The live broadcast stream and the replay endpoint SHALL produce equivalent envelopes for the same underlying event.

#### Scenario: Tool call events are broadcast

- **WHEN** Claude invokes a tool (e.g. Bash, Read, Grep)
- **THEN** AG-UI `ToolCallStart`, `ToolCallArgs`, and `ToolCallEnd` envelopes SHALL be broadcast in that order
- **AND** the corresponding `ToolCallResult` envelope SHALL be broadcast when the tool result arrives

#### Scenario: Multiple clients receive the same stream

- **WHEN** multiple browser tabs join the same session
- **THEN** all SHALL receive the same AG-UI envelopes via `ReceiveSessionEvent`
- **AND** envelopes SHALL carry identical `seq` and `eventId` values across all tabs

#### Scenario: Live stream is consistent with replay

- **WHEN** a client captures the live stream of envelopes for a session and later calls `GET /api/sessions/{id}/events?mode=full`
- **THEN** the replayed envelopes SHALL be equal by value to the live envelopes for the same session

### Requirement: Session resume from A2A event log

The system SHALL resume prior sessions and reconstruct client state from a per-session append-only A2A event log. The event log SHALL replace any prior `ClaudeMessage` JSONL cache. Sessions discovered on disk SHALL be listable, and the client SHALL rebuild its full render state by replaying the stored events through the same AG-UI translator used for live broadcast.

#### Scenario: Resumable sessions are listed

- **WHEN** a client queries resumable sessions for an entity
- **THEN** stopped sessions whose event logs exist SHALL be listed with mode, model, and last-activity timestamp

#### Scenario: Resume restores conversation context from events

- **WHEN** a user resumes a prior session
- **THEN** the session SHALL return to its next valid state (typically `WaitingForInput`) and the client SHALL rebuild its view by calling `/api/sessions/{id}/events?mode=full`

#### Scenario: All events survive resume with perfect fidelity

- **WHEN** a session ran with a mixture of text, thinking, tool use, tool result, hook, and system messages
- **THEN** all of those events SHALL be present in the replayed stream
- **AND** none of them SHALL be silently dropped

### Requirement: Session completes a turn

The session SHALL transition to `WaitingForInput` when the SDK emits a terminal status-update (AG-UI `RunFinished` or `RunError`), and the corresponding envelope SHALL be present in the event log before the transition is broadcast to clients.

#### Scenario: Final assistant message is captured in the event log

- **WHEN** a turn emits assistant text followed by the terminal status-update
- **THEN** the assistant text envelope SHALL be persisted in the event log with a lower `seq` than the `RunFinished` envelope
- **AND** no assistant text envelope SHALL be lost when the turn completes
