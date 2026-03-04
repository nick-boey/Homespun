import { useMutation } from '@tanstack/react-query'
import { Orchestration } from '@/api'

export interface GenerateBranchIdResult {
  branchId: string
  wasAiGenerated: boolean
}

/**
 * Hook to generate a branch ID suggestion from a title using AI.
 */
export function useGenerateBranchId() {
  return useMutation({
    mutationFn: async (title: string): Promise<GenerateBranchIdResult> => {
      const result = await Orchestration.postApiOrchestrationGenerateBranchId({
        body: { title },
      })

      if (result.error) {
        throw new Error(result.error.detail ?? 'Failed to generate branch ID')
      }

      if (!result.data?.success || !result.data.branchId) {
        throw new Error(result.data?.error ?? 'Failed to generate branch ID')
      }

      return {
        branchId: result.data.branchId,
        wasAiGenerated: result.data.wasAiGenerated ?? false,
      }
    },
  })
}
