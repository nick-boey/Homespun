import { useQuery } from '@tanstack/react-query'
import { AgentPrompts } from '@/api'
import type { AgentPrompt } from '@/api/generated/types.gen'

export const issueAgentProjectPromptsQueryKey = (projectId: string) =>
  ['prompts', 'issue-agent', 'project', projectId] as const

/**
 * Hook for fetching merged issue agent prompts for a project.
 * Returns project-specific issue agent prompts + non-overridden global issue agent prompts.
 */
export function useIssueAgentProjectPrompts(projectId: string) {
  return useQuery<AgentPrompt[]>({
    queryKey: issueAgentProjectPromptsQueryKey(projectId),
    queryFn: async () => {
      const response = await AgentPrompts.getApiAgentPromptsIssueAgentAvailableByProjectId({
        path: { projectId },
      })

      if (response.error) {
        throw new Error(
          typeof response.error === 'object' && 'detail' in response.error
            ? (response.error as { detail: string }).detail
            : 'Failed to fetch issue agent prompts for project'
        )
      }

      if (!response.data) {
        throw new Error('Failed to fetch issue agent prompts for project')
      }

      return response.data
    },
    enabled: !!projectId,
  })
}
