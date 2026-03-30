import { useMutation, useQueryClient } from '@tanstack/react-query'
import { AgentPrompts } from '@/api'
import type { AgentPrompt } from '@/api/generated/types.gen'
import { projectPromptsQueryKey } from './use-project-prompts'
import { mergedProjectPromptsQueryKey } from './use-merged-project-prompts'
import { globalPromptsQueryKey } from './use-global-prompts'
import { issueAgentPromptsQueryKey } from './use-issue-agent-prompts'
import { issueAgentProjectPromptsQueryKey } from './use-issue-agent-project-prompts'
import { agentPromptsQueryKey } from '@/features/agents/hooks/use-agent-prompts'
import type { PromptChanges } from '../utils/prompt-diff'

interface UseApplyPromptChangesOptions {
  projectId?: string
  isGlobal?: boolean
  onSuccess?: () => void
  onError?: (error: Error) => void
}

interface ApplyPromptChangesParams {
  changes: PromptChanges
  /** Current prompts used to resolve names to IDs for API calls */
  currentPrompts: AgentPrompt[]
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
    mutationFn: async ({ changes, currentPrompts }: ApplyPromptChangesParams) => {
      // Build a name→id lookup from current prompts
      const nameToId = new Map<string, string>()
      for (const p of currentPrompts) {
        if (p.name && p.id) {
          nameToId.set(p.name, p.id)
        }
      }

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

      // Execute updates (resolve name → id for API)
      for (const update of changes.updates) {
        const id = update.name ? nameToId.get(update.name) : undefined
        if (!id) continue

        const result = await AgentPrompts.putApiAgentPromptsById({
          path: { id },
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

      // Execute deletes (resolve name → id for API)
      for (const name of changes.deletes) {
        const id = nameToId.get(name)
        if (!id) continue

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
        queryClient.invalidateQueries({
          queryKey: issueAgentProjectPromptsQueryKey(projectId),
        })
      }
      onSuccess?.()
    },
    onError: (error) => {
      onError?.(error as Error)
    },
  })
}
