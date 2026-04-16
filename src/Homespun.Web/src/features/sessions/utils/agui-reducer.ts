/**
 * Pure reducer that folds an ordered stream of {@link SessionEventEnvelope}s into a
 * renderable session state.
 *
 * This is the one place AG-UI → render-state translation lives on the client. The reducer
 * is used both by the live SignalR path and by replay (`GET /api/sessions/{id}/events`) so
 * the final state shape is identical regardless of arrival path — which is the whole point
 * of the a2a-native-messaging change.
 *
 * Design constraints:
 *  - Pure: `(state, envelope) => state`. No side effects, no React, no time-dependency.
 *  - Idempotent by `eventId`: applying the same envelope twice must not change the state
 *    beyond the first application. Dedup is the hook's responsibility; the reducer enforces
 *    monotonicity of `lastSeenSeq` so even an accidentally-replayed envelope cannot rewind.
 *  - Tolerant of unknown shapes: unknown AG-UI event types are recorded into
 *    {@link AGUISessionState.unknownEvents} without throwing, so the UI can surface diagnostics.
 */

import type {
  AGUIEvent,
  AGUIPlanPendingData,
  AGUIThinkingData,
  AGUIUserMessageData,
  SessionEventEnvelope,
  ToolCallArgsEvent,
  ToolCallEndEvent,
  ToolCallResultEvent,
  ToolCallStartEvent,
  TextMessageContentEvent,
  TextMessageEndEvent,
  TextMessageStartEvent,
  CustomEvent,
  RunErrorEvent,
  RunFinishedEvent,
  RunStartedEvent,
} from '@/types/session-events'
import { AGUICustomEventName } from '@/types/session-events'

// ============================================================================
// Render state shape
// ============================================================================

/** A text block within an assistant message. */
export interface AGUITextBlock {
  kind: 'text'
  /** Whole-block text. Worker runs with `includePartialMessages=false` so there are no sub-token deltas. */
  text: string
  /** Still accumulating content (between `TextMessageStart` and `TextMessageEnd`). */
  isStreaming: boolean
}

/** A reasoning ("thinking") block emitted via the `thinking` custom event. */
export interface AGUIThinkingBlock {
  kind: 'thinking'
  text: string
}

/** A tool_use block with args and (eventually) a result. */
export interface AGUIToolUseBlock {
  kind: 'toolUse'
  toolCallId: string
  toolName: string
  /** JSON-stringified input accumulated from ToolCallArgs events. */
  input: string
  /** Stringified result content from a `ToolCallResult` event. */
  result?: string
  /** True between `ToolCallStart` and `ToolCallEnd`. */
  isStreaming: boolean
}

export type AGUIContentBlock = AGUITextBlock | AGUIThinkingBlock | AGUIToolUseBlock

/**
 * Top-level message in the session. Each AG-UI `TextMessageStart` / `ToolCallStart` with a
 * new `messageId` or `parentMessageId` creates (or finds) one of these. Tool results are
 * folded back into the `toolUse` block that spawned them, not into a new message.
 */
export interface AGUIMessage {
  id: string
  role: 'user' | 'assistant' | 'system' | 'tool'
  content: AGUIContentBlock[]
  /** Unix ms when the earliest envelope for this message was observed. */
  createdAt: number
}

export interface AGUIHookStarted {
  hookId: string
  hookName: string
  hookEvent: string
}

export interface AGUIHookResponse {
  hookId: string
  hookName: string
  output?: string
  exitCode?: number
  outcome: string
}

