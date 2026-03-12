import { useQuery } from '@tanstack/react-query'
import { Issues } from '@/api'

export const projectAssigneesQueryKey = (projectId: string) =>
  ['projectAssignees', projectId] as const

export interface UseProjectAssigneesResult {
  assignees: string[]
  isLoading: boolean
  isError: boolean
  error: Error | null
  refetch: () => void
}

/**
 * Hook for fetching unique assignees for a project's issues.
 * Returns all unique email addresses found in issue assignments,
 * plus the current user if configured.
 *
 * @param projectId - The project ID to fetch assignees for
 * @returns List of unique assignee emails and query state
 */
export function useProjectAssignees(projectId: string): UseProjectAssigneesResult {
  const query = useQuery({
    queryKey: projectAssigneesQueryKey(projectId),
    queryFn: async () => {
      const response = await Issues.getApiProjectsByProjectIdIssuesAssignees({
        path: { projectId },
      })

      if (response.error) {
        throw new Error(response.error.detail ?? 'Failed to fetch assignees')
      }

      return response.data ?? []
    },
    enabled: !!projectId,
  })

  return {
    assignees: query.data ?? [],
    isLoading: query.isLoading,
    isError: query.isError,
    error: query.error,
    refetch: query.refetch,
  }
}
