## MODIFIED Requirements

### Requirement: Worker session uses a single long-lived SDK query per session

A worker session SHALL back its entire lifetime with a single `query()` invocation. The SDK `Query` is constructed with a persistent input iterable that stays `await`-suspended between turns; follow-up user messages SHALL be delivered by pushing into that same iterable rather than by invoking `Query.streamInput(...)` with a fresh finite iterable.

Rationale: `@anthropic-ai/claude-agent-sdk`'s `streamInput` implementation calls `transport.endInput()` after the supplied iterable is exhausted, which closes stdin to the CLI subprocess and terminates it. Any design that feeds the SDK a finite initial iterable OR that calls `streamInput()` per turn cannot survive past the first turn.

#### Scenario: Initial prompt is delivered via a persistent input iterable

- **WHEN** a session is created
- **THEN** the worker SHALL pass an `AsyncIterable` to `query({ prompt, options })` whose iterator does not return until the session is closed
- **AND** the initial user prompt SHALL be the first value pushed into that iterable
- **AND** the worker SHALL NOT pass a finite `once(...)` generator as the `prompt`

#### Scenario: Follow-up message reuses the existing query via the persistent input iterable

- **WHEN** a user sends a follow-up message to a session that has already produced a `result`
- **THEN** the worker SHALL push the message into the session's persistent input iterable
- **AND** the worker SHALL NOT invoke `Query.streamInput(...)` for that message
- **AND** the SDK CLI process SHALL NOT be restarted for that turn
- **AND** the conversation SHALL continue under the same `conversationId`

#### Scenario: Mid-session mode switch applies to the live query

- **WHEN** `setMode` is invoked on a session that has produced a prior `result`
- **THEN** the new permission mode SHALL be applied to the live query without restarting it

#### Scenario: Mid-session model switch applies to the live query

- **WHEN** `setModel` is invoked on a session that has produced a prior `result`
- **THEN** the new model SHALL take effect on the next turn without restarting the query

#### Scenario: Session close terminates the input iterable

- **WHEN** a session is closed
- **THEN** the worker SHALL close the session's input iterable so that its async iterator returns `{ done: true }`
- **AND** the worker SHALL close the SDK `Query`

## ADDED Requirements

### Requirement: Output channel preserves events across sequential per-turn consumers

The worker's `OutputChannel` SHALL deliver every event pushed into it to exactly one consumer. When a consumer's `for await` loop exits (e.g. because a per-HTTP-request SSE stream returned on `result`) and a subsequent consumer begins iterating the same channel, any events pushed in between SHALL be delivered to the new consumer; no events SHALL be dropped into a stale pending-promise resolver.

Rationale: The worker's single long-lived `Query` forwarder pushes events into `OutputChannel` for the session's lifetime, while per-HTTP-request SSE streams consume it per turn. If a pending resolver from an aborted iteration is reused by a later producer, the event is silently lost.

#### Scenario: Event pushed between iterator re-entries is delivered to next consumer

- **GIVEN** an `OutputChannel` whose prior consumer's `for await` has returned
- **WHEN** the producer pushes an event before the next consumer begins iterating
- **THEN** the new consumer SHALL receive that event on its first `next()`

#### Scenario: Pending resolver is cleared on consumer abort

- **WHEN** a consumer's `for await` on the channel is aborted while awaiting the next event
- **THEN** the channel's pending resolver SHALL be cleared
- **AND** subsequent pushes SHALL land in the internal queue rather than invoke a stale resolver

### Requirement: Debug logging of every SDK boundary message when DEBUG_AGENT_SDK is enabled

When the `DEBUG_AGENT_SDK` environment variable is set to `"true"`, the worker SHALL emit structured log entries at the SDK boundary covering every outbound control or message and every inbound SDK message. When the variable is unset or any other value, the worker SHALL emit no such entries.

The log channel SHALL be `stdout` via the existing `utils/logger.ts` structured-JSON format so that both `docker logs` (for standalone / live-test invocations) and the Promtail→Loki PLG pipeline (for docker compose invocations) receive the entries unchanged.

#### Scenario: Session creation options are logged on session start

- **GIVEN** `DEBUG_AGENT_SDK=true`
- **WHEN** a session is created
- **THEN** the worker SHALL emit a log entry with direction `tx` describing the full session options passed to `query({...})`, excluding raw credentials

#### Scenario: Every user message into the input queue is logged

- **GIVEN** `DEBUG_AGENT_SDK=true`
- **WHEN** a user message is pushed into the session's input iterable (initial or follow-up)
- **THEN** the worker SHALL emit a log entry with direction `tx` containing the user message payload

#### Scenario: Control requests to the SDK are logged

- **GIVEN** `DEBUG_AGENT_SDK=true`
- **WHEN** the worker invokes `Query.setPermissionMode(...)` or `Query.setModel(...)`
- **THEN** the worker SHALL emit a log entry with direction `tx` containing the control-request arguments

#### Scenario: Every raw SDK message received is logged

- **GIVEN** `DEBUG_AGENT_SDK=true`
- **WHEN** the worker's query forwarder yields a message from the SDK `Query`
- **THEN** the worker SHALL emit a log entry with direction `rx` containing the raw SDK message

#### Scenario: Debug logging is disabled by default

- **GIVEN** `DEBUG_AGENT_SDK` is unset or set to any value other than `"true"`
- **WHEN** the worker runs any session activity
- **THEN** the worker SHALL NOT emit any SDK-boundary debug log entries

#### Scenario: Debug env var propagates into the worker container from both invocation paths

- **WHEN** the worker is started via `docker-compose.yml` and the host environment has `DEBUG_AGENT_SDK=true`
- **THEN** the `worker:` service SHALL receive `DEBUG_AGENT_SDK=true`
- **WHEN** the worker is started by the live-test container fixture and the host environment has `DEBUG_AGENT_SDK=true`
- **THEN** the spawned container SHALL receive `DEBUG_AGENT_SDK=true` via `-e`
