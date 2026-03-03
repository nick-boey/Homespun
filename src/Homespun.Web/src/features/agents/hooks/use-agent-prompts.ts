import { useQuery } from '@tanstack/react-query'
import { AgentPrompts } from '@/api'
import type { AgentPrompt } from '@/api/generated/types.gen'

export const agentPromptsQueryKey = (projectId: string) => ['agent-prompts', projectId] as const

/**
 * Hook to fetch available agent prompts for a project.
 * Returns both project-specific and global prompts.
 */
export function useAgentPrompts(projectId: string) {
  return useQuery({
    queryKey: agentPromptsQueryKey(projectId),
    queryFn: async (): Promise<AgentPrompt[]> => {
      const response = await AgentPrompts.getApiAgentPromptsAvailableForProjectByProjectId({
        path: { projectId },
      })
      return response.data as AgentPrompt[]
    },
    enabled: !!projectId,
  })
}
