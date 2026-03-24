import { useMutation, useQueryClient } from '@tanstack/react-query'
import { AgentPrompts } from '@/api'
import { projectPromptsQueryKey } from './use-project-prompts'
import { mergedProjectPromptsQueryKey } from './use-merged-project-prompts'
import { agentPromptsQueryKey } from '@/features/agents/hooks/use-agent-prompts'
import { issueAgentProjectPromptsQueryKey } from './use-issue-agent-project-prompts'

interface UseDeleteAllProjectPromptsOptions {
  projectId: string
  onSuccess?: () => void
  onError?: (error: Error) => void
}

export function useDeleteAllProjectPrompts(options: UseDeleteAllProjectPromptsOptions) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async () => {
      const result = await AgentPrompts.deleteApiAgentPromptsProjectByProjectIdAll({
        path: { projectId: options.projectId },
      })

      if (result.error) {
        throw result.error
      }

      return result.data
    },
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: projectPromptsQueryKey(options.projectId),
      })
      queryClient.invalidateQueries({
        queryKey: mergedProjectPromptsQueryKey(options.projectId),
      })
      queryClient.invalidateQueries({
        queryKey: agentPromptsQueryKey(options.projectId),
      })
      queryClient.invalidateQueries({
        queryKey: issueAgentProjectPromptsQueryKey(options.projectId),
      })
      options.onSuccess?.()
    },
    onError: (error) => {
      options.onError?.(error as Error)
    },
  })
}