/** Full render state rebuilt by folding envelopes in order. */
export interface AGUISessionState {
  /** Highest applied `seq`; the reducer never rewinds. */
  lastSeenSeq: number
  /** All messages, in order. */
  messages: AGUIMessage[]
  /** Pending question-to-user (if any). Cleared on the next user-text or status transition. */
  pendingQuestion: unknown | null
  /** Pending plan-for-approval (if any). */
  pendingPlan: AGUIPlanPendingData | null
  /** Reverse index: toolCallId → ordinal of the message that owns the block. */
  toolCallIndex: Record<string, string>
  /** `system.init` payload, if observed. Handy for model/tools badges in the UI. */
  systemInit: { model?: string; tools?: string[]; permissionMode?: string } | null
  /** All hook.started + hook.response events in order. */
  hookEvents: Array<
    { kind: 'started'; data: AGUIHookStarted } | { kind: 'response'; data: AGUIHookResponse }
  >
  /** `true` between RunStarted and RunFinished/RunError. */
  isRunning: boolean
  /** Last RunError message, if any. */
  lastError: { message: string; code?: string } | null
  /** Collected `raw` and unknown envelopes (for diagnostics; not normally rendered). */
  unknownEvents: Array<{ seq: number; eventId: string; event: AGUIEvent }>
  /**
   * Composite keys (`${eventId}::${event.type}`) of envelopes already folded into state.
   * Lets the reducer stay idempotent when the same envelope arrives twice without
   * dropping sibling envelopes that share `eventId` but differ in `event.type`.
   */
  appliedEnvelopeKeys: Set<string>
}

export const initialAGUISessionState: AGUISessionState = {
  lastSeenSeq: 0,
  messages: [],
  pendingQuestion: null,
  pendingPlan: null,
  toolCallIndex: {},
  systemInit: null,
  hookEvents: [],
  isRunning: false,
  lastError: null,
  unknownEvents: [],
  appliedEnvelopeKeys: new Set<string>(),
}

// ============================================================================
// Apply one envelope
// ============================================================================

/**
 * Fold one envelope into the state. Returns a new state value; the input is never mutated.
 *
 * Contract:
 *  - If `envelope.seq <= state.lastSeenSeq`, the envelope is dropped and the reference-
 *    equal state is returned. This is the reducer-level replay safety net; the hook's
 *    eventId dedup is the primary line of defense but this keeps a drifted caller from
 *    rewinding the state.
 */
export function applyEnvelope(
  state: AGUISessionState,
  envelope: SessionEventEnvelope
): AGUISessionState {
  // A parent A2A event can translate into multiple AG-UI envelopes that share the same
  // `seq` and `eventId` (e.g. text start/content/end). Dedup by composite key so siblings
  // still apply, while true duplicates are idempotent.
  const key = `${envelope.eventId}::${envelope.event.type}`
  if (state.appliedEnvelopeKeys.has(key)) {
    return state
  }
  // Drift safety: strictly older seqs rewind the stream and are dropped.
  if (envelope.seq < state.lastSeenSeq) {
    return state
  }

  const next = applyEvent(state, envelope.event)
  const appliedEnvelopeKeys = new Set(state.appliedEnvelopeKeys)
  appliedEnvelopeKeys.add(key)
  return {
    ...next,
    lastSeenSeq: Math.max(state.lastSeenSeq, envelope.seq),
    appliedEnvelopeKeys,
  }
}

function applyEvent(state: AGUISessionState, event: AGUIEvent): AGUISessionState {
  switch (event.type) {
    case 'RUN_STARTED':
      return applyRunStarted(state, event)
    case 'RUN_FINISHED':
      return applyRunFinished(state, event)
    case 'RUN_ERROR':
      return applyRunError(state, event)
    case 'TEXT_MESSAGE_START':
      return applyTextStart(state, event)
    case 'TEXT_MESSAGE_CONTENT':
      return applyTextContent(state, event)
    case 'TEXT_MESSAGE_END':
      return applyTextEnd(state, event)
    case 'TOOL_CALL_START':
      return applyToolStart(state, event)
    case 'TOOL_CALL_ARGS':
      return applyToolArgs(state, event)
    case 'TOOL_CALL_END':
      return applyToolEnd(state, event)
    case 'TOOL_CALL_RESULT':
      return applyToolResult(state, event)
    case 'CUSTOM':
      return applyCustom(state, event)
    default:
      // STATE_SNAPSHOT / STATE_DELTA / unknown — not rendered today but preserved for debugging.
      return {
        ...state,
        unknownEvents: [...state.unknownEvents, { seq: state.lastSeenSeq + 1, eventId: '', event }],
      }
  }
}

// --------------------------- Run lifecycle ---------------------------

function applyRunStarted(state: AGUISessionState, _event: RunStartedEvent): AGUISessionState {
  return { ...state, isRunning: true, lastError: null }
}

function applyRunFinished(state: AGUISessionState, _event: RunFinishedEvent): AGUISessionState {
  return { ...state, isRunning: false }
}

