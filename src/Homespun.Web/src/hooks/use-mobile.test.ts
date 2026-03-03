import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { renderHook, act } from '@testing-library/react'
import { useMobile } from './use-mobile'

describe('useMobile', () => {
  let originalMatchMedia: typeof window.matchMedia
  let mockMediaQueryList: {
    matches: boolean
    addEventListener: ReturnType<typeof vi.fn>
    removeEventListener: ReturnType<typeof vi.fn>
  }
  let changeHandler: ((event: MediaQueryListEvent) => void) | null = null

  beforeEach(() => {
    originalMatchMedia = window.matchMedia
    mockMediaQueryList = {
      matches: false,
      addEventListener: vi.fn((event, handler) => {
        if (event === 'change') {
          changeHandler = handler
        }
      }),
      removeEventListener: vi.fn(),
    }

    window.matchMedia = vi.fn().mockReturnValue(mockMediaQueryList)
  })

  afterEach(() => {
    window.matchMedia = originalMatchMedia
    changeHandler = null
  })

  it('returns false when viewport is larger than mobile breakpoint', () => {
    mockMediaQueryList.matches = false

    const { result } = renderHook(() => useMobile())

    expect(result.current).toBe(false)
  })

  it('returns true when viewport is smaller than mobile breakpoint', () => {
    mockMediaQueryList.matches = true

    const { result } = renderHook(() => useMobile())

    expect(result.current).toBe(true)
  })

  it('uses correct media query breakpoint (767px)', () => {
    renderHook(() => useMobile())

    expect(window.matchMedia).toHaveBeenCalledWith('(max-width: 767px)')
  })

  it('adds event listener on mount', () => {
    renderHook(() => useMobile())

    expect(mockMediaQueryList.addEventListener).toHaveBeenCalledWith('change', expect.any(Function))
  })

  it('removes event listener on unmount', () => {
    const { unmount } = renderHook(() => useMobile())

    unmount()

    expect(mockMediaQueryList.removeEventListener).toHaveBeenCalledWith(
      'change',
      expect.any(Function)
    )
  })

  it('updates when media query changes', () => {
    mockMediaQueryList.matches = false

    const { result } = renderHook(() => useMobile())

    expect(result.current).toBe(false)

    // Simulate a media query change to mobile
    act(() => {
      mockMediaQueryList.matches = true
      changeHandler?.({ matches: true } as MediaQueryListEvent)
    })

    expect(result.current).toBe(true)

    // Simulate a media query change back to desktop
    act(() => {
      mockMediaQueryList.matches = false
      changeHandler?.({ matches: false } as MediaQueryListEvent)
    })

    expect(result.current).toBe(false)
  })
})
