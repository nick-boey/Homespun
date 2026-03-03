import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { AgentPrompts } from '@/api'
import type {
  AgentPrompt,
  CreateAgentPromptRequest,
  UpdateAgentPromptRequest,
} from '@/api/generated/types.gen'

export const projectPromptsQueryKey = (projectId: string) => ['project-prompts', projectId] as const
export const availableGlobalPromptsQueryKey = (projectId: string) =>
  ['available-global-prompts', projectId] as const

/**
 * Hook to fetch project-specific prompts (not including global ones).
 */
export function useProjectPrompts(projectId: string) {
  const query = useQuery({
    queryKey: projectPromptsQueryKey(projectId),
    queryFn: async (): Promise<AgentPrompt[]> => {
      const response = await AgentPrompts.getApiAgentPromptsProjectByProjectId({
        path: { projectId },
      })
      return response.data as AgentPrompt[]
    },
    enabled: !!projectId,
  })

  return {
    prompts: query.data ?? [],
    isLoading: query.isLoading,
    isError: query.isError,
    error: query.error,
    refetch: query.refetch,
  }
}

/**
 * Hook to fetch global prompts that are not overridden by project-specific prompts.
 */
export function useAvailableGlobalPrompts(projectId: string) {
  const query = useQuery({
    queryKey: availableGlobalPromptsQueryKey(projectId),
    queryFn: async (): Promise<AgentPrompt[]> => {
      const response = await AgentPrompts.getApiAgentPromptsAvailableForProjectByProjectId({
        path: { projectId },
      })
      // Filter to only global prompts (those without projectId)
      const prompts = response.data as AgentPrompt[]
      return prompts.filter((p) => !p.projectId)
    },
    enabled: !!projectId,
  })

  return {
    globalPrompts: query.data ?? [],
    isLoading: query.isLoading,
    isError: query.isError,
    error: query.error,
  }
}

/**
 * Hook to create a new prompt.
 */
export function useCreatePrompt() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (request: CreateAgentPromptRequest): Promise<AgentPrompt> => {
      const response = await AgentPrompts.postApiAgentPrompts({
        body: request,
      })
      return response.data as AgentPrompt
    },
    onSuccess: (_, variables) => {
      // Invalidate relevant queries
      if (variables.projectId) {
        queryClient.invalidateQueries({
          queryKey: projectPromptsQueryKey(variables.projectId),
        })
        queryClient.invalidateQueries({
          queryKey: ['agent-prompts', variables.projectId],
        })
      } else {
        // Global prompt, invalidate all project queries
        queryClient.invalidateQueries({ queryKey: ['project-prompts'] })
        queryClient.invalidateQueries({ queryKey: ['available-global-prompts'] })
        queryClient.invalidateQueries({ queryKey: ['agent-prompts'] })
      }
    },
  })
}

/**
 * Hook to update an existing prompt.
 */
export function useUpdatePrompt() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async ({
      id,
      request,
    }: {
      id: string
      request: UpdateAgentPromptRequest
      projectId?: string
    }): Promise<AgentPrompt> => {
      const response = await AgentPrompts.putApiAgentPromptsById({
        path: { id },
        body: request,
      })
      return response.data as AgentPrompt
    },
    onSuccess: (_, variables) => {
      if (variables.projectId) {
        queryClient.invalidateQueries({
          queryKey: projectPromptsQueryKey(variables.projectId),
        })
        queryClient.invalidateQueries({
          queryKey: ['agent-prompts', variables.projectId],
        })
      }
      // Always invalidate global queries in case a global prompt was updated
      queryClient.invalidateQueries({ queryKey: ['project-prompts'] })
      queryClient.invalidateQueries({ queryKey: ['available-global-prompts'] })
      queryClient.invalidateQueries({ queryKey: ['agent-prompts'] })
    },
  })
}

/**
 * Hook to delete a prompt.
 */
export function useDeletePrompt() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async ({ id }: { id: string; projectId?: string }): Promise<void> => {
      await AgentPrompts.deleteApiAgentPromptsById({
        path: { id },
      })
    },
    onSuccess: (_, variables) => {
      if (variables.projectId) {
        queryClient.invalidateQueries({
          queryKey: projectPromptsQueryKey(variables.projectId),
        })
        queryClient.invalidateQueries({
          queryKey: ['agent-prompts', variables.projectId],
        })
      }
      // Always invalidate global queries
      queryClient.invalidateQueries({ queryKey: ['project-prompts'] })
      queryClient.invalidateQueries({ queryKey: ['available-global-prompts'] })
      queryClient.invalidateQueries({ queryKey: ['agent-prompts'] })
    },
  })
}
