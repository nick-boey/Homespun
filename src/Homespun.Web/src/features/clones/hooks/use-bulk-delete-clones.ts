import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Clones, type BulkDeleteClonesResponse } from '@/api'
import { enrichedClonesQueryKey } from './use-enriched-clones'

export function useBulkDeleteClones() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async ({ projectId, clonePaths }: { projectId: string; clonePaths: string[] }) => {
      const response = await Clones.deleteApiClonesBulk({
        query: { projectId },
        body: { clonePaths },
      })
      if (response.error) {
        throw new Error(response.error?.detail ?? 'Failed to bulk delete clones')
      }
      return response.data as BulkDeleteClonesResponse
    },
    onSuccess: (_, { projectId }) => {
      // Invalidate enriched clones query
      queryClient.invalidateQueries({
        queryKey: enrichedClonesQueryKey(projectId),
      })
      // Also invalidate regular clones query used by branches tab
      queryClient.invalidateQueries({
        queryKey: ['clones', projectId],
      })
    },
  })
}
