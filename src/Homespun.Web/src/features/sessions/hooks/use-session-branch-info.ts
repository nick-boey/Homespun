import { useQuery } from '@tanstack/react-query'
import { Clones } from '@/api'
import type { ClaudeSession } from '@/types/signalr'

/**
 * Fetches branch and commit information for a session's working directory.
 * Only fetches for clone sessions (entityId starts with 'clone:').
 */
export function useSessionBranchInfo(session: ClaudeSession | undefined) {
  const workingDirectory = session?.workingDirectory
  const entityId = session?.entityId
  const isCloneSession = entityId?.startsWith('clone:') ?? false

  const query = useQuery({
    queryKey: ['session-branch-info', workingDirectory],
    queryFn: async () => {
      if (!workingDirectory) {
        return null
      }

      const response = await Clones.getApiClonesSessionBranchInfo({
        query: {
          workingDirectory,
        },
      })

      return response.data
    },
    enabled: Boolean(workingDirectory) && isCloneSession,
    staleTime: 30 * 1000, // 30 seconds
    refetchInterval: 30 * 1000, // Auto-refresh every 30 seconds
  })

  return {
    ...query,
    isCloneSession,
  }
}
