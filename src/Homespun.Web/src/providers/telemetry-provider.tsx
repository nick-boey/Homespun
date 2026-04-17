import React, { useEffect, useMemo, useRef, useCallback } from 'react'
import { useRouterState } from '@tanstack/react-router'
import { TelemetryService } from '@/lib/telemetry'
import { setGlobalTelemetryService } from '@/lib/telemetry/telemetry-singleton'
import { TelemetryContext, type TelemetryContextValue } from './telemetry-context'

interface TelemetryProviderProps {
  children: React.ReactNode
  enabled?: boolean
  endpoint?: string
}

/**
 * Provider that initializes telemetry service and tracks page views
 */
export function TelemetryProvider({ children, enabled = true, endpoint }: TelemetryProviderProps) {
  const serviceRef = useRef<TelemetryService | undefined>(undefined)
  const routerState = useRouterState()

  // Initialize telemetry service
  useEffect(() => {
    const enabledFromEnv = import.meta.env.VITE_TELEMETRY_ENABLED !== 'false'
    const telemetryEnabled = enabled && enabledFromEnv

    serviceRef.current = new TelemetryService({
      enabled: telemetryEnabled,
      endpoint: endpoint || import.meta.env.VITE_TELEMETRY_ENDPOINT || '/api/client-telemetry',
      batchSize: import.meta.env.VITE_TELEMETRY_BATCH_SIZE
        ? parseInt(import.meta.env.VITE_TELEMETRY_BATCH_SIZE, 10)
        : 50,
      flushInterval: import.meta.env.VITE_TELEMETRY_FLUSH_INTERVAL
        ? parseInt(import.meta.env.VITE_TELEMETRY_FLUSH_INTERVAL, 10)
        : 10000,
    })

    // Set global instance for non-React code
    setGlobalTelemetryService(serviceRef.current)

    return () => {
      serviceRef.current?.stop()
    }
  }, [enabled, endpoint])

  // Track route changes
  useEffect(() => {
    if (serviceRef.current) {
      const location = routerState.location
      const path = location.href
      const title = document.title || path
      serviceRef.current.trackPageView(path, title)
    }
  }, [routerState.location])

  // Memoized tracking methods
  const trackPageView = useCallback<TelemetryContextValue['trackPageView']>(
    (url, title, properties) => {
      serviceRef.current?.trackPageView(url, title, properties)
    },
    []
  )

  const trackEvent = useCallback<TelemetryContextValue['trackEvent']>((name, properties) => {
    serviceRef.current?.trackEvent(name, properties)
  }, [])

  const trackException = useCallback<TelemetryContextValue['trackException']>(
    (error, properties) => {
      serviceRef.current?.trackException(error, properties)
    },
    []
  )

  const trackDependency = useCallback<TelemetryContextValue['trackDependency']>(
    (name, duration, success, statusCode, properties) => {
      serviceRef.current?.trackDependency(name, duration, success, statusCode, properties)
    },
    []
  )

  // Context value with memoized methods
  const contextValue = useMemo(
    () => ({
      trackPageView,
      trackEvent,
      trackException,
      trackDependency,
    }),
    [trackPageView, trackEvent, trackException, trackDependency]
  )

  return <TelemetryContext.Provider value={contextValue}>{children}</TelemetryContext.Provider>
}
