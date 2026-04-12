import { useQuery } from '@tanstack/react-query'
import { ProjectClones, type EnrichedCloneInfo } from '@/api'

export const enrichedClonesQueryKey = (projectId: string) =>
  ['clones', 'enriched', projectId] as const

export function useEnrichedClones(projectId: string) {
  return useQuery({
    queryKey: enrichedClonesQueryKey(projectId),
    queryFn: async () => {
      const response = await ProjectClones.getApiProjectsByProjectIdClonesEnriched({
        path: { projectId },
      })
      if (response.error) {
        throw new Error(response.error?.detail ?? 'Failed to fetch enriched clones')
      }
      return response.data as EnrichedCloneInfo[]
    },
    enabled: !!projectId,
  })
}
