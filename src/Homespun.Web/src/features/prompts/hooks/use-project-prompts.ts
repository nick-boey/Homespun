import { useQuery } from '@tanstack/react-query'
import { AgentPrompts } from '@/api'
import type { AgentPrompt } from '@/api/generated/types.gen'

export const projectPromptsQueryKey = (projectId: string) => ['project-prompts', projectId] as const

/**
 * Hook to fetch project-specific agent prompts.
 * Returns only prompts that belong to the specified project.
 */
export function useProjectPrompts(projectId: string) {
  return useQuery({
    queryKey: projectPromptsQueryKey(projectId),
    queryFn: async (): Promise<AgentPrompt[]> => {
      const response = await AgentPrompts.getApiAgentPromptsProjectByProjectId({
        path: { projectId },
      })
      if (response.error) {
        throw new Error(
          typeof response.error === 'object' && 'detail' in response.error
            ? (response.error as { detail: string }).detail
            : 'Failed to fetch prompts'
        )
      }
      return response.data as AgentPrompt[]
    },
    enabled: !!projectId,
  })
}
