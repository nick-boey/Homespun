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

/**
 * Extract a human-readable error message from a hey-api error response.
 * The server sends plain text for 400s (e.g. "Invalid email format"); default
 * to the generic fallback for anything else.
 */
function extractErrorMessage(error: unknown): string {
  if (typeof error === 'string' && error.length > 0) {
    return error
  }
  if (error && typeof error === 'object') {
    const record = error as Record<string, unknown>
    if (typeof record.error === 'string' && record.error.length > 0) {
      return record.error
    }
    if (typeof record.message === 'string' && record.message.length > 0) {
      return record.message
    }
  }
  return 'Failed to update user email'
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
        // Propagate the server's error body (e.g. "Invalid email format") so
        // callers can surface it via toast/inline feedback instead of failing
        // silently.
        throw new Error(extractErrorMessage(result.error))
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
