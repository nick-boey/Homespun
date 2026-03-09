import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook } from '@testing-library/react'
import { useTelemetry } from './use-telemetry'
import { TelemetryContext } from '@/providers/telemetry-context'
import { type ReactNode } from 'react'

// Unmock the useTelemetry hook for this test file
vi.unmock('@/hooks/use-telemetry')

// Create context value mock
const mockTelemetry = {
  trackPageView: vi.fn(),
  trackEvent: vi.fn(),
  trackException: vi.fn(),
  trackDependency: vi.fn(),
}

// Create a wrapper component that provides the context
const createWrapper = (value = mockTelemetry) => {
  return ({ children }: { children: ReactNode }) => (
    <TelemetryContext.Provider value={value}>{children}</TelemetryContext.Provider>
  )
}

describe('useTelemetry', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('returns telemetry methods from context', () => {
    const { result } = renderHook(() => useTelemetry(), {
      wrapper: createWrapper(),
    })

    expect(result.current).toBe(mockTelemetry)
    expect(result.current.trackPageView).toBe(mockTelemetry.trackPageView)
    expect(result.current.trackEvent).toBe(mockTelemetry.trackEvent)
    expect(result.current.trackException).toBe(mockTelemetry.trackException)
    expect(result.current.trackDependency).toBe(mockTelemetry.trackDependency)
  })

  it('methods remain stable across re-renders', () => {
    const { result, rerender } = renderHook(() => useTelemetry(), {
      wrapper: createWrapper(),
    })

    const firstRender = result.current

    rerender()

    const secondRender = result.current

    expect(firstRender).toBe(secondRender)
    expect(firstRender.trackPageView).toBe(secondRender.trackPageView)
    expect(firstRender.trackEvent).toBe(secondRender.trackEvent)
  })

  it('can call tracking methods', () => {
    const { result } = renderHook(() => useTelemetry(), {
      wrapper: createWrapper(),
    })

    result.current.trackPageView('/test', 'Test Page')
    expect(mockTelemetry.trackPageView).toHaveBeenCalledWith('/test', 'Test Page')

    result.current.trackEvent('test_event', { foo: 'bar' })
    expect(mockTelemetry.trackEvent).toHaveBeenCalledWith('test_event', { foo: 'bar' })

    const error = new Error('Test error')
    result.current.trackException(error)
    expect(mockTelemetry.trackException).toHaveBeenCalledWith(error)

    result.current.trackDependency('GET /api/test', 100, true, 200)
    expect(mockTelemetry.trackDependency).toHaveBeenCalledWith('GET /api/test', 100, true, 200)
  })

  it('throws error when used outside of provider', () => {
    // Mock console.error to avoid noise in test output
    const mockConsoleError = vi.spyOn(console, 'error').mockImplementation(() => {})

    expect(() => {
      renderHook(() => useTelemetry())
    }).toThrow('useTelemetry must be used within a TelemetryProvider')

    mockConsoleError.mockRestore()
  })
})
