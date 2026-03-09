import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { postApiIssuesApplyAgentChanges, postApiIssuesResolveConflicts } from '@/api'
import type {
  ApplyAgentChangesRequest,
  ApplyAgentChangesResponse,
  ResolveConflictsRequest,
} from '@/api/generated'
import { toast } from '@/hooks/use-toast'

export function useApplyAgentChanges(issueId: string) {
  const queryClient = useQueryClient()

  return useMutation<ApplyAgentChangesResponse, Error, ApplyAgentChangesRequest>({
    mutationFn: async (request) => {
      const response = await postApiIssuesApplyAgentChanges(issueId, {
        body: request,
      })

      if (!response.ok) {
        throw new Error('Failed to apply changes')
      }

      return response.data
    },
    onSuccess: (data) => {
      if (data.success) {
        toast({
          title: 'Changes Applied',
          description: data.message,
        })

        // Invalidate issue queries to refresh the UI
        queryClient.invalidateQueries({ queryKey: ['issues'] })
        queryClient.invalidateQueries({ queryKey: ['issue', issueId] })
      } else if (data.conflicts && data.conflicts.length > 0) {
        toast({
          title: 'Conflicts Detected',
          description: data.message,
          variant: 'destructive',
        })
      } else {
        toast({
          title: 'Apply Failed',
          description: data.message,
          variant: 'destructive',
        })
      }
    },
    onError: (error) => {
      toast({
        title: 'Error',
        description: error.message || 'Failed to apply changes',
        variant: 'destructive',
      })
    },
  })
}

export function usePreviewAgentChanges(issueId: string, sessionId: string, projectId: string) {
  return useQuery<ApplyAgentChangesResponse>({
    queryKey: ['agent-changes-preview', issueId, sessionId],
    queryFn: async () => {
      const response = await postApiIssuesApplyAgentChanges(issueId, {
        body: {
          projectId,
          sessionId,
          dryRun: true,
          conflictStrategy: 'Manual',
        },
      })

      if (!response.ok) {
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
      const response = await postApiIssuesResolveConflicts(issueId, {
        body: request,
      })

      if (!response.ok) {
        throw new Error('Failed to resolve conflicts')
      }

      return response.data
    },
    onSuccess: (data) => {
      if (data.success) {
        toast({
          title: 'Conflicts Resolved',
          description: data.message,
        })

        // Invalidate issue queries to refresh the UI
        queryClient.invalidateQueries({ queryKey: ['issues'] })
        queryClient.invalidateQueries({ queryKey: ['issue', issueId] })
      } else {
        toast({
          title: 'Resolution Failed',
          description: data.message,
          variant: 'destructive',
        })
      }
    },
    onError: (error) => {
      toast({
        title: 'Error',
        description: error.message || 'Failed to resolve conflicts',
        variant: 'destructive',
      })
    },
  })
}