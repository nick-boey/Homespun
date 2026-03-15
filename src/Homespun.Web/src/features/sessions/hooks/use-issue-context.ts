import { useQuery } from '@tanstack/react-query'
import { Issues } from '@/api'
import type { IssueResponse } from '@/api/generated/types.gen'
import { generateBranchName } from '@/features/issues/services/branch-name'
import type { PromptContext } from '../utils/render-prompt-template'

/**
 * Capitalizes the first letter of a string.
 */
function capitalize(str: string): string {
  if (!str) return str
  return str.charAt(0).toUpperCase() + str.slice(1)
}

/**
 * Hook to fetch issue context for prompt template rendering.
 * Returns null for PR entities (only works for issues).
 *
 * @param entityId - The entity ID (issue or PR ID)
 * @param projectId - The project ID
 * @returns Query result with PromptContext data
 */
export function useIssueContext(
  entityId: string | null | undefined,
  projectId: string | null | undefined
) {
  // Don't fetch for PR entities
  const isPR = entityId?.toLowerCase().startsWith('pr-') ?? false
  const isEnabled = !!entityId && !!projectId && !isPR

  return useQuery({
    queryKey: ['issue-context', entityId, projectId],
    queryFn: async (): Promise<PromptContext | null> => {
      if (!entityId || !projectId) return null

      const response = await Issues.getApiIssuesByIssueId({
        path: { issueId: entityId },
        query: { projectId },
      })

      const issue = response.data as IssueResponse

      const branchName = generateBranchName(issue) ?? ''
      const issueType = issue.type ? capitalize(issue.type) : 'Task'

      return {
        title: issue.title ?? '',
        id: issue.id ?? entityId,
        description: issue.description ?? '',
        branch: branchName,
        type: issueType,
      }
    },
    enabled: isEnabled,
    staleTime: 5 * 60 * 1000, // Cache for 5 minutes
  })
}
