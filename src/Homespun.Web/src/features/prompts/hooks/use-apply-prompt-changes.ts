import { useMutation, useQueryClient } from '@tanstack/react-query'
import { AgentPrompts } from '@/api'
import { projectPromptsQueryKey } from './use-project-prompts'
import { mergedProjectPromptsQueryKey } from './use-merged-project-prompts'
import { globalPromptsQueryKey } from './use-global-prompts'
import { issueAgentPromptsQueryKey } from './use-issue-agent-prompts'
import { agentPromptsQueryKey } from '@/features/agents/hooks/use-agent-prompts'
import type { PromptChanges } from '../utils/prompt-diff'

interface UseApplyPromptChangesOptions {
  projectId?: string
  isGlobal?: boolean
  onSuccess?: () => void
  onError?: (error: Error) => void
}

/**
 * Hook to apply bulk changes to prompts (creates, updates, deletes).
 * Executes operations sequentially: creates first, then updates, then deletes.
 * Stops on first error.
 */
export function useApplyPromptChanges(options: UseApplyPromptChangesOptions) {
  const queryClient = useQueryClient()
  const { projectId, isGlobal, onSuccess, onError } = options

  return useMutation({
    mutationFn: async (changes: PromptChanges) => {
      // Execute creates
      for (const create of changes.creates) {
        const result = await AgentPrompts.postApiAgentPrompts({
          body: {
            name: create.name,
            initialMessage: create.initialMessage,
            mode: create.mode,
            projectId: isGlobal ? null : projectId,
          },
        })

        if (result.error) {
          throw result.error
        }
      }

      // Execute updates
      for (const update of changes.updates) {
        const result = await AgentPrompts.putApiAgentPromptsById({
          path: { id: update.id },
          body: {
            name: update.name,
            initialMessage: update.initialMessage,
            mode: update.mode,
          },
        })

        if (result.error) {
          throw result.error
        }
      }

      // Execute deletes
      for (const id of changes.deletes) {
        const result = await AgentPrompts.deleteApiAgentPromptsById({
          path: { id },
        })

        if (result.error) {
          throw result.error
        }
      }
    },
    onSuccess: () => {
      // Invalidate relevant queries
      if (isGlobal) {
        queryClient.invalidateQueries({ queryKey: globalPromptsQueryKey() })
        queryClient.invalidateQueries({ queryKey: issueAgentPromptsQueryKey })
      } else if (projectId) {
        queryClient.invalidateQueries({
          queryKey: projectPromptsQueryKey(projectId),
        })
        queryClient.invalidateQueries({
          queryKey: mergedProjectPromptsQueryKey(projectId),
        })
        queryClient.invalidateQueries({
          queryKey: agentPromptsQueryKey(projectId),
        })
      }
      onSuccess?.()
    },
    onError: (error) => {
      onError?.(error as Error)
    },
  })
}
