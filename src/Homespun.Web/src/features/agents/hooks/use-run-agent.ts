import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Issues } from '@/api'
import type { RunAgentResponse } from '@/api/generated/types.gen'
import { sessionsQueryKey } from '@/features/sessions/hooks/use-sessions'

export interface RunAgentParams {
  /** The issue ID to run the agent on */
  issueId: string
  /** The project ID */
  projectId: string
  /** The agent prompt ID to use */
  promptId: string
  /** The Claude model to use (e.g., "claude-sonnet-4-20250514") */
  model?: string
  /** Base branch to create the working branch from */
  baseBranch?: string
}

export interface RunAgentResult {
  /** The created session ID */
  sessionId: string
  /** The branch name used for the session */
  branchName: string
  /** The clone path where the agent is working */
  clonePath: string
}

/**
 * Hook to run an agent on an issue using the server-side endpoint.
 *
 * This endpoint handles the complete agent startup flow:
 * 1. Fetches issue data
 * 2. Resolves or creates the working branch/clone
 * 3. Fetches the prompt and renders the template with issue context
 * 4. Creates the session and sends the initial message
 */
export function useRunAgent() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (params: RunAgentParams): Promise<RunAgentResult> => {
      const response = await Issues.postApiIssuesByIssueIdRun({
        path: { issueId: params.issueId },
        body: {
          projectId: params.projectId,
          promptId: params.promptId,
          model: params.model,
          baseBranch: params.baseBranch,
        },
      })

      if (response.error || !response.data) {
        throw new Error(response.error?.detail ?? 'Failed to run agent')
      }

      const data = response.data as RunAgentResponse

      return {
        sessionId: data.sessionId ?? '',
        branchName: data.branchName ?? '',
        clonePath: data.clonePath ?? '',
      }
    },
    onSuccess: () => {
      // Invalidate sessions query to refresh the list
      queryClient.invalidateQueries({ queryKey: sessionsQueryKey })
    },
  })
}
