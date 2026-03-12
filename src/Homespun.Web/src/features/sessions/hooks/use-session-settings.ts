import { useSessionSettingsStore, type ModelSelection } from '@/stores/session-settings-store'
import { normalizeSessionMode } from '@/lib/utils/session-mode'
import type { ClaudeSession, SessionMode } from '@/types/signalr'

/**
 * Hook to get session settings with fallbacks.
 * Prefers server data from the session object, falls back to cached values,
 * and finally to default values.
 *
 * @param sessionId - The session ID to get settings for
 * @param session - The session object from server (may be null/undefined while loading)
 * @returns The mode and model for the session
 */
export function useSessionSettings(
  sessionId: string,
  session: ClaudeSession | null | undefined
): { mode: SessionMode; model: ModelSelection } {
  const cachedSettings = useSessionSettingsStore((s) => s.sessions[sessionId])

  // Prefer server data, fall back to cache, then defaults
  // Use normalizeSessionMode to handle numeric values from SignalR (C# enum serialization)
  return {
    mode: normalizeSessionMode(session?.mode ?? cachedSettings?.mode),
    model: (session?.model ?? cachedSettings?.model ?? 'opus') as ModelSelection,
  }
}
