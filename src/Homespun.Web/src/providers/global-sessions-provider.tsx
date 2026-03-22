import type { ReactNode } from 'react'
import { useGlobalSessionsSignalR } from '@/hooks/use-global-sessions-signalr'

export interface GlobalSessionsProviderProps {
  children: ReactNode
}

/**
 * Provider component that subscribes to global session SignalR events.
 * This ensures the header status indicator stays in sync with session changes
 * regardless of which page is currently active.
 *
 * Mount this at the application root level inside SignalRProvider.
 */
export function GlobalSessionsProvider({ children }: GlobalSessionsProviderProps) {
  useGlobalSessionsSignalR()
  return <>{children}</>
}
