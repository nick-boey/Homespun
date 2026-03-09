import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Projects, type CreateProjectRequest, type Project } from '@/api'
import { useTelemetry } from '@/hooks/use-telemetry'

interface UseCreateProjectOptions {
  onSuccess?: (project: Project) => void
  onError?: (error: Error) => void
}

export function useCreateProject(options?: UseCreateProjectOptions) {
  const queryClient = useQueryClient()
  const telemetry = useTelemetry()

  return useMutation({
    mutationFn: async (data: CreateProjectRequest) => {
      const result = await Projects.postApiProjects({
        body: data,
      })

      if (result.error) {
        throw result.error
      }

      return result.data
    },
    onSuccess: (data) => {
      // Track successful project creation
      telemetry.trackEvent('project_created', {
        projectId: data?.id || '',
        projectName: data?.name || '',
        githubOwner: data?.gitHubOwner || '',
        githubRepo: data?.gitHubRepo || '',
      })

      // Invalidate projects list to refetch
      queryClient.invalidateQueries({ queryKey: ['projects'] })
      options?.onSuccess?.(data as Project)
    },
    onError: (error) => {
      // Track failed project creation
      telemetry.trackEvent('project_creation_failed', {
        error: error.message || 'Unknown error',
      })

      options?.onError?.(error as Error)
    },
  })
}
