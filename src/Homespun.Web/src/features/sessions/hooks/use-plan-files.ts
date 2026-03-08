import { useQuery, useQueries } from '@tanstack/react-query'
import { Plans } from '@/api'
import type { ClaudeSession, PlanFileInfo } from '@/api/generated'

export function usePlanFiles(session: ClaudeSession | undefined) {
  const workingDirectory = session?.workingDirectory

  // Fetch list of plan files
  const planListQuery = useQuery({
    queryKey: ['plan-files', workingDirectory],
    queryFn: async () => {
      if (!workingDirectory) {
        return []
      }

      const response = await Plans.getApiPlans({
        query: {
          workingDirectory,
        },
      })

      return response.data || []
    },
    enabled: Boolean(workingDirectory),
    staleTime: 30 * 1000, // 30 seconds
  })

  return planListQuery
}

// Hook to fetch individual plan content
export function usePlanContent(workingDirectory: string | undefined, fileName: string | undefined) {
  return useQuery({
    queryKey: ['plan-content', workingDirectory, fileName],
    queryFn: async () => {
      if (!workingDirectory || !fileName) {
        return null
      }

      const response = await Plans.getApiPlansContent({
        query: {
          workingDirectory,
          fileName,
        },
      })

      return response.data
    },
    enabled: Boolean(workingDirectory && fileName),
    staleTime: 60 * 1000, // 1 minute
  })
}

// Hook to fetch multiple plan contents at once
export function usePlanContents(
  workingDirectory: string | undefined,
  planFiles: PlanFileInfo[] | undefined
) {
  const queries = useQueries({
    queries:
      planFiles?.map((file) => ({
        queryKey: ['plan-content', workingDirectory, file.fileName],
        queryFn: async () => {
          if (!workingDirectory || !file.fileName) {
            return null
          }

          const response = await Plans.getApiPlansContent({
            query: {
              workingDirectory,
              fileName: file.fileName,
            },
          })

          return {
            fileName: file.fileName,
            content: response.data,
          }
        },
        enabled: Boolean(workingDirectory && file.fileName),
        staleTime: 60 * 1000, // 1 minute
      })) || [],
  })

  return queries
}
