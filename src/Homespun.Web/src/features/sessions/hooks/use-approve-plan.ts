import { useState, useCallback } from 'react'
import { useClaudeCodeHub } from '@/providers/signalr-provider'

export interface UseApprovePlanResult {
  /** Approve the plan and clear conversation context */
  approveClearContext: () => Promise<void>
  /** Approve the plan and keep existing conversation context */
  approveKeepContext: () => Promise<void>
  /** Reject the plan with optional feedback */
  reject: (feedback?: string) => Promise<void>
  /** Whether an approval action is in progress */
  isLoading: boolean
  /** Error message if the last action failed */
  error: string | undefined
}

/**
 * Hook for approving or rejecting a plan in a Claude session.
 *
 * Provides three actions:
 * - approveClearContext: Approve the plan and start fresh (clear context)
 * - approveKeepContext: Approve the plan and keep existing context
 * - reject: Reject the plan with optional feedback for revision
 */
export function useApprovePlan(sessionId: string): UseApprovePlanResult {
  const { methods, isConnected } = useClaudeCodeHub()
  const [isLoading, setIsLoading] = useState(false)
  const [error, setError] = useState<string | undefined>()

  const approveClearContext = useCallback(async () => {
    if (!methods || !isConnected) return

    setIsLoading(true)
    setError(undefined)

    try {
      await methods.approvePlan(sessionId, true, false, null)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to approve plan')
    } finally {
      setIsLoading(false)
    }
  }, [methods, isConnected, sessionId])

  const approveKeepContext = useCallback(async () => {
    if (!methods || !isConnected) return

    setIsLoading(true)
    setError(undefined)

    try {
      await methods.approvePlan(sessionId, true, true, null)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to approve plan')
    } finally {
      setIsLoading(false)
    }
  }, [methods, isConnected, sessionId])

  const reject = useCallback(
    async (feedback?: string) => {
      if (!methods || !isConnected) return

      setIsLoading(true)
      setError(undefined)

      try {
        await methods.approvePlan(sessionId, false, false, feedback ?? null)
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Failed to reject plan')
      } finally {
        setIsLoading(false)
      }
    },
    [methods, isConnected, sessionId]
  )

  return {
    approveClearContext,
    approveKeepContext,
    reject,
    isLoading,
    error,
  }
}
