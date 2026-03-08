import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { QueryClientProvider, QueryClient } from '@tanstack/react-query'
import { usePlanFiles, usePlanContent } from './use-plan-files'
import { Plans } from '@/api'
import type { PlanFileInfo } from '@/api/generated'
import { createMockSession } from '@/test/test-utils'

vi.mock('@/api', () => ({
  Plans: {
    getApiPlans: vi.fn(),
    getApiPlansContent: vi.fn(),
  },
}))

describe('usePlanFiles', () => {
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
    const { result } = renderHook(() => usePlanFiles(undefined), { wrapper })

    expect(result.current.data).toBe(undefined)
    expect(result.current.isLoading).toBe(false)
  })

  it('returns empty array when workingDirectory is missing', async () => {
    const session = createMockSession({
      workingDirectory: null,
    })

    const { result } = renderHook(() => usePlanFiles(session), { wrapper })

    expect(result.current.data).toBe(undefined)
    expect(result.current.isLoading).toBe(false)
  })

  it('fetches plan files for a session', async () => {
    const mockPlans: PlanFileInfo[] = [
      {
        fileName: 'plan-20240312.md',
        filePath: '/path/to/project/.claude/plans/plan-20240312.md',
        lastModified: '2024-03-12T10:30:00Z',
        fileSizeBytes: 2048,
        preview: '# Implementation Plan\n\nThis plan outlines the steps...',
      },
      {
        fileName: 'plan-feature-auth.md',
        filePath: '/path/to/project/.claude/plans/plan-feature-auth.md',
        lastModified: '2024-03-11T14:15:00Z',
        fileSizeBytes: 4096,
        preview: '# Authentication Feature\n\n## Overview\n...',
      },
    ]

    vi.mocked(Plans.getApiPlans).mockResolvedValue({
      data: mockPlans,
      response: new Response(),
      request: new Request('http://test'),
      error: undefined,
    } as Awaited<ReturnType<typeof Plans.getApiPlans>>)

    const session = createMockSession({
      workingDirectory: '/path/to/project',
    })

    const { result } = renderHook(() => usePlanFiles(session), { wrapper })

    expect(result.current.isLoading).toBe(true)

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false)
    })

    expect(result.current.data).toEqual(mockPlans)
    expect(Plans.getApiPlans).toHaveBeenCalledWith({
      query: {
        workingDirectory: '/path/to/project',
      },
    })
  })

  it('returns empty array when API returns null', async () => {
    vi.mocked(Plans.getApiPlans).mockResolvedValue({
      data: null,
      response: new Response(),
      request: new Request('http://test'),
      error: undefined,
    } as unknown as Awaited<ReturnType<typeof Plans.getApiPlans>>)

    const session = createMockSession({
      workingDirectory: '/path/to/project',
    })

    const { result } = renderHook(() => usePlanFiles(session), { wrapper })

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false)
    })

    expect(result.current.data).toEqual([])
  })
})

describe('usePlanContent', () => {
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

  it('returns null when workingDirectory is undefined', async () => {
    const { result } = renderHook(() => usePlanContent(undefined, 'plan.md'), { wrapper })

    expect(result.current.data).toBe(undefined)
    expect(result.current.isLoading).toBe(false)
  })

  it('returns null when fileName is undefined', async () => {
    const { result } = renderHook(() => usePlanContent('/path/to/project', undefined), { wrapper })

    expect(result.current.data).toBe(undefined)
    expect(result.current.isLoading).toBe(false)
  })

  it('fetches plan content when both parameters are provided', async () => {
    const mockContent = `# Implementation Plan

## Overview
This plan outlines the steps to implement the authentication feature.

## Steps
1. Create user model
2. Implement JWT tokens
3. Add login endpoint
4. Add logout endpoint
5. Create middleware for auth`

    vi.mocked(Plans.getApiPlansContent).mockResolvedValue({
      data: mockContent,
      response: new Response(),
      request: new Request('http://test'),
      error: undefined,
    } as Awaited<ReturnType<typeof Plans.getApiPlansContent>>)

    const { result } = renderHook(
      () => usePlanContent('/path/to/project', 'plan-feature-auth.md'),
      { wrapper }
    )

    expect(result.current.isLoading).toBe(true)

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false)
    })

    expect(result.current.data).toEqual(mockContent)
    expect(Plans.getApiPlansContent).toHaveBeenCalledWith({
      query: {
        workingDirectory: '/path/to/project',
        fileName: 'plan-feature-auth.md',
      },
    })
  })

  it('handles API errors gracefully', async () => {
    vi.mocked(Plans.getApiPlansContent).mockRejectedValue(new Error('API Error'))

    const { result } = renderHook(
      () => usePlanContent('/path/to/project', 'plan-feature-auth.md'),
      { wrapper }
    )

    await waitFor(() => {
      expect(result.current.isError).toBe(true)
    })

    expect(result.current.data).toBe(undefined)
  })
})
