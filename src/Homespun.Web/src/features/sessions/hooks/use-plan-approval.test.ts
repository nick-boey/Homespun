import { describe, it, expect, vi, beforeEach, type Mock } from 'vitest'
import { renderHook, act } from '@testing-library/react'
import { usePlanApproval } from './use-plan-approval'
import { useClaudeCodeHub } from '@/providers/signalr-provider'
import type { ClaudeSession, CustomEvent, ClaudeSessionStatus } from '@/types/signalr'

// Mock the SignalR provider
vi.mock('@/providers/signalr-provider', () => ({
  useClaudeCodeHub: vi.fn(),
}))

const mockSession: ClaudeSession = {
  id: 'session-123',
  entityId: 'entity-123',
  projectId: 'project-456',
  workingDirectory: '/path/to/project',
  model: 'opus',
  mode: 'Build',
  status: 'WaitingForPlanExecution',
  createdAt: '2024-01-01T00:00:00Z',
  lastActivityAt: '2024-01-01T01:00:00Z',
  messages: [],
  totalCostUsd: 0.05,
  totalDurationMs: 3600000,
  hasPendingPlanApproval: true,
  planContent: '# Implementation Plan\n\n1. Step one\n2. Step two',
  planFilePath: '/path/to/plan.md',
  contextClearMarkers: [],
}

