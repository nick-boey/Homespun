import { describe, it, expect } from 'vitest'
import type { ClaudeMessage, ClaudeMessageContent } from '@/types/tool-execution'
import { groupToolExecutions } from './tool-execution-grouper'

describe('groupToolExecutions', () => {
  it('returns empty array for empty messages', () => {
    const result = groupToolExecutions([])
    expect(result).toEqual([])
  })

  it('returns regular message for non-tool content', () => {
    const messages: ClaudeMessage[] = [
      {
        id: 'msg1',
        role: 1, // Assistant
        sessionId: "session1",
        content: [{ contentType: 'text', text: 'Hello world' }],
        createdAt: '2024-01-01T00:00:00Z',
      },
    ]

    const result = groupToolExecutions(messages)
    expect(result).toHaveLength(1)
    expect(result[0]).toEqual({
      type: 'message',
      message: messages[0],
    })
  })

  it('groups single tool use and result', () => {
    const toolUse: ClaudeMessageContent = {
      contentType: 'tool_use',
      toolUseId: 'tool1',
      name: 'read_file',
      input: { path: 'test.ts' },
    }

    const toolResult: ClaudeMessageContent = {
      contentType: 'tool_result',
      toolUseId: 'tool1',
      content: 'file contents',
    }

    const messages: ClaudeMessage[] = [
      {
        id: 'msg1',
        role: 1, // Assistant
        sessionId: "session1",
        content: [toolUse],
        createdAt: '2024-01-01T00:00:00Z',
      },
      {
        id: 'msg2',
        role: 0, // User (tool results come from user)
        sessionId: "session1",
        content: [toolResult],
        createdAt: '2024-01-01T00:00:01Z',
      },
    ]

    const result = groupToolExecutions(messages)
    expect(result).toHaveLength(1)
    expect(result[0]).toEqual({
      type: 'toolGroup',
      group: {
        id: expect.any(String),
        executions: [
          {
            toolUse,
            toolResult,
            isRunning: false,
          },
        ],
        timestamp: '2024-01-01T00:00:00Z',
        originalMessageIds: ['msg1', 'msg2'],
      },
    })
  })

  it('groups multiple consecutive tool uses', () => {
    const toolUse1: ClaudeMessageContent = {
      contentType: 'tool_use',
      toolUseId: 'tool1',
      name: 'read_file',
      input: { path: 'test1.ts' },
    }

    const toolUse2: ClaudeMessageContent = {
      contentType: 'tool_use',
      toolUseId: 'tool2',
      name: 'write_file',
      input: { path: 'test2.ts' },
    }

    const messages: ClaudeMessage[] = [
      {
        id: 'msg1',
        role: 1, // Assistant
        sessionId: "session1",
        content: [toolUse1, toolUse2],
        createdAt: '2024-01-01T00:00:00Z',
      },
      {
        id: 'msg2',
        role: 0, // User
        sessionId: "session1",
        content: [
          { contentType: 'tool_result', toolUseId: 'tool1', content: 'result1' },
          { contentType: 'tool_result', toolUseId: 'tool2', content: 'result2' },
        ],
        createdAt: '2024-01-01T00:00:01Z',
      },
    ]

    const result = groupToolExecutions(messages)
    expect(result).toHaveLength(1)

    const group = result[0]
    expect(group.type).toBe('toolGroup')
    if (group.type === 'toolGroup') {
      expect(group.group.executions).toHaveLength(2)
      expect(group.group.executions[0].toolUse.toolUseId).toBe('tool1')
      expect(group.group.executions[1].toolUse.toolUseId).toBe('tool2')
    }
  })

  it('handles tool use without result (running state)', () => {
    const toolUse: ClaudeMessageContent = {
      contentType: 'tool_use',
      toolUseId: 'tool1',
      name: 'long_running_task',
      input: {},
    }

    const messages: ClaudeMessage[] = [
      {
        id: 'msg1',
        role: 1, // Assistant
        sessionId: "session1",
        content: [toolUse],
        createdAt: '2024-01-01T00:00:00Z',
      },
    ]

    const result = groupToolExecutions(messages)
    expect(result).toHaveLength(1)

    const group = result[0]
    expect(group.type).toBe('toolGroup')
    if (group.type === 'toolGroup') {
      expect(group.group.executions).toHaveLength(1)
      expect(group.group.executions[0]).toEqual({
        toolUse,
        toolResult: undefined,
        isRunning: true,
      })
    }
  })

  it('separates tool groups with text messages between them', () => {
    const messages: ClaudeMessage[] = [
      {
        id: 'msg1',
        role: 1, // Assistant
        sessionId: "session1",
        content: [{ contentType: 'tool_use', toolUseId: 'tool1', name: 'read_file', input: {} }],
        createdAt: '2024-01-01T00:00:00Z',
      },
      {
        id: 'msg2',
        role: 0, // User
        sessionId: "session1",
        content: [{ contentType: 'tool_result', toolUseId: 'tool1', content: 'result1' }],
        createdAt: '2024-01-01T00:00:01Z',
      },
      {
        id: 'msg3',
        role: 1, // Assistant
        sessionId: "session1",
        content: [{ contentType: 'text', text: 'Here are the results' }],
        createdAt: '2024-01-01T00:00:02Z',
      },
      {
        id: 'msg4',
        role: 1, // Assistant
        sessionId: "session1",
        content: [{ contentType: 'tool_use', toolUseId: 'tool2', name: 'write_file', input: {} }],
        createdAt: '2024-01-01T00:00:03Z',
      },
    ]

    const result = groupToolExecutions(messages)
    expect(result).toHaveLength(3)
    expect(result[0].type).toBe('toolGroup')
    expect(result[1].type).toBe('message')
    expect(result[2].type).toBe('toolGroup')
  })

  it('handles mixed content in assistant messages', () => {
    const messages: ClaudeMessage[] = [
      {
        id: 'msg1',
        role: 1, // Assistant
        sessionId: "session1",
        content: [
          { contentType: 'text', text: 'Let me read the file' },
          { contentType: 'tool_use', toolUseId: 'tool1', name: 'read_file', input: {} },
        ],
        createdAt: '2024-01-01T00:00:00Z',
      },
      {
        id: 'msg2',
        role: 0, // User
        sessionId: "session1",
        content: [{ contentType: 'tool_result', toolUseId: 'tool1', content: 'result' }],
        createdAt: '2024-01-01T00:00:01Z',
      },
    ]

    const result = groupToolExecutions(messages)
    expect(result).toHaveLength(2)
    expect(result[0].type).toBe('message')
    expect(result[1].type).toBe('toolGroup')
  })

  it('handles tool result stored in tool use content block', () => {
    const toolUse: ClaudeMessageContent = {
      contentType: 'tool_use',
      toolUseId: 'tool1',
      name: 'read_file',
      input: { path: 'test.ts' },
      toolResult: 'file contents from toolUse block',
    }

    const messages: ClaudeMessage[] = [
      {
        id: 'msg1',
        role: 1, // Assistant
        sessionId: "session1",
        content: [toolUse],
        createdAt: '2024-01-01T00:00:00Z',
      },
    ]

    const result = groupToolExecutions(messages)
    expect(result).toHaveLength(1)

    const group = result[0]
    expect(group.type).toBe('toolGroup')
    if (group.type === 'toolGroup') {
      expect(group.group.executions[0].toolResult).toEqual({
        contentType: 'tool_result',
        toolUseId: 'tool1',
        content: 'file contents from toolUse block',
      })
      expect(group.group.executions[0].isRunning).toBe(false)
    }
  })

  it('handles user messages between assistant tool uses', () => {
    const messages: ClaudeMessage[] = [
      {
        id: 'msg1',
        role: 1, // Assistant
        sessionId: "session1",
        content: [{ contentType: 'tool_use', toolUseId: 'tool1', name: 'read_file', input: {} }],
        createdAt: '2024-01-01T00:00:00Z',
      },
      {
        id: 'msg2',
        role: 0, // User
        sessionId: "session1",
        content: [{ contentType: 'text', text: 'User message' }],
        createdAt: '2024-01-01T00:00:01Z',
      },
    ]

    const result = groupToolExecutions(messages)
    expect(result).toHaveLength(2)
    expect(result[0].type).toBe('toolGroup')
    expect(result[1].type).toBe('message')
  })

  it('preserves error state in tool results', () => {
    const toolUse: ClaudeMessageContent = {
      contentType: 'tool_use',
      toolUseId: 'tool1',
      name: 'read_file',
      input: { path: 'nonexistent.ts' },
    }

    const toolResult: ClaudeMessageContent = {
      contentType: 'tool_result',
      toolUseId: 'tool1',
      content: 'Error: File not found',
      isError: true,
    }

    const messages: ClaudeMessage[] = [
      {
        id: 'msg1',
        role: 1, // Assistant
        sessionId: "session1",
        content: [toolUse],
        createdAt: '2024-01-01T00:00:00Z',
      },
      {
        id: 'msg2',
        role: 0, // User
        sessionId: "session1",
        content: [toolResult],
        createdAt: '2024-01-01T00:00:01Z',
      },
    ]

    const result = groupToolExecutions(messages)
    expect(result).toHaveLength(1)

    const group = result[0]
    expect(group.type).toBe('toolGroup')
    if (group.type === 'toolGroup') {
      expect(group.group.executions[0].toolResult?.isError).toBe(true)
    }
  })
})
