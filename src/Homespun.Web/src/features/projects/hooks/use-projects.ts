import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { Projects } from '@/api'
import { useTelemetry } from '@/hooks/use-telemetry'

export const projectsQueryKey = ['projects'] as const

export function useProjects() {
  return useQuery({
    queryKey: projectsQueryKey,
    queryFn: async () => {
      const response = await Projects.getApiProjects()
      return response.data
    },
  })
}

export function useDeleteProject() {
  const queryClient = useQueryClient()
  const telemetry = useTelemetry()

  return useMutation({
    mutationFn: async (projectId: string) => {
      await Projects.deleteApiProjectsById({ path: { id: projectId } })
      return projectId
    },
    onSuccess: (projectId) => {
      // Track successful project deletion
      telemetry.trackEvent('project_deleted', {
        projectId,
      })

      queryClient.invalidateQueries({ queryKey: projectsQueryKey })
    },
    onError: (error: Error, projectId) => {
      // Track failed project deletion
      telemetry.trackEvent('project_deletion_failed', {
        projectId,
        error: error.message || 'Unknown error',
      })
    },
  })
}
