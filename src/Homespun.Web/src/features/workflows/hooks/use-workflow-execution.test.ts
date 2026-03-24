import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, act } from '@testing-library/react'
import { createMockHubConnection } from '@/test/mocks/signalr'
import { useWorkflowExecution } from './use-workflow-execution'

vi.mock('@/lib/signalr/connection', () => ({
  createHubConnection: vi.fn(),
  startConnection: vi.fn(),
  stopConnection: vi.fn(),
  getConnectionStatus: vi.fn(),
}))

import { createHubConnection, startConnection, stopConnection } from '@/lib/signalr/connection'

describe('useWorkflowExecution', () => {
  let mockConnection: ReturnType<typeof createMockHubConnection>

  beforeEach(() => {
    vi.clearAllMocks()
    mockConnection = createMockHubConnection()
    vi.mocked(createHubConnection).mockReturnValue(mockConnection as never)
    vi.mocked(startConnection).mockResolvedValue(true)
  })

  it('creates a connection to the workflow hub on mount', () => {
    renderHook(() => useWorkflowExecution('exec-1'))

    expect(createHubConnection).toHaveBeenCalledWith(
      expect.objectContaining({ hubUrl: '/hubs/workflows' })
    )
    expect(startConnection).toHaveBeenCalled()
  })

  it('joins execution group after connection starts', () => {
    renderHook(() => useWorkflowExecution('exec-1'))

    // Simulate connection becoming ready
    const onStatusChange = vi.mocked(createHubConnection).mock.calls[0][0].onStatusChange
    act(() => {
      onStatusChange?.('connected')
    })

    expect(mockConnection.invoke).toHaveBeenCalledWith('JoinExecution', 'exec-1')
  })

  it('stops connection on unmount', () => {
    const { unmount } = renderHook(() => useWorkflowExecution('exec-1'))

    unmount()

    expect(stopConnection).toHaveBeenCalledWith(mockConnection)
  })

  it('updates step status on StepStarted event', () => {
    const { result } = renderHook(() => useWorkflowExecution('exec-1'))

    act(() => {
      mockConnection.simulateEvent('StepStarted', 'exec-1', 'step-1', 0)
    })

    expect(result.current.stepStatuses).toEqual(
      expect.objectContaining({
        'step-1': expect.objectContaining({
          status: 'running',
          stepIndex: 0,
        }),
      })
    )
  })

  it('updates step status on StepCompleted event', () => {
    const { result } = renderHook(() => useWorkflowExecution('exec-1'))

    act(() => {
      mockConnection.simulateEvent('StepStarted', 'exec-1', 'step-1', 0)
    })

    const output = { result: 'success' }
    act(() => {
      mockConnection.simulateEvent('StepCompleted', 'exec-1', 'step-1', 'completed', output)
    })

    expect(result.current.stepStatuses).toEqual(
      expect.objectContaining({
        'step-1': expect.objectContaining({
          status: 'completed',
          output,
        }),
      })
    )
  })

  it('updates step status on StepFailed event', () => {
    const { result } = renderHook(() => useWorkflowExecution('exec-1'))

    act(() => {
      mockConnection.simulateEvent('StepStarted', 'exec-1', 'step-1', 0)
    })

    act(() => {
      mockConnection.simulateEvent('StepFailed', 'exec-1', 'step-1', 'Something went wrong')
    })

    expect(result.current.stepStatuses).toEqual(
      expect.objectContaining({
        'step-1': expect.objectContaining({
          status: 'failed',
          error: 'Something went wrong',
        }),
      })
    )
  })

  it('updates retry count on StepRetrying event', () => {
    const { result } = renderHook(() => useWorkflowExecution('exec-1'))

    act(() => {
      mockConnection.simulateEvent('StepStarted', 'exec-1', 'step-1', 0)
    })

    act(() => {
      mockConnection.simulateEvent('StepRetrying', 'exec-1', 'step-1', 2, 5)
    })

    expect(result.current.stepStatuses).toEqual(
      expect.objectContaining({
        'step-1': expect.objectContaining({
          status: 'running',
          retryCount: 2,
          maxRetries: 5,
        }),
      })
    )
  })

  it('sets workflow status on WorkflowCompleted event', () => {
    const { result } = renderHook(() => useWorkflowExecution('exec-1'))

    act(() => {
      mockConnection.simulateEvent('WorkflowCompleted', 'exec-1', 'completed')
    })

    expect(result.current.workflowStatus).toBe('completed')
  })

  it('sets workflow status and error on WorkflowFailed event', () => {
    const { result } = renderHook(() => useWorkflowExecution('exec-1'))

    act(() => {
      mockConnection.simulateEvent('WorkflowFailed', 'exec-1', 'Pipeline error')
    })

    expect(result.current.workflowStatus).toBe('failed')
    expect(result.current.workflowError).toBe('Pipeline error')
  })

  it('sets gate pending state on GatePending event', () => {
    const { result } = renderHook(() => useWorkflowExecution('exec-1'))

    act(() => {
      mockConnection.simulateEvent('GatePending', 'exec-1', 'step-gate', 'Deploy Approval')
    })

    expect(result.current.pendingGate).toEqual({
      stepId: 'step-gate',
      gateName: 'Deploy Approval',
    })
  })

  it('clears pending gate after StepCompleted for the gate step', () => {
    const { result } = renderHook(() => useWorkflowExecution('exec-1'))

    act(() => {
      mockConnection.simulateEvent('GatePending', 'exec-1', 'step-gate', 'Deploy Approval')
    })

    expect(result.current.pendingGate).not.toBeNull()

    act(() => {
      mockConnection.simulateEvent('StepCompleted', 'exec-1', 'step-gate', 'completed', null)
    })

    expect(result.current.pendingGate).toBeNull()
  })

  it('returns connection status', () => {
    const { result } = renderHook(() => useWorkflowExecution('exec-1'))

    expect(result.current.connectionStatus).toBe('disconnected')
  })

  it('re-joins execution group on reconnect', () => {
    renderHook(() => useWorkflowExecution('exec-1'))

    // Clear previous invocations from connection setup
    mockConnection.invoke.mockClear()

    const onReconnected = vi.mocked(createHubConnection).mock.calls[0][0].onReconnected
    act(() => {
      onReconnected?.()
    })

    expect(mockConnection.invoke).toHaveBeenCalledWith('JoinExecution', 'exec-1')
  })
})
