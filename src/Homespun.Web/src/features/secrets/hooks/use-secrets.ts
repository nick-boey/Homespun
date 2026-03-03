import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { Secrets, type ProblemDetails } from '@/api'

export const secretsQueryKey = (projectId: string) => ['secrets', projectId] as const

export function useSecrets(projectId: string) {
  const query = useQuery({
    queryKey: secretsQueryKey(projectId),
    queryFn: async () => {
      const response = await Secrets.getApiProjectsByProjectIdSecrets({
        path: { projectId },
      })
      if (response.error) {
        const error = response.error as ProblemDetails
        throw new Error(error?.detail ?? 'Failed to fetch secrets')
      }
      return response.data
    },
    enabled: !!projectId,
  })

  return {
    secrets: query.data?.secrets ?? [],
    isLoading: query.isLoading,
    isSuccess: query.isSuccess,
    isError: query.isError,
    error: query.error,
    refetch: query.refetch,
  }
}

export function useCreateSecret() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async ({
      projectId,
      name,
      value,
    }: {
      projectId: string
      name: string
      value: string
    }) => {
      const response = await Secrets.postApiProjectsByProjectIdSecrets({
        path: { projectId },
        body: { name, value },
      })
      if (response.error) {
        throw new Error(response.error?.detail ?? 'Failed to create secret')
      }
    },
    onSuccess: (_data, variables) => {
      queryClient.invalidateQueries({ queryKey: secretsQueryKey(variables.projectId) })
    },
  })
}

export function useUpdateSecret() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async ({
      projectId,
      name,
      value,
    }: {
      projectId: string
      name: string
      value: string
    }) => {
      const response = await Secrets.putApiProjectsByProjectIdSecretsByName({
        path: { projectId, name },
        body: { value },
      })
      if (response.error) {
        throw new Error(response.error?.detail ?? 'Failed to update secret')
      }
    },
    onSuccess: (_data, variables) => {
      queryClient.invalidateQueries({ queryKey: secretsQueryKey(variables.projectId) })
    },
  })
}

export function useDeleteSecret() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async ({ projectId, name }: { projectId: string; name: string }) => {
      const response = await Secrets.deleteApiProjectsByProjectIdSecretsByName({
        path: { projectId, name },
      })
      if (response.error) {
        throw new Error(response.error?.detail ?? 'Failed to delete secret')
      }
    },
    onSuccess: (_data, variables) => {
      queryClient.invalidateQueries({ queryKey: secretsQueryKey(variables.projectId) })
    },
  })
}