function applyRunError(state: AGUISessionState, event: RunErrorEvent): AGUISessionState {
  return {
    ...state,
    isRunning: false,
    lastError: { message: event.message, code: event.code },
  }
}

// --------------------------- Text messages ---------------------------

function applyTextStart(state: AGUISessionState, event: TextMessageStartEvent): AGUISessionState {
  const existing = state.messages.find((m) => m.id === event.messageId)
  if (existing) {
    // Same messageId seen again — append a new text block to the existing message
    // (multi-block turns emit one TEXT_MESSAGE_START per block).
    return replaceMessage(state, event.messageId, (m) => ({
      ...m,
      content: [...m.content, { kind: 'text', text: '', isStreaming: true }],
    }))
  }
  const newMessage: AGUIMessage = {
    id: event.messageId,
    role: normalizeRole(event.role),
    content: [{ kind: 'text', text: '', isStreaming: true }],
    createdAt: event.timestamp,
  }
  return { ...state, messages: [...state.messages, newMessage] }
}

function applyTextContent(
  state: AGUISessionState,
  event: TextMessageContentEvent
): AGUISessionState {
  return replaceMessage(state, event.messageId, (m) => {
    // Append delta to the last streaming text block, or create one if none exists
    // (defensive for out-of-order content before start).
    const content = [...m.content]
    const lastStreamingTextIdx = findLastIndex(content, (b) => b.kind === 'text' && b.isStreaming)
    if (lastStreamingTextIdx === -1) {
      content.push({ kind: 'text', text: event.delta, isStreaming: true })
    } else {
      const block = content[lastStreamingTextIdx] as AGUITextBlock
      content[lastStreamingTextIdx] = { ...block, text: block.text + event.delta }
    }
    return { ...m, content }
  })
}

function applyTextEnd(state: AGUISessionState, event: TextMessageEndEvent): AGUISessionState {
  return replaceMessage(state, event.messageId, (m) => ({
    ...m,
    content: m.content.map((b) =>
      b.kind === 'text' && b.isStreaming ? { ...b, isStreaming: false } : b
    ),
  }))
}

// --------------------------- Tool calls ---------------------------

function applyToolStart(state: AGUISessionState, event: ToolCallStartEvent): AGUISessionState {
  const parentId = event.parentMessageId ?? `tool-${event.toolCallId}`
  const block: AGUIToolUseBlock = {
    kind: 'toolUse',
    toolCallId: event.toolCallId,
    toolName: event.toolCallName,
    input: '',
    isStreaming: true,
  }

  const existing = state.messages.find((m) => m.id === parentId)
  let nextMessages: AGUIMessage[]
  if (existing) {
    nextMessages = state.messages.map((m) =>
      m.id === parentId ? { ...m, content: [...m.content, block] } : m
    )
  } else {
    nextMessages = [
      ...state.messages,
      {
        id: parentId,
        role: 'assistant',
        content: [block],
        createdAt: event.timestamp,
      },
    ]
  }

  return {
    ...state,
    messages: nextMessages,
    toolCallIndex: { ...state.toolCallIndex, [event.toolCallId]: parentId },
  }
}

function applyToolArgs(state: AGUISessionState, event: ToolCallArgsEvent): AGUISessionState {
  const parentId = state.toolCallIndex[event.toolCallId]
  if (!parentId) return state
  return replaceMessage(state, parentId, (m) => ({
    ...m,
    content: m.content.map((b) =>
      b.kind === 'toolUse' && b.toolCallId === event.toolCallId
        ? { ...b, input: b.input + event.delta }
        : b
    ),
  }))
}

function applyToolEnd(state: AGUISessionState, event: ToolCallEndEvent): AGUISessionState {
  const parentId = state.toolCallIndex[event.toolCallId]
  if (!parentId) return state
  return replaceMessage(state, parentId, (m) => ({
    ...m,
    content: m.content.map((b) =>
      b.kind === 'toolUse' && b.toolCallId === event.toolCallId ? { ...b, isStreaming: false } : b
    ),
  }))
}

function applyToolResult(state: AGUISessionState, event: ToolCallResultEvent): AGUISessionState {
  const parentId = state.toolCallIndex[event.toolCallId]
  if (!parentId) return state
  return replaceMessage(state, parentId, (m) => ({
    ...m,
    content: m.content.map((b) =>
      b.kind === 'toolUse' && b.toolCallId === event.toolCallId
        ? { ...b, result: event.content, isStreaming: false }
        : b
    ),
  }))
}

