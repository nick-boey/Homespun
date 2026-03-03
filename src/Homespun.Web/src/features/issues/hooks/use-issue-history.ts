import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { Issues, type IssueHistoryState } from '@/api'

export function useIssueHistory(projectId: string) {
  const queryClient = useQueryClient()
  const queryKey = ['issueHistory', projectId]

  const historyQuery = useQuery({
    queryKey,
    queryFn: async () => {
      const result = await Issues.getApiProjectsByProjectIdIssuesHistoryState({
        path: { projectId },
      })
      if (result.error) {
        throw new Error(result.error.detail ?? 'Failed to fetch history state')
      }
      return result.data
    },
    enabled: !!projectId,
  })

  const undoMutation = useMutation({
    mutationFn: async () => {
      const result = await Issues.postApiProjectsByProjectIdIssuesHistoryUndo({
        path: { projectId },
      })
      if (result.error) {
        throw new Error(result.error.detail ?? 'Failed to undo')
      }
      return result.data
    },
    onSuccess: (data) => {
      if (data?.state) {
        queryClient.setQueryData<IssueHistoryState>(queryKey, data.state)
      }
      queryClient.invalidateQueries({ queryKey: ['issues', projectId] })
      queryClient.invalidateQueries({ queryKey: ['taskGraph', projectId] })
    },
  })

  const redoMutation = useMutation({
    mutationFn: async () => {
      const result = await Issues.postApiProjectsByProjectIdIssuesHistoryRedo({
        path: { projectId },
      })
      if (result.error) {
        throw new Error(result.error.detail ?? 'Failed to redo')
      }
      return result.data
    },
    onSuccess: (data) => {
      if (data?.state) {
        queryClient.setQueryData<IssueHistoryState>(queryKey, data.state)
      }
      queryClient.invalidateQueries({ queryKey: ['issues', projectId] })
      queryClient.invalidateQueries({ queryKey: ['taskGraph', projectId] })
    },
  })

  const historyState = historyQuery.data

  return {
    historyState,
    isLoading: historyQuery.isLoading,
    isError: historyQuery.isError,
    error: historyQuery.error,
    canUndo: historyState?.canUndo ?? false,
    canRedo: historyState?.canRedo ?? false,
    undoDescription: historyState?.undoDescription ?? null,
    redoDescription: historyState?.redoDescription ?? null,
    undo: undoMutation.mutate,
    redo: redoMutation.mutate,
    isUndoing: undoMutation.isPending,
    isRedoing: redoMutation.isPending,
    refetch: historyQuery.refetch,
  }
}
