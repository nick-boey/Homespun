import { describe, it, expect, vi, beforeEach, type Mock } from 'vitest'
import { renderHook, waitFor, act } from '@testing-library/react'
import { useApprovePlan } from './use-approve-plan'
import { useClaudeCodeHub } from '@/providers/signalr-provider'

// Mock the SignalR provider
vi.mock('@/providers/signalr-provider', () => ({
  useClaudeCodeHub: vi.fn(),
}))

describe('useApprovePlan', () => {
  const mockApprovePlan = vi.fn()

  beforeEach(() => {
    vi.clearAllMocks()
    mockApprovePlan.mockResolvedValue(undefined)
    ;(useClaudeCodeHub as Mock).mockReturnValue({
      methods: {
        approvePlan: mockApprovePlan,
      },
      isConnected: true,
    })
  })

  it('returns approve functions and initial state', () => {
    const { result } = renderHook(() => useApprovePlan('session-123'))

    expect(result.current.approveClearContext).toBeDefined()
    expect(result.current.approveKeepContext).toBeDefined()
    expect(result.current.reject).toBeDefined()
    expect(result.current.isLoading).toBe(false)
    expect(result.current.error).toBeUndefined()
  })

  describe('approveClearContext', () => {
    it('calls approvePlan with approved=true, keepContext=false', async () => {
      const { result } = renderHook(() => useApprovePlan('session-123'))

      await act(async () => {
        await result.current.approveClearContext()
      })

      expect(mockApprovePlan).toHaveBeenCalledWith('session-123', true, false, null)
    })

    it('sets loading state during approval', async () => {
      let resolvePromise: () => void
      mockApprovePlan.mockImplementation(
        () =>
          new Promise<void>((resolve) => {
            resolvePromise = resolve
          })
      )

      const { result } = renderHook(() => useApprovePlan('session-123'))

      act(() => {
        result.current.approveClearContext()
      })

      expect(result.current.isLoading).toBe(true)

      await act(async () => {
        resolvePromise!()
      })

      await waitFor(() => {
        expect(result.current.isLoading).toBe(false)
      })
    })

    it('handles error during approval', async () => {
      mockApprovePlan.mockRejectedValueOnce(new Error('Connection failed'))

      const { result } = renderHook(() => useApprovePlan('session-123'))

      await act(async () => {
        await result.current.approveClearContext()
      })

      expect(result.current.error).toBe('Connection failed')
      expect(result.current.isLoading).toBe(false)
    })
  })

  describe('approveKeepContext', () => {
    it('calls approvePlan with approved=true, keepContext=true', async () => {
      const { result } = renderHook(() => useApprovePlan('session-123'))

      await act(async () => {
        await result.current.approveKeepContext()
      })

      expect(mockApprovePlan).toHaveBeenCalledWith('session-123', true, true, null)
    })
  })

  describe('reject', () => {
    it('calls approvePlan with approved=false and feedback', async () => {
      const { result } = renderHook(() => useApprovePlan('session-123'))

      await act(async () => {
        await result.current.reject('Please add more detail to step 3')
      })

      expect(mockApprovePlan).toHaveBeenCalledWith(
        'session-123',
        false,
        false,
        'Please add more detail to step 3'
      )
    })

    it('calls approvePlan with null feedback when none provided', async () => {
      const { result } = renderHook(() => useApprovePlan('session-123'))

      await act(async () => {
        await result.current.reject()
      })

      expect(mockApprovePlan).toHaveBeenCalledWith('session-123', false, false, null)
    })
  })

  it('does not call approvePlan when not connected', async () => {
    ;(useClaudeCodeHub as Mock).mockReturnValue({
      methods: null,
      isConnected: false,
    })

    const { result } = renderHook(() => useApprovePlan('session-123'))

    await act(async () => {
      await result.current.approveClearContext()
    })

    expect(mockApprovePlan).not.toHaveBeenCalled()
  })

  it('clears error state on successful subsequent call', async () => {
    mockApprovePlan.mockRejectedValueOnce(new Error('First error'))
    mockApprovePlan.mockResolvedValueOnce(undefined)

    const { result } = renderHook(() => useApprovePlan('session-123'))

    // First call fails
    await act(async () => {
      await result.current.approveClearContext()
    })

    expect(result.current.error).toBe('First error')

    // Second call succeeds
    await act(async () => {
      await result.current.approveClearContext()
    })

    expect(result.current.error).toBeUndefined()
  })
})
