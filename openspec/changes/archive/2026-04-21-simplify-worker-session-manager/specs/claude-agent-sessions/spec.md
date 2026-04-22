## ADDED Requirements

### Requirement: Single authoritative source for session message replay

Full-session message replay SHALL be served exclusively by the server-side `MessageCacheStore`. The worker SHALL NOT maintain an in-memory message history, and SHALL NOT expose a replay endpoint.

#### Scenario: Replay request after worker restart
- **WHEN** a client requests message history for a session whose worker has been restarted
- **THEN** the server SHALL serve the replay from `MessageCacheStore`
- **AND** no fallback to the worker SHALL be attempted

#### Scenario: Worker has no replay endpoint
- **WHEN** any caller issues `GET /sessions/:id/messages` against the worker
- **THEN** the request SHALL receive a 404 response

### Requirement: Worker session uses a single long-lived SDK query per session

A worker session SHALL back its entire lifetime with a single `query()` invocation; follow-up user messages SHALL be delivered via the SDK's streaming-input primitive without tearing down and rebuilding the query.

#### Scenario: Follow-up message reuses the existing query
- **WHEN** a user sends a follow-up message to a session that has already produced a `result`
- **THEN** the worker SHALL push the message into the existing query via streaming input
- **AND** the SDK CLI process SHALL NOT be restarted for that turn
- **AND** the conversation SHALL continue under the same `conversationId`

#### Scenario: Mid-session mode switch applies to the live query
- **WHEN** `setMode` is invoked on a session that has produced a prior `result`
- **THEN** the new permission mode SHALL be applied to the live query without restarting it

#### Scenario: Mid-session model switch applies to the live query
- **WHEN** `setModel` is invoked on a session that has produced a prior `result`
- **THEN** the new model SHALL take effect on the next turn without restarting the query

