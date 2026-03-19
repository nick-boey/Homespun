import { useState, useCallback } from 'react'
import { useClaudeCodeHub } from '@/providers/signalr-provider'
import { useNavigate } from '@tanstack/react-router'
import { toast } from 'sonner'
import type { ClaudeSession } from '@/types/signalr'

export interface UseClearContextOptions {
  /** Called when context is successfully cleared and new session starts */
  onSuccess?: (newSession: ClaudeSession) => void
  /** Called when clearing context fails */
  onError?: (error: Error) => void
}

export interface UseClearContextResult {
  /** Clear context and start a new session */
  clearContext: (sessionId: string, initialPrompt?: string) => Promise<ClaudeSession | null>
  /** Whether the operation is in progress */
  isPending: boolean
  /** Any error that occurred */
  error: Error | null
}

/**
 * Hook to clear session context and start a new session for the same entity/project.
 * The old session is preserved in history but a fresh session is created.
 */
export function useClearContext(options: UseClearContextOptions = {}): UseClearContextResult {
  const { methods, isConnected } = useClaudeCodeHub()
  const navigate = useNavigate()
  const [isPending, setIsPending] = useState(false)
  const [error, setError] = useState<Error | null>(null)

  const clearContext = useCallback(
    async (sessionId: string, initialPrompt?: string): Promise<ClaudeSession | null> => {
      if (!isConnected || !methods) {
        const err = new Error('Not connected to server')
        setError(err)
        options.onError?.(err)
        return null
      }

      setIsPending(true)
      setError(null)

      try {
        const newSession = await methods.clearContextAndStartNew(sessionId, initialPrompt ?? null)

        toast.success('New session started')

        // Navigate to the new session
        if (newSession?.id) {
          navigate({ to: '/sessions/$sessionId', params: { sessionId: newSession.id } })
        }

        options.onSuccess?.(newSession)
        return newSession
      } catch (err) {
        const error = err instanceof Error ? err : new Error(String(err))
        setError(error)
        toast.error('Failed to start new session')
        options.onError?.(error)
        return null
      } finally {
        setIsPending(false)
      }
    },
    [isConnected, methods, navigate, options]
  )

  return {
    clearContext,
    isPending,
    error,
  }
}
