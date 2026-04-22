/**
 * TDD tests for the pure AG-UI reducer.
 * Covers tasks 8.1, 8.2, 8.3 of the a2a-native-messaging OpenSpec change:
 *  - 8.1: canned sequence → expected final state
 *  - 8.2: dedup-by-seq (reducer-level safety net even when the hook's eventId dedup misses)
 *  - 8.3: replay-interleave produces a final state identical to strictly-sequential delivery
 */

import { describe, expect, it } from 'vitest'
import { applyEnvelope, initialAGUISessionState, type AGUISessionState } from './agui-reducer'
import type { AGUIEvent, SessionEventEnvelope } from '@/types/session-events'
import { AGUICustomEventName } from '@/types/session-events'

function env(
  seq: number,
  eventId: string,
  event: AGUIEvent,
  sessionId = 's1'
): SessionEventEnvelope {
  return { seq, sessionId, eventId, event }
}

function replay(
  envelopes: SessionEventEnvelope[],
  initial: AGUISessionState = initialAGUISessionState
): AGUISessionState {
  return envelopes.reduce<AGUISessionState>(applyEnvelope, initial)
}

// ---------------------------------------------------------------- 8.1

describe('applyEnvelope — canned sequence (task 8.1)', () => {
  it('builds a message with start / content / end events', () => {
    const final = replay([
      env(1, 'e1', { type: 'RUN_STARTED', threadId: 's1', runId: 'r1', timestamp: 0 }),
      env(2, 'e2', {
        type: 'TEXT_MESSAGE_START',
        messageId: 'm1',
        role: 'assistant',
        timestamp: 1,
      }),
      env(3, 'e3', {
        type: 'TEXT_MESSAGE_CONTENT',
        messageId: 'm1',
        delta: 'Hello, ',
        timestamp: 2,
      }),
      env(4, 'e4', {
        type: 'TEXT_MESSAGE_CONTENT',
        messageId: 'm1',
        delta: 'world.',
        timestamp: 3,
      }),
      env(5, 'e5', { type: 'TEXT_MESSAGE_END', messageId: 'm1', timestamp: 4 }),
      env(6, 'e6', { type: 'RUN_FINISHED', threadId: 's1', runId: 'r1', timestamp: 5 }),
    ])

    expect(final.lastSeenSeq).toBe(6)
    expect(final.messages).toHaveLength(1)
    expect(final.messages[0].role).toBe('assistant')
    expect(final.messages[0].content).toHaveLength(1)
    expect(final.messages[0].content[0]).toEqual({
      kind: 'text',
      text: 'Hello, world.',
      isStreaming: false,
    })
    expect(final.isRunning).toBe(false)
  })

  it('folds a tool call into its parent message', () => {
    const final = replay([
      // A standalone tool call — no preceding TEXT_MESSAGE_START, so the reducer synthesizes
      // a parent message with just the toolUse block.
      env(1, 'e1', {
        type: 'TOOL_CALL_START',
        toolCallId: 'tc1',
        toolCallName: 'Bash',
        parentMessageId: 'm1',
        timestamp: 0,
      }),
      env(2, 'e2', {
        type: 'TOOL_CALL_ARGS',
        toolCallId: 'tc1',
        delta: '{"command":"ls"}',
        timestamp: 1,
      }),
      env(3, 'e3', { type: 'TOOL_CALL_END', toolCallId: 'tc1', timestamp: 2 }),
      env(4, 'e4', {
        type: 'TOOL_CALL_RESULT',
        toolCallId: 'tc1',
        content: 'a.txt\nb.txt',
        messageId: 'm1',
        role: 'tool',
        timestamp: 3,
      }),
    ])

    expect(final.messages).toHaveLength(1)
    const blocks = final.messages[0].content
    expect(blocks).toHaveLength(1)
    expect(blocks[0]).toEqual({
      kind: 'toolUse',
      toolCallId: 'tc1',
      toolName: 'Bash',
      input: '{"command":"ls"}',
      result: 'a.txt\nb.txt',
      isStreaming: false,
    })
  })

  it('routes stale question.pending Custom events to unknownEvents without mutating state', () => {
    // question.pending is retired as of the questions-plans-as-tools change. A stale
    // server (pre-deploy) might still emit it; the reducer must ignore it gracefully so
    // the rest of the state stream continues to fold correctly.
    const final = replay([
      env(1, 'e1', {
        type: 'CUSTOM',
        name: 'question.pending',
        value: {
          id: 'q1',
          toolUseId: 'tu1',
          questions: [{ question: 'pick one', options: [] }],
        },
        timestamp: 0,
      }),
    ])

    expect(final.messages).toHaveLength(0)
    expect(final.unknownEvents).toHaveLength(1)
    expect(final.unknownEvents[0].event).toMatchObject({
      type: 'CUSTOM',
      name: 'question.pending',
    })
    // Sanity: no pendingQuestion / pendingPlan fields exist on the reducer state anymore.
    expect(final).not.toHaveProperty('pendingQuestion')
    expect(final).not.toHaveProperty('pendingPlan')
  })

  it('captures system.init into state.systemInit', () => {
    const final = replay([
      env(1, 'e1', {
        type: 'CUSTOM',
        name: AGUICustomEventName.SystemInit,
        value: { model: 'sonnet', tools: ['Bash', 'Read'], permissionMode: 'default' },
        timestamp: 0,
      }),
    ])
    expect(final.systemInit).toEqual({
      model: 'sonnet',
      tools: ['Bash', 'Read'],
      permissionMode: 'default',
    })
  })
})

