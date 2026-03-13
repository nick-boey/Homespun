import { useQuery } from '@tanstack/react-query'
import { ProjectSearch, type SearchablePrResponse } from '@/api'

export const searchablePrsQueryKey = (projectId: string) => ['searchable-prs', projectId] as const

export interface UseSearchablePrsResult {
  prs: SearchablePrResponse[] | undefined
  hash: string | undefined
  isLoading: boolean
  isSuccess: boolean
  isError: boolean
  error: Error | null
  refetch: () => void
}

/**
 * Hook for fetching searchable PRs for # mention autocomplete.
 * Uses hash-based caching to avoid refetching unchanged data.
 *
 * @param projectId - The project ID to fetch PRs for
 * @returns PRs data and query state
 */
export function useSearchablePrs(projectId: string): UseSearchablePrsResult {
  const query = useQuery({
    queryKey: searchablePrsQueryKey(projectId),
    queryFn: async () => {
      // Get cached hash if available (for conditional requests)
      const cachedHash = undefined // TODO: implement hash caching for 304 responses

      const response = await ProjectSearch.getApiProjectsByProjectIdSearchPrs({
        path: { projectId },
        query: { hash: cachedHash },
      })

      if (response.error || !response.data) {
        throw new Error(response.error?.detail ?? 'Failed to fetch searchable PRs')
      }

      return response.data
    },
    enabled: !!projectId,
    staleTime: 5 * 60 * 1000, // Consider fresh for 5 minutes
  })

  return {
    prs: query.data?.prs ?? undefined,
    hash: query.data?.hash ?? undefined,
    isLoading: query.isLoading,
    isSuccess: query.isSuccess,
    isError: query.isError,
    error: query.error,
    refetch: query.refetch,
  }
}
