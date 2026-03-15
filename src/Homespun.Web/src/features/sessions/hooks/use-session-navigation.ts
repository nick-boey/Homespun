import { useMemo } from 'react'
import { useSessions } from './use-sessions'
import { ClaudeSessionStatus } from '@/api'

// Running states that should be excluded from navigation targets
const RUNNING_STATUSES: ClaudeSessionStatus[] = [
  ClaudeSessionStatus.STARTING,
  ClaudeSessionStatus.RUNNING_HOOKS,
  ClaudeSessionStatus.RUNNING,
]

export interface SessionNavigation {
  previousSessionId: string | null
  nextSessionId: string | null
  hasPrevious: boolean
  hasNext: boolean
  isLoading: boolean
}

/**
 * Hook to get navigation targets (previous/next sessions) for the current session.
 * - Previous = newer session (higher in the list)
 * - Next = older session (lower in the list)
 * - Excludes running sessions from navigation targets (Starting, RunningHooks, Running)
 * - Includes waiting/stopped/error sessions
 */
export function useSessionNavigation(currentSessionId: string): SessionNavigation {
  const { data: sessions, isLoading } = useSessions()

  return useMemo(() => {
    if (isLoading || !sessions) {
      return {
        previousSessionId: null,
        nextSessionId: null,
        hasPrevious: false,
        hasNext: false,
        isLoading,
      }
    }

    // Filter out running sessions and current session, then sort by lastActivityAt descending
    const navigableSessions = sessions
      .filter((session) => {
        // Exclude current session
        if (session.id === currentSessionId) return false
        // Exclude running sessions
        if (session.status !== undefined && RUNNING_STATUSES.includes(session.status)) {
          return false
        }
        return true
      })
      .sort((a, b) => {
        // Sort by lastActivityAt descending (newest first)
        // Treat null/undefined as oldest (time = 0)
        const timeA = a.lastActivityAt ? new Date(a.lastActivityAt).getTime() : 0
        const timeB = b.lastActivityAt ? new Date(b.lastActivityAt).getTime() : 0
        return timeB - timeA
      })

    // Find the current session's position in the sorted list (including running sessions)
    // We need to find where the current session would be in the full sorted list
    const allSortedSessions = [...sessions].sort((a, b) => {
      const timeA = a.lastActivityAt ? new Date(a.lastActivityAt).getTime() : 0
      const timeB = b.lastActivityAt ? new Date(b.lastActivityAt).getTime() : 0
      return timeB - timeA
    })

    const currentIndex = allSortedSessions.findIndex((s) => s.id === currentSessionId)
    if (currentIndex === -1) {
      // Current session not found
      return {
        previousSessionId: null,
        nextSessionId: null,
        hasPrevious: false,
        hasNext: false,
        isLoading: false,
      }
    }

    const currentSession = allSortedSessions[currentIndex]
    const currentTime = currentSession?.lastActivityAt
      ? new Date(currentSession.lastActivityAt).getTime()
      : 0

    // Previous = closest newer session (last in the sorted list that is newer than current)
    // Since navigableSessions is sorted descending (newest first), filter and take the last item
    const newerSessions = navigableSessions.filter((session) => {
      const sessionTime = session.lastActivityAt ? new Date(session.lastActivityAt).getTime() : 0
      return sessionTime > currentTime
    })
    const previousSession =
      newerSessions.length > 0 ? newerSessions[newerSessions.length - 1] : undefined

    // Next = first navigable session that is older than current (larger index in sorted list)
    const nextSession = navigableSessions.find((session) => {
      const sessionTime = session.lastActivityAt ? new Date(session.lastActivityAt).getTime() : 0
      return sessionTime < currentTime
    })

    return {
      previousSessionId: previousSession?.id ?? null,
      nextSessionId: nextSession?.id ?? null,
      hasPrevious: !!previousSession,
      hasNext: !!nextSession,
      isLoading: false,
    }
  }, [sessions, isLoading, currentSessionId])
}
