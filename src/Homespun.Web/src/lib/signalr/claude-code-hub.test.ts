/**
 * Tests for Claude Code hub event handlers and methods.
 */

import { describe, it, expect, vi, beforeEach } from 'vitest'
import type { HubConnection } from '@microsoft/signalr'
import {
  registerClaudeCodeHubEvents,
  createClaudeCodeHubMethods,
  type ClaudeCodeHubEvents,
} from './claude-code-hub'
import type { ClaudeSession } from '@/types/signalr'

function createMockConnection(): HubConnection & {
  _handlers: Map<string, (...args: unknown[]) => void>
  simulateEvent: (name: string, ...args: unknown[]) => void
} {
  const handlers = new Map<string, (...args: unknown[]) => void>()

  return {
    on: vi.fn((name: string, handler: (...args: unknown[]) => void) => {
      handlers.set(name, handler)
    }),
    off: vi.fn((name: string) => {
      handlers.delete(name)
    }),
    invoke: vi.fn().mockResolvedValue(undefined),
    _handlers: handlers,
    simulateEvent: (name: string, ...args: unknown[]) => {
      const handler = handlers.get(name)
      if (handler) {
        handler(...args)
      }
    },
  } as unknown as HubConnection & {
    _handlers: Map<string, (...args: unknown[]) => void>
    simulateEvent: (name: string, ...args: unknown[]) => void
  }
}

// traceInvoke always prepends a traceparent string as the first wire arg.
// Outside an active span it is an empty string (we intentionally keep the
// argument shape stable so the server-side filter always reads arg0).
const TRACEPARENT = expect.any(String)

describe('registerClaudeCodeHubEvents', () => {
  let mockConnection: ReturnType<typeof createMockConnection>

  beforeEach(() => {
    mockConnection = createMockConnection()
  })

  it('registers session lifecycle events', () => {
    const handlers: ClaudeCodeHubEvents = {
      onSessionStarted: vi.fn(),
      onSessionStopped: vi.fn(),
      onSessionState: vi.fn(),
      onSessionStatusChanged: vi.fn(),
    }

    registerClaudeCodeHubEvents(mockConnection, handlers)

    expect(mockConnection.on).toHaveBeenCalledWith('SessionStarted', expect.any(Function))
    expect(mockConnection.on).toHaveBeenCalledWith('SessionStopped', expect.any(Function))
    expect(mockConnection.on).toHaveBeenCalledWith('SessionState', expect.any(Function))
    expect(mockConnection.on).toHaveBeenCalledWith('SessionStatusChanged', expect.any(Function))
  })

  it('registers AG-UI events', () => {
    const handlers: ClaudeCodeHubEvents = {
      onAGUIRunStarted: vi.fn(),
      onAGUITextMessageContent: vi.fn(),
      onAGUIToolCallStart: vi.fn(),
    }

    registerClaudeCodeHubEvents(mockConnection, handlers)

    expect(mockConnection.on).toHaveBeenCalledWith('AGUI_RunStarted', expect.any(Function))
    expect(mockConnection.on).toHaveBeenCalledWith('AGUI_TextMessageContent', expect.any(Function))
    expect(mockConnection.on).toHaveBeenCalledWith('AGUI_ToolCallStart', expect.any(Function))
  })

  it('calls handlers when events are received', () => {
    const onSessionStarted = vi.fn()
    const handlers: ClaudeCodeHubEvents = { onSessionStarted }

    registerClaudeCodeHubEvents(mockConnection, handlers)

    const mockSession: ClaudeSession = {
      id: 'session-1',
      entityId: 'entity-1',
      projectId: 'project-1',
      workingDirectory: '/test',
      model: 'claude-3',
      mode: 'build',
      status: 'running',
      createdAt: new Date().toISOString(),
      lastActivityAt: new Date().toISOString(),
      messages: [],
      totalCostUsd: 0,
      totalDurationMs: 0,
      hasPendingPlanApproval: false,
      contextClearMarkers: [],
    }

    mockConnection.simulateEvent('SessionStarted', mockSession)

    expect(onSessionStarted).toHaveBeenCalledWith(mockSession)
  })

  it('returns cleanup function that removes handlers', () => {
    const handlers: ClaudeCodeHubEvents = {
      onSessionStarted: vi.fn(),
      onSessionStopped: vi.fn(),
    }

    const cleanup = registerClaudeCodeHubEvents(mockConnection, handlers)

    expect(mockConnection._handlers.size).toBe(2)

    cleanup()

    expect(mockConnection.off).toHaveBeenCalledWith('SessionStarted', expect.any(Function))
    expect(mockConnection.off).toHaveBeenCalledWith('SessionStopped', expect.any(Function))
  })

  it('only registers provided handlers', () => {
    const handlers: ClaudeCodeHubEvents = {
      onSessionStarted: vi.fn(),
      // Not providing onSessionStopped
    }

    registerClaudeCodeHubEvents(mockConnection, handlers)

    expect(mockConnection.on).toHaveBeenCalledWith('SessionStarted', expect.any(Function))
    expect(mockConnection.on).not.toHaveBeenCalledWith('SessionStopped', expect.any(Function))
  })
})

