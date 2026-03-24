import { useMutation, useQueryClient } from '@tanstack/react-query'
import { AgentPrompts, type CreateAgentPromptRequest, type AgentPrompt } from '@/api'
import { projectPromptsQueryKey } from './use-project-prompts'
import { mergedProjectPromptsQueryKey } from './use-merged-project-prompts'
import { issueAgentPromptsQueryKey } from './use-issue-agent-prompts'
import { issueAgentProjectPromptsQueryKey } from './use-issue-agent-project-prompts'
import { agentPromptsQueryKey } from '@/features/agents/hooks/use-agent-prompts'

interface UseCreatePromptOptions {
  projectId?: string
  onSuccess?: (prompt: AgentPrompt) => void
  onError?: (error: Error) => void
}

export function useCreatePrompt(options: UseCreatePromptOptions) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (data: CreateAgentPromptRequest) => {
      const result = await AgentPrompts.postApiAgentPrompts({
        body: data,
      })

      if (result.error) {
        throw result.error
      }

      return result.data
    },
    onSuccess: (data) => {
      if (options.projectId) {
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
        // Invalidate issue agent project prompts
        queryClient.invalidateQueries({
          queryKey: issueAgentProjectPromptsQueryKey(options.projectId),
        })
      } else {
        // Invalidate global prompts list
        queryClient.invalidateQueries({
          queryKey: ['global-prompts'],
        })
        // Invalidate issue agent prompts
        queryClient.invalidateQueries({
          queryKey: issueAgentPromptsQueryKey,
        })
      }
      options.onSuccess?.(data as AgentPrompt)
    },
    onError: (error) => {
      options.onError?.(error as Error)
    },
  })
}
