import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { Settings, type UserSettingsResponse, type UpdateUserEmailRequest } from '@/api'

export const USER_SETTINGS_QUERY_KEY = ['user-settings']

export interface UserSettingsResult {
  userEmail: string | null | undefined
  isLoading: boolean
  isError: boolean
  error: Error | null
  refetch: () => void
}

export function useUserSettings(): UserSettingsResult {
  const query = useQuery({
    queryKey: USER_SETTINGS_QUERY_KEY,
    queryFn: async () => {
      const result = await Settings.getApiSettingsUser()
      if (result.error || !result.data) {
        throw new Error('Failed to fetch user settings')
      }
      return result.data
    },
  })

  return {
    userEmail: query.data?.userEmail,
    isLoading: query.isLoading,
    isError: query.isError,
    error: query.error,
    refetch: query.refetch,
  }
}

export interface UpdateUserEmailResult {
  mutate: (email: string) => void
  mutateAsync: (email: string) => Promise<UserSettingsResponse>
  isPending: boolean
  isError: boolean
  error: Error | null
}

export function useUpdateUserEmail(): UpdateUserEmailResult {
  const queryClient = useQueryClient()

  const mutation = useMutation({
    mutationFn: async (email: string) => {
      const request: UpdateUserEmailRequest = { email }
      const result = await Settings.putApiSettingsUserEmail({ body: request })
      if (result.error || !result.data) {
        throw new Error('Failed to update user email')
      }
      return result.data
    },
    onSuccess: (data) => {
      queryClient.setQueryData(USER_SETTINGS_QUERY_KEY, data)
    },
  })

  return {
    mutate: mutation.mutate,
    mutateAsync: mutation.mutateAsync,
    isPending: mutation.isPending,
    isError: mutation.isError,
    error: mutation.error,
  }
}
