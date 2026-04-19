/**
 * Client-side mirror of the server's session-event envelope + AG-UI event types.
 *
 * These types are hand-maintained to match the C# records in
 * `Homespun.Shared/Models/Sessions/`:
 *
 * - `SessionEventEnvelope.cs` → {@link SessionEventEnvelope}
 * - `AGUIEvents.cs` → {@link AGUIEvent} discriminated union and {@link AGUICustomEventName}
 *
 * Keep the TypeScript here in sync when those C# types change.
 *
 * The envelope is emitted live by the SignalR hub method `ReceiveSessionEvent`, and the
 * same envelope shape is returned from `GET /api/sessions/{id}/events`. Clients dedupe by
 * {@link SessionEventEnvelope.eventId} and track progress via {@link SessionEventEnvelope.seq}.
 */

// ============================================================================
// Envelope
// ============================================================================

export interface SessionEventEnvelope {
  /** Per-session monotonic sequence number starting at 1. */
  seq: number
  /** Claude Code session id. */
  sessionId: string
  /** Stable UUID; same across live and replay deliveries of the same event. */
  eventId: string
  /** AG-UI event payload. */
  event: AGUIEvent
  /**
   * Optional W3C traceparent captured from the server's `Activity.Current` at
   * broadcast time. The client uses it via `withExtractedContext` so the
   * reducer-apply span parents to the server ingest span and Seq shows a
   * contiguous trace from user click → server → envelope → reducer.
   */
  traceparent?: string | null
}

// ============================================================================
// AG-UI Event Types (canonical)
// ============================================================================

export interface AGUIBaseEvent {
  type: string
  /** Unix timestamp in milliseconds. */
  timestamp: number
}

export interface RunStartedEvent extends AGUIBaseEvent {
  type: 'RUN_STARTED'
  threadId: string
  runId: string
}

export interface RunFinishedEvent extends AGUIBaseEvent {
  type: 'RUN_FINISHED'
  threadId: string
  runId: string
  result?: unknown
}

export interface RunErrorEvent extends AGUIBaseEvent {
  type: 'RUN_ERROR'
  message: string
  code?: string
}

export interface TextMessageStartEvent extends AGUIBaseEvent {
  type: 'TEXT_MESSAGE_START'
  messageId: string
  role: 'assistant' | 'user' | 'tool' | string
}

export interface TextMessageContentEvent extends AGUIBaseEvent {
  type: 'TEXT_MESSAGE_CONTENT'
  messageId: string
  /** Whole text block — `includePartialMessages=false` on the worker means no per-token deltas. */
  delta: string
}

export interface TextMessageEndEvent extends AGUIBaseEvent {
  type: 'TEXT_MESSAGE_END'
  messageId: string
}

export interface ToolCallStartEvent extends AGUIBaseEvent {
  type: 'TOOL_CALL_START'
  toolCallId: string
  toolCallName: string
  parentMessageId?: string
}

export interface ToolCallArgsEvent extends AGUIBaseEvent {
  type: 'TOOL_CALL_ARGS'
  toolCallId: string
  /** Whole JSON input at once — no delta streaming. */
  delta: string
}

export interface ToolCallEndEvent extends AGUIBaseEvent {
  type: 'TOOL_CALL_END'
  toolCallId: string
}

export interface ToolCallResultEvent extends AGUIBaseEvent {
  type: 'TOOL_CALL_RESULT'
  toolCallId: string
  content: string
  messageId?: string
  role: string
}

export interface StateSnapshotEvent extends AGUIBaseEvent {
  type: 'STATE_SNAPSHOT'
  snapshot: unknown
}

export interface StateDeltaEvent extends AGUIBaseEvent {
  type: 'STATE_DELTA'
  delta: unknown
}

export interface CustomEvent extends AGUIBaseEvent {
  type: 'CUSTOM'
  /** See {@link AGUICustomEventName}. */
  name: string
  /** Payload specific to the custom event name. */
  value: unknown
}

export type AGUIEvent =
  | RunStartedEvent
  | RunFinishedEvent
  | RunErrorEvent
  | TextMessageStartEvent
  | TextMessageContentEvent
  | TextMessageEndEvent
  | ToolCallStartEvent
  | ToolCallArgsEvent
  | ToolCallEndEvent
  | ToolCallResultEvent
  | StateSnapshotEvent
  | StateDeltaEvent
  | CustomEvent

// ============================================================================
// Custom Event Name Catalog (mirrors C# AGUICustomEventName)
// ============================================================================

/**
 * Homespun-namespaced names for AG-UI `Custom` events.
 * Keep in sync with `Homespun.Shared/Models/Sessions/AGUIEvents.cs`.
 */
export const AGUICustomEventName = {
  /** Agent reasoning block. Payload: `AGUIThinkingData`. */
  Thinking: 'thinking',
  /** Hook invocation started. Payload: `AGUIHookStartedData`. */
  HookStarted: 'hook.started',
  /** Hook invocation finished. Payload: `AGUIHookResponseData`. */
  HookResponse: 'hook.response',
  /** Session init/system message from the worker. Payload: `AGUISystemInitData`. */
  SystemInit: 'system.init',
  /** Claude is asking a question. Payload: `PendingQuestion`. */
  QuestionPending: 'question.pending',
  /** Claude is presenting a plan for approval. Payload: `AGUIPlanPendingData`. */
  PlanPending: 'plan.pending',
  /** Session resumed from paused/input-required state. Payload: `{}`. */
  StatusResumed: 'status.resumed',
  /** Workflow-level (issue-agent, rebase, etc.) completion. Payload: `AGUIWorkflowCompleteData`. */
  WorkflowComplete: 'workflow.complete',
  /** Server echo of a user-submitted message for multi-tab support. Payload: `AGUIUserMessageData`. */
  UserMessage: 'user.message',
  /** Fallback for unrecognized A2A variants. Payload: `{ original: unknown }`. */
  Raw: 'raw',
  /** Homespun-internal context-clear lifecycle notification. Payload: `{ sessionId: string }`. */
  ContextCleared: 'context.cleared',
} as const

export type AGUICustomEventNameValue =
  (typeof AGUICustomEventName)[keyof typeof AGUICustomEventName]

// ============================================================================
// Custom Event Payload Shapes
// ============================================================================

export interface AGUIThinkingData {
  text: string
  parentMessageId?: string
}

export interface AGUIHookStartedData {
  hookId: string
  hookName: string
  hookEvent: string
}

export interface AGUIHookResponseData {
  hookId: string
  hookName: string
  output?: string
  exitCode?: number
  outcome: string
}

export interface AGUISystemInitData {
  model?: string
  tools?: string[]
  permissionMode?: string
}

export interface AGUIPlanPendingData {
  planContent: string
  planFilePath?: string
}

export interface AGUIWorkflowCompleteData {
  status: string
  outputs?: unknown
  artifacts?: unknown[]
}

export interface AGUIUserMessageData {
  text: string
}

export interface AGUIRawData {
  original: unknown
}

export interface AGUIContextClearedData {
  sessionId: string
}
