import { useMutation, useQueryClient } from '@tanstack/react-query'
import { IssuesAgent } from '@/api'
import { sessionsQueryKey } from '@/features/sessions'

export interface CreateIssuesAgentSessionParams {
  projectId: string
  model?: string
  /** Optional selected issue ID to focus on */
  selectedIssueId?: string
  /** Optional user instructions for the agent */
  userInstructions?: string
  /** Optional prompt name for the agent session */
  promptName?: string | null
}

export interface CreateIssuesAgentSessionResult {
  sessionId: string
  branchName: string
  clonePath: string
}

export function useCreateIssuesAgentSession() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (
      params: CreateIssuesAgentSessionParams
    ): Promise<CreateIssuesAgentSessionResult> => {
      const response = await IssuesAgent.postApiIssuesAgentSession({
        body: {
          projectId: params.projectId,
          model: params.model,
          selectedIssueId: params.selectedIssueId,
          userInstructions: params.userInstructions,
          promptName: params.promptName,
        },
      })

      if (response.error) {
        throw new Error(response.error?.detail ?? 'Failed to create Issues Agent session')
      }

      return {
        sessionId: response.data?.sessionId ?? '',
        branchName: response.data?.branchName ?? '',
        clonePath: response.data?.clonePath ?? '',
      }
    },
    onSuccess: () => {
      // Invalidate sessions query to show the new session
      queryClient.invalidateQueries({ queryKey: sessionsQueryKey })
    },
  })
}
