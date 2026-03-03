import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { Clones, type CloneInfo, type CreateCloneRequest } from '@/api'

export const clonesQueryKey = (projectId: string) => ['clones', projectId] as const

export function useClones(projectId: string) {
  return useQuery({
    queryKey: clonesQueryKey(projectId),
    queryFn: async () => {
      const response = await Clones.getApiClones({
        query: { projectId },
      })
      if (response.error) {
        throw new Error(response.error?.detail ?? 'Failed to fetch clones')
      }
      return response.data as CloneInfo[]
    },
    enabled: !!projectId,
  })
}

export function useCreateClone() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (request: CreateCloneRequest) => {
      const response = await Clones.postApiClones({
        body: request,
      })
      if (response.error) {
        throw new Error(response.error?.detail ?? 'Failed to create clone')
      }
      return response.data
    },
    onSuccess: (_data, variables) => {
      queryClient.invalidateQueries({ queryKey: clonesQueryKey(variables.projectId!) })
    },
  })
}

export function useDeleteClone() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async ({
      projectId,
      clonePath,
    }: {
      projectId: string
      clonePath: string
    }) => {
      const response = await Clones.deleteApiClones({
        query: { projectId, clonePath },
      })
      if (response.error) {
        throw new Error(response.error?.detail ?? 'Failed to delete clone')
      }
    },
    onSuccess: (_data, variables) => {
      queryClient.invalidateQueries({ queryKey: clonesQueryKey(variables.projectId) })
    },
  })
}

export function usePullClone() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async ({
      projectId: _projectId,
      clonePath,
    }: {
      projectId: string
      clonePath: string
    }) => {
      const response = await Clones.postApiClonesPull({
        query: { clonePath },
      })
      if (response.error) {
        throw new Error(response.error?.detail ?? 'Failed to pull latest changes')
      }
    },
    onSuccess: (_data, variables) => {
      queryClient.invalidateQueries({ queryKey: clonesQueryKey(variables.projectId) })
    },
  })
}

export function usePruneClones() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (projectId: string) => {
      const response = await Clones.postApiClonesPrune({
        query: { projectId },
      })
      if (response.error) {
        throw new Error(response.error?.detail ?? 'Failed to prune clones')
      }
    },
    onSuccess: (_data, projectId) => {
      queryClient.invalidateQueries({ queryKey: clonesQueryKey(projectId) })
    },
  })
}
