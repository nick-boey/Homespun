import { useState, useEffect, useCallback, useRef } from 'react'
import { useClaudeCodeHub } from '@/providers/signalr-provider'
import type { ClaudeSession } from '@/types/signalr'

export interface UseSessionResult {
  session: ClaudeSession | null | undefined
  isLoading: boolean
  isNotFound: boolean
  error: string | undefined
  refetch: () => Promise<void>
}

export function useSession(sessionId: string): UseSessionResult {
  const { connection, methods, isConnected } = useClaudeCodeHub()
  const [session, setSession] = useState<ClaudeSession | null | undefined>(undefined)
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState<string | undefined>()
  const hasJoinedRef = useRef(false)
  const currentSessionIdRef = useRef(sessionId)

  const fetchSession = useCallback(async () => {
    if (!methods || !isConnected) {
      return
    }

    setIsLoading(true)
    setError(undefined)

    try {
      const result = await methods.getSession(sessionId)
      setSession(result)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to fetch session')
    } finally {
      setIsLoading(false)
    }
  }, [methods, isConnected, sessionId])

  // Handle session ID changes
  useEffect(() => {
    const previousSessionId = currentSessionIdRef.current
    currentSessionIdRef.current = sessionId

    // If session ID changed, leave the old session
    if (previousSessionId !== sessionId && hasJoinedRef.current && methods) {
      methods.leaveSession(previousSessionId).catch(() => {})
      hasJoinedRef.current = false
    }
  }, [sessionId, methods])

  // Fetch session and join group
  useEffect(() => {
    if (!isConnected || !methods) {
      return
    }

    fetchSession()

    // Join session group
    methods
      .joinSession(sessionId)
      .then(() => {
        hasJoinedRef.current = true
      })
      .catch(() => {})

    return () => {
      if (hasJoinedRef.current && methods) {
        methods.leaveSession(sessionId).catch(() => {})
        hasJoinedRef.current = false
      }
    }
  }, [isConnected, methods, sessionId, fetchSession])

  // Register event handlers
  useEffect(() => {
    if (!connection) {
      return
    }

    const handleSessionState = (updatedSession: ClaudeSession) => {
      if (updatedSession.id === sessionId) {
        setSession(updatedSession)
      }
    }

    const handleSessionModeModelChanged = (
      updatedSessionId: string,
      mode: ClaudeSession['mode'],
      model: string
    ) => {
      if (updatedSessionId === sessionId) {
        setSession((prevSession) => {
          if (!prevSession || prevSession.id !== sessionId) return prevSession
          return { ...prevSession, mode, model }
        })
      }
    }

    connection.on('SessionState', handleSessionState)
    connection.on('SessionModeModelChanged', handleSessionModeModelChanged)

    return () => {
      connection.off('SessionState', handleSessionState)
      connection.off('SessionModeModelChanged', handleSessionModeModelChanged)
    }
  }, [connection, sessionId])

  return {
    session,
    isLoading,
    isNotFound: session === null,
    error,
    refetch: fetchSession,
  }
}
