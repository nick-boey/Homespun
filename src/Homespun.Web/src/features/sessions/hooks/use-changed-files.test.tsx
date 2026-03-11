import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { QueryClientProvider, QueryClient } from '@tanstack/react-query'
import { useChangedFiles } from './use-changed-files'
import { Clones } from '@/api'
import type { FileChangeInfo } from '@/api/generated'
import { createMockSession } from '@/test/test-utils'

vi.mock('@/api', () => ({
  Clones: {
    getApiClonesChangedFiles: vi.fn(),
  },
}))

describe('useChangedFiles', () => {
  let queryClient: QueryClient

  beforeEach(() => {
    queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    })
    vi.clearAllMocks()
  })

  const wrapper = ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  )

  it('returns empty array when session is undefined', async () => {
    const { result } = renderHook(() => useChangedFiles(undefined), { wrapper })

    expect(result.current.data).toBe(undefined)
    expect(result.current.isLoading).toBe(false)
  })

  it('returns empty array when workingDirectory is missing', async () => {
    const session = createMockSession({
      workingDirectory: undefined,
    })

    const { result } = renderHook(() => useChangedFiles(session), { wrapper })

    expect(result.current.data).toBe(undefined)
    expect(result.current.isLoading).toBe(false)
  })

  it('fetches changed files for a session', async () => {
    const mockFiles: FileChangeInfo[] = [
      {
        filePath: 'src/components/Button.tsx',
        additions: 25,
        deletions: 5,
        status: 1, // Modified
      },
      {
        filePath: 'src/components/NewComponent.tsx',
        additions: 100,
        deletions: 0,
        status: 0, // Added
      },
      {
        filePath: 'src/components/OldComponent.tsx',
        additions: 0,
        deletions: 50,
        status: 2, // Deleted
      },
    ]

    vi.mocked(Clones.getApiClonesChangedFiles).mockResolvedValue({
      data: mockFiles,
      response: new Response(),
      request: new Request('http://test'),
      error: undefined,
    } as Awaited<ReturnType<typeof Clones.getApiClonesChangedFiles>>)

    const session = createMockSession({
      workingDirectory: '/path/to/project',
    })

    const { result } = renderHook(() => useChangedFiles(session), { wrapper })

    expect(result.current.isLoading).toBe(true)

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false)
    })

    expect(result.current.data).toEqual(mockFiles)
    expect(Clones.getApiClonesChangedFiles).toHaveBeenCalledWith({
      query: {
        workingDirectory: '/path/to/project',
        targetBranch: 'main',
      },
    })
  })

  it('uses main as default target branch', async () => {
    vi.mocked(Clones.getApiClonesChangedFiles).mockResolvedValue({
      data: [],
      response: new Response(),
      request: new Request('http://test'),
      error: undefined,
    } as Awaited<ReturnType<typeof Clones.getApiClonesChangedFiles>>)

    const session = createMockSession({
      workingDirectory: '/path/to/project',
    })

    const { result } = renderHook(() => useChangedFiles(session), { wrapper })

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false)
    })

    expect(Clones.getApiClonesChangedFiles).toHaveBeenCalledWith({
      query: {
        workingDirectory: '/path/to/project',
        targetBranch: 'main',
      },
    })
  })

  it('returns empty array when API returns null', async () => {
    vi.mocked(Clones.getApiClonesChangedFiles).mockResolvedValue({
      data: null,
      response: new Response(),
      request: new Request('http://test'),
      error: undefined,
    } as unknown as Awaited<ReturnType<typeof Clones.getApiClonesChangedFiles>>)

    const session = createMockSession({
      workingDirectory: '/path/to/project',
    })

    const { result } = renderHook(() => useChangedFiles(session), { wrapper })

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false)
    })

    expect(result.current.data).toEqual([])
  })

  it('handles API errors gracefully', async () => {
    vi.mocked(Clones.getApiClonesChangedFiles).mockRejectedValue(new Error('API Error'))

    const session = createMockSession({
      workingDirectory: '/path/to/project',
    })

    const { result } = renderHook(() => useChangedFiles(session), { wrapper })

    await waitFor(() => {
      expect(result.current.isError).toBe(true)
    })

    expect(result.current.data).toBe(undefined)
  })
})
