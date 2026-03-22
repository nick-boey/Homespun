import { useQuery } from '@tanstack/react-query'
import { AgentPrompts } from '@/api'
import type { AgentPrompt } from '@/api/generated/types.gen'

export const issueAgentPromptsQueryKey = ['prompts', 'issue-agent'] as const

/**
 * Hook for fetching issue agent prompts (IssueAgentModification and IssueAgentSystem).
 * These are specialized prompts for the Issues Agent workflow.
 */
export function useIssueAgentPrompts() {
  return useQuery<AgentPrompt[]>({
    queryKey: issueAgentPromptsQueryKey,
    queryFn: async () => {
      const response = await AgentPrompts.getApiAgentPromptsIssueAgentPrompts()

      if (response.error) {
        throw new Error(
          typeof response.error === 'object' && 'detail' in response.error
            ? (response.error as { detail: string }).detail
            : 'Failed to fetch issue agent prompts'
        )
      }

      if (!response.data) {
        throw new Error('Failed to fetch issue agent prompts')
      }

      return response.data
    },
  })
}
