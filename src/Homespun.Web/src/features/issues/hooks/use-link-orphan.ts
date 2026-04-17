import { useMutation, useQueryClient } from '@tanstack/react-query'
import { ChangeSnapshot } from '@/api'
import { taskGraphQueryKey } from './use-task-graph'

export interface LinkOrphanParams {
  projectId: string
  /** Leave omitted for main-branch orphans. */
  branch?: string | null
  changeName: string
  fleeceId: string
}

/**
 * Writes a `.homespun.yaml` sidecar linking an orphan change to a Fleece
 * issue. The next graph load reflects the link (cache is invalidated
 * server-side for branch orphans).
 */
export function useLinkOrphan() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (params: LinkOrphanParams) => {
      const response = await ChangeSnapshot.postApiOpenspecChangesLink({
        body: {
          projectId: params.projectId,
          branch: params.branch ?? null,
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
