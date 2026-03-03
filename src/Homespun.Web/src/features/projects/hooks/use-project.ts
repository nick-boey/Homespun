import { useQuery } from '@tanstack/react-query'
import { Projects, type Project } from '@/api'

export function useProject(projectId: string) {
  const query = useQuery({
    queryKey: ['project', projectId],
    queryFn: async () => {
      const result = await Projects.getApiProjectsById({
        path: { id: projectId },
      })

      if (result.error || !result.data) {
        throw new Error(result.error?.detail ?? 'Project not found')
      }

      return result.data
    },
    enabled: !!projectId,
  })

  return {
    project: query.data as Project | undefined,
    isLoading: query.isLoading,
    isError: query.isError,
    error: query.error,
    refetch: query.refetch,
  }
}
