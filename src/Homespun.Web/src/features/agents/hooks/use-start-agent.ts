import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Sessions } from '@/api'
import type { ClaudeSession, CreateSessionRequest, SessionMode } from '@/api/generated/types.gen'
import { invalidateAllSessionsQueries } from '@/features/sessions/hooks/use-sessions'
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
 *
 * The outbound `POST /api/sessions` is captured by the OTel fetch
 * auto-instrumentation, so the full request/response span — plus any server
 * spans downstream — lands in Seq without an explicit trackEvent call.
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

      // Invalidate all session queries to refresh all session displays
      invalidateAllSessionsQueries(queryClient)
    },
  })
}
