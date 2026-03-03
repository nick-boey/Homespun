import { useQuery } from '@tanstack/react-query'
import { useMemo } from 'react'
import { Sessions } from '@/api'
import type { SessionSummary, ClaudeSessionStatus } from '@/api/generated/types.gen'

export const projectSessionsQueryKey = (projectId: string) =>
  ['project-sessions', projectId] as const

// Session statuses that indicate the session is active
// Based on ClaudeSessionStatus enum in types.gen.ts
const ACTIVE_STATUSES: ClaudeSessionStatus[] = [
  0, // Starting
  1, // RunningHooks
  2, // Running
  3, // WaitingForInput
]

// Session statuses that indicate the session is actively processing
const PROCESSING_STATUSES: ClaudeSessionStatus[] = [
  0, // Starting
  1, // RunningHooks
  2, // Running
]

/**
 * Hook to fetch all sessions for a project.
 */
export function useProjectSessions(projectId: string) {
  return useQuery({
    queryKey: projectSessionsQueryKey(projectId),
    queryFn: async (): Promise<SessionSummary[]> => {
      const response = await Sessions.getApiSessionsProjectByProjectId({
        path: { projectId },
      })
      return response.data as SessionSummary[]
    },
    enabled: !!projectId,
    refetchInterval: 5000, // Poll every 5 seconds for updates
  })
}

/**
 * Hook to get count and status of active sessions for a project.
 */
export function useActiveSessionCount(projectId: string) {
  const { data: sessions, ...rest } = useProjectSessions(projectId)

  const result = useMemo(() => {
    const activeSessions = (sessions ?? []).filter((s) =>
      ACTIVE_STATUSES.includes(s.status as ClaudeSessionStatus)
    )
    const processingSessions = activeSessions.filter((s) =>
      PROCESSING_STATUSES.includes(s.status as ClaudeSessionStatus)
    )

    return {
      count: activeSessions.length,
      hasActive: activeSessions.length > 0,
      isProcessing: processingSessions.length > 0,
      activeSessions,
    }
  }, [sessions])

  return {
    ...result,
    ...rest,
  }
}
