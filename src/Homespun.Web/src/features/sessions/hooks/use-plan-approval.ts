import { useState, useEffect, useCallback } from 'react'
import { useClaudeCodeHub } from '@/providers/signalr-provider'
import type {
  ClaudeSession,
  ClaudeSessionStatus,
  CustomEvent,
  AGUIPlanPendingData,
} from '@/types/signalr'
import { AGUICustomEventName } from '@/types/signalr'

export interface UsePlanApprovalResult {
  /** Whether there is a pending plan awaiting approval */
  hasPendingPlan: boolean
  /** The plan content markdown */
  planContent: string | undefined
  /** The file path where the plan was written */
  planFilePath: string | undefined
}

interface PlanOverride {
  hasPendingPlan: boolean
  planContent: string | undefined
  planFilePath: string | undefined
}

/**
 * Hook for tracking plan approval state in a Claude session.
 *
 * Listens for SignalR events:
 * - AGUICustomEvent with name="PlanPending" to detect new plans
 * - SessionStatusChanged to clear plan state when approval completes
 * - ContextCleared to clear plan state when context is reset
 */
export function usePlanApproval(
  sessionId: string,
  session: ClaudeSession | null | undefined
): UsePlanApprovalResult {
  const { connection } = useClaudeCodeHub()

  // Track plan state from SignalR events (overrides session prop)
  // This is separate from session to handle real-time updates
  const [planOverride, setPlanOverride] = useState<PlanOverride | null>(null)

  // Handle PlanPending custom event
  const handleCustomEvent = useCallback((event: CustomEvent) => {
    if (event.name !== AGUICustomEventName.PlanPending) return

    const planData = event.value as AGUIPlanPendingData
    setPlanOverride({
      hasPendingPlan: true,
      planContent: planData.planContent,
      planFilePath: planData.planFilePath,
    })
  }, [])

  // Handle session status changes
  const handleSessionStatusChanged = useCallback(
    (changedSessionId: string, _status: ClaudeSessionStatus, hasPendingPlanApproval: boolean) => {
      if (changedSessionId !== sessionId) return

      if (!hasPendingPlanApproval) {
        setPlanOverride({
          hasPendingPlan: false,
          planContent: undefined,
          planFilePath: undefined,
        })
      }
    },
    [sessionId]
  )

  // Handle context cleared
  const handleContextCleared = useCallback(
    (clearedSessionId: string) => {
      if (clearedSessionId !== sessionId) return

      setPlanOverride({
        hasPendingPlan: false,
        planContent: undefined,
        planFilePath: undefined,
      })
    },
    [sessionId]
  )

  // Register event handlers
  useEffect(() => {
    if (!connection) return

    connection.on('AGUICustomEvent', handleCustomEvent)
    connection.on('SessionStatusChanged', handleSessionStatusChanged)
    connection.on('ContextCleared', handleContextCleared)

    return () => {
      connection.off('AGUICustomEvent', handleCustomEvent)
      connection.off('SessionStatusChanged', handleSessionStatusChanged)
      connection.off('ContextCleared', handleContextCleared)
    }
  }, [connection, handleCustomEvent, handleSessionStatusChanged, handleContextCleared])

  // Use override if set, otherwise use session values
  // The override takes precedence for real-time updates from SignalR
  if (planOverride !== null) {
    return {
      hasPendingPlan: planOverride.hasPendingPlan,
      planContent: planOverride.planContent,
      planFilePath: planOverride.planFilePath,
    }
  }

  return {
    hasPendingPlan: session?.hasPendingPlanApproval ?? false,
    planContent: session?.planContent,
    planFilePath: session?.planFilePath,
  }
}