describe('createClaudeCodeHubMethods', () => {
  let mockConnection: ReturnType<typeof createMockConnection>

  beforeEach(() => {
    mockConnection = createMockConnection()
  })

  it('creates joinSession method', async () => {
    const methods = createClaudeCodeHubMethods(mockConnection)

    await methods.joinSession('session-1')

    expect(mockConnection.invoke).toHaveBeenCalledWith('JoinSession', TRACEPARENT, 'session-1')
  })

  it('creates leaveSession method', async () => {
    const methods = createClaudeCodeHubMethods(mockConnection)

    await methods.leaveSession('session-1')

    expect(mockConnection.invoke).toHaveBeenCalledWith('LeaveSession', TRACEPARENT, 'session-1')
  })

  it('creates sendMessage method with default mode', async () => {
    const methods = createClaudeCodeHubMethods(mockConnection)

    await methods.sendMessage('session-1', 'Hello')

    expect(mockConnection.invoke).toHaveBeenCalledWith(
      'SendMessage',
      TRACEPARENT,
      'session-1',
      'Hello',
      'build'
    )
  })

  it('creates sendMessage method with plan mode', async () => {
    const methods = createClaudeCodeHubMethods(mockConnection)

    await methods.sendMessage('session-1', 'Hello', 'plan')

    expect(mockConnection.invoke).toHaveBeenCalledWith(
      'SendMessage',
      TRACEPARENT,
      'session-1',
      'Hello',
      'plan'
    )
  })

  it('creates stopSession method', async () => {
    const methods = createClaudeCodeHubMethods(mockConnection)

    await methods.stopSession('session-1')

    expect(mockConnection.invoke).toHaveBeenCalledWith('StopSession', TRACEPARENT, 'session-1')
  })

  it('creates getAllSessions method', async () => {
    const mockSessions: ClaudeSession[] = []
    mockConnection.invoke = vi.fn().mockResolvedValue(mockSessions)

    const methods = createClaudeCodeHubMethods(mockConnection)
    const result = await methods.getAllSessions()

    expect(mockConnection.invoke).toHaveBeenCalledWith('GetAllSessions', TRACEPARENT)
    expect(result).toBe(mockSessions)
  })

  it('creates answerQuestion method', async () => {
    const methods = createClaudeCodeHubMethods(mockConnection)
    const answersJson = JSON.stringify({ q1: 'answer1' })

    await methods.answerQuestion('session-1', answersJson)

    expect(mockConnection.invoke).toHaveBeenCalledWith(
      'AnswerQuestion',
      TRACEPARENT,
      'session-1',
      answersJson
    )
  })

  it('creates executePlan method with default clearContext', async () => {
    const methods = createClaudeCodeHubMethods(mockConnection)

    await methods.executePlan('session-1')

    expect(mockConnection.invoke).toHaveBeenCalledWith(
      'ExecutePlan',
      TRACEPARENT,
      'session-1',
      true
    )
  })

  it('creates executePlan method with clearContext false', async () => {
    const methods = createClaudeCodeHubMethods(mockConnection)

    await methods.executePlan('session-1', false)

    expect(mockConnection.invoke).toHaveBeenCalledWith(
      'ExecutePlan',
      TRACEPARENT,
      'session-1',
      false
    )
  })
})
