import { describe, it, expect, vi, beforeEach, type Mock } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createElement, type ReactNode } from 'react'
import {
  useSecrets,
  useCreateSecret,
  useUpdateSecret,
  useDeleteSecret,
  secretsQueryKey,
} from './use-secrets'
import { Secrets } from '@/api'
import type { SecretInfo } from '@/api/generated/types.gen'

vi.mock('@/api', () => ({
  Secrets: {
    getApiProjectsByProjectIdSecrets: vi.fn(),
    postApiProjectsByProjectIdSecrets: vi.fn(),
    putApiProjectsByProjectIdSecretsByName: vi.fn(),
    deleteApiProjectsByProjectIdSecretsByName: vi.fn(),
  },
}))

const mockSecrets: SecretInfo[] = [
  {
    name: 'API_KEY',
    lastModified: '2024-01-15T10:30:00Z',
  },
  {
    name: 'DATABASE_URL',
    lastModified: '2024-01-14T08:00:00Z',
  },
]

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  })
  return ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client: queryClient }, children)
}

describe('secretsQueryKey', () => {
  it('returns correct query key format', () => {
    expect(secretsQueryKey('project-1')).toEqual(['secrets', 'project-1'])
  })
})

describe('useSecrets', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('fetches secrets successfully', async () => {
    const mockGetSecrets = Secrets.getApiProjectsByProjectIdSecrets as Mock
    mockGetSecrets.mockResolvedValueOnce({ data: { secrets: mockSecrets } })

    const { result } = renderHook(() => useSecrets('project-1'), {
      wrapper: createWrapper(),
    })

    expect(result.current.isLoading).toBe(true)

    await waitFor(() => {
      expect(result.current.isSuccess).toBe(true)
    })

    expect(result.current.secrets).toEqual(mockSecrets)
    expect(mockGetSecrets).toHaveBeenCalledWith({
      path: { projectId: 'project-1' },
    })
  })

  it('returns empty array when no secrets exist', async () => {
    const mockGetSecrets = Secrets.getApiProjectsByProjectIdSecrets as Mock
    mockGetSecrets.mockResolvedValueOnce({ data: { secrets: [] } })

    const { result } = renderHook(() => useSecrets('project-1'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isSuccess).toBe(true)
    })

    expect(result.current.secrets).toEqual([])
  })

  it('does not fetch when projectId is empty', () => {
    const mockGetSecrets = Secrets.getApiProjectsByProjectIdSecrets as Mock

    const { result } = renderHook(() => useSecrets(''), {
      wrapper: createWrapper(),
    })

    expect(result.current.isLoading).toBe(false)
    expect(mockGetSecrets).not.toHaveBeenCalled()
  })

  it('handles error response', async () => {
    const mockGetSecrets = Secrets.getApiProjectsByProjectIdSecrets as Mock
    mockGetSecrets.mockResolvedValueOnce({
      error: { detail: 'Project not found' },
    })

    const { result } = renderHook(() => useSecrets('nonexistent'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isError).toBe(true)
    })

    expect(result.current.error?.message).toBe('Project not found')
  })
})

