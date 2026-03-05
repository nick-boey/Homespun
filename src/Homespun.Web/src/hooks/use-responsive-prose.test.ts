import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { renderHook } from '@testing-library/react'
import { useResponsiveProse } from './use-responsive-prose'
import * as useMobileModule from './use-mobile'

// Mock the useMobile hook
vi.mock('./use-mobile', () => ({
  useMobile: vi.fn(),
}))

describe('useResponsiveProse', () => {
  const mockedUseMobile = vi.mocked(useMobileModule.useMobile)

  beforeEach(() => {
    vi.clearAllMocks()
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('returns prose-sm for mobile viewports', () => {
    mockedUseMobile.mockReturnValue(true)

    const { result } = renderHook(() => useResponsiveProse())

    expect(result.current).toBe('prose-sm')
  })

  it('returns prose for desktop viewports', () => {
    mockedUseMobile.mockReturnValue(false)

    const { result } = renderHook(() => useResponsiveProse())

    expect(result.current).toBe('prose')
  })

  it('updates when viewport changes from mobile to desktop', () => {
    mockedUseMobile.mockReturnValue(true)

    const { result, rerender } = renderHook(() => useResponsiveProse())
    expect(result.current).toBe('prose-sm')

    // Simulate viewport change
    mockedUseMobile.mockReturnValue(false)
    rerender()

    expect(result.current).toBe('prose')
  })

  it('updates when viewport changes from desktop to mobile', () => {
    mockedUseMobile.mockReturnValue(false)

    const { result, rerender } = renderHook(() => useResponsiveProse())
    expect(result.current).toBe('prose')

    // Simulate viewport change
    mockedUseMobile.mockReturnValue(true)
    rerender()

    expect(result.current).toBe('prose-sm')
  })

  it('can include additional prose modifiers', () => {
    mockedUseMobile.mockReturnValue(true)

    const { result } = renderHook(() => useResponsiveProse({ invert: true }))

    expect(result.current).toBe('prose-sm prose-invert')
  })

  it('includes base prose class when requested', () => {
    mockedUseMobile.mockReturnValue(true)

    const { result } = renderHook(() => useResponsiveProse({ includeBase: true }))

    expect(result.current).toBe('prose prose-sm')
  })

  it('combines base class and modifiers correctly', () => {
    mockedUseMobile.mockReturnValue(false)

    const { result } = renderHook(() => useResponsiveProse({ includeBase: true, invert: true }))

    expect(result.current).toBe('prose prose-invert')
  })
})
