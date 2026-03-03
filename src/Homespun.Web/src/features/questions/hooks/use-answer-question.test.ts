import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, act, waitFor } from '@testing-library/react'
import { useAnswerQuestion } from './use-answer-question'

// Mock the SignalR provider
const mockAnswerQuestion = vi.fn()
const mockMethods = {
  answerQuestion: mockAnswerQuestion,
}

vi.mock('@/providers/signalr-provider', () => ({
  useClaudeCodeHub: () => ({
    methods: mockMethods,
    isConnected: true,
  }),
}))

describe('useAnswerQuestion', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockAnswerQuestion.mockResolvedValue(undefined)
  })

  it('returns initial state', () => {
    const { result } = renderHook(() => useAnswerQuestion({ sessionId: 'session-123' }))

    expect(result.current.isSubmitting).toBe(false)
    expect(result.current.error).toBeUndefined()
    expect(typeof result.current.answerQuestion).toBe('function')
    expect(typeof result.current.reset).toBe('function')
  })

  it('calls SignalR method with correct parameters', async () => {
    const { result } = renderHook(() => useAnswerQuestion({ sessionId: 'session-123' }))

    const answers = { 'Question 1': 'Answer 1' }

    await act(async () => {
      await result.current.answerQuestion(answers)
    })

    expect(mockAnswerQuestion).toHaveBeenCalledWith('session-123', JSON.stringify(answers))
  })

  it('sets isSubmitting to true while submitting', async () => {
    let resolvePromise: () => void
    const controlledPromise = new Promise<void>((resolve) => {
      resolvePromise = resolve
    })

    mockAnswerQuestion.mockImplementation(() => controlledPromise)

    const { result } = renderHook(() => useAnswerQuestion({ sessionId: 'session-123' }))

    // Start the async call without awaiting
    let answerPromise: Promise<void>
    act(() => {
      answerPromise = result.current.answerQuestion({ question: 'answer' })
    })

    // Wait for state to update to submitting
    await waitFor(() => {
      expect(result.current.isSubmitting).toBe(true)
    })

    // Resolve the promise
    act(() => {
      resolvePromise!()
    })

    // Wait for it to complete
    await act(async () => {
      await answerPromise!
    })

    expect(result.current.isSubmitting).toBe(false)
  })

  it('calls onSuccess callback on successful submission', async () => {
    const onSuccess = vi.fn()

    const { result } = renderHook(() =>
      useAnswerQuestion({
        sessionId: 'session-123',
        onSuccess,
      })
    )

    await act(async () => {
      await result.current.answerQuestion({ question: 'answer' })
    })

    expect(onSuccess).toHaveBeenCalled()
  })

  it('sets error and calls onError on failure', async () => {
    const error = new Error('Network error')
    mockAnswerQuestion.mockRejectedValue(error)

    const onError = vi.fn()

    const { result } = renderHook(() =>
      useAnswerQuestion({
        sessionId: 'session-123',
        onError,
      })
    )

    await act(async () => {
      await result.current.answerQuestion({ question: 'answer' })
    })

    expect(result.current.error).toBe('Network error')
    expect(onError).toHaveBeenCalledWith(error)
  })

  it('reset clears error state', async () => {
    const error = new Error('Network error')
    mockAnswerQuestion.mockRejectedValue(error)

    const { result } = renderHook(() => useAnswerQuestion({ sessionId: 'session-123' }))

    await act(async () => {
      await result.current.answerQuestion({ question: 'answer' })
    })

    expect(result.current.error).toBe('Network error')

    act(() => {
      result.current.reset()
    })

    expect(result.current.error).toBeUndefined()
    expect(result.current.isSubmitting).toBe(false)
  })
})
