## ADDED Requirements

### Requirement: A2A events are stored verbatim with per-session monotonic sequence numbers

The server SHALL persist every A2A event received from the worker into an append-only per-session log, assigning a strictly monotonic `seq` number starting at 1 for each new session. The stored record SHALL include the original A2A event payload, a server-assigned UUID `eventId`, the session id, the `seq`, and the receive timestamp.

#### Scenario: First event in a new session receives seq=1

- **WHEN** the server receives the first A2A event for a previously-unknown session
- **THEN** the event SHALL be appended to the session's event log with `seq = 1`
- **AND** the event SHALL be assigned a stable UUID `eventId`

#### Scenario: Subsequent events increment seq monotonically

- **WHEN** the server receives additional A2A events for an existing session
- **THEN** each event SHALL be appended with `seq` equal to one greater than the previous event's `seq`

#### Scenario: Concurrent appends to the same session remain monotonic

- **WHEN** two A2A events arrive concurrently for the same session
- **THEN** the store SHALL serialize the appends such that resulting `seq` values are strictly monotonic and line-ordered in the underlying file

#### Scenario: Events persist before broadcast

- **WHEN** an A2A event is received
- **THEN** the event SHALL be written to the on-disk event log before any live SignalR broadcast for that event is dispatched

### Requirement: A2A events are translated to AG-UI envelopes by a single translator

The server SHALL translate every stored A2A event to an AG-UI event envelope using a single pure translation function that is used for both live broadcast and replay.

#### Scenario: Live broadcast and replay use the same translator

- **WHEN** the same A2A event is broadcast live and later fetched via replay
- **THEN** the resulting AG-UI envelopes SHALL be equal by value (same `seq`, `eventId`, AG-UI event type, and payload)

#### Scenario: Canonical A2A events map to canonical AG-UI events

- **WHEN** an A2A `Message` with an agent text block is translated
- **THEN** the result SHALL be an AG-UI `TextMessageStart` + `TextMessageContent` + `TextMessageEnd` sequence for that block
- **WHEN** an A2A `Message` with an agent tool_use block is translated
- **THEN** the result SHALL be an AG-UI `ToolCallStart` + `ToolCallArgs` + `ToolCallEnd` sequence
- **WHEN** an A2A `StatusUpdate` with state `completed` is translated
- **THEN** the result SHALL be an AG-UI `RunFinished` event

#### Scenario: Non-canonical concerns map to AG-UI Custom events

- **WHEN** an A2A `Message` system-init, system-hook_started, system-hook_response, thinking block, `StatusUpdate` input-required (question/plan), status_resumed, or workflow_complete is translated
- **THEN** the result SHALL be an AG-UI `Custom` event with a Homespun-namespaced `name` (`system.init`, `hook.started`, `hook.response`, `thinking`, `question.pending`, `plan.pending`, `status.resumed`, `workflow.complete`) and the source payload carried in `data`

#### Scenario: Unknown A2A variants never break translation

- **WHEN** the translator receives an A2A event whose shape is unknown
- **THEN** the translator SHALL emit an AG-UI `Custom` event with `name = "raw"` and the original payload under `data.original`
- **AND** the translator SHALL NOT throw

### Requirement: Replay endpoint serves AG-UI envelopes from the event log

The system SHALL expose `GET /api/sessions/{sessionId}/events?since={long?}&mode={incremental|full}` which returns stored events translated through the same translator used for live broadcast, in `seq`-ascending order.

#### Scenario: Incremental mode returns only events after since

- **WHEN** a client requests `?since=N&mode=incremental`
- **THEN** the response SHALL contain envelopes with `seq > N` in ascending order

#### Scenario: Full mode ignores since

- **WHEN** a client requests `?mode=full`
- **THEN** the response SHALL contain all envelopes starting from `seq = 1`

#### Scenario: Default mode comes from server config

- **WHEN** `mode` is absent and `SessionEvents:ReplayMode` is set to `Incremental` (default)
- **THEN** the endpoint SHALL behave as incremental
- **WHEN** `mode` is absent and `SessionEvents:ReplayMode` is set to `Full`
- **THEN** the endpoint SHALL behave as full

#### Scenario: Since beyond the end returns empty

- **WHEN** a client requests `?since=N` where `N` is greater than or equal to the current highest seq
- **THEN** the response SHALL be an empty array with HTTP 200
- **AND** the response SHALL NOT be treated as an error

#### Scenario: Unknown session returns 404

- **WHEN** a client requests events for a session id that has no event log
- **THEN** the response SHALL be HTTP 404

### Requirement: Live AG-UI broadcasts carry stable envelope metadata

The server SHALL broadcast every AG-UI event to SignalR clients wrapped in a `SessionEventEnvelope` carrying `seq`, `sessionId`, `eventId`, and the AG-UI event itself.

#### Scenario: Live envelope matches replay envelope

- **WHEN** a client observes an envelope live and subsequently fetches the same event via replay
- **THEN** both envelopes SHALL have the same `eventId` and `seq`

#### Scenario: Hub exposes one broadcast method for AG-UI events

- **WHEN** the server broadcasts any AG-UI event
- **THEN** it SHALL use a single hub method `ReceiveSessionEvent(sessionId, envelope)`
- **AND** per-type `BroadcastAGUI*` hub methods SHALL NOT be used

### Requirement: Client reconciles live and replay via eventId dedup

Client consumers SHALL track the last-seen `seq` per session and SHALL deduplicate envelopes by `eventId` when merging live and replay streams.

#### Scenario: Duplicate eventIds are ignored

- **WHEN** a client receives the same envelope (same `eventId`) from both live broadcast and a replay response
- **THEN** the client SHALL apply the envelope exactly once to its render state

#### Scenario: Refresh resumes from lastSeenSeq

- **WHEN** a client mounts or its SignalR connection reconnects with a persisted `lastSeenSeq = N`
- **THEN** the client SHALL fetch `/api/sessions/{id}/events?since=N` before rendering
- **AND** the client SHALL merge the replay response with any buffered live envelopes before updating render state

#### Scenario: Client mode override takes precedence over server default

- **WHEN** a client appends `mode=full` to its replay request
- **THEN** the endpoint SHALL return all events regardless of `SessionEvents:ReplayMode`

### Requirement: Cache purge on upgrade

On first startup after an upgrade that introduces the A2A event log format, the system SHALL delete any pre-existing session cache files under the cache directory and log a warning indicating the count of deleted files.

#### Scenario: Pre-existing ClaudeMessage cache files are removed

- **WHEN** the server starts and finds `*.jsonl` files under the cache directory
- **THEN** those files SHALL be deleted before the server begins serving requests
- **AND** a warning SHALL be logged naming the count of files removed

#### Scenario: Environment opt-out preserves files

- **WHEN** `HOMESPUN_SKIP_CACHE_PURGE=true` is set in the process environment
- **THEN** the purge SHALL be skipped and a warning SHALL be logged that the cache is intact
