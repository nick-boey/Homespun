import { useQuery } from '@tanstack/react-query'
import { AgentPrompts } from '@/api'
import type { AgentPrompt } from '@/api/generated/types.gen'

export const mergedProjectPromptsQueryKey = (projectId: string) =>
  ['merged-project-prompts', projectId] as const

/**
 * Hook to fetch all prompts available for a project.
 * Returns project-specific prompts, overrides, and non-overridden global prompts.
 */
export function useMergedProjectPrompts(projectId: string) {
  return useQuery({
    queryKey: mergedProjectPromptsQueryKey(projectId),
    queryFn: async (): Promise<AgentPrompt[]> => {
      const response = await AgentPrompts.getApiAgentPromptsAvailableForProjectByProjectId({
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
