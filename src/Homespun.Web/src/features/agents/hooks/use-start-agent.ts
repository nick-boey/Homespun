import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Sessions } from '@/api'
import type { ClaudeSession, CreateSessionRequest, SessionMode } from '@/api/generated/types.gen'
import { sessionsQueryKey } from '@/features/sessions/hooks/use-sessions'

export interface StartAgentParams {
  entityId: string
  projectId: string
  mode?: SessionMode
  model?: string
  workingDirectory?: string
  systemPrompt?: string
}

/**
 * Hook to start a new agent session for an issue or PR.
 */
export function useStartAgent() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (params: StartAgentParams): Promise<ClaudeSession> => {
      const request: CreateSessionRequest = {
        entityId: params.entityId,
        projectId: params.projectId,
        mode: params.mode,
        model: params.model,
        workingDirectory: params.workingDirectory,
        systemPrompt: params.systemPrompt,
      }

      const response = await Sessions.postApiSessions({
        body: request,
      })

      return response.data as ClaudeSession
    },
    onSuccess: () => {
      // Invalidate sessions query to refresh the list
      queryClient.invalidateQueries({ queryKey: sessionsQueryKey })
    },
  })
}
