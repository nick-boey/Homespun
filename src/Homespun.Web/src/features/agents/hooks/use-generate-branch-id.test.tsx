import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, act } from '@testing-library/react'
import { QueryClientProvider, QueryClient } from '@tanstack/react-query'
import { useGenerateBranchId } from './use-generate-branch-id'
import { Orchestration } from '@/api'

vi.mock('@/api', () => ({
  Orchestration: {
    postApiOrchestrationGenerateBranchId: vi.fn(),
  },
}))

describe('useGenerateBranchId', () => {
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

  it('generates a branch ID from title', async () => {
    vi.mocked(Orchestration.postApiOrchestrationGenerateBranchId).mockResolvedValue({
      data: {
        success: true,
        branchId: 'add-user-auth',
        wasAiGenerated: true,
      },
      response: new Response(),
      request: new Request('http://test'),
      error: undefined,
    } as Awaited<ReturnType<typeof Orchestration.postApiOrchestrationGenerateBranchId>>)

    const { result } = renderHook(() => useGenerateBranchId(), { wrapper })

    await act(async () => {
      const branchIdResult = await result.current.mutateAsync('Add user authentication')
      expect(branchIdResult.branchId).toBe('add-user-auth')
      expect(branchIdResult.wasAiGenerated).toBe(true)
    })
  })

  it('handles API errors', async () => {
    vi.mocked(Orchestration.postApiOrchestrationGenerateBranchId).mockResolvedValue({
      data: undefined,
      response: new Response(null, { status: 500 }),
      request: new Request('http://test'),
      error: { detail: 'API error' },
    } as Awaited<ReturnType<typeof Orchestration.postApiOrchestrationGenerateBranchId>>)

    const { result } = renderHook(() => useGenerateBranchId(), { wrapper })

    await expect(
      act(async () => {
        await result.current.mutateAsync('Test title')
      })
    ).rejects.toThrow('API error')
  })

  it('handles unsuccessful response', async () => {
    vi.mocked(Orchestration.postApiOrchestrationGenerateBranchId).mockResolvedValue({
      data: {
        success: false,
        error: 'Failed to generate',
      },
      response: new Response(),
      request: new Request('http://test'),
      error: undefined,
    } as Awaited<ReturnType<typeof Orchestration.postApiOrchestrationGenerateBranchId>>)

    const { result } = renderHook(() => useGenerateBranchId(), { wrapper })

    await expect(
      act(async () => {
        await result.current.mutateAsync('Test title')
      })
    ).rejects.toThrow('Failed to generate')
  })
})
