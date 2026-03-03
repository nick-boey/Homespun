import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { Secrets } from '@/api'
import type { SecretInfo, CreateSecretRequest } from '@/api/generated/types.gen'

export const secretsQueryKey = (projectId: string) => ['secrets', projectId] as const

/**
 * Hook to fetch secrets for a project.
 * Note: Only secret names are returned, never the values.
 */
export function useSecrets(projectId: string) {
  const query = useQuery({
    queryKey: secretsQueryKey(projectId),
    queryFn: async (): Promise<SecretInfo[]> => {
      const response = await Secrets.getApiProjectsByProjectIdSecrets({
        path: { projectId },
      })
      return response.data?.secrets ?? []
    },
    enabled: !!projectId,
  })

  return {
    secrets: query.data ?? [],
    isLoading: query.isLoading,
    isError: query.isError,
    error: query.error,
    refetch: query.refetch,
  }
}

/**
 * Hook to create a new secret.
 */
export function useCreateSecret(projectId: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (request: CreateSecretRequest): Promise<void> => {
      await Secrets.postApiProjectsByProjectIdSecrets({
        path: { projectId },
        body: request,
      })
    },
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: secretsQueryKey(projectId),
      })
    },
  })
}

/**
 * Hook to update an existing secret.
 */
export function useUpdateSecret(projectId: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async ({ name, value }: { name: string; value: string }): Promise<void> => {
      await Secrets.putApiProjectsByProjectIdSecretsByName({
        path: { projectId, name },
        body: { value },
      })
    },
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: secretsQueryKey(projectId),
      })
    },
  })
}

/**
 * Hook to delete a secret.
 */
export function useDeleteSecret(projectId: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (name: string): Promise<void> => {
      await Secrets.deleteApiProjectsByProjectIdSecretsByName({
        path: { projectId, name },
      })
    },
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: secretsQueryKey(projectId),
      })
    },
  })
}
