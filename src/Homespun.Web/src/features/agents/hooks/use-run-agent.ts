import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Issues, ClaudeSessionStatus, SessionMode } from '@/api'
import type { RunAgentAcceptedResponse } from '@/api/generated/types.gen'
import { invalidateAllSessionsQueries } from '@/features/sessions/hooks/use-sessions'

export interface RunAgentParams {
  /** The issue ID to run the agent on */
  issueId: string
  /** The project ID */
  projectId: string
  /** The session mode to use */
  mode?: SessionMode
  /** The Claude model to use (e.g., "sonnet") */
  model?: string
  /** Base branch to create the working branch from */
  baseBranch?: string
  /** Optional user instructions to send as the initial message */
  userInstructions?: string
}

export interface RunAgentResult {
  /** The issue ID the agent is starting on */
  issueId: string
  /** The branch name used for the session */
  branchName: string
  /** A human-readable message about the status */
  message: string
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
 * This endpoint returns 202 Accepted immediately and handles agent startup in the background:
 * 1. Validates project, issue, and prompt
 * 2. Queues background work to create clone and start session
 * 3. SignalR notifications inform when agent is ready or fails
 */
export function useRunAgent() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (params: RunAgentParams): Promise<RunAgentResult> => {
      const response = await Issues.postApiIssuesByIssueIdRun({
        path: { issueId: params.issueId },
        body: {
          projectId: params.projectId,
          mode: params.mode,
          model: params.model,
          baseBranch: params.baseBranch,
          userInstructions: params.userInstructions,
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
        // Extract error message - detail is only on ProblemDetails type
        const errorMessage =
          response.error && 'detail' in response.error && typeof response.error.detail === 'string'
            ? response.error.detail
            : 'Failed to run agent'
        throw new Error(errorMessage)
      }

      const data = response.data as RunAgentAcceptedResponse

      return {
        issueId: data.issueId ?? '',
        branchName: data.branchName ?? '',
        message: data.message ?? 'Agent is starting',
      }
    },
    onSuccess: () => {
      // Invalidate all session queries to refresh session displays when agent starts
      invalidateAllSessionsQueries(queryClient)
    },
  })
}
