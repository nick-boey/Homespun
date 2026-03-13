import { useQuery } from '@tanstack/react-query'
import { ProjectSearch } from '@/api'

export const projectFilesQueryKey = (projectId: string) => ['project-files', projectId] as const

export interface UseProjectFilesResult {
  files: string[] | undefined
  hash: string | undefined
  isLoading: boolean
  isSuccess: boolean
  isError: boolean
  error: Error | null
  refetch: () => void
}

/**
 * Hook for fetching project files for @ mention autocomplete.
 * Uses hash-based caching to avoid refetching unchanged data.
 *
 * @param projectId - The project ID to fetch files for
 * @returns Files data and query state
 */
export function useProjectFiles(projectId: string): UseProjectFilesResult {
  const query = useQuery({
    queryKey: projectFilesQueryKey(projectId),
    queryFn: async () => {
      // Get cached hash if available (for conditional requests)
      const cachedHash = undefined // TODO: implement hash caching for 304 responses

      const response = await ProjectSearch.getApiProjectsByProjectIdSearchFiles({
        path: { projectId },
        query: { hash: cachedHash },
      })

      if (response.error || !response.data) {
        throw new Error(response.error?.detail ?? 'Failed to fetch project files')
      }

      return response.data
    },
    enabled: !!projectId,
    staleTime: 5 * 60 * 1000, // Consider fresh for 5 minutes
  })

  return {
    files: query.data?.files ?? undefined,
    hash: query.data?.hash ?? undefined,
    isLoading: query.isLoading,
    isSuccess: query.isSuccess,
    isError: query.isError,
    error: query.error,
    refetch: query.refetch,
  }
}
