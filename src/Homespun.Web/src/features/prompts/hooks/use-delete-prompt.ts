import { useMutation, useQueryClient } from '@tanstack/react-query'
import { AgentPrompts } from '@/api'
import { projectPromptsQueryKey } from './use-project-prompts'
import { agentPromptsQueryKey } from '@/features/agents/hooks/use-agent-prompts'

interface UseDeletePromptOptions {
  projectId?: string
  onSuccess?: () => void
  onError?: (error: Error) => void
}

export function useDeletePrompt(options: UseDeletePromptOptions) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (id: string) => {
      const result = await AgentPrompts.deleteApiAgentPromptsById({
        path: { id },
      })

      if (result.error) {
        throw result.error
      }

      return result.data
    },
    onSuccess: () => {
      if (options.projectId) {
        queryClient.invalidateQueries({
          queryKey: projectPromptsQueryKey(options.projectId),
        })
        queryClient.invalidateQueries({
          queryKey: agentPromptsQueryKey(options.projectId),
        })
      } else {
        // Invalidate global prompts list
        queryClient.invalidateQueries({
          queryKey: ['global-prompts'],
        })
      }
      options.onSuccess?.()
    },
    onError: (error) => {
      options.onError?.(error as Error)
    },
  })
}
