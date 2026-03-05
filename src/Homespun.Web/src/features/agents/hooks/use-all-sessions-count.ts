import { useQuery } from '@tanstack/react-query'
import { useMemo } from 'react'
import { Sessions } from '@/api'
import type { SessionSummary, ClaudeSessionStatus } from '@/api/generated/types.gen'

// Status groupings as per requirements
const WORKING_STATUSES: ClaudeSessionStatus[] = [
  0, // Starting
  1, // RunningHooks
  2, // Running
]

const WAITING_STATUSES: ClaudeSessionStatus[] = [
  3, // WaitingForInput
  4, // WaitingForQuestionAnswer
  5, // WaitingForPlanExecution
]

const ERROR_STATUSES: ClaudeSessionStatus[] = [
  7, // Error
]

// Individual waiting status types
const WAITING_FOR_INPUT_STATUS: ClaudeSessionStatus = 3
const WAITING_FOR_ANSWER_STATUS: ClaudeSessionStatus = 4
const WAITING_FOR_PLAN_STATUS: ClaudeSessionStatus = 5

export const allSessionsCountQueryKey = ['all-sessions-count'] as const

export interface AllSessionsCount {
  workingCount: number
  waitingCount: number
  waitingForInputCount: number
  waitingForAnswerCount: number
  waitingForPlanCount: number
  errorCount: number
  totalActive: number
  hasActive: boolean
  isProcessing: boolean
  hasError: boolean
  isLoading: boolean
  isError: boolean
}

/**
 * Hook to fetch and count sessions across all projects.
 * Groups sessions by status category (working, waiting, error).
 */
export function useAllSessionsCount() {
  const {
    data: sessions,
    isLoading,
    isError,
    ...rest
  } = useQuery({
    queryKey: allSessionsCountQueryKey,
    queryFn: async (): Promise<SessionSummary[]> => {
      const response = await Sessions.getApiSessions()
      return response.data as SessionSummary[]
    },
    refetchInterval: 5000, // Poll every 5 seconds for real-time updates
  })

  const result = useMemo(() => {
    if (!sessions) {
      return {
        workingCount: 0,
        waitingCount: 0,
        waitingForInputCount: 0,
        waitingForAnswerCount: 0,
        waitingForPlanCount: 0,
        errorCount: 0,
        totalActive: 0,
        hasActive: false,
        isProcessing: false,
        hasError: false,
      }
    }

    const workingSessions = sessions.filter((s) =>
      WORKING_STATUSES.includes(s.status as ClaudeSessionStatus)
    )
    const waitingSessions = sessions.filter((s) =>
      WAITING_STATUSES.includes(s.status as ClaudeSessionStatus)
    )
    const errorSessions = sessions.filter((s) =>
      ERROR_STATUSES.includes(s.status as ClaudeSessionStatus)
    )

    // Count granular waiting status types
    const waitingForInputSessions = sessions.filter((s) => s.status === WAITING_FOR_INPUT_STATUS)
    const waitingForAnswerSessions = sessions.filter((s) => s.status === WAITING_FOR_ANSWER_STATUS)
    const waitingForPlanSessions = sessions.filter((s) => s.status === WAITING_FOR_PLAN_STATUS)

    // Total active includes working and waiting, but not errors
    const totalActive = workingSessions.length + waitingSessions.length

    return {
      workingCount: workingSessions.length,
      waitingCount: waitingSessions.length,
      waitingForInputCount: waitingForInputSessions.length,
      waitingForAnswerCount: waitingForAnswerSessions.length,
      waitingForPlanCount: waitingForPlanSessions.length,
      errorCount: errorSessions.length,
      totalActive,
      hasActive: totalActive > 0 || errorSessions.length > 0,
      isProcessing: workingSessions.length > 0,
      hasError: errorSessions.length > 0,
    }
  }, [sessions])

  return {
    ...result,
    isLoading,
    isError,
    ...rest,
  }
}
