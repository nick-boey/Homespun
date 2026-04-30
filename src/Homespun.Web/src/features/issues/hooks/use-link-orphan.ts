import { useMutation, useQueryClient } from '@tanstack/react-query'
import { ChangeSnapshot } from '@/api'
import { taskGraphQueryKey } from './use-task-graph'

export interface LinkOrphanParams {
  projectId: string
  changeName: string
  fleeceId: string
}

/**
 * Writes a `.homespun.yaml` sidecar linking an orphan change to a Fleece issue.
 * Emits one branchless `POST /api/openspec/changes/link`; the server discovers
 * every clone carrying the change directory and writes every sidecar within
 * the request, so callers do not fan out per occurrence.
 */
export function useLinkOrphan() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (params: LinkOrphanParams) => {
      const response = await ChangeSnapshot.postApiOpenspecChangesLink({
        body: {
          projectId: params.projectId,
          changeName: params.changeName,
          fleeceId: params.fleeceId,
        },
      })
      if (response.error) {
        const detail =
          typeof response.error === 'object' &&
          response.error !== null &&
          'detail' in response.error &&
          typeof (response.error as { detail: unknown }).detail === 'string'
            ? (response.error as { detail: string }).detail
            : 'Failed to link orphan change'
        throw new Error(detail)
      }
      return params
    },
    onSuccess: (params) => {
      queryClient.invalidateQueries({ queryKey: taskGraphQueryKey(params.projectId) })
    },
  })
}
