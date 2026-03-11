import { describe, it, expect } from 'vitest'
import { parseTodosFromMessages } from './todo-parser'
import type { ClaudeMessage } from '@/types/signalr'

// Helper to create a message with TodoWrite tool use
function createTodoWriteMessage(
  todos: Array<{
    content: string
    activeForm: string
    status: 'pending' | 'in_progress' | 'completed'
  }>
): ClaudeMessage {
  return {
    id: 'msg-1',
    sessionId: 'session-1',
    role: 'Assistant',
    content: [
      {
        type: 'ToolUse',
        toolName: 'TodoWrite',
        toolInput: JSON.stringify({ todos }),
        index: 0,
        isStreaming: false,
      },
    ],
    createdAt: new Date().toISOString(),
    isStreaming: false,
  }
}

describe('parseTodosFromMessages', () => {
  it('returns empty array when no messages provided', () => {
    const result = parseTodosFromMessages([])
    expect(result).toEqual([])
  })

  it('returns empty array when no TodoWrite tool calls exist', () => {
    const messages: ClaudeMessage[] = [
      {
        id: 'msg-1',
        sessionId: 'session-1',
        role: 'Assistant',
        content: [{ type: 'Text', text: 'Hello there', index: 0, isStreaming: false }],
        createdAt: new Date().toISOString(),
        isStreaming: false,
      },
      {
        id: 'msg-2',
        sessionId: 'session-1',
        role: 'Assistant',
        content: [
          { type: 'ToolUse', toolName: 'Bash', toolInput: 'ls', index: 0, isStreaming: false },
        ],
        createdAt: new Date().toISOString(),
        isStreaming: false,
      },
    ]

    const result = parseTodosFromMessages(messages)
    expect(result).toEqual([])
  })

  it('parses todos from the last TodoWrite tool call', () => {
    const messages: ClaudeMessage[] = [
      createTodoWriteMessage([
        {
          content: 'Write tests',
          activeForm: 'Writing tests',
          status: 'completed',
        },
        {
          content: 'Implement feature',
          activeForm: 'Implementing feature',
          status: 'pending',
        },
      ]),
      createTodoWriteMessage([
        {
          content: 'Write tests',
          activeForm: 'Writing tests',
          status: 'completed',
        },
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
      ]),
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
    const messages: ClaudeMessage[] = [
      createTodoWriteMessage([
        { content: 'Task 1', activeForm: 'Doing Task 1', status: 'pending' },
      ]),
      {
        id: 'msg-2',
        sessionId: 'session-1',
        role: 'Assistant',
        content: [{ type: 'Text', text: 'Let me work on this', index: 0, isStreaming: false }],
        createdAt: new Date().toISOString(),
        isStreaming: false,
      },
      createTodoWriteMessage([
        { content: 'Task 1', activeForm: 'Doing Task 1', status: 'completed' },
        { content: 'Task 2', activeForm: 'Doing Task 2', status: 'pending' },
      ]),
    ]

    const result = parseTodosFromMessages(messages)
    expect(result).toHaveLength(2)
    expect(result[0].status).toBe('completed')
    expect(result[1].status).toBe('pending')
  })

  it('handles malformed JSON in tool input gracefully', () => {
    const messages: ClaudeMessage[] = [
      {
        id: 'msg-1',
        sessionId: 'session-1',
        role: 'Assistant',
        content: [
          {
            type: 'ToolUse',
            toolName: 'TodoWrite',
            toolInput: 'not valid json',
            index: 0,
            isStreaming: false,
          },
        ],
        createdAt: new Date().toISOString(),
        isStreaming: false,
      },
    ]

    const result = parseTodosFromMessages(messages)
    expect(result).toEqual([])
  })

  it('handles missing or null tool input', () => {
    const messages: ClaudeMessage[] = [
      {
        id: 'msg-1',
        sessionId: 'session-1',
        role: 'Assistant',
        content: [
          {
            type: 'ToolUse',
            toolName: 'TodoWrite',
            toolInput: undefined,
            index: 0,
            isStreaming: false,
          },
        ],
        createdAt: new Date().toISOString(),
        isStreaming: false,
      },
    ]

    const result = parseTodosFromMessages(messages)
    expect(result).toEqual([])
  })

  it('handles empty todos array in tool input', () => {
    const messages: ClaudeMessage[] = [
      {
        id: 'msg-1',
        sessionId: 'session-1',
        role: 'Assistant',
        content: [
          {
            type: 'ToolUse',
            toolName: 'TodoWrite',
            toolInput: JSON.stringify({ todos: [] }),
            index: 0,
            isStreaming: false,
          },
        ],
        createdAt: new Date().toISOString(),
        isStreaming: false,
      },
    ]

    const result = parseTodosFromMessages(messages)
    expect(result).toEqual([])
  })

  it('normalizes status values with underscores', () => {
    const messages: ClaudeMessage[] = [
      {
        id: 'msg-1',
        sessionId: 'session-1',
        role: 'Assistant',
        content: [
          {
            type: 'ToolUse',
            toolName: 'TodoWrite',
            toolInput: JSON.stringify({
              todos: [
                {
                  content: 'Task with underscore status',
                  activeForm: 'Working on task',
                  status: 'in_progress', // With underscore
                },
              ],
            }),
            index: 0,
            isStreaming: false,
          },
        ],
        createdAt: new Date().toISOString(),
        isStreaming: false,
      },
    ]

    const result = parseTodosFromMessages(messages)
    expect(result[0].status).toBe('in_progress')
  })

  it('handles missing properties with defaults', () => {
    const messages: ClaudeMessage[] = [
      {
        id: 'msg-1',
        sessionId: 'session-1',
        role: 'Assistant',
        content: [
          {
            type: 'ToolUse',
            toolName: 'TodoWrite',
            toolInput: JSON.stringify({
              todos: [
                {
                  // Missing content and activeForm
                  status: 'pending',
                },
                {
                  content: 'Valid task',
                  // Missing activeForm
                  status: 'completed',
                },
              ],
            }),
            index: 0,
            isStreaming: false,
          },
        ],
        createdAt: new Date().toISOString(),
        isStreaming: false,
      },
    ]

    const result = parseTodosFromMessages(messages)
    expect(result).toHaveLength(2)
    expect(result[0]).toEqual({
      content: '',
      activeForm: '',
      status: 'pending',
    })
    expect(result[1]).toEqual({
      content: 'Valid task',
      activeForm: '',
      status: 'completed',
    })
  })

  it('finds TodoWrite across multiple content blocks', () => {
    const messages: ClaudeMessage[] = [
      {
        id: 'msg-1',
        sessionId: 'session-1',
        role: 'Assistant',
        content: [
          { type: 'Text', text: 'Let me update the todos', index: 0, isStreaming: false },
          {
            type: 'ToolUse',
            toolName: 'TodoWrite',
            toolInput: JSON.stringify({
              todos: [
                {
                  content: 'Found in second content block',
                  activeForm: 'Working on it',
                  status: 'in_progress',
                },
              ],
            }),
            index: 1,
            isStreaming: false,
          },
          { type: 'Text', text: 'Updated!', index: 2, isStreaming: false },
        ],
        createdAt: new Date().toISOString(),
        isStreaming: false,
      },
    ]

    const result = parseTodosFromMessages(messages)
    expect(result).toHaveLength(1)
    expect(result[0].content).toBe('Found in second content block')
  })
})