describe('useCreateSecret', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('creates a secret successfully', async () => {
    const mockPostSecrets = Secrets.postApiProjectsByProjectIdSecrets as Mock
    mockPostSecrets.mockResolvedValueOnce({})

    const { result } = renderHook(() => useCreateSecret(), {
      wrapper: createWrapper(),
    })

    result.current.mutate({
      projectId: 'project-1',
      name: 'NEW_SECRET',
      value: 'secret-value-123',
    })

    await waitFor(() => {
      expect(result.current.isSuccess).toBe(true)
    })

    expect(mockPostSecrets).toHaveBeenCalledWith({
      path: { projectId: 'project-1' },
      body: { name: 'NEW_SECRET', value: 'secret-value-123' },
    })
  })

  it('handles creation error with invalid name', async () => {
    const mockPostSecrets = Secrets.postApiProjectsByProjectIdSecrets as Mock
    mockPostSecrets.mockResolvedValueOnce({
      error: { detail: 'Invalid secret name' },
    })

    const { result } = renderHook(() => useCreateSecret(), {
      wrapper: createWrapper(),
    })

    result.current.mutate({
      projectId: 'project-1',
      name: '123-invalid',
      value: 'value',
    })

    await waitFor(() => {
      expect(result.current.isError).toBe(true)
    })

    expect(result.current.error?.message).toBe('Invalid secret name')
  })

  it('handles project not found error', async () => {
    const mockPostSecrets = Secrets.postApiProjectsByProjectIdSecrets as Mock
    mockPostSecrets.mockResolvedValueOnce({
      error: { detail: 'Project not found' },
    })

    const { result } = renderHook(() => useCreateSecret(), {
      wrapper: createWrapper(),
    })

    result.current.mutate({
      projectId: 'nonexistent',
      name: 'SECRET',
      value: 'value',
    })

    await waitFor(() => {
      expect(result.current.isError).toBe(true)
    })

    expect(result.current.error?.message).toBe('Project not found')
  })
})

describe('useUpdateSecret', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('updates a secret successfully', async () => {
    const mockPutSecrets = Secrets.putApiProjectsByProjectIdSecretsByName as Mock
    mockPutSecrets.mockResolvedValueOnce({})

    const { result } = renderHook(() => useUpdateSecret(), {
      wrapper: createWrapper(),
    })

    result.current.mutate({
      projectId: 'project-1',
      name: 'API_KEY',
      value: 'new-secret-value',
    })

    await waitFor(() => {
      expect(result.current.isSuccess).toBe(true)
    })

    expect(mockPutSecrets).toHaveBeenCalledWith({
      path: { projectId: 'project-1', name: 'API_KEY' },
      body: { value: 'new-secret-value' },
    })
  })

  it('handles secret not found error', async () => {
    const mockPutSecrets = Secrets.putApiProjectsByProjectIdSecretsByName as Mock
    mockPutSecrets.mockResolvedValueOnce({
      error: { detail: 'Secret not found' },
    })

    const { result } = renderHook(() => useUpdateSecret(), {
      wrapper: createWrapper(),
    })

    result.current.mutate({
      projectId: 'project-1',
      name: 'NONEXISTENT',
      value: 'value',
    })

    await waitFor(() => {
      expect(result.current.isError).toBe(true)
    })

    expect(result.current.error?.message).toBe('Secret not found')
  })
})

describe('useDeleteSecret', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('deletes a secret successfully', async () => {
    const mockDeleteSecrets = Secrets.deleteApiProjectsByProjectIdSecretsByName as Mock
    mockDeleteSecrets.mockResolvedValueOnce({})

    const { result } = renderHook(() => useDeleteSecret(), {
      wrapper: createWrapper(),
    })

    result.current.mutate({
      projectId: 'project-1',
      name: 'API_KEY',
    })

    await waitFor(() => {
      expect(result.current.isSuccess).toBe(true)
    })

    expect(mockDeleteSecrets).toHaveBeenCalledWith({
      path: { projectId: 'project-1', name: 'API_KEY' },
    })
  })

  it('handles secret not found error', async () => {
    const mockDeleteSecrets = Secrets.deleteApiProjectsByProjectIdSecretsByName as Mock
    mockDeleteSecrets.mockResolvedValueOnce({
      error: { detail: 'Secret not found' },
    })

    const { result } = renderHook(() => useDeleteSecret(), {
      wrapper: createWrapper(),
    })

    result.current.mutate({
      projectId: 'project-1',
      name: 'NONEXISTENT',
    })

    await waitFor(() => {
      expect(result.current.isError).toBe(true)
    })

    expect(result.current.error?.message).toBe('Secret not found')
  })
})
