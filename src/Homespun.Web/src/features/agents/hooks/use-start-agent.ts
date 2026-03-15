import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Sessions } from '@/api'
import type { ClaudeSession, CreateSessionRequest, SessionMode } from '@/api/generated/types.gen'
import { sessionsQueryKey } from '@/features/sessions/hooks/use-sessions'
import { useTelemetry } from '@/hooks/use-telemetry'
import { useSessionSettingsStore, type ModelSelection } from '@/stores/session-settings-store'
import { fromApiSessionMode } from '@/lib/utils/session-mode'

export interface StartAgentParams {
  entityId: string
  projectId: string
  mode?: SessionMode
  model?: string
  workingDirectory?: string
  systemPrompt?: string
  /** Initial message to send immediately after session creation to start agent work */
  initialMessage?: string
}

/**
 * Hook to start a new agent session for an issue or PR.
 */
export function useStartAgent() {
  const queryClient = useQueryClient()
  const telemetry = useTelemetry()

  return useMutation({
    mutationFn: async (params: StartAgentParams): Promise<ClaudeSession> => {
      const request: CreateSessionRequest = {
        entityId: params.entityId,
        projectId: params.projectId,
        mode: params.mode,
        model: params.model,
        workingDirectory: params.workingDirectory,
        systemPrompt: params.systemPrompt,
        initialMessage: params.initialMessage,
      }

      const response = await Sessions.postApiSessions({
        body: request,
      })

      return response.data as ClaudeSession
    },
    onSuccess: (session, params) => {
      // Cache initial mode/model for this session for immediate display
      if (session.id) {
        const mode = params.mode !== undefined ? fromApiSessionMode(params.mode) : 'build'
        useSessionSettingsStore
          .getState()
          .initSession(session.id, mode, (params.model ?? 'opus') as ModelSelection)
      }

      // Track successful agent launch
      telemetry.trackEvent('agent_launched', {
        sessionId: session.id || '',
        entityId: params.entityId,
        projectId: params.projectId,
        mode: (params.mode ?? 'build').toString(),
        model: params.model || '',
        hasInitialMessage: params.initialMessage ? 'true' : 'false',
      })

      // Invalidate sessions query to refresh the list
      queryClient.invalidateQueries({ queryKey: sessionsQueryKey })
    },
    onError: (error: Error, params) => {
      // Track failed agent launch
      telemetry.trackEvent('agent_launch_failed', {
        entityId: params.entityId,
        projectId: params.projectId,
        mode: (params.mode ?? 'build').toString(),
        error: error.message || 'Unknown error',
      })
    },
  })
}
