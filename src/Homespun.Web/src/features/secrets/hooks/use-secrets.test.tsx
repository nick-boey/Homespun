import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { useSecrets } from './use-secrets'
import { Secrets } from '@/api'
import type { ReactNode } from 'react'
import type { SecretInfo, SecretsListResponse } from '@/api/generated/types.gen'

vi.mock('@/api', () => ({
  Secrets: {
    getApiProjectsByProjectIdSecrets: vi.fn(),
    postApiProjectsByProjectIdSecrets: vi.fn(),
    putApiProjectsByProjectIdSecretsByName: vi.fn(),
    deleteApiProjectsByProjectIdSecretsByName: vi.fn(),
  },
}))

const mockGetSecrets = vi.mocked(Secrets.getApiProjectsByProjectIdSecrets)

function createMockResponse<T>(data: T) {
  return {
    data,
    request: new Request('http://localhost/api/test'),
    response: new Response(),
  }
}

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
    },
  })
  return function Wrapper({ children }: { children: ReactNode }) {
    return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  }
}

describe('useSecrets', () => {
  const mockSecrets: SecretInfo[] = [
    {
      name: 'API_KEY',
      lastModified: '2024-01-15T10:30:00Z',
    },
    {
      name: 'DATABASE_URL',
      lastModified: '2024-01-14T15:45:00Z',
    },
  ]

  const mockResponse: SecretsListResponse = {
    secrets: mockSecrets,
  }

  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('should fetch secrets for a project', async () => {
    mockGetSecrets.mockResolvedValueOnce(createMockResponse(mockResponse))

    const { result } = renderHook(() => useSecrets('project-1'), {
      wrapper: createWrapper(),
    })

    expect(result.current.isLoading).toBe(true)

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false)
    })

    expect(result.current.secrets).toEqual(mockSecrets)
    expect(mockGetSecrets).toHaveBeenCalledWith({
      path: { projectId: 'project-1' },
    })
  })

  it('should not fetch if projectId is empty', async () => {
    const { result } = renderHook(() => useSecrets(''), {
      wrapper: createWrapper(),
    })

    expect(result.current.isLoading).toBe(false)
    expect(mockGetSecrets).not.toHaveBeenCalled()
    expect(result.current.secrets).toEqual([])
  })

  it('should handle errors', async () => {
    mockGetSecrets.mockRejectedValueOnce(new Error('API Error'))

    const { result } = renderHook(() => useSecrets('project-1'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isError).toBe(true)
    })
  })

  it('should return empty array when response has no secrets', async () => {
    mockGetSecrets.mockResolvedValueOnce(
      createMockResponse({ secrets: null } as unknown as SecretsListResponse)
    )

    const { result } = renderHook(() => useSecrets('project-1'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false)
    })

    expect(result.current.secrets).toEqual([])
  })
})
