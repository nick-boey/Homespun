import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { Projects } from '@/api/generated/sdk.gen'
import type { Project, CreateProjectRequest } from '@/api/generated/types.gen'

export const projectKeys = {
  all: ['projects'] as const,
  lists: () => [...projectKeys.all, 'list'] as const,
  list: () => [...projectKeys.lists()] as const,
  details: () => [...projectKeys.all, 'detail'] as const,
  detail: (id: string) => [...projectKeys.details(), id] as const,
}

export function useProjects() {
  return useQuery({
    queryKey: projectKeys.list(),
    queryFn: async () => {
      const response = await Projects.getApiProjects()
      return response.data as Project[]
    },
  })
}

export function useProject(projectId: string | undefined) {
  return useQuery({
    queryKey: projectKeys.detail(projectId ?? ''),
    queryFn: async () => {
      const response = await Projects.getApiProjectsById({
        path: { id: projectId! },
      })
      return response.data as Project
    },
    enabled: !!projectId,
  })
}

export function useCreateProject() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (request: CreateProjectRequest) => {
      const response = await Projects.postApiProjects({
        body: request,
      })
      return response.data as Project
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: projectKeys.lists() })
    },
  })
}

export function useDeleteProject() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (projectId: string) => {
      const response = await Projects.deleteApiProjectsById({
        path: { id: projectId },
      })
      return response.data
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: projectKeys.lists() })
    },
  })
}
