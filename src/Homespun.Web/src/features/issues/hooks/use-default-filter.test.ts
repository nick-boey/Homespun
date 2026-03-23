import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook } from '@testing-library/react'
import { useDefaultFilter } from './use-default-filter'

// Mock the useUserSettings hook
vi.mock('@/features/settings', () => ({
  useUserSettings: vi.fn(),
}))

import { useUserSettings } from '@/features/settings'

const mockUseUserSettings = vi.mocked(useUserSettings)

describe('useDefaultFilter', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('returns "assigned:me" when user email is configured', () => {
    mockUseUserSettings.mockReturnValue({
      userEmail: 'test@example.com',
      isLoading: false,
      isError: false,
      error: null,
      refetch: vi.fn(),
    })

    const { result } = renderHook(() => useDefaultFilter())

    expect(result.current.defaultFilterQuery).toBe('assigned:me')
    expect(result.current.userEmail).toBe('test@example.com')
  })

  it('returns empty string when user email is not configured', () => {
    mockUseUserSettings.mockReturnValue({
      userEmail: null,
      isLoading: false,
      isError: false,
      error: null,
      refetch: vi.fn(),
    })

    const { result } = renderHook(() => useDefaultFilter())

    expect(result.current.defaultFilterQuery).toBe('')
    expect(result.current.userEmail).toBeNull()
  })

  it('returns empty string when user email is undefined', () => {
    mockUseUserSettings.mockReturnValue({
      userEmail: undefined,
      isLoading: false,
      isError: false,
      error: null,
      refetch: vi.fn(),
    })

    const { result } = renderHook(() => useDefaultFilter())

    expect(result.current.defaultFilterQuery).toBe('')
    expect(result.current.userEmail).toBeUndefined()
  })

  it('returns loading state from useUserSettings', () => {
    mockUseUserSettings.mockReturnValue({
      userEmail: null,
      isLoading: true,
      isError: false,
      error: null,
      refetch: vi.fn(),
    })

    const { result } = renderHook(() => useDefaultFilter())

    expect(result.current.isLoading).toBe(true)
  })
})
