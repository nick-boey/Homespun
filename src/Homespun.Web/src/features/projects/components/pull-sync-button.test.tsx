import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { cleanup, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import * as React from 'react'
import { PullSyncButton } from './pull-sync-button'
import { FleeceIssueSync, PullRequests } from '@/api'

vi.mock('@/api', () => ({
  FleeceIssueSync: {
    postApiFleeceSyncByProjectIdPull: vi.fn(),
    postApiFleeceSyncByProjectIdSync: vi.fn(),
    postApiFleeceSyncByProjectIdDiscardNonFleeceAndPull: vi.fn(),
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

  afterEach(() => {
    cleanup()
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

  it('shows conflict dialog when pull returns hasNonFleeceChanges with failure', async () => {
    const user = userEvent.setup()

    vi.mocked(FleeceIssueSync.postApiFleeceSyncByProjectIdPull).mockResolvedValue({
      data: {
        success: false,
        issuesMerged: 0,
        wasBehindRemote: true,
        commitsPulled: 0,
        hasNonFleeceChanges: true,
        nonFleeceChangedFiles: ['src/SomeFile.cs', 'README.md'],
        errorMessage: 'Pull failed due to conflicting uncommitted changes.',
      },
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

    // Wait for the conflict dialog to appear
    await vi.waitFor(() => {
      expect(screen.getByText(/uncommitted changes/i)).toBeInTheDocument()
    })

    // Should show the conflicting files
    expect(screen.getByText(/src\/SomeFile\.cs/)).toBeInTheDocument()
    expect(screen.getByText(/README\.md/)).toBeInTheDocument()

    // Should not show error toast (dialog shown instead)
    expect(toast.error).not.toHaveBeenCalled()
  })

  it('clicking cancel dismisses conflict dialog without side effects', async () => {
    const user = userEvent.setup()

    vi.mocked(FleeceIssueSync.postApiFleeceSyncByProjectIdPull).mockResolvedValue({
      data: {
        success: false,
        issuesMerged: 0,
        wasBehindRemote: true,
        commitsPulled: 0,
        hasNonFleeceChanges: true,
        nonFleeceChangedFiles: ['src/SomeFile.cs'],
        errorMessage: 'Pull failed due to conflicting uncommitted changes.',
      },
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

    await user.click(screen.getByRole('button', { name: /pull/i }))

    // Wait for dialog
    await vi.waitFor(() => {
      expect(screen.getByText(/uncommitted changes/i)).toBeInTheDocument()
    })

    // Click cancel
    const cancelButton = screen.getByRole('button', { name: /cancel/i })
    await user.click(cancelButton)

    // Dialog should be dismissed
    await vi.waitFor(() => {
      expect(screen.queryByText(/uncommitted changes/i)).not.toBeInTheDocument()
    })

    // Discard should not have been called
    expect(
      FleeceIssueSync.postApiFleeceSyncByProjectIdDiscardNonFleeceAndPull
    ).not.toHaveBeenCalled()
  })

  it('clicking discard and retry calls discard endpoint then retries pull', async () => {
    const user = userEvent.setup()

    // First pull fails with non-fleece changes
    vi.mocked(FleeceIssueSync.postApiFleeceSyncByProjectIdPull)
      .mockResolvedValueOnce({
        data: {
          success: false,
          issuesMerged: 0,
          wasBehindRemote: true,
          commitsPulled: 0,
          hasNonFleeceChanges: true,
          nonFleeceChangedFiles: ['src/SomeFile.cs'],
          errorMessage: 'Pull failed due to conflicting uncommitted changes.',
        },
        response: new Response(),
        request: new Request('http://test'),
        error: undefined,
      } as Awaited<ReturnType<typeof FleeceIssueSync.postApiFleeceSyncByProjectIdPull>>)
      .mockResolvedValueOnce({
        data: { success: true, issuesMerged: 0, wasBehindRemote: true, commitsPulled: 2 },
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

    vi.mocked(
      FleeceIssueSync.postApiFleeceSyncByProjectIdDiscardNonFleeceAndPull
    ).mockResolvedValue({
      data: { success: true, issuesMerged: 0, wasBehindRemote: true, commitsPulled: 2 },
      response: new Response(),
      request: new Request('http://test'),
      error: undefined,
    } as Awaited<
      ReturnType<typeof FleeceIssueSync.postApiFleeceSyncByProjectIdDiscardNonFleeceAndPull>
    >)

    render(<PullSyncButton projectId="test-project" />, {
      wrapper: createWrapper(),
    })

    // Trigger the pull
    await user.click(screen.getByRole('button', { name: /pull/i }))

    // Wait for dialog
    await vi.waitFor(() => {
      expect(screen.getByText(/uncommitted changes/i)).toBeInTheDocument()
    })

    // Click discard and retry
    const discardButton = screen.getByRole('button', { name: /discard.*retry/i })
    await user.click(discardButton)

    // Verify discard was called
    await vi.waitFor(() => {
      expect(
        FleeceIssueSync.postApiFleeceSyncByProjectIdDiscardNonFleeceAndPull
      ).toHaveBeenCalledWith({
        path: { projectId: 'test-project' },
      })
    })

    // Verify pull was only called once (initial attempt), and the combined endpoint handles the retry
    expect(FleeceIssueSync.postApiFleeceSyncByProjectIdPull).toHaveBeenCalledTimes(1)

    // Wait for the discard mutation's onSuccess to complete (shows toast, clears dialog)
    await vi.waitFor(() => {
      expect(toast.success).toHaveBeenCalled()
    })

    // Wait for dialog to be dismissed before test cleanup
    await vi.waitFor(() => {
      expect(screen.queryByText(/uncommitted changes/i)).not.toBeInTheDocument()
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
