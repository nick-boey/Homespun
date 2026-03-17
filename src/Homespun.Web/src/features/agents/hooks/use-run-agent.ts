import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Issues, ClaudeSessionStatus } from '@/api'
import type { RunAgentResponse } from '@/api/generated/types.gen'
import { invalidateAllSessionsQueries } from '@/features/sessions/hooks/use-sessions'

export interface RunAgentParams {
  /** The issue ID to run the agent on */
  issueId: string
  /** The project ID */
  projectId: string
  /** The agent prompt ID to use, null for None */
  promptId: string | null
  /** The Claude model to use (e.g., "sonnet") */
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

/** Error type for when an agent is already running on the issue */
export interface AgentConflictError extends Error {
  name: 'AgentConflictError'
  sessionId: string
  status: ClaudeSessionStatus
}

/** Create an AgentConflictError */
function createAgentConflictError(
  sessionId: string,
  status: ClaudeSessionStatus
): AgentConflictError {
  const error = new Error('An agent is already running on this issue') as AgentConflictError
  error.name = 'AgentConflictError'
  error.sessionId = sessionId
  error.status = status
  return error
}

/** Type guard to check if an error is an AgentConflictError */
export function isAgentConflictError(error: unknown): error is AgentConflictError {
  return (
    error !== null &&
    typeof error === 'object' &&
    'name' in error &&
    error.name === 'AgentConflictError' &&
    'sessionId' in error &&
    'status' in error
  )
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

      // Check for 409 Conflict (agent already running)
      // The response object contains the raw Response, so we check the status
      if (response.response?.status === 409 && response.error) {
        // Extract conflict data from the error response
        const conflictData = response.error as unknown as {
          sessionId?: string
          status?: ClaudeSessionStatus
          message?: string
        }
        throw createAgentConflictError(
          conflictData.sessionId ?? '',
          conflictData.status ?? ClaudeSessionStatus.RUNNING
        )
      }

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
      // Invalidate all session queries to refresh all session displays
      invalidateAllSessionsQueries(queryClient)
    },
  })
}
