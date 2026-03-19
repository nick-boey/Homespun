import { useQuery } from '@tanstack/react-query'
import { Sessions } from '@/api'
import type { ClaudeMessage } from '@/types/signalr'

export const historicalSessionMessagesQueryKey = (sessionId?: string) =>
  ['historical-session-messages', sessionId] as const

export interface UseHistoricalSessionMessagesResult {
  messages: ClaudeMessage[]
  isLoading: boolean
  error: Error | null
}

/**
 * Fetches cached messages for a historical session.
 * Use this hook when viewing past sessions that are no longer active.
 */
export function useHistoricalSessionMessages(
  sessionId: string | undefined | null
): UseHistoricalSessionMessagesResult {
  const query = useQuery({
    queryKey: historicalSessionMessagesQueryKey(sessionId ?? undefined),
    queryFn: async () => {
      const response = await Sessions.getApiSessionsByIdCachedMessages({
        path: { id: sessionId! },
      })
      return response.data
    },
    enabled: !!sessionId,
    // Cache for a longer period since historical messages don't change
    staleTime: 5 * 60 * 1000, // 5 minutes
  })

  return {
    messages: (query.data as ClaudeMessage[]) ?? [],
    isLoading: query.isLoading,
    error: query.error,
  }
}
