import { describe, expect, it } from 'vitest'

import type {
  AGUIMessage,
  AGUITextBlock,
  AGUIThinkingBlock,
  AGUIToolUseBlock,
} from '../utils/agui-reducer'

import { convertAGUIMessage } from './convertAGUIMessage'

function makeAssistant(
  content: AGUIMessage['content'],
  overrides: Partial<AGUIMessage> = {}
): AGUIMessage {
  return {
    id: 'msg-1',
    role: 'assistant',
    content,
    createdAt: 1_700_000_000_000,
    ...overrides,
  }
}

const textBlock: AGUITextBlock = { kind: 'text', text: 'hello', isStreaming: false }
const thinkingBlock: AGUIThinkingBlock = { kind: 'thinking', text: 'pondering' }
const toolUseBlock: AGUIToolUseBlock = {
  kind: 'toolUse',
  toolCallId: 'call-1',
  toolName: 'Bash',
  input: '{"command":"ls"}',
  result: 'file.txt\n',
  isStreaming: false,
}

describe('convertAGUIMessage — required mappings', () => {
  it('maps a text block to a text content part', () => {
    const out = convertAGUIMessage(makeAssistant([textBlock]))
    expect(out.role).toBe('assistant')
    expect(out.content).toEqual([{ type: 'text', text: 'hello' }])
  })

  it('maps a thinking block to a reasoning content part', () => {
    const out = convertAGUIMessage(makeAssistant([thinkingBlock]))
    expect(out.content).toEqual([{ type: 'reasoning', text: 'pondering' }])
  })

  it('maps a toolUse block to a tool-call part with toolCallId/toolName/argsText/result', () => {
    const out = convertAGUIMessage(makeAssistant([toolUseBlock]))
    expect(out.content).toEqual([
      {
        type: 'tool-call',
        toolCallId: 'call-1',
        toolName: 'Bash',
        argsText: '{"command":"ls"}',
        result: 'file.txt\n',
      },
    ])
  })

  it('preserves block order in multi-block assistant messages', () => {
    const out = convertAGUIMessage(makeAssistant([thinkingBlock, toolUseBlock, textBlock]))
    const content = out.content as readonly { type: string }[]
    const types = content.map((p) => p.type)
    expect(types).toEqual(['reasoning', 'tool-call', 'text'])
  })

  it('passes user role through unchanged', () => {
    const out = convertAGUIMessage({
      id: 'u1',
      role: 'user',
      content: [textBlock],
      createdAt: 0,
    })
    expect(out.role).toBe('user')
  })

  it('passes system role through unchanged', () => {
    const out = convertAGUIMessage({
      id: 's1',
      role: 'system',
      content: [textBlock],
      createdAt: 0,
    })
    expect(out.role).toBe('system')
  })

  it('carries the message id through as id', () => {
    const out = convertAGUIMessage(makeAssistant([textBlock], { id: 'msg-42' }))
    expect(out.id).toBe('msg-42')
  })

  it('is pure — same input yields deep-equal output', () => {
    const msg = makeAssistant([textBlock, thinkingBlock, toolUseBlock])
    const a = convertAGUIMessage(msg)
    const b = convertAGUIMessage(msg)
    expect(a).toEqual(b)
  })
})

describe('convertAGUIMessage — edge cases', () => {
  it('keeps text part for a streaming text block', () => {
    const out = convertAGUIMessage(
      makeAssistant([{ kind: 'text', text: 'part', isStreaming: true }])
    )
    expect(out.content).toEqual([{ type: 'text', text: 'part' }])
  })

  it('renders toolUse without result as result: undefined', () => {
    const out = convertAGUIMessage(
      makeAssistant([
        {
          kind: 'toolUse',
          toolCallId: 'c2',
          toolName: 'Read',
          input: '{}',
          isStreaming: true,
        },
      ])
    )
    expect(out.content).toEqual([
      {
        type: 'tool-call',
        toolCallId: 'c2',
        toolName: 'Read',
        argsText: '{}',
        result: undefined,
      },
    ])
  })

  it('renders a zero-content message with an empty content array', () => {
    const out = convertAGUIMessage(makeAssistant([]))
    expect(out.content).toEqual([])
  })
})
