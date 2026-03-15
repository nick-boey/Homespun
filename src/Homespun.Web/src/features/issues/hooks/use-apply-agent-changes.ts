import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Issues, ConflictResolutionStrategy } from '@/api'
import type {
  ApplyAgentChangesRequest,
  ApplyAgentChangesResponse,
  ResolveConflictsRequest,
} from '@/api'
import { toast } from 'sonner'

export function useApplyAgentChanges(issueId: string) {
  const queryClient = useQueryClient()

  return useMutation<ApplyAgentChangesResponse, Error, ApplyAgentChangesRequest>({
    mutationFn: async (request) => {
      const response = await Issues.postApiIssuesByIssueIdApplyAgentChanges({
        path: { issueId },
        body: request,
      })

      if (response.error || !response.data) {
        throw new Error('Failed to apply changes')
      }

      return response.data
    },
    onSuccess: (data) => {
      if (data.success) {
        toast.success('Changes Applied', {
          description: data.message ?? undefined,
        })

        // Invalidate issue queries to refresh the UI
        queryClient.invalidateQueries({ queryKey: ['issues'] })
        queryClient.invalidateQueries({ queryKey: ['issue', issueId] })
      } else if (data.conflicts && data.conflicts.length > 0) {
        toast.error('Conflicts Detected', {
          description: data.message ?? undefined,
        })
      } else {
        toast.error('Apply Failed', {
          description: data.message ?? undefined,
        })
      }
    },
    onError: (error) => {
      toast.error('Error', {
        description: error.message || 'Failed to apply changes',
      })
    },
  })
}

export function usePreviewAgentChanges(issueId: string, sessionId: string, projectId: string) {
  return useQuery<ApplyAgentChangesResponse>({
    queryKey: ['agent-changes-preview', issueId, sessionId],
    queryFn: async () => {
      const response = await Issues.postApiIssuesByIssueIdApplyAgentChanges({
        path: { issueId },
        body: {
          projectId,
          sessionId,
          dryRun: true,
          conflictStrategy: ConflictResolutionStrategy.MANUAL,
        },
      })

      if (response.error || !response.data) {
        throw new Error('Failed to preview changes')
      }

      return response.data
    },
    enabled: !!issueId && !!sessionId && !!projectId,
  })
}

export function useResolveConflicts(issueId: string) {
  const queryClient = useQueryClient()

  return useMutation<ApplyAgentChangesResponse, Error, ResolveConflictsRequest>({
    mutationFn: async (request) => {
      const response = await Issues.postApiIssuesByIssueIdResolveConflicts({
        path: { issueId },
        body: request,
      })

      if (response.error || !response.data) {
        throw new Error('Failed to resolve conflicts')
      }

      return response.data
    },
    onSuccess: (data) => {
      if (data.success) {
        toast.success('Conflicts Resolved', {
          description: data.message ?? undefined,
        })

        // Invalidate issue queries to refresh the UI
        queryClient.invalidateQueries({ queryKey: ['issues'] })
        queryClient.invalidateQueries({ queryKey: ['issue', issueId] })
      } else {
        toast.error('Resolution Failed', {
          description: data.message ?? undefined,
        })
      }
    },
    onError: (error) => {
      toast.error('Error', {
        description: error.message || 'Failed to resolve conflicts',
      })
    },
  })
}
