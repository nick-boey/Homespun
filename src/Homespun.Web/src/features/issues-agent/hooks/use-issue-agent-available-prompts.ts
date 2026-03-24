import { useQuery } from '@tanstack/react-query'
import { AgentPrompts } from '@/api'
import type { AgentPrompt } from '@/api/generated/types.gen'

export const issueAgentPromptsQueryKey = (projectId: string) =>
  ['issue-agent-prompts', projectId] as const

/**
 * Hook to fetch available issue agent prompts for a project.
 * Returns both project-specific and global issue agent prompts.
 */
export function useIssueAgentAvailablePrompts(projectId: string) {
  return useQuery({
    queryKey: issueAgentPromptsQueryKey(projectId),
    queryFn: async (): Promise<AgentPrompt[]> => {
      const response = await AgentPrompts.getApiAgentPromptsIssueAgentAvailableByProjectId({
        path: { projectId },
      })
      return response.data as AgentPrompt[]
    },
    enabled: !!projectId,
  })
}
