import { useQuery } from '@tanstack/react-query'
import { Issues, type IssueResponse } from '@/api'

export const issueQueryKey = (issueId: string, projectId: string) =>
  ['issue', issueId, projectId] as const

export interface UseIssueResult {
  issue: IssueResponse | undefined
  isLoading: boolean
  isSuccess: boolean
  isError: boolean
  error: Error | null
  refetch: () => void
}

/**
 * Hook for fetching a single issue by ID.
 *
 * @param issueId - The issue ID to fetch
 * @param projectId - The project ID the issue belongs to
 * @returns Issue data and query state
 */
export function useIssue(issueId: string, projectId: string): UseIssueResult {
  const query = useQuery({
    queryKey: issueQueryKey(issueId, projectId),
    queryFn: async () => {
      const response = await Issues.getApiIssuesByIssueId({
        path: { issueId },
        query: { projectId },
      })

      if (response.error || !response.data) {
        throw new Error(response.error?.detail ?? 'Failed to fetch issue')
      }

      return response.data
    },
    enabled: !!issueId && !!projectId,
  })

  return {
    issue: query.data,
    isLoading: query.isLoading,
    isSuccess: query.isSuccess,
    isError: query.isError,
    error: query.error,
    refetch: query.refetch,
  }
}
