import { useQuery } from '@tanstack/react-query'
import { AgentPrompts } from '@/api'
import type { AgentPrompt } from '@/api/generated/types.gen'

export const globalPromptsQueryKey = () => ['prompts', 'global'] as const

/**
 * Hook to fetch global agent prompts.
 * Returns only prompts where projectId is null.
 */
export function useGlobalPrompts() {
  return useQuery({
    queryKey: globalPromptsQueryKey(),
    queryFn: async () => {
      const response = await AgentPrompts.getApiAgentPrompts()

      if (response.error) {
        throw new Error(
          typeof response.error === 'object' && 'detail' in response.error
            ? (response.error as { detail: string }).detail
            : 'Failed to fetch prompts'
        )
      }

      // Filter to only return global prompts (where projectId is null)
      const allPrompts = response.data as AgentPrompt[]
      return allPrompts.filter((p) => p.projectId === null)
    },
  })
}
