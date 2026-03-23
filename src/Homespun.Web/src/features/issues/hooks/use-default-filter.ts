import { useMemo } from 'react'
import { useUserSettings } from '@/features/settings'

export interface UseDefaultFilterResult {
  /** The default filter query string */
  defaultFilterQuery: string
  /** The current user's email (for resolving "me" keyword) */
  userEmail: string | null | undefined
  /** Whether the user settings are still loading */
  isLoading: boolean
}

/**
 * Hook that provides the default filter query for the issue graph.
 *
 * Returns "assigned:me" when user email is configured,
 * or an empty string when no user email is set.
 */
export function useDefaultFilter(): UseDefaultFilterResult {
  const { userEmail, isLoading } = useUserSettings()

  const defaultFilterQuery = useMemo(() => {
    if (userEmail) {
      return 'assigned:me'
    }
    return ''
  }, [userEmail])

  return {
    defaultFilterQuery,
    userEmail,
    isLoading,
  }
}
