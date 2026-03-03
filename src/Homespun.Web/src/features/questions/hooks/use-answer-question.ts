import { useState, useCallback } from 'react'
import { useClaudeCodeHub } from '@/providers/signalr-provider'

export interface UseAnswerQuestionOptions {
  sessionId: string
  onSuccess?: () => void
  onError?: (error: Error) => void
}

export interface UseAnswerQuestionResult {
  answerQuestion: (answers: Record<string, string>) => Promise<void>
  isSubmitting: boolean
  error: string | undefined
  reset: () => void
}

export function useAnswerQuestion({
  sessionId,
  onSuccess,
  onError,
}: UseAnswerQuestionOptions): UseAnswerQuestionResult {
  const { methods, isConnected } = useClaudeCodeHub()
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [error, setError] = useState<string | undefined>()

  const answerQuestion = useCallback(
    async (answers: Record<string, string>) => {
      if (!methods || !isConnected) {
        const err = new Error('Not connected to server')
        setError(err.message)
        onError?.(err)
        return
      }

      setIsSubmitting(true)
      setError(undefined)

      try {
        const answersJson = JSON.stringify(answers)
        await methods.answerQuestion(sessionId, answersJson)
        onSuccess?.()
      } catch (err) {
        const error = err instanceof Error ? err : new Error('Failed to submit answers')
        setError(error.message)
        onError?.(error)
      } finally {
        setIsSubmitting(false)
      }
    },
    [methods, isConnected, sessionId, onSuccess, onError]
  )

  const reset = useCallback(() => {
    setError(undefined)
    setIsSubmitting(false)
  }, [])

  return {
    answerQuestion,
    isSubmitting,
    error,
    reset,
  }
}
