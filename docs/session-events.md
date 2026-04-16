# Session events — wire protocol

This document describes the event contract that flows from the worker to the Homespun server to the client (React web app).

- **Worker → server**: A2A protocol, over SSE from the worker container.
- **Server storage**: `A2AEventRecord` append-only JSONL, one record per line at `{baseDir}/{projectId}/{sessionId}.events.jsonl`.
- **Server → client**: `SessionEventEnvelope` wrapping an AG-UI `Event`, delivered live over SignalR (`ReceiveSessionEvent`) and on replay via `GET /api/sessions/{sessionId}/events`.

The same AG-UI envelope shape is used for both live and replay, so clients can use a single reducer for both paths. See `proposal.md` and `design.md` under `openspec/changes/a2a-native-messaging/` for the rationale.

## Envelope

```ts
interface SessionEventEnvelope {
  seq: number          // per-session monotonic, starting at 1
  sessionId: string
  eventId: string      // stable UUID, same across live and replay
  event: AGUIEvent
}
```

Clients deduplicate by `eventId` and resume from `lastSeenSeq` by passing `?since={lastSeenSeq}` to the replay endpoint.

## AG-UI canonical events

These come from the [AG-UI protocol spec](https://docs.ag-ui.com/concepts/events) and are used unchanged. The discriminator field is `type`.

| `type`                 | Purpose                                                               |
| ---------------------- | --------------------------------------------------------------------- |
| `RUN_STARTED`          | A new agent turn begins.                                              |
| `RUN_FINISHED`         | Turn ends successfully. `result` may carry cost/duration/etc.         |
| `RUN_ERROR`            | Turn ends with an error. `message` + optional `code`.                 |
| `TEXT_MESSAGE_START`   | Start of an assistant (or user) text block. `messageId`, `role`.       |
| `TEXT_MESSAGE_CONTENT` | Whole text block contents (no per-token deltas — worker runs with `includePartialMessages=false`). |
| `TEXT_MESSAGE_END`     | End of a text block.                                                  |
| `TOOL_CALL_START`      | Assistant invokes a tool. `toolCallId`, `toolCallName`, `parentMessageId`. |
| `TOOL_CALL_ARGS`       | Whole JSON input at once (no delta JSON).                              |
| `TOOL_CALL_END`        | Tool call finished (args complete; result may follow).                |
| `TOOL_CALL_RESULT`     | Tool result observed. Correlated by `toolCallId`.                      |
| `STATE_SNAPSHOT`       | Full state snapshot (reserved; not emitted today).                     |
| `STATE_DELTA`          | Incremental state delta (reserved; not emitted today).                 |
| `CUSTOM`               | Homespun-namespaced extension. See catalog below.                     |

## Custom event name catalog

Custom events carry two fields:

```ts
interface CustomEvent {
  type: 'CUSTOM'
  name: string   // one of the values below
  value: unknown // payload shape depends on name
}
```

All names are lowercase with dot separators and scoped to the Homespun namespace so non-Homespun AG-UI consumers can safely ignore unrecognized names.

| `name`              | Source A2A event                                         | Payload (`value`)                                                            | Semantic meaning                                              |
| ------------------- | -------------------------------------------------------- | ---------------------------------------------------------------------------- | -------------------------------------------------------------- |
| `thinking`          | `Message` with a `thinking` block                        | `{ text: string, parentMessageId?: string }`                                 | Agent-side reasoning preceding a text or tool-use block.       |
| `hook.started`      | `Message` with `kind=hook_started`                       | `{ hookId: string, hookName: string, hookEvent: string }`                    | A hook (SessionStart, PreToolUse, etc.) began executing.       |
| `hook.response`     | `Message` with `kind=hook_response`                      | `{ hookId, hookName, output?, exitCode?, outcome }`                          | A hook finished. Includes exit code and any captured output.   |
| `system.init`       | `Message` with `sdkMessageType=system`, `subtype=init`   | `{ model?: string, tools?: string[], permissionMode?: string }`              | Worker-emitted session init describing model / tools / mode.   |
| `question.pending`  | `StatusUpdate` input-required, `inputType=question`      | `PendingQuestion`                                                            | Claude is asking the user a question; session paused.          |
| `plan.pending`      | `StatusUpdate` input-required, `inputType=plan-approval` | `{ planContent: string, planFilePath?: string }`                             | Claude presented a plan for approval; session paused.          |
| `status.resumed`    | `StatusUpdate` with `status_resumed`                     | `{}`                                                                          | Session resumed from a paused/input-required state.            |
| `workflow.complete` | `StatusUpdate` with `workflow_complete`                  | `{ status: string, outputs?: unknown, artifacts?: unknown[] }`                | Higher-level workflow (issue-agent, rebase) finished.          |
| `user.message`      | — (server-originated on hub `SendMessage`)                | `{ text: string }`                                                            | Server echo of a user-submitted message for multi-tab sync.    |
| `raw`               | Unknown A2A variant (fallback)                            | `{ original: unknown }`                                                       | Translator could not recognize the A2A event; payload preserved. |
| `context.cleared`   | — (Homespun lifecycle, not from A2A translation)         | `{ sessionId: string }`                                                       | Homespun-internal context-clear notification.                  |

## Resumption protocol

On mount or SignalR reconnect the client:

1. Reads `lastSeenSeq` for the session from Zustand (defaults to `0`).
2. Requests `GET /api/sessions/{id}/events?since={lastSeenSeq}` (mode defaults to `incremental` server-side; client may force `mode=full`).
3. Feeds the returned envelopes through the reducer before processing any buffered live envelopes.
4. Deduplicates by `eventId` against a bounded `Set<string>` of recently-seen ids (LRU at N=10 000 per session).
5. Advances `lastSeenSeq` to `max(lastSeenSeq, envelope.seq)` as it applies each envelope.

## Append-before-broadcast

For every A2A event received from the worker the server:

1. Assigns `seq` (monotonic per-session).
2. Writes the `A2AEventRecord` to the JSONL log and flushes.
3. Translates to an `AGUIEvent` and wraps it in a `SessionEventEnvelope`.
4. Broadcasts via SignalR.

The write-before-broadcast order guarantees that any replay query served after a live broadcast includes that event — clients never see a live event that cannot be replayed.
