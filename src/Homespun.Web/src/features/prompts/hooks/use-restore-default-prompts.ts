import { useMutation, useQueryClient } from '@tanstack/react-query'
import { AgentPrompts } from '@/api'
import { globalPromptsQueryKey } from './use-global-prompts'
import { issueAgentPromptsQueryKey } from './use-issue-agent-prompts'

interface UseRestoreDefaultPromptsOptions {
  onSuccess?: () => void
  onError?: (error: Error) => void
}

export function useRestoreDefaultPrompts(options: UseRestoreDefaultPromptsOptions = {}) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async () => {
      const result = await AgentPrompts.postApiAgentPromptsRestoreDefaults()

      if (result.error) {
        throw result.error
      }

      return result.data
    },
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: globalPromptsQueryKey(),
      })
      queryClient.invalidateQueries({
        queryKey: issueAgentPromptsQueryKey,
      })
      options.onSuccess?.()
    },
    onError: (error) => {
      options.onError?.(error as Error)
    },
  })
}
