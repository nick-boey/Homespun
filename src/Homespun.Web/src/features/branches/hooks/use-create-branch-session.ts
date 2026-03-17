import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Clones, type CreateBranchSessionRequest } from '@/api'
import { clonesQueryKey } from './use-clones'
import { sessionsQueryKey } from '@/features/sessions'

export interface CreateBranchSessionParams {
  projectId: string
  branchName: string
  baseBranch?: string
}

export interface CreateBranchSessionResult {
  sessionId: string
  branchName: string
  clonePath: string
}

export function useCreateBranchSession() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (params: CreateBranchSessionParams): Promise<CreateBranchSessionResult> => {
      const request: CreateBranchSessionRequest = {
        projectId: params.projectId,
        branchName: params.branchName,
        baseBranch: params.baseBranch,
      }

      const response = await Clones.postApiClonesSession({
        body: request,
      })

      if (response.error) {
        throw new Error(response.error?.detail ?? 'Failed to create branch session')
      }

      return {
        sessionId: response.data?.sessionId ?? '',
        branchName: response.data?.branchName ?? '',
        clonePath: response.data?.clonePath ?? '',
      }
    },
    onSuccess: (_data, variables) => {
      // Invalidate both clones and sessions queries
      queryClient.invalidateQueries({ queryKey: clonesQueryKey(variables.projectId) })
      queryClient.invalidateQueries({ queryKey: sessionsQueryKey })
    },
  })
}
