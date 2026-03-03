import { useQuery } from '@tanstack/react-query'
import { Clones, type BranchInfo } from '@/api'

export const branchesQueryKey = (repoPath: string) => ['branches', repoPath] as const

export function useBranches(repoPath: string) {
  return useQuery({
    queryKey: branchesQueryKey(repoPath),
    queryFn: async () => {
      const response = await Clones.getApiClonesBranches({
        query: { repoPath },
      })
      if (response.error) {
        throw new Error(response.error?.detail ?? 'Failed to fetch branches')
      }
      return response.data as BranchInfo[]
    },
    enabled: !!repoPath,
  })
}

/**
 * Filter branches that are only on remote (no local clone)
 */
export function getRemoteOnlyBranches(branches: BranchInfo[]): BranchInfo[] {
  return branches.filter(
    (branch) =>
      branch.hasRemote && !branch.hasClone && branch.shortName !== 'main' && branch.shortName !== 'master'
  )
}

/**
 * Filter branches that have local clones
 */
export function getLocalBranches(branches: BranchInfo[]): BranchInfo[] {
  return branches.filter((branch) => branch.hasClone)
}
