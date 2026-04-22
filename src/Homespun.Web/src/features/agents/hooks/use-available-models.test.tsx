import { describe, it, expect, vi } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { useAvailableModels, availableModelsQueryKey } from './use-available-models'
import { Models } from '@/api'
import type { ClaudeModelInfo } from '@/api/generated/types.gen'

vi.mock('@/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/api')>()
  return {
    ...actual,
    Models: {
      getApiModels: vi.fn(),
    },
  }
})

describe('useAvailableModels', () => {
  const createWrapper = () => {
    const queryClient = new QueryClient({
      defaultOptions: {
        queries: { retry: false, refetchInterval: false },
      },
    })
    return ({ children }: { children: React.ReactNode }) => (
      <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
    )
  }

  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('returns the catalog from the API', async () => {
    const catalog: ClaudeModelInfo[] = [
      {
        id: 'claude-opus-4-7-20251101',
        displayName: 'Claude Opus 4.7',
        createdAt: '2025-11-01T00:00:00Z',
        isDefault: true,
      },
      {
        id: 'claude-sonnet-4-6-20250601',
        displayName: 'Claude Sonnet 4.6',
        createdAt: '2025-06-01T00:00:00Z',
      },
    ]

    vi.mocked(Models.getApiModels).mockResolvedValueOnce({
      data: catalog,
      request: {} as Request,
      response: {} as Response,
    })

    const { result } = renderHook(() => useAvailableModels(), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false)
    })

    expect(result.current.models).toEqual(catalog)
    expect(result.current.defaultModel?.id).toBe('claude-opus-4-7-20251101')
  })

  it('identifies the default entry by isDefault flag', async () => {
    const catalog: ClaudeModelInfo[] = [
      {
        id: 'claude-a',
        displayName: 'A',
        createdAt: '2024-01-01T00:00:00Z',
      },
      {
        id: 'claude-b',
        displayName: 'B',
        createdAt: '2024-02-01T00:00:00Z',
        isDefault: true,
      },
      {
        id: 'claude-c',
        displayName: 'C',
        createdAt: '2024-03-01T00:00:00Z',
      },
    ]

    vi.mocked(Models.getApiModels).mockResolvedValueOnce({
      data: catalog,
      request: {} as Request,
      response: {} as Response,
    })

    const { result } = renderHook(() => useAvailableModels(), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.defaultModel?.id).toBe('claude-b')
    })
  })

  it('uses a 24-hour staleTime', async () => {
    const catalog: ClaudeModelInfo[] = [
      {
        id: 'claude-only',
        displayName: 'Only',
        createdAt: '2024-01-01T00:00:00Z',
        isDefault: true,
      },
    ]

    vi.mocked(Models.getApiModels).mockResolvedValue({
      data: catalog,
      request: {} as Request,
      response: {} as Response,
    })

    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    })
    const wrapper = ({ children }: { children: React.ReactNode }) => (
      <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
    )

    const { result } = renderHook(() => useAvailableModels(), { wrapper })

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false)
    })

    const cached = queryClient.getQueryCache().find({ queryKey: availableModelsQueryKey })
    const observerStaleTime = cached?.observers[0]?.options.staleTime
    expect(observerStaleTime).toBe(24 * 60 * 60 * 1000)
  })
})
