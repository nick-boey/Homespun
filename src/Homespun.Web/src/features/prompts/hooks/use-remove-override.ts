import { useMutation, useQueryClient } from '@tanstack/react-query'
import { AgentPrompts, type AgentPrompt } from '@/api'
import { projectPromptsQueryKey } from './use-project-prompts'
import { agentPromptsQueryKey } from '@/features/agents/hooks/use-agent-prompts'

interface UseRemoveOverrideOptions {
  projectId: string
  onSuccess?: (globalPrompt: AgentPrompt) => void
  onError?: (error: Error) => void
}

export function useRemoveOverride(options: UseRemoveOverrideOptions) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (promptId: string) => {
      const result = await AgentPrompts.deleteApiAgentPromptsByIdOverride({
        path: { id: promptId },
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
