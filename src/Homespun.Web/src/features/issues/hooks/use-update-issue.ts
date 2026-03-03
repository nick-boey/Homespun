import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Issues, type IssueResponse, type UpdateIssueRequest } from '@/api'
import { issueQueryKey } from './use-issue'

export interface UseUpdateIssueOptions {
  onSuccess?: (data: IssueResponse) => void
  onError?: (error: Error) => void
}

export interface UpdateIssueParams {
  issueId: string
  data: UpdateIssueRequest
}

/**
 * Hook for updating an issue.
 *
 * @param options - Optional callbacks for success/error
 * @returns Mutation object for updating issues
 */
export function useUpdateIssue(options?: UseUpdateIssueOptions) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async ({ issueId, data }: UpdateIssueParams) => {
      const response = await Issues.putApiIssuesByIssueId({
        path: { issueId },
        body: data,
      })

      if (response.error || !response.data) {
        throw new Error(response.error?.detail ?? 'Failed to update issue')
      }

      return response.data
    },
    onSuccess: (data) => {
      // Invalidate issue cache
      if (data.id) {
        queryClient.invalidateQueries({
          queryKey: issueQueryKey(data.id, data.id),
        })
      }
      // Invalidate task graph to reflect changes
      queryClient.invalidateQueries({
        queryKey: ['taskGraph'],
      })
      options?.onSuccess?.(data as IssueResponse)
    },
    onError: (error) => {
      options?.onError?.(error as Error)
    },
  })
}
