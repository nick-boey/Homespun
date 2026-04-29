import { describe, expect, it } from 'vitest'

import type { AGUIMessage } from '../utils/agui-reducer'

import { coalesceAssistantMessages } from './coalesceAssistantMessages'

function assistant(id: string, blocks: AGUIMessage['content']): AGUIMessage {
  return { id, role: 'assistant', content: blocks, createdAt: 0 }
}

function user(id: string, text: string): AGUIMessage {
  return {
    id,
    role: 'user',
    content: [{ kind: 'text', text, isStreaming: false }],
    createdAt: 0,
  }
}

describe('coalesceAssistantMessages', () => {
  it('returns input unchanged when no consecutive assistant messages exist', () => {
    const input: AGUIMessage[] = [
      user('u1', 'hi'),
      assistant('a1', [{ kind: 'text', text: 'hello', isStreaming: false }]),
      user('u2', 'next'),
      assistant('a2', [{ kind: 'text', text: 'world', isStreaming: false }]),
    ]

    const out = coalesceAssistantMessages(input)

    expect(out).toHaveLength(4)
    expect(out.map((m) => m.id)).toEqual(['u1', 'a1', 'u2', 'a2'])
  })

  it('merges N consecutive tool-only assistant messages into a single message', () => {
    const input: AGUIMessage[] = [
      user('u1', 'do work'),
      assistant('a1', [
        {
          kind: 'toolUse',
          toolCallId: 'tc1',
          toolName: 'Bash',
          input: '{"command":"ls"}',
          isStreaming: false,
        },
      ]),
      assistant('a2', [
        {
          kind: 'toolUse',
          toolCallId: 'tc2',
          toolName: 'Read',
          input: '{"path":"a"}',
          isStreaming: false,
        },
      ]),
      assistant('a3', [
        {
          kind: 'toolUse',
          toolCallId: 'tc3',
          toolName: 'Grep',
          input: '{"pattern":"x"}',
          isStreaming: false,
        },
      ]),
    ]

    const out = coalesceAssistantMessages(input)

    expect(out).toHaveLength(2)
    expect(out[0].id).toBe('u1')
    expect(out[1].id).toBe('a1')
    expect(out[1].content.map((b) => b.kind)).toEqual(['toolUse', 'toolUse', 'toolUse'])
    const toolNames = out[1].content
      .filter((b): b is Extract<typeof b, { kind: 'toolUse' }> => b.kind === 'toolUse')
      .map((b) => b.toolName)
    expect(toolNames).toEqual(['Bash', 'Read', 'Grep'])
  })

  it('keeps text + tool-use blocks together when assistant turns are merged', () => {
    const input: AGUIMessage[] = [
      assistant('a1', [
        { kind: 'text', text: 'about to call a tool', isStreaming: false },
        {
          kind: 'toolUse',
          toolCallId: 'tc1',
          toolName: 'Bash',
          input: '{}',
          isStreaming: false,
        },
      ]),
      assistant('a2', [
        {
          kind: 'toolUse',
          toolCallId: 'tc2',
          toolName: 'Read',
          input: '{}',
          isStreaming: false,
        },
      ]),
      assistant('a3', [{ kind: 'text', text: 'done', isStreaming: false }]),
    ]

    const out = coalesceAssistantMessages(input)

    expect(out).toHaveLength(1)
    expect(out[0].id).toBe('a1')
    expect(out[0].content.map((b) => b.kind)).toEqual(['text', 'toolUse', 'toolUse', 'text'])
  })

  it('does not merge across an intervening user message', () => {
    const input: AGUIMessage[] = [
      assistant('a1', [
        {
          kind: 'toolUse',
          toolCallId: 'tc1',
          toolName: 'Bash',
          input: '{}',
          isStreaming: false,
        },
      ]),
      user('u1', 'wait'),
      assistant('a2', [
        {
          kind: 'toolUse',
          toolCallId: 'tc2',
          toolName: 'Read',
          input: '{}',
          isStreaming: false,
        },
      ]),
    ]

    const out = coalesceAssistantMessages(input)

    expect(out).toHaveLength(3)
    expect(out.map((m) => m.id)).toEqual(['a1', 'u1', 'a2'])
  })

  it('does not mutate the input array or its messages', () => {
    const a1 = assistant('a1', [
      { kind: 'toolUse', toolCallId: 'tc1', toolName: 'Bash', input: '{}', isStreaming: false },
    ])
    const a2 = assistant('a2', [
      { kind: 'toolUse', toolCallId: 'tc2', toolName: 'Read', input: '{}', isStreaming: false },
    ])
    const input: AGUIMessage[] = [a1, a2]
    const inputSnapshot = JSON.stringify(input)

    coalesceAssistantMessages(input)

    expect(JSON.stringify(input)).toBe(inputSnapshot)
    expect(input[0]).toBe(a1)
    expect(input[1]).toBe(a2)
  })
})
