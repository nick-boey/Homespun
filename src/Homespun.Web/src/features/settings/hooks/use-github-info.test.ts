import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import * as React from 'react'
import { useGitHubInfo } from './use-github-info'
import { GitHubInfo } from '@/api'

vi.mock('@/api', () => ({
  GitHubInfo: {
    getApiGithubStatus: vi.fn(),
    getApiGithubAuthStatus: vi.fn(),
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

describe('useGitHubInfo', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('returns loading state initially', () => {
    vi.mocked(GitHubInfo.getApiGithubStatus).mockReturnValue(
      new Promise(() => {}) as ReturnType<typeof GitHubInfo.getApiGithubStatus>
    )
    vi.mocked(GitHubInfo.getApiGithubAuthStatus).mockReturnValue(
      new Promise(() => {}) as ReturnType<typeof GitHubInfo.getApiGithubAuthStatus>
    )

    const { result } = renderHook(() => useGitHubInfo(), {
      wrapper: createWrapper(),
    })

    expect(result.current.isLoading).toBe(true)
    expect(result.current.status).toBeUndefined()
    expect(result.current.authStatus).toBeUndefined()
  })

  it('returns status and auth status on success', async () => {
    const mockStatus = {
      isConfigured: true,
      maskedToken: 'ghp_***xyz',
    }
    const mockAuthStatus = {
      isAuthenticated: true,
      username: 'testuser',
      message: 'Authenticated with GitHub',
      authMethod: 1,
    }

    vi.mocked(GitHubInfo.getApiGithubStatus).mockResolvedValue({
      data: mockStatus,
      response: new Response(),
      request: new Request('http://test'),
      error: undefined,
    } as Awaited<ReturnType<typeof GitHubInfo.getApiGithubStatus>>)

    vi.mocked(GitHubInfo.getApiGithubAuthStatus).mockResolvedValue({
      data: mockAuthStatus,
      response: new Response(),
      request: new Request('http://test'),
      error: undefined,
    } as Awaited<ReturnType<typeof GitHubInfo.getApiGithubAuthStatus>>)

    const { result } = renderHook(() => useGitHubInfo(), {
      wrapper: createWrapper(),
    })

    await waitFor(() => expect(result.current.isLoading).toBe(false))

    expect(result.current.status).toEqual(mockStatus)
    expect(result.current.authStatus).toEqual(mockAuthStatus)
    expect(result.current.isError).toBe(false)
  })

  it('returns error state when status fetch fails', async () => {
    vi.mocked(GitHubInfo.getApiGithubStatus).mockResolvedValue({
      data: undefined,
      response: new Response(null, { status: 500 }),
      request: new Request('http://test'),
      error: { detail: 'Server error' },
    } as Awaited<ReturnType<typeof GitHubInfo.getApiGithubStatus>>)

    vi.mocked(GitHubInfo.getApiGithubAuthStatus).mockResolvedValue({
      data: { isAuthenticated: false },
      response: new Response(),
      request: new Request('http://test'),
      error: undefined,
    } as Awaited<ReturnType<typeof GitHubInfo.getApiGithubAuthStatus>>)

    const { result } = renderHook(() => useGitHubInfo(), {
      wrapper: createWrapper(),
    })

    await waitFor(() => expect(result.current.isLoading).toBe(false))

    expect(result.current.isError).toBe(true)
  })

  it('returns not authenticated status', async () => {
    const mockStatus = {
      isConfigured: false,
      maskedToken: null,
    }
    const mockAuthStatus = {
      isAuthenticated: false,
      username: null,
      errorMessage: 'Not authenticated',
      authMethod: 0,
    }

    vi.mocked(GitHubInfo.getApiGithubStatus).mockResolvedValue({
      data: mockStatus,
      response: new Response(),
      request: new Request('http://test'),
      error: undefined,
    } as Awaited<ReturnType<typeof GitHubInfo.getApiGithubStatus>>)

    vi.mocked(GitHubInfo.getApiGithubAuthStatus).mockResolvedValue({
      data: mockAuthStatus,
      response: new Response(),
      request: new Request('http://test'),
      error: undefined,
    } as Awaited<ReturnType<typeof GitHubInfo.getApiGithubAuthStatus>>)

    const { result } = renderHook(() => useGitHubInfo(), {
      wrapper: createWrapper(),
    })

    await waitFor(() => expect(result.current.isLoading).toBe(false))

    expect(result.current.status?.isConfigured).toBe(false)
    expect(result.current.authStatus?.isAuthenticated).toBe(false)
  })
})
