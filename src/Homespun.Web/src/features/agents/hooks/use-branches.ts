import { useQuery } from '@tanstack/react-query'
import { Clones, type BranchInfo } from '@/api'

export const branchesQueryKey = (repoPath: string) => ['branches', repoPath]

/**
 * Hook to fetch available branches for a repository.
 * @param repoPath - Path to the git repository
 * @param defaultBranch - Optional default branch to prioritize in the list
 */
export function useBranches(repoPath: string | undefined, defaultBranch?: string | null) {
  const query = useQuery({
    queryKey: branchesQueryKey(repoPath ?? ''),
    queryFn: async () => {
      const result = await Clones.getApiClonesBranches({
        query: { repoPath: repoPath! },
      })

      if (result.error || !result.data) {
        throw new Error('Failed to fetch branches')
      }

      // Sort branches alphabetically, but put default branch first
      const branches = result.data as BranchInfo[]
      const sorted = [...branches].sort((a, b) => {
        const aName = a.shortName ?? ''
        const bName = b.shortName ?? ''
        // Default branch always first
        if (defaultBranch) {
          if (aName === defaultBranch) return -1
          if (bName === defaultBranch) return 1
        }
        return aName.localeCompare(bName)
      })

      return sorted
    },
    enabled: !!repoPath,
    staleTime: 30000, // Consider branches fresh for 30 seconds
  })

  return {
    branches: query.data ?? [],
    isLoading: query.isLoading,
    isError: query.isError,
    error: query.error,
    refetch: query.refetch,
  }
}