// --------------------------- Custom events ---------------------------

function applyCustom(state: AGUISessionState, event: CustomEvent): AGUISessionState {
  switch (event.name) {
    case AGUICustomEventName.SystemInit:
      return { ...state, systemInit: event.value as AGUISessionState['systemInit'] }

    case AGUICustomEventName.Thinking: {
      const thinking = event.value as AGUIThinkingData
      const parentId = thinking.parentMessageId
      if (!parentId) {
        // Orphan thinking — park into a new message so it's visible.
        return {
          ...state,
          messages: [
            ...state.messages,
            {
              id: `thinking-${event.timestamp}-${state.messages.length}`,
              role: 'assistant',
              content: [{ kind: 'thinking', text: thinking.text }],
              createdAt: event.timestamp,
            },
          ],
        }
      }
      const existing = state.messages.find((m) => m.id === parentId)
      if (!existing) {
        return {
          ...state,
          messages: [
            ...state.messages,
            {
              id: parentId,
              role: 'assistant',
              content: [{ kind: 'thinking', text: thinking.text }],
              createdAt: event.timestamp,
            },
          ],
        }
      }
      return replaceMessage(state, parentId, (m) => ({
        ...m,
        content: [...m.content, { kind: 'thinking', text: thinking.text }],
      }))
    }

    case AGUICustomEventName.HookStarted:
      return {
        ...state,
        hookEvents: [
          ...state.hookEvents,
          { kind: 'started', data: event.value as AGUIHookStarted },
        ],
      }

    case AGUICustomEventName.HookResponse:
      return {
        ...state,
        hookEvents: [
          ...state.hookEvents,
          { kind: 'response', data: event.value as AGUIHookResponse },
        ],
      }

    case AGUICustomEventName.QuestionPending:
      return { ...state, pendingQuestion: event.value }

    case AGUICustomEventName.PlanPending:
      return { ...state, pendingPlan: event.value as AGUIPlanPendingData }

    case AGUICustomEventName.StatusResumed:
      // Resuming clears any transient input-required state; run status itself is
      // driven by RunStarted/Finished events.
      return { ...state, pendingQuestion: null, pendingPlan: null }

    case AGUICustomEventName.ContextCleared:
      // Treated as a full reset — the backend starts a fresh event log, so any envelopes
      // arriving after this belong to a new session from the client's perspective.
      return { ...initialAGUISessionState, lastSeenSeq: state.lastSeenSeq }

    case AGUICustomEventName.UserMessage: {
      const userMsg = event.value as AGUIUserMessageData
      return {
        ...state,
        messages: [
          ...state.messages,
          {
            id: `user-${event.timestamp}-${state.messages.length}`,
            role: 'user',
            content: [{ kind: 'text', text: userMsg.text, isStreaming: false }],
            createdAt: event.timestamp,
          },
        ],
      }
    }

    case AGUICustomEventName.WorkflowComplete:
      // Presentation concern (typically a toast) — not part of the message tree.
      return state

    case AGUICustomEventName.Raw:
    default:
      return {
        ...state,
        unknownEvents: [...state.unknownEvents, { seq: state.lastSeenSeq + 1, eventId: '', event }],
      }
  }
}

// --------------------------- Helpers ---------------------------

function replaceMessage(
  state: AGUISessionState,
  id: string,
  updater: (m: AGUIMessage) => AGUIMessage
): AGUISessionState {
  const idx = state.messages.findIndex((m) => m.id === id)
  if (idx === -1) return state
  const next = [...state.messages]
  next[idx] = updater(state.messages[idx])
  return { ...state, messages: next }
}

function normalizeRole(role: string): AGUIMessage['role'] {
  switch (role.toLowerCase()) {
    case 'user':
      return 'user'
    case 'system':
      return 'system'
    case 'tool':
      return 'tool'
    default:
      return 'assistant'
  }
}

function findLastIndex<T>(arr: readonly T[], predicate: (item: T) => boolean): number {
  for (let i = arr.length - 1; i >= 0; i--) {
    if (predicate(arr[i])) return i
  }
  return -1
}
