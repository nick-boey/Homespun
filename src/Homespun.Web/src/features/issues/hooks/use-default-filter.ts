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
 * Returns "is:next assigned:me" when user email is configured,
 * or just "is:next" when no user email is set.
 */
export function useDefaultFilter(): UseDefaultFilterResult {
  const { userEmail, isLoading } = useUserSettings()

  const defaultFilterQuery = useMemo(() => {
    const parts = ['is:next']
    if (userEmail) {
      parts.push('assigned:me')
    }
    return parts.join(' ')
  }, [userEmail])

  return {
    defaultFilterQuery,
    userEmail,
    isLoading,
  }
}
