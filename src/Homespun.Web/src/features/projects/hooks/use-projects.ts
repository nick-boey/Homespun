import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { Projects } from '@/api'

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

  return useMutation({
    mutationFn: async (projectId: string) => {
      await Projects.deleteApiProjectsById({ path: { id: projectId } })
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: projectsQueryKey })
    },
  })
}
