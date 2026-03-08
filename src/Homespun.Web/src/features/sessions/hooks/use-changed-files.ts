import { useQuery } from '@tanstack/react-query'
import { Clones } from '@/api'
import type { ClaudeSession } from '@/api/generated'

export function useChangedFiles(session: ClaudeSession | undefined) {
  const workingDirectory = session?.workingDirectory
  const targetBranch = 'main' // Always use main as the target branch

  return useQuery({
    queryKey: ['changed-files', workingDirectory, targetBranch],
    queryFn: async () => {
      if (!workingDirectory) {
        return []
      }

      const response = await Clones.getApiClonesChangedFiles({
        query: {
          workingDirectory,
          targetBranch,
        },
      })

      return response.data || []
    },
    enabled: Boolean(workingDirectory),
    staleTime: 30 * 1000, // 30 seconds
  })
}
