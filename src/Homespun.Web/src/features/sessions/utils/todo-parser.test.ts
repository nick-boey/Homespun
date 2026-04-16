import { describe, it, expect } from 'vitest'
import { parseTodosFromMessages } from './todo-parser'
import type { AGUIMessage } from './agui-reducer'

// Test helpers — construct the AG-UI message shape the reducer would produce by folding
// envelopes. Tests short-circuit the reducer and hand completed messages to the parser.

function toolUseMessage(toolName: string, input: string | undefined, id = 'msg-1'): AGUIMessage {
  return {
    id,
    role: 'assistant',
    createdAt: Date.now(),
    content: [
      {
        kind: 'toolUse',
        toolCallId: `tool-${id}`,
        toolName,
        input: input ?? '',
        isStreaming: false,
      },
    ],
  }
}

function textMessage(text: string, id = 'msg-text'): AGUIMessage {
  return {
    id,
    role: 'assistant',
    createdAt: Date.now(),
    content: [{ kind: 'text', text, isStreaming: false }],
  }
}

function todoWriteMessage(
  todos: Array<{
    content?: string
    activeForm?: string
    status: 'pending' | 'in_progress' | 'completed'
  }>,
  id = 'msg-1'
): AGUIMessage {
  return toolUseMessage('TodoWrite', JSON.stringify({ todos }), id)
}

describe('parseTodosFromMessages', () => {
  it('returns empty array when no messages provided', () => {
    expect(parseTodosFromMessages([])).toEqual([])
  })

  it('returns empty array when no TodoWrite tool calls exist', () => {
    const messages: AGUIMessage[] = [
      textMessage('Hello there'),
      toolUseMessage('Bash', 'ls', 'msg-2'),
    ]

    expect(parseTodosFromMessages(messages)).toEqual([])
  })

  it('parses todos from the last TodoWrite tool call', () => {
    const messages: AGUIMessage[] = [
      todoWriteMessage(
        [
          { content: 'Write tests', activeForm: 'Writing tests', status: 'completed' },
          { content: 'Implement feature', activeForm: 'Implementing feature', status: 'pending' },
        ],
        'first'
      ),
      todoWriteMessage(
        [
          { content: 'Write tests', activeForm: 'Writing tests', status: 'completed' },
          {
            content: 'Implement feature',
            activeForm: 'Implementing feature',
            status: 'in_progress',
          },
          {
            content: 'Add documentation',
            activeForm: 'Adding documentation',
            status: 'pending',
          },
        ],
        'second'
      ),
    ]

    const result = parseTodosFromMessages(messages)
    expect(result).toHaveLength(3)
    expect(result[0]).toEqual({
      content: 'Write tests',
      activeForm: 'Writing tests',
      status: 'completed',
    })
    expect(result[1]).toEqual({
      content: 'Implement feature',
      activeForm: 'Implementing feature',
      status: 'in_progress',
    })
    expect(result[2]).toEqual({
      content: 'Add documentation',
      activeForm: 'Adding documentation',
      status: 'pending',
    })
  })

  it('handles multiple TodoWrite calls across different messages', () => {
    const messages: AGUIMessage[] = [
      todoWriteMessage(
        [{ content: 'Task 1', activeForm: 'Doing Task 1', status: 'pending' }],
        'first'
      ),
      textMessage('Let me work on this'),
      todoWriteMessage(
        [
          { content: 'Task 1', activeForm: 'Doing Task 1', status: 'completed' },
          { content: 'Task 2', activeForm: 'Doing Task 2', status: 'pending' },
        ],
        'second'
      ),
    ]

    const result = parseTodosFromMessages(messages)
    expect(result).toHaveLength(2)
    expect(result[0].status).toBe('completed')
    expect(result[1].status).toBe('pending')
  })

  it('handles malformed JSON in tool input gracefully', () => {
    const messages: AGUIMessage[] = [toolUseMessage('TodoWrite', 'not valid json')]

    expect(parseTodosFromMessages(messages)).toEqual([])
  })

  it('handles missing or null tool input', () => {
    const messages: AGUIMessage[] = [toolUseMessage('TodoWrite', undefined)]

    expect(parseTodosFromMessages(messages)).toEqual([])
  })

  it('handles empty todos array in tool input', () => {
    const messages: AGUIMessage[] = [toolUseMessage('TodoWrite', JSON.stringify({ todos: [] }))]

    expect(parseTodosFromMessages(messages)).toEqual([])
  })

  it('normalizes status values with underscores', () => {
    const messages: AGUIMessage[] = [
      toolUseMessage(
        'TodoWrite',
        JSON.stringify({
          todos: [
            {
              content: 'Task with underscore status',
              activeForm: 'Working on task',
              status: 'in_progress',
            },
          ],
        })
      ),
    ]

    const result = parseTodosFromMessages(messages)
    expect(result[0].status).toBe('in_progress')
  })

  it('handles missing properties with defaults', () => {
    const messages: AGUIMessage[] = [
      toolUseMessage(
        'TodoWrite',
        JSON.stringify({
          todos: [{ status: 'pending' }, { content: 'Valid task', status: 'completed' }],
        })
      ),
    ]

    const result = parseTodosFromMessages(messages)
    expect(result).toHaveLength(2)
    expect(result[0]).toEqual({ content: '', activeForm: '', status: 'pending' })
    expect(result[1]).toEqual({ content: 'Valid task', activeForm: '', status: 'completed' })
  })

  it('finds TodoWrite across multiple content blocks on a single message', () => {
    // The reducer groups multiple tool-call blocks into one message; this exercises
    // the `flatMap` path in parseTodosFromMessages.
    const message: AGUIMessage = {
      id: 'multi',
      role: 'assistant',
      createdAt: Date.now(),
      content: [
        { kind: 'text', text: 'Let me update the todos', isStreaming: false },
        {
          kind: 'toolUse',
          toolCallId: 'tool-multi',
          toolName: 'TodoWrite',
          input: JSON.stringify({
            todos: [
              {
                content: 'Found in second content block',
                activeForm: 'Working on it',
                status: 'in_progress',
              },
            ],
          }),
          isStreaming: false,
        },
        { kind: 'text', text: 'Updated!', isStreaming: false },
      ],
    }

    const result = parseTodosFromMessages([message])
    expect(result).toHaveLength(1)
    expect(result[0].content).toBe('Found in second content block')
  })
})
