import { useState, useCallback } from 'react'
import { toast } from 'sonner'
import { useClaudeCodeHub } from '@/providers/signalr-provider'
import { useSessionSettingsStore, type ModelSelection } from '@/stores/session-settings-store'
import type { SessionMode } from '@/types/signalr'

interface UseChangeSessionSettingsResult {
  changeMode: (mode: SessionMode) => Promise<void>
  changeModel: (model: ModelSelection) => Promise<void>
  isChanging: boolean
  error: string | null
}

/**
 * Hook to change session mode and model.
 * Updates the local cache optimistically and calls SignalR methods
 * to persist changes to the server.
 */
export function useChangeSessionSettings(sessionId: string): UseChangeSessionSettingsResult {
  const { methods } = useClaudeCodeHub()
  const [isChanging, setIsChanging] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const changeMode = useCallback(
    async (mode: SessionMode) => {
      if (!methods) {
        setError('Not connected to server')
        return
      }

      setIsChanging(true)
      setError(null)

      // Update cache optimistically
      const currentSettings = useSessionSettingsStore.getState().getSession(sessionId)
      if (currentSettings) {
        useSessionSettingsStore.getState().updateSession(sessionId, mode, currentSettings.model)
      } else {
        useSessionSettingsStore.getState().initSession(sessionId, mode, 'opus')
      }

      try {
        await methods.setSessionMode(sessionId, mode)
      } catch (err) {
        // Revert on error
        if (currentSettings) {
          useSessionSettingsStore
            .getState()
            .updateSession(sessionId, currentSettings.mode, currentSettings.model)
        }
        const errorMessage = err instanceof Error ? err.message : 'Failed to change mode'
        setError(errorMessage)
        toast.error(errorMessage)
      } finally {
        setIsChanging(false)
      }
    },
    [methods, sessionId]
  )

  const changeModel = useCallback(
    async (model: ModelSelection) => {
      if (!methods) {
        setError('Not connected to server')
        return
      }

      setIsChanging(true)
      setError(null)

      // Update cache optimistically
      const currentSettings = useSessionSettingsStore.getState().getSession(sessionId)
      if (currentSettings) {
        useSessionSettingsStore.getState().updateSession(sessionId, currentSettings.mode, model)
      } else {
        useSessionSettingsStore.getState().initSession(sessionId, 'build', model)
      }

      try {
        await methods.setSessionModel(sessionId, model)
      } catch (err) {
        // Revert on error
        if (currentSettings) {
          useSessionSettingsStore
            .getState()
            .updateSession(sessionId, currentSettings.mode, currentSettings.model)
        }
        const errorMessage = err instanceof Error ? err.message : 'Failed to change model'
        setError(errorMessage)
        toast.error(errorMessage)
      } finally {
        setIsChanging(false)
      }
    },
    [methods, sessionId]
  )

  return { changeMode, changeModel, isChanging, error }
}
