import { useQuery } from '@tanstack/react-query'
import { Skills } from '@/api'
import type { DiscoveredSkills } from '@/api/generated/types.gen'

export const projectSkillsQueryKey = (projectId: string) => ['project-skills', projectId] as const

/**
 * Fetch the set of skills available to dispatch for a project. Returns
 * {openSpec, homespun, general} categorised by how the server identified them.
 */
export function useProjectSkills(projectId: string) {
  return useQuery({
    queryKey: projectSkillsQueryKey(projectId),
    queryFn: async (): Promise<DiscoveredSkills> => {
      const response = await Skills.getApiSkillsProjectByProjectId({
        path: { projectId },
      })
      return (response.data as DiscoveredSkills) ?? { openSpec: [], homespun: [], general: [] }
    },
    enabled: !!projectId,
  })
}
