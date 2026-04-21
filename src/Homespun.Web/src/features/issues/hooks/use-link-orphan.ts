import { useMutation, useQueryClient } from '@tanstack/react-query'
import { ChangeSnapshot } from '@/api'
import { taskGraphQueryKey } from './use-task-graph'

export interface LinkOrphanOccurrence {
  /** `null` for main-branch, otherwise the branch name. */
  branch: string | null
  changeName: string
}

export interface LinkOrphanParams {
  projectId: string
  /**
   * All clones that carry the change directory. One POST is emitted per
   * occurrence; `Promise.all` waits for every call before the task-graph
   * cache is invalidated.
   */
  occurrences: LinkOrphanOccurrence[]
  fleeceId: string
}

/**
 * Writes a `.homespun.yaml` sidecar linking an orphan change to a Fleece
 * issue in every clone that carries the change directory. Partial failure
 * surfaces as a single rejection; already-written sidecars remain in place.
 */
export function useLinkOrphan() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (params: LinkOrphanParams) => {
      await Promise.all(
        params.occurrences.map(async (occurrence) => {
          const response = await ChangeSnapshot.postApiOpenspecChangesLink({
            body: {
              projectId: params.projectId,
              branch: occurrence.branch,
              changeName: occurrence.changeName,
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
        })
      )
      return params
    },
    onSuccess: (params) => {
      queryClient.invalidateQueries({ queryKey: taskGraphQueryKey(params.projectId) })
    },
  })
}
