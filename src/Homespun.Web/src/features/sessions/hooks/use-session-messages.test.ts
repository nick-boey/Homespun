import { describe, it, expect, vi, beforeEach, type Mock } from 'vitest'
import { renderHook, act, waitFor } from '@testing-library/react'
import { useSessionMessages } from './use-session-messages'
import { useClaudeCodeHub } from '@/providers/signalr-provider'
import type { ClaudeMessage } from '@/types/signalr'

// Mock the SignalR provider
vi.mock('@/providers/signalr-provider', () => ({
  useClaudeCodeHub: vi.fn(),
}))

const createMockMessage = (id: string, text: string): ClaudeMessage => ({
  id,
  sessionId: 'session-123',
  role: 'user',
  content: [{ type: 'text', text, isStreaming: false, index: 0 }],
  createdAt: '2024-01-01T00:00:00Z',
  isStreaming: false,
})

describe('useSessionMessages', () => {
  const mockConnection = {
    on: vi.fn(),
    off: vi.fn(),
  }

  beforeEach(() => {
    vi.clearAllMocks()
    ;(useClaudeCodeHub as Mock).mockReturnValue({
      connection: mockConnection,
    })
  })

  it('starts with empty messages when loading', () => {
    const initialMessages = [createMockMessage('1', 'Hello')]

    const { result } = renderHook(() =>
      useSessionMessages({
        sessionId: 'session-123',
        initialMessages,
        isLoading: true,
      })
    )

    expect(result.current.messages).toEqual([])
  })

  it('does not update messages while loading', () => {
    const initialMessages = [createMockMessage('1', 'Hello')]

    const { result, rerender } = renderHook(
      ({ isLoading, initialMessages }) =>
        useSessionMessages({
          sessionId: 'session-123',
          initialMessages,
          isLoading,
        }),
      {
        initialProps: { isLoading: true, initialMessages },
      }
    )

    expect(result.current.messages).toEqual([])

    // Simulate new initial messages while still loading
    const newMessages = [createMockMessage('1', 'Hello'), createMockMessage('2', 'World')]
    rerender({ isLoading: true, initialMessages: newMessages })

    expect(result.current.messages).toEqual([])
  })

  it('updates messages when loading completes with data', () => {
    const initialMessages = [createMockMessage('1', 'Hello')]

    const { result, rerender } = renderHook(
      ({ isLoading, initialMessages }) =>
        useSessionMessages({
          sessionId: 'session-123',
          initialMessages,
          isLoading,
        }),
      {
        initialProps: { isLoading: true, initialMessages: [] as ClaudeMessage[] },
      }
    )

    expect(result.current.messages).toEqual([])

    // Simulate loading completing with messages
    rerender({ isLoading: false, initialMessages })

    expect(result.current.messages).toEqual(initialMessages)
  })

  it('sets empty messages when loading completes with no messages', () => {
    const { result, rerender } = renderHook(
      ({ isLoading, initialMessages }) =>
        useSessionMessages({
          sessionId: 'session-123',
          initialMessages,
          isLoading,
        }),
      {
        initialProps: { isLoading: true, initialMessages: [] as ClaudeMessage[] },
      }
    )

    expect(result.current.messages).toEqual([])

    rerender({ isLoading: false, initialMessages: [] })

    expect(result.current.messages).toEqual([])
  })

  it('resets state when sessionId changes', () => {
    const initialMessages = [createMockMessage('1', 'Hello')]

    const { result, rerender } = renderHook(
      ({ sessionId, isLoading, initialMessages }) =>
        useSessionMessages({
          sessionId,
          initialMessages,
          isLoading,
        }),
      {
        initialProps: { sessionId: 'session-123', isLoading: false, initialMessages },
      }
    )

    expect(result.current.messages).toEqual(initialMessages)

    // Change sessionId - should reset messages
    rerender({ sessionId: 'session-456', isLoading: true, initialMessages: [] })

    expect(result.current.messages).toEqual([])
  })

  it('loads new messages after sessionId changes and loading completes', () => {
    const initialMessages = [createMockMessage('1', 'Hello')]
    const newMessages = [createMockMessage('2', 'New session message')]

    const { result, rerender } = renderHook(
      ({ sessionId, isLoading, initialMessages }) =>
        useSessionMessages({
          sessionId,
          initialMessages,
          isLoading,
        }),
      {
        initialProps: { sessionId: 'session-123', isLoading: false, initialMessages },
      }
    )

    expect(result.current.messages).toEqual(initialMessages)

    // Change sessionId - starts loading
    rerender({ sessionId: 'session-456', isLoading: true, initialMessages: [] })
    expect(result.current.messages).toEqual([])

    // Loading completes with new messages
    rerender({ sessionId: 'session-456', isLoading: false, initialMessages: newMessages })
    expect(result.current.messages).toEqual(newMessages)
  })

  it('continues receiving real-time updates after initial load', async () => {
    const initialMessages = [createMockMessage('1', 'Hello')]

    const { result, rerender } = renderHook(
      ({ isLoading, initialMessages }) =>
        useSessionMessages({
          sessionId: 'session-123',
          initialMessages,
          isLoading,
        }),
      {
        initialProps: { isLoading: true, initialMessages: [] as ClaudeMessage[] },
      }
    )

    // Complete loading
    rerender({ isLoading: false, initialMessages })
    expect(result.current.messages).toEqual(initialMessages)

    // Simulate a real-time message via SignalR
    const textMessageStartCall = mockConnection.on.mock.calls.find(
      (call) => call[0] === 'AGUI_TextMessageStart'
    )
    expect(textMessageStartCall).toBeDefined()

    const handler = textMessageStartCall![1]
    act(() => {
      handler({
        messageId: 'new-message',
        role: 'assistant',
        timestamp: '2024-01-01T01:00:00Z',
      })
    })

    await waitFor(() => {
      expect(result.current.messages).toHaveLength(2)
      expect(result.current.messages[1].id).toBe('new-message')
    })
  })

  it('adds user message locally', () => {
    const initialMessages = [createMockMessage('1', 'Hello')]

    const { result } = renderHook(
      ({ isLoading, initialMessages }) =>
        useSessionMessages({
          sessionId: 'session-123',
          initialMessages,
          isLoading,
        }),
      {
        initialProps: { isLoading: false, initialMessages },
      }
    )

    expect(result.current.messages).toEqual(initialMessages)

    act(() => {
      result.current.addUserMessage('New user message')
    })

    expect(result.current.messages).toHaveLength(2)
    expect(result.current.messages[1].role).toBe('user')
    expect(result.current.messages[1].content[0].text).toBe('New user message')
  })

  it('defaults isLoading to false for backwards compatibility', () => {
    const initialMessages = [createMockMessage('1', 'Hello')]

    const { result } = renderHook(() =>
      useSessionMessages({
        sessionId: 'session-123',
        initialMessages,
      })
    )

    // Without isLoading, it should immediately use initialMessages
    expect(result.current.messages).toEqual(initialMessages)
  })

  it('cleans up event handlers on unmount', () => {
    const { unmount } = renderHook(() =>
      useSessionMessages({
        sessionId: 'session-123',
        initialMessages: [],
        isLoading: false,
      })
    )

    expect(mockConnection.on).toHaveBeenCalled()

    unmount()

    expect(mockConnection.off).toHaveBeenCalled()
  })
})