describe('usePlanApproval', () => {
  const mockConnection = {
    on: vi.fn(),
    off: vi.fn(),
  }

  beforeEach(() => {
    vi.clearAllMocks()
    ;(useClaudeCodeHub as Mock).mockReturnValue({
      connection: mockConnection,
      isConnected: true,
    })
  })

  it('returns initial state from session', () => {
    const { result } = renderHook(() => usePlanApproval('session-123', mockSession))

    expect(result.current.hasPendingPlan).toBe(true)
    expect(result.current.planContent).toBe('# Implementation Plan\n\n1. Step one\n2. Step two')
    expect(result.current.planFilePath).toBe('/path/to/plan.md')
  })

  it('returns false for hasPendingPlan when session has no plan', () => {
    const sessionWithoutPlan: ClaudeSession = {
      ...mockSession,
      hasPendingPlanApproval: false,
      planContent: undefined,
      planFilePath: undefined,
    }

    const { result } = renderHook(() => usePlanApproval('session-123', sessionWithoutPlan))

    expect(result.current.hasPendingPlan).toBe(false)
    expect(result.current.planContent).toBeUndefined()
    expect(result.current.planFilePath).toBeUndefined()
  })

  it('returns null values when session is null', () => {
    const { result } = renderHook(() => usePlanApproval('session-123', null))

    expect(result.current.hasPendingPlan).toBe(false)
    expect(result.current.planContent).toBeUndefined()
    expect(result.current.planFilePath).toBeUndefined()
  })

  it('registers event handlers for plan events', () => {
    renderHook(() => usePlanApproval('session-123', mockSession))

    const registeredEvents = mockConnection.on.mock.calls.map((call) => call[0])
    expect(registeredEvents).toContain('AGUICustomEvent')
    expect(registeredEvents).toContain('SessionStatusChanged')
    expect(registeredEvents).toContain('ContextCleared')
  })

  it('cleans up event handlers on unmount', () => {
    const { unmount } = renderHook(() => usePlanApproval('session-123', mockSession))

    unmount()

    expect(mockConnection.off).toHaveBeenCalled()
  })

  describe('AGUICustomEvent handling', () => {
    it('updates plan state when PlanPending event is received', async () => {
      const sessionWithoutPlan: ClaudeSession = {
        ...mockSession,
        hasPendingPlanApproval: false,
        planContent: undefined,
        planFilePath: undefined,
      }

      const { result } = renderHook(() => usePlanApproval('session-123', sessionWithoutPlan))

      // Initially no plan
      expect(result.current.hasPendingPlan).toBe(false)

      // Find the AGUICustomEvent handler
      const customEventCall = mockConnection.on.mock.calls.find(
        (call) => call[0] === 'AGUICustomEvent'
      )
      expect(customEventCall).toBeDefined()

      const handler = customEventCall![1]
      const planPendingEvent: CustomEvent = {
        type: 'CUSTOM',
        name: 'PlanPending',
        value: {
          planContent: '# New Plan\n\n1. New step',
          planFilePath: '/path/to/new-plan.md',
        },
        timestamp: Date.now(),
      }

      act(() => {
        handler(planPendingEvent)
      })

      expect(result.current.hasPendingPlan).toBe(true)
      expect(result.current.planContent).toBe('# New Plan\n\n1. New step')
      expect(result.current.planFilePath).toBe('/path/to/new-plan.md')
    })

    it('ignores non-PlanPending custom events', () => {
      const { result } = renderHook(() => usePlanApproval('session-123', mockSession))

      const customEventCall = mockConnection.on.mock.calls.find(
        (call) => call[0] === 'AGUICustomEvent'
      )
      const handler = customEventCall![1]

      const otherEvent: CustomEvent = {
        type: 'CUSTOM',
        name: 'QuestionPending',
        value: { questions: [] },
        timestamp: Date.now(),
      }

      const initialPlanContent = result.current.planContent

      act(() => {
        handler(otherEvent)
      })

      // Should remain unchanged
      expect(result.current.planContent).toBe(initialPlanContent)
    })
  })

  describe('SessionStatusChanged handling', () => {
    it('clears plan state when status changes and hasPendingPlanApproval is false', async () => {
      const { result } = renderHook(() => usePlanApproval('session-123', mockSession))

      // Initially has plan
      expect(result.current.hasPendingPlan).toBe(true)

      const statusChangedCall = mockConnection.on.mock.calls.find(
        (call) => call[0] === 'SessionStatusChanged'
      )
      expect(statusChangedCall).toBeDefined()

      const handler = statusChangedCall![1]

      act(() => {
        handler('session-123', 'Running' as ClaudeSessionStatus, false)
      })

      expect(result.current.hasPendingPlan).toBe(false)
      expect(result.current.planContent).toBeUndefined()
      expect(result.current.planFilePath).toBeUndefined()
    })

    it('ignores status changes for other sessions', () => {
      const { result } = renderHook(() => usePlanApproval('session-123', mockSession))

      const statusChangedCall = mockConnection.on.mock.calls.find(
        (call) => call[0] === 'SessionStatusChanged'
      )
      const handler = statusChangedCall![1]

      const initialPlanContent = result.current.planContent

      act(() => {
        handler('other-session', 'Running' as ClaudeSessionStatus, false)
      })

      // Should remain unchanged
      expect(result.current.planContent).toBe(initialPlanContent)
      expect(result.current.hasPendingPlan).toBe(true)
    })

    it('keeps plan state when hasPendingPlanApproval remains true', () => {
      const { result } = renderHook(() => usePlanApproval('session-123', mockSession))

      const statusChangedCall = mockConnection.on.mock.calls.find(
        (call) => call[0] === 'SessionStatusChanged'
      )
      const handler = statusChangedCall![1]

      act(() => {
        handler('session-123', 'WaitingForPlanExecution' as ClaudeSessionStatus, true)
      })

      // Should remain with plan
      expect(result.current.hasPendingPlan).toBe(true)
      expect(result.current.planContent).toBe('# Implementation Plan\n\n1. Step one\n2. Step two')
    })
  })

  describe('ContextCleared handling', () => {
    it('clears plan state when context is cleared', () => {
      const { result } = renderHook(() => usePlanApproval('session-123', mockSession))

      // Initially has plan
      expect(result.current.hasPendingPlan).toBe(true)

      const contextClearedCall = mockConnection.on.mock.calls.find(
        (call) => call[0] === 'ContextCleared'
      )
      expect(contextClearedCall).toBeDefined()

      const handler = contextClearedCall![1]

      act(() => {
        handler('session-123')
      })

      expect(result.current.hasPendingPlan).toBe(false)
      expect(result.current.planContent).toBeUndefined()
      expect(result.current.planFilePath).toBeUndefined()
    })

    it('ignores context cleared for other sessions', () => {
      const { result } = renderHook(() => usePlanApproval('session-123', mockSession))

      const contextClearedCall = mockConnection.on.mock.calls.find(
        (call) => call[0] === 'ContextCleared'
      )
      const handler = contextClearedCall![1]

      const initialPlanContent = result.current.planContent

      act(() => {
        handler('other-session')
      })

      // Should remain unchanged
      expect(result.current.planContent).toBe(initialPlanContent)
      expect(result.current.hasPendingPlan).toBe(true)
    })
  })

  it('updates when session prop changes', () => {
    const { result, rerender } = renderHook(
      ({ session }) => usePlanApproval('session-123', session),
      { initialProps: { session: mockSession } }
    )

    expect(result.current.planContent).toBe('# Implementation Plan\n\n1. Step one\n2. Step two')

    const updatedSession: ClaudeSession = {
      ...mockSession,
      planContent: '# Updated Plan',
    }

    rerender({ session: updatedSession })

    expect(result.current.planContent).toBe('# Updated Plan')
  })
})
