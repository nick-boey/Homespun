/**
 * useCreateIssue - Mutation hook for creating new issues.
 *
 * Supports creating issues as siblings, children (via parentIssueId),
 * or parents (via childIssueId) of existing issues.
 */

import { useState, useCallback } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { Issues } from '@/api'
import type { IssueResponse, IssueType } from '@/api'
import { taskGraphQueryKey } from './use-task-graph'

export interface UseCreateIssueOptions {
  /** The project ID to create issues in */
  projectId: string
  /** Callback fired when an issue is successfully created */
  onSuccess?: (issue: IssueResponse) => void
}

export interface CreateIssueParams {
  /** The title for the new issue */
  title: string
  /** Issue type (0=Task, 1=Bug, 2=Feature, 3=Chore) */
  type?: IssueType
  /** Set this to make the new issue a child of the specified parent */
  parentIssueId?: string
  /** Set this to make the new issue a parent of the specified child */
  childIssueId?: string
  /** Sort order for positioning within parent's children */
  parentSortOrder?: string
}

export interface UseCreateIssueReturn {
  /** Creates a new issue with the given parameters */
  createIssue: (params: CreateIssueParams) => Promise<IssueResponse>
  /** Whether a creation is in progress */
  isCreating: boolean
}

/**
 * Hook for creating new issues with parent/child relationships.
 */
export function useCreateIssue(options: UseCreateIssueOptions): UseCreateIssueReturn {
  const { projectId, onSuccess } = options
  const queryClient = useQueryClient()
  const [isCreating, setIsCreating] = useState(false)

  const createIssue = useCallback(
    async (params: CreateIssueParams): Promise<IssueResponse> => {
      const { title, type = 0, parentIssueId, childIssueId, parentSortOrder } = params

      setIsCreating(true)
      try {
        const response = await Issues.postApiIssues({
          body: {
            projectId,
            title,
            type,
            parentIssueId,
            childIssueId,
            parentSortOrder,
          },
        })

        const issue = response.data as IssueResponse

        // Invalidate task graph to refresh the view
        await queryClient.invalidateQueries({
          queryKey: taskGraphQueryKey(projectId),
        })

        onSuccess?.(issue)
        return issue
      } finally {
        setIsCreating(false)
      }
    },
    [projectId, queryClient, onSuccess]
  )

  return {
    createIssue,
    isCreating,
  }
}

export default useCreateIssue
