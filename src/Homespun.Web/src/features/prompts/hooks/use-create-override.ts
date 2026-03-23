import { useMutation, useQueryClient } from '@tanstack/react-query'
import { AgentPrompts, type CreateOverrideRequest, type AgentPrompt } from '@/api'
import { projectPromptsQueryKey } from './use-project-prompts'
import { mergedProjectPromptsQueryKey } from './use-merged-project-prompts'
import { agentPromptsQueryKey } from '@/features/agents/hooks/use-agent-prompts'

interface UseCreateOverrideOptions {
  projectId: string
  onSuccess?: (prompt: AgentPrompt) => void
  onError?: (error: Error) => void
}

export function useCreateOverride(options: UseCreateOverrideOptions) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (data: CreateOverrideRequest) => {
      const result = await AgentPrompts.postApiAgentPromptsCreateOverride({
        body: data,
      })

      if (result.error) {
        throw result.error
      }

      return result.data
    },
    onSuccess: (data) => {
      // Invalidate project prompts list
      queryClient.invalidateQueries({
        queryKey: projectPromptsQueryKey(options.projectId),
      })
      queryClient.invalidateQueries({
        queryKey: mergedProjectPromptsQueryKey(options.projectId),
      })
      // Also invalidate available prompts for agent launcher
      queryClient.invalidateQueries({
        queryKey: agentPromptsQueryKey(options.projectId),
      })
      // Invalidate global prompts as well since the available list may have changed
      queryClient.invalidateQueries({
        queryKey: ['global-prompts'],
      })
      options.onSuccess?.(data as AgentPrompt)
    },
    onError: (error) => {
      options.onError?.(error as Error)
    },
  })
}
