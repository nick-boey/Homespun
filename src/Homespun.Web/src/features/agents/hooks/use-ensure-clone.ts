import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { useCallback, useMemo } from 'react'
import { ProjectClones, Issues } from '@/api'

interface UseEnsureCloneOptions {
  projectId: string
  issueId: string
  /** Optional base branch to create the new branch from */
  baseBranch?: string
}

interface UseEnsureCloneResult {
  /** The resolved branch name for the issue */
  branchName: string | undefined
  /** Whether the clone already exists */
  cloneExists: boolean | undefined
  /** Whether the initial checks are loading */
  isLoading: boolean
  /** Whether a clone creation is in progress */
  isCreating: boolean
  /** Whether there was an error */
  isError: boolean
  /** The error if one occurred */
  error: Error | null
  /** Ensure the clone exists, creating it if necessary. Returns the clone path. */
  ensureClone: () => Promise<string>
}

/**
 * Hook for ensuring a clone exists for a given issue.
 *
 * This hook:
 * 1. Resolves the branch name for the issue
 * 2. Checks if a clone already exists for that branch
 * 3. Provides an `ensureClone` function to create the clone if needed
 *
 * @param options - The project ID and issue ID
 * @returns Clone state and the ensureClone function
 */
export function useEnsureClone({
  projectId,
  issueId,
  baseBranch,
}: UseEnsureCloneOptions): UseEnsureCloneResult {
  const queryClient = useQueryClient()
  const enabled = !!projectId && !!issueId

  // Step 1: Resolve branch name for the issue
  const branchQuery = useQuery({
    queryKey: ['issue', issueId, 'resolved-branch', projectId] as const,
    queryFn: async () => {
      const response = await Issues.getApiIssuesByIssueIdResolvedBranch({
        path: { issueId },
        query: { projectId },
      })

      if (response.error || !response.data) {
        throw new Error(response.error?.detail ?? 'Failed to resolve branch name')
      }

      return response.data.branchName ?? null
    },
    enabled,
  })

  const branchName = branchQuery.data ?? undefined

  // Step 2: Check if clone exists (only when we have a branch name)
  const cloneExistsQuery = useQuery({
    queryKey: ['clone', 'exists', projectId, branchName] as const,
    queryFn: async () => {
      if (!branchName) return null

      const response = await ProjectClones.getApiProjectsByProjectIdClonesExists({
        path: { projectId },
        query: { branchName },
      })

      if (response.error) {
        throw new Error(response.error?.detail ?? 'Failed to check clone status')
      }

      return response.data?.exists ?? false
    },
    enabled: enabled && !!branchName,
  })

  // Step 3: Mutation to create clone
  const createCloneMutation = useMutation({
    mutationFn: async (branchNameToCreate: string) => {
      const response = await ProjectClones.postApiProjectsByProjectIdClones({
        path: { projectId },
        body: {
          branchName: branchNameToCreate,
          createBranch: true,
          baseBranch: baseBranch || undefined,
        },
      })

      if (response.error || !response.data) {
        throw new Error(response.error?.detail ?? 'Failed to create clone')
      }

      return response.data.path ?? ''
    },
    onSuccess: () => {
      // Invalidate clone exists query to refresh the state
      queryClient.invalidateQueries({
        queryKey: ['clone', 'exists', projectId, branchName],
      })
    },
  })

  // Combined loading state
  const isLoading = branchQuery.isLoading || (branchQuery.isSuccess && cloneExistsQuery.isLoading)

  // Combined error state
  const isError = branchQuery.isError || cloneExistsQuery.isError
  const error = useMemo(() => {
    if (branchQuery.error) return branchQuery.error as Error
    if (cloneExistsQuery.error) return cloneExistsQuery.error as Error
    return null
  }, [branchQuery.error, cloneExistsQuery.error])

  // Ensure clone function
  const ensureClone = useCallback(async (): Promise<string> => {
    if (!branchName) {
      throw new Error('Branch name not resolved')
    }

    // Always call create - it's idempotent and will return the path
    // whether the clone exists or not
    return createCloneMutation.mutateAsync(branchName)
  }, [branchName, createCloneMutation])

  return {
    branchName,
    cloneExists: cloneExistsQuery.data ?? undefined,
    isLoading,
    isCreating: createCloneMutation.isPending,
    isError,
    error,
    ensureClone,
  }
}
