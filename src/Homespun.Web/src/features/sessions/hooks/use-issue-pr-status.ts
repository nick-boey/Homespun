import { useQuery } from '@tanstack/react-query'
import { IssuePrStatus } from '@/api'
import type { ClaudeSession } from '@/types/signalr'

export function useIssuePrStatus(session: ClaudeSession | undefined) {
  const isClone = session?.entityId?.startsWith('clone:')
  const projectId = session?.projectId
  const issueId = session?.entityId?.replace('clone:', '')

  return useQuery({
    queryKey: ['issue-pr-status', projectId, issueId],
    queryFn: async () => {
      if (!projectId || !issueId || !isClone) {
        return null
      }

      const response = await IssuePrStatus.getApiIssuePrStatusByProjectIdByIssueId({
        path: {
          projectId,
          issueId,
        },
      })

      return response.data
    },
    enabled: Boolean(projectId && issueId && isClone),
    staleTime: 30 * 1000, // 30 seconds
  })
}