// ---------------------------------------------------------------- 8.2

describe('applyEnvelope — dedup / monotonicity (task 8.2)', () => {
  it('is idempotent when the same envelope arrives twice (same seq)', () => {
    const envelope = env(1, 'e1', {
      type: 'TEXT_MESSAGE_START',
      messageId: 'm1',
      role: 'assistant',
      timestamp: 0,
    })
    const first = applyEnvelope(initialAGUISessionState, envelope)
    const second = applyEnvelope(first, envelope)

    // Re-applying does not create a duplicate message or advance state.
    expect(second.messages).toHaveLength(1)
    expect(second).toBe(first) // reference-equal — reducer short-circuits
  })

  it('drops out-of-order replays (seq <= lastSeenSeq) without rewinding', () => {
    const forward = replay([
      env(1, 'e1', {
        type: 'TEXT_MESSAGE_START',
        messageId: 'm1',
        role: 'assistant',
        timestamp: 0,
      }),
      env(2, 'e2', {
        type: 'TEXT_MESSAGE_CONTENT',
        messageId: 'm1',
        delta: 'hi',
        timestamp: 1,
      }),
    ])
    // Arriving "stale" envelope with an earlier seq must be ignored.
    const withStale = applyEnvelope(
      forward,
      env(1, 'e1-again', {
        type: 'TEXT_MESSAGE_START',
        messageId: 'm2',
        role: 'assistant',
        timestamp: 2,
      })
    )
    expect(withStale).toBe(forward)
    expect(withStale.messages).toHaveLength(1)
    expect(withStale.messages[0].id).toBe('m1')
  })
})

// ---------------------------------------------------------------- 8.3

describe('applyEnvelope — replay interleave (task 8.3)', () => {
  it('replay 1..5 with live envelope 4 arriving mid-fetch produces the same state', () => {
    // Arrange: 5 envelopes of a simple turn.
    const envelopes: SessionEventEnvelope[] = [
      env(1, 'e1', { type: 'RUN_STARTED', threadId: 's1', runId: 'r1', timestamp: 0 }),
      env(2, 'e2', {
        type: 'TEXT_MESSAGE_START',
        messageId: 'm1',
        role: 'assistant',
        timestamp: 1,
      }),
      env(3, 'e3', {
        type: 'TEXT_MESSAGE_CONTENT',
        messageId: 'm1',
        delta: 'a',
        timestamp: 2,
      }),
      env(4, 'e4', {
        type: 'TEXT_MESSAGE_CONTENT',
        messageId: 'm1',
        delta: 'b',
        timestamp: 3,
      }),
      env(5, 'e5', { type: 'TEXT_MESSAGE_END', messageId: 'm1', timestamp: 4 }),
    ]

    // Sequential delivery — the ground truth.
    const sequential = replay(envelopes)

    // Interleaved: replay 1..5 first, then "live" 4 arrives. The seq-guard in the reducer
    // drops the duplicate. Final state must match.
    const interleaved = applyEnvelope(replay(envelopes), envelopes[3])

    expect(interleaved).toEqual(sequential)
  })
})

describe('applyEnvelope — empty CUSTOM drops', () => {
  it('drops empty user.message events so tool-answer receipts do not render as empty pills', () => {
    const final = replay([
      env(1, 'e1', {
        type: 'CUSTOM',
        name: AGUICustomEventName.UserMessage,
        value: { text: '' },
        timestamp: 0,
      }),
      env(2, 'e2', {
        type: 'CUSTOM',
        name: AGUICustomEventName.UserMessage,
        value: { text: '   \n\t' },
        timestamp: 1,
      }),
    ])
    expect(final.messages).toEqual([])
  })

  it('keeps non-empty user.message events', () => {
    const final = replay([
      env(1, 'e1', {
        type: 'CUSTOM',
        name: AGUICustomEventName.UserMessage,
        value: { text: 'hello' },
        timestamp: 0,
      }),
    ])
    expect(final.messages).toHaveLength(1)
    expect(final.messages[0].role).toBe('user')
  })

  it('drops empty thinking events', () => {
    const final = replay([
      env(1, 'e1', {
        type: 'CUSTOM',
        name: AGUICustomEventName.Thinking,
        value: { text: '' },
        timestamp: 0,
      }),
    ])
    expect(final.messages).toEqual([])
  })
})
