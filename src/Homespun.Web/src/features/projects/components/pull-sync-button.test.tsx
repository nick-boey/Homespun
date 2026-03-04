import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import * as React from 'react'
import { PullSyncButton } from './pull-sync-button'
import { FleeceIssueSync, PullRequests } from '@/api'

vi.mock('@/api', () => ({
  FleeceIssueSync: {
    postApiFleeceSyncByProjectIdPull: vi.fn(),
    postApiFleeceSyncByProjectIdSync: vi.fn(),
  },
  PullRequests: {
    postApiProjectsByProjectIdSync: vi.fn(),
  },
}))

vi.mock('sonner', () => ({
  toast: {
    success: vi.fn(),
    error: vi.fn(),
  },
}))

import { toast } from 'sonner'

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
      },
      mutations: {
        retry: false,
      },
    },
  })
  return function Wrapper({ children }: { children: React.ReactNode }) {
    return React.createElement(QueryClientProvider, { client: queryClient }, children)
  }
}

describe('PullSyncButton', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('renders pull button', () => {
    render(<PullSyncButton projectId="test-project" />, {
      wrapper: createWrapper(),
    })

    expect(screen.getByRole('button', { name: /pull/i })).toBeInTheDocument()
  })

  it('renders dropdown trigger', () => {
    render(<PullSyncButton projectId="test-project" />, {
      wrapper: createWrapper(),
    })

    // There should be two buttons - main button and dropdown trigger
    const buttons = screen.getAllByRole('button')
    expect(buttons.length).toBeGreaterThanOrEqual(2)
  })

  it('shows sync option in dropdown', async () => {
    const user = userEvent.setup()

    render(<PullSyncButton projectId="test-project" />, {
      wrapper: createWrapper(),
    })

    // Click the dropdown trigger (the chevron button)
    const buttons = screen.getAllByRole('button')
    const dropdownTrigger = buttons.find((b) => b.getAttribute('aria-haspopup') === 'menu')
    if (dropdownTrigger) {
      await user.click(dropdownTrigger)
    }

    // Check for sync option in the dropdown
    expect(await screen.findByText(/sync/i)).toBeInTheDocument()
  })

  it('calls pull API when pull button is clicked', async () => {
    const user = userEvent.setup()

    vi.mocked(FleeceIssueSync.postApiFleeceSyncByProjectIdPull).mockResolvedValue({
      data: { success: true, issuesMerged: 0, wasBehindRemote: false, commitsPulled: 0 },
      response: new Response(),
      request: new Request('http://test'),
      error: undefined,
    } as Awaited<ReturnType<typeof FleeceIssueSync.postApiFleeceSyncByProjectIdPull>>)

    vi.mocked(PullRequests.postApiProjectsByProjectIdSync).mockResolvedValue({
      data: { imported: 0, updated: 0, removed: 0 },
      response: new Response(),
      request: new Request('http://test'),
      error: undefined,
    } as Awaited<ReturnType<typeof PullRequests.postApiProjectsByProjectIdSync>>)

    render(<PullSyncButton projectId="test-project" />, {
      wrapper: createWrapper(),
    })

    const pullButton = screen.getByRole('button', { name: /pull/i })
    await user.click(pullButton)

    expect(FleeceIssueSync.postApiFleeceSyncByProjectIdPull).toHaveBeenCalledWith({
      path: { projectId: 'test-project' },
    })
  })

  it('shows success toast after successful pull', async () => {
    const user = userEvent.setup()

    vi.mocked(FleeceIssueSync.postApiFleeceSyncByProjectIdPull).mockResolvedValue({
      data: { success: true, issuesMerged: 3, wasBehindRemote: true, commitsPulled: 2 },
      response: new Response(),
      request: new Request('http://test'),
      error: undefined,
    } as Awaited<ReturnType<typeof FleeceIssueSync.postApiFleeceSyncByProjectIdPull>>)

    vi.mocked(PullRequests.postApiProjectsByProjectIdSync).mockResolvedValue({
      data: { imported: 1, updated: 0, removed: 0 },
      response: new Response(),
      request: new Request('http://test'),
      error: undefined,
    } as Awaited<ReturnType<typeof PullRequests.postApiProjectsByProjectIdSync>>)

    render(<PullSyncButton projectId="test-project" />, {
      wrapper: createWrapper(),
    })

    const pullButton = screen.getByRole('button', { name: /pull/i })
    await user.click(pullButton)

    // Wait for the mutation to complete
    await vi.waitFor(() => {
      expect(toast.success).toHaveBeenCalled()
    })
  })

  it('shows error toast when pull fails', async () => {
    const user = userEvent.setup()

    vi.mocked(FleeceIssueSync.postApiFleeceSyncByProjectIdPull).mockResolvedValue({
      data: undefined,
      response: new Response(null, { status: 500 }),
      request: new Request('http://test'),
      error: { detail: 'Server error' },
    } as Awaited<ReturnType<typeof FleeceIssueSync.postApiFleeceSyncByProjectIdPull>>)

    render(<PullSyncButton projectId="test-project" />, {
      wrapper: createWrapper(),
    })

    const pullButton = screen.getByRole('button', { name: /pull/i })
    await user.click(pullButton)

    await vi.waitFor(() => {
      expect(toast.error).toHaveBeenCalled()
    })
  })

  it('disables buttons during loading', async () => {
    const user = userEvent.setup()

    // Create a promise that never resolves to keep loading state
    vi.mocked(FleeceIssueSync.postApiFleeceSyncByProjectIdPull).mockReturnValue(
      new Promise(() => {}) as ReturnType<typeof FleeceIssueSync.postApiFleeceSyncByProjectIdPull>
    )

    vi.mocked(PullRequests.postApiProjectsByProjectIdSync).mockReturnValue(
      new Promise(() => {}) as ReturnType<typeof PullRequests.postApiProjectsByProjectIdSync>
    )

    render(<PullSyncButton projectId="test-project" />, {
      wrapper: createWrapper(),
    })

    const pullButton = screen.getByRole('button', { name: /pull/i })
    await user.click(pullButton)

    // Button should be disabled while loading
    expect(pullButton).toBeDisabled()
  })
})
