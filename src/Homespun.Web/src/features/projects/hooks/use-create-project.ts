import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Projects, type CreateProjectRequest, type Project } from '@/api'

interface UseCreateProjectOptions {
  onSuccess?: (project: Project) => void
  onError?: (error: Error) => void
}

export function useCreateProject(options?: UseCreateProjectOptions) {
  const queryClient = useQueryClient()

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
      // Invalidate projects list to refetch
      queryClient.invalidateQueries({ queryKey: ['projects'] })
      options?.onSuccess?.(data as Project)
    },
    onError: (error) => {
      options?.onError?.(error as Error)
    },
  })
}
