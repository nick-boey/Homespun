import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import * as React from 'react'
import { useGitConfig } from './use-git-config'
import { GitHubInfo } from '@/api'

vi.mock('@/api', () => ({
  GitHubInfo: {
    getApiGithubGitConfig: vi.fn(),
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

describe('useGitConfig', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('returns loading state initially', () => {
    vi.mocked(GitHubInfo.getApiGithubGitConfig).mockReturnValue(
      new Promise(() => {}) as ReturnType<typeof GitHubInfo.getApiGithubGitConfig>
    )

    const { result } = renderHook(() => useGitConfig(), {
      wrapper: createWrapper(),
    })

    expect(result.current.isLoading).toBe(true)
    expect(result.current.config).toBeUndefined()
  })

  it('returns git config on success', async () => {
    const mockConfig = {
      authorName: 'Test User',
      authorEmail: 'test@example.com',
    }

    vi.mocked(GitHubInfo.getApiGithubGitConfig).mockResolvedValue({
      data: mockConfig,
      response: new Response(),
      request: new Request('http://test'),
      error: undefined,
    } as Awaited<ReturnType<typeof GitHubInfo.getApiGithubGitConfig>>)

    const { result } = renderHook(() => useGitConfig(), {
      wrapper: createWrapper(),
    })

    await waitFor(() => expect(result.current.isLoading).toBe(false))

    expect(result.current.config).toEqual(mockConfig)
    expect(result.current.isError).toBe(false)
  })

  it('returns error state when fetch fails', async () => {
    vi.mocked(GitHubInfo.getApiGithubGitConfig).mockResolvedValue({
      data: undefined,
      response: new Response(null, { status: 500 }),
      request: new Request('http://test'),
      error: { detail: 'Server error' },
    } as Awaited<ReturnType<typeof GitHubInfo.getApiGithubGitConfig>>)

    const { result } = renderHook(() => useGitConfig(), {
      wrapper: createWrapper(),
    })

    await waitFor(() => expect(result.current.isLoading).toBe(false))

    expect(result.current.isError).toBe(true)
    expect(result.current.config).toBeUndefined()
  })

  it('returns default values when config has defaults', async () => {
    const mockConfig = {
      authorName: 'Homespun Bot',
      authorEmail: 'homespun@localhost',
    }

    vi.mocked(GitHubInfo.getApiGithubGitConfig).mockResolvedValue({
      data: mockConfig,
      response: new Response(),
      request: new Request('http://test'),
      error: undefined,
    } as Awaited<ReturnType<typeof GitHubInfo.getApiGithubGitConfig>>)

    const { result } = renderHook(() => useGitConfig(), {
      wrapper: createWrapper(),
    })

    await waitFor(() => expect(result.current.isLoading).toBe(false))

    expect(result.current.config?.authorName).toBe('Homespun Bot')
    expect(result.current.config?.authorEmail).toBe('homespun@localhost')
  })
})
