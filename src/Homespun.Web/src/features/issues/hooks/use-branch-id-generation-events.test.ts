import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { useBranchIdGenerationEvents } from './use-branch-id-generation-events'
import { useBranchIdGenerationStore } from '../stores/branch-id-generation-store'

// Mock the SignalR provider hooks
const mockConnection = {
  on: vi.fn(),
  off: vi.fn(),
}

vi.mock('@/providers/signalr-provider', () => ({
  useNotificationHub: () => ({
    connection: mockConnection,
    status: 'connected',
    isConnected: true,
    isReconnecting: false,
  }),
}))

// Mock TanStack Query
const mockInvalidateQueries = vi.fn()
const mockSetQueryData = vi.fn()
vi.mock('@tanstack/react-query', () => ({
  useQueryClient: () => ({
    invalidateQueries: mockInvalidateQueries,
    setQueryData: mockSetQueryData,
  }),
}))

// Mock sonner toast
vi.mock('sonner', () => ({
  toast: {
    success: vi.fn(),
    error: vi.fn(),
  },
}))

import { toast } from 'sonner'

describe('useBranchIdGenerationEvents', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    useBranchIdGenerationStore.setState({
      generatingIssues: new Set(['issue-1']),
    })
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('registers event handlers on mount when connected', () => {
    renderHook(() => useBranchIdGenerationEvents({ projectId: 'project-1' }))

    expect(mockConnection.on).toHaveBeenCalledWith('BranchIdGenerated', expect.any(Function))
    expect(mockConnection.on).toHaveBeenCalledWith('BranchIdGenerationFailed', expect.any(Function))
  })

  it('unregisters event handlers on unmount', () => {
    const { unmount } = renderHook(() => useBranchIdGenerationEvents({ projectId: 'project-1' }))

    unmount()

    expect(mockConnection.off).toHaveBeenCalledWith('BranchIdGenerated', expect.any(Function))
    expect(mockConnection.off).toHaveBeenCalledWith(
      'BranchIdGenerationFailed',
      expect.any(Function)
    )
  })

  it('calls markComplete on store when BranchIdGenerated event received', async () => {
    let branchIdGeneratedHandler: (
      issueId: string,
      projectId: string,
      branchId: string,
      wasAiGenerated: boolean
    ) => void = () => {}

    mockConnection.on.mockImplementation((event: string, handler: unknown) => {
      if (event === 'BranchIdGenerated') {
        branchIdGeneratedHandler = handler as typeof branchIdGeneratedHandler
      }
    })

    renderHook(() => useBranchIdGenerationEvents({ projectId: 'project-1' }))

    branchIdGeneratedHandler('issue-1', 'project-1', 'feature/my-branch', true)

    await waitFor(() => {
      expect(useBranchIdGenerationStore.getState().isGenerating('issue-1')).toBe(false)
    })
  })

  it('shows success toast when BranchIdGenerated event received', async () => {
    let branchIdGeneratedHandler: (
      issueId: string,
      projectId: string,
      branchId: string,
      wasAiGenerated: boolean
    ) => void = () => {}

    mockConnection.on.mockImplementation((event: string, handler: unknown) => {
      if (event === 'BranchIdGenerated') {
        branchIdGeneratedHandler = handler as typeof branchIdGeneratedHandler
      }
    })

    renderHook(() => useBranchIdGenerationEvents({ projectId: 'project-1' }))

    branchIdGeneratedHandler('issue-1', 'project-1', 'feature/my-branch', true)

    await waitFor(() => {
      expect(toast.success).toHaveBeenCalledWith('Branch ID generated', expect.any(Object))
    })
  })

  it('updates issue cache via setQueryData when BranchIdGenerated event received', async () => {
    let branchIdGeneratedHandler: (
      issueId: string,
      projectId: string,
      branchId: string,
      wasAiGenerated: boolean
    ) => void = () => {}

    mockConnection.on.mockImplementation((event: string, handler: unknown) => {
      if (event === 'BranchIdGenerated') {
        branchIdGeneratedHandler = handler as typeof branchIdGeneratedHandler
      }
    })

    renderHook(() => useBranchIdGenerationEvents({ projectId: 'project-1' }))

    branchIdGeneratedHandler('issue-1', 'project-1', 'feature/my-branch', true)

    await waitFor(() => {
      expect(mockSetQueryData).toHaveBeenCalledWith(
        ['issue', 'issue-1', 'project-1'],
        expect.any(Function)
      )
    })

    // Verify the updater function works correctly
    const updaterFn = mockSetQueryData.mock.calls[0][1]
    expect(updaterFn(undefined)).toBeUndefined()
    expect(updaterFn({ id: 'issue-1', workingBranchId: 'old-branch' })).toEqual({
      id: 'issue-1',
      workingBranchId: 'feature/my-branch',
    })
  })

  it('does not invalidate issue query when BranchIdGenerated event received', async () => {
    let branchIdGeneratedHandler: (
      issueId: string,
      projectId: string,
      branchId: string,
      wasAiGenerated: boolean
    ) => void = () => {}

    mockConnection.on.mockImplementation((event: string, handler: unknown) => {
      if (event === 'BranchIdGenerated') {
        branchIdGeneratedHandler = handler as typeof branchIdGeneratedHandler
      }
    })

    renderHook(() => useBranchIdGenerationEvents({ projectId: 'project-1' }))

    branchIdGeneratedHandler('issue-1', 'project-1', 'feature/my-branch', true)

    await waitFor(() => {
      // Only task graph should be invalidated, not the issue query
      expect(mockInvalidateQueries).toHaveBeenCalledTimes(1)
      expect(mockInvalidateQueries).toHaveBeenCalledWith({
        queryKey: ['taskGraph', 'project-1'],
      })
    })
  })

  it('invalidates task graph query when BranchIdGenerated event received', async () => {
    let branchIdGeneratedHandler: (
      issueId: string,
      projectId: string,
      branchId: string,
      wasAiGenerated: boolean
    ) => void = () => {}

    mockConnection.on.mockImplementation((event: string, handler: unknown) => {
      if (event === 'BranchIdGenerated') {
        branchIdGeneratedHandler = handler as typeof branchIdGeneratedHandler
      }
    })

    renderHook(() => useBranchIdGenerationEvents({ projectId: 'project-1' }))

    branchIdGeneratedHandler('issue-1', 'project-1', 'feature/my-branch', true)

    await waitFor(() => {
      expect(mockInvalidateQueries).toHaveBeenCalledWith({
        queryKey: ['taskGraph', 'project-1'],
      })
    })
  })

  it('filters events by projectId when provided', async () => {
    let branchIdGeneratedHandler: (
      issueId: string,
      projectId: string,
      branchId: string,
      wasAiGenerated: boolean
    ) => void = () => {}

    mockConnection.on.mockImplementation((event: string, handler: unknown) => {
      if (event === 'BranchIdGenerated') {
        branchIdGeneratedHandler = handler as typeof branchIdGeneratedHandler
      }
    })

    renderHook(() => useBranchIdGenerationEvents({ projectId: 'project-1' }))

    // Event for a different project should be ignored
    branchIdGeneratedHandler('issue-2', 'project-2', 'feature/other', true)

    await waitFor(() => {
      expect(toast.success).not.toHaveBeenCalled()
      expect(mockInvalidateQueries).not.toHaveBeenCalled()
      expect(mockSetQueryData).not.toHaveBeenCalled()
    })
  })

  it('shows error toast when BranchIdGenerationFailed event received', async () => {
    let branchIdFailedHandler: (
      issueId: string,
      projectId: string,
      error: string
    ) => void = () => {}

    mockConnection.on.mockImplementation((event: string, handler: unknown) => {
      if (event === 'BranchIdGenerationFailed') {
        branchIdFailedHandler = handler as typeof branchIdFailedHandler
      }
    })

    renderHook(() => useBranchIdGenerationEvents({ projectId: 'project-1' }))

    branchIdFailedHandler('issue-1', 'project-1', 'AI generation failed')

    await waitFor(() => {
      expect(toast.error).toHaveBeenCalledWith('Branch ID generation failed', expect.any(Object))
    })
  })

  it('calls markComplete on store when BranchIdGenerationFailed event received', async () => {
    let branchIdFailedHandler: (
      issueId: string,
      projectId: string,
      error: string
    ) => void = () => {}

    mockConnection.on.mockImplementation((event: string, handler: unknown) => {
      if (event === 'BranchIdGenerationFailed') {
        branchIdFailedHandler = handler as typeof branchIdFailedHandler
      }
    })

    renderHook(() => useBranchIdGenerationEvents({ projectId: 'project-1' }))

    branchIdFailedHandler('issue-1', 'project-1', 'AI generation failed')

    await waitFor(() => {
      expect(useBranchIdGenerationStore.getState().isGenerating('issue-1')).toBe(false)
    })
  })
})
