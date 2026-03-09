import { createContext } from 'react'

export interface TelemetryContextValue {
  trackPageView: (url: string, title?: string, properties?: Record<string, string>) => void
  trackEvent: (name: string, properties?: Record<string, string>) => void
  trackException: (error: Error | string | unknown, properties?: Record<string, string>) => void
  trackDependency: (
    name: string,
    duration: number,
    success: boolean,
    statusCode?: number,
    properties?: Record<string, string>
  ) => void
}

export const TelemetryContext = createContext<TelemetryContextValue | null>(null)
