import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, waitFor, act } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import * as React from 'react'
import { useUserSettings, useUpdateUserEmail } from './use-user-settings'
import { Settings } from '@/api'

vi.mock('@/api', () => ({
  Settings: {
    getApiSettingsUser: vi.fn(),
    putApiSettingsUserEmail: vi.fn(),
  },
}))

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
      },
    },
  })
  return function Wrapper({ children }: { children: React.ReactNode }) {
    return React.createElement(QueryClientProvider, { client: queryClient }, children)
  }
}

describe('useUserSettings', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('returns loading state initially', () => {
    vi.mocked(Settings.getApiSettingsUser).mockReturnValue(
      new Promise(() => {}) as ReturnType<typeof Settings.getApiSettingsUser>
    )

    const { result } = renderHook(() => useUserSettings(), {
      wrapper: createWrapper(),
    })

    expect(result.current.isLoading).toBe(true)
    expect(result.current.userEmail).toBeUndefined()
  })

  it('returns user email on success', async () => {
    const mockResponse = {
      userEmail: 'test@example.com',
    }

    vi.mocked(Settings.getApiSettingsUser).mockResolvedValue({
      data: mockResponse,
      response: new Response(),
      request: new Request('http://test'),
      error: undefined,
    } as Awaited<ReturnType<typeof Settings.getApiSettingsUser>>)

    const { result } = renderHook(() => useUserSettings(), {
      wrapper: createWrapper(),
    })

    await waitFor(() => expect(result.current.isLoading).toBe(false))

    expect(result.current.userEmail).toBe('test@example.com')
    expect(result.current.isError).toBe(false)
  })

  it('returns null when no email is configured', async () => {
    const mockResponse = {
      userEmail: null,
    }

    vi.mocked(Settings.getApiSettingsUser).mockResolvedValue({
      data: mockResponse,
      response: new Response(),
      request: new Request('http://test'),
      error: undefined,
    } as Awaited<ReturnType<typeof Settings.getApiSettingsUser>>)

    const { result } = renderHook(() => useUserSettings(), {
      wrapper: createWrapper(),
    })

    await waitFor(() => expect(result.current.isLoading).toBe(false))

    expect(result.current.userEmail).toBeNull()
    expect(result.current.isError).toBe(false)
  })

  it('returns error state when fetch fails', async () => {
    vi.mocked(Settings.getApiSettingsUser).mockResolvedValue({
      data: undefined,
      response: new Response(null, { status: 500 }),
      request: new Request('http://test'),
      error: { detail: 'Server error' },
    } as Awaited<ReturnType<typeof Settings.getApiSettingsUser>>)

    const { result } = renderHook(() => useUserSettings(), {
      wrapper: createWrapper(),
    })

    await waitFor(() => expect(result.current.isLoading).toBe(false))

    expect(result.current.isError).toBe(true)
  })
})

describe('useUpdateUserEmail', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('updates user email successfully', async () => {
    const mockResponse = {
      userEmail: 'new@example.com',
    }

    vi.mocked(Settings.getApiSettingsUser).mockResolvedValue({
      data: { userEmail: 'old@example.com' },
      response: new Response(),
      request: new Request('http://test'),
      error: undefined,
    } as Awaited<ReturnType<typeof Settings.getApiSettingsUser>>)

    vi.mocked(Settings.putApiSettingsUserEmail).mockResolvedValue({
      data: mockResponse,
      response: new Response(),
      request: new Request('http://test'),
      error: undefined,
    } as Awaited<ReturnType<typeof Settings.putApiSettingsUserEmail>>)

    const { result } = renderHook(() => useUpdateUserEmail(), {
      wrapper: createWrapper(),
    })

    await act(async () => {
      await result.current.mutateAsync('new@example.com')
    })

    expect(Settings.putApiSettingsUserEmail).toHaveBeenCalledWith({
      body: { email: 'new@example.com' },
    })
  })

  it('handles update error', async () => {
    vi.mocked(Settings.putApiSettingsUserEmail).mockResolvedValue({
      data: undefined,
      response: new Response(null, { status: 400 }),
      request: new Request('http://test'),
      error: { detail: 'Invalid email' },
    } as Awaited<ReturnType<typeof Settings.putApiSettingsUserEmail>>)

    const { result } = renderHook(() => useUpdateUserEmail(), {
      wrapper: createWrapper(),
    })

    await expect(
      act(async () => {
        await result.current.mutateAsync('invalid')
      })
    ).rejects.toThrow('Failed to update user email')

    // After rejection, we need to wait for React Query to update the error state
    await waitFor(() => expect(result.current.isError).toBe(true))
  })
})
