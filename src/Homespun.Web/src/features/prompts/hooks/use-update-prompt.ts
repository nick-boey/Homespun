import { useMutation, useQueryClient } from '@tanstack/react-query'
import { AgentPrompts, type UpdateAgentPromptRequest, type AgentPrompt } from '@/api'
import { globalPromptsQueryKey } from './use-global-prompts'
import { projectPromptsQueryKey } from './use-project-prompts'
import { mergedProjectPromptsQueryKey } from './use-merged-project-prompts'
import { issueAgentPromptsQueryKey } from './use-issue-agent-prompts'
import { issueAgentProjectPromptsQueryKey } from './use-issue-agent-project-prompts'
import { agentPromptsQueryKey } from '@/features/agents/hooks/use-agent-prompts'

interface UseUpdatePromptOptions {
  projectId?: string
  onSuccess?: (prompt: AgentPrompt) => void
  onError?: (error: Error) => void
}

interface UpdatePromptParams extends UpdateAgentPromptRequest {
  id: string
}

export function useUpdatePrompt(options: UseUpdatePromptOptions) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async ({ id, ...data }: UpdatePromptParams) => {
      const result = await AgentPrompts.putApiAgentPromptsById({
        path: { id },
        body: data,
      })

      if (result.error) {
        throw result.error
      }

      return result.data
    },
    onSuccess: (data) => {
      if (options.projectId) {
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
      } else {
        // Invalidate global prompts list
        queryClient.invalidateQueries({
          queryKey: globalPromptsQueryKey(),
        })
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
