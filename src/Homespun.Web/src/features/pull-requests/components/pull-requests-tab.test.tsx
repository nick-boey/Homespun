import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import {
  PullRequests,
  type PullRequestWithStatus,
  type PullRequestWithTime,
  PullRequestStatus,
} from '@/api'
import { PullRequestsTab } from './pull-requests-tab'
import type { ReactNode } from 'react'

vi.mock('@/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/api')>()
  return {
    ...actual,
    PullRequests: {
      getApiProjectsByProjectIdPullRequestsOpen: vi.fn(),
      getApiProjectsByProjectIdPullRequestsMerged: vi.fn(),
      postApiProjectsByProjectIdSync: vi.fn(),
    },
  }
})

const createWrapper = () => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
    },
  })
  return ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  )
}

describe('PullRequestsTab', () => {
  const mockOpenPRs: PullRequestWithStatus[] = [
    {
      pullRequest: {
        number: 123,
        title: 'Add new feature',
        status: PullRequestStatus.READY_FOR_REVIEW,
        branchName: 'feature/new-feature',
        htmlUrl: 'https://github.com/owner/repo/pull/123',
        checksPassing: true,
        isApproved: false,
        approvalCount: 0,
        changesRequestedCount: 0,
      },
      status: PullRequestStatus.READY_FOR_REVIEW,
      time: 3600,
    },
  ]

  const mockMergedPRs: PullRequestWithTime[] = [
    {
      pullRequest: {
        number: 120,
        title: 'Merged feature',
        status: PullRequestStatus.MERGED,
        branchName: 'feature/merged',
        htmlUrl: 'https://github.com/owner/repo/pull/120',
        mergedAt: '2024-01-15T10:00:00Z',
      },
      time: 7200,
    },
  ]

  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(PullRequests.getApiProjectsByProjectIdPullRequestsOpen).mockResolvedValue({
      data: mockOpenPRs,
      error: undefined,
      request: new Request('http://test'),
      response: new Response(),
    })
    vi.mocked(PullRequests.getApiProjectsByProjectIdPullRequestsMerged).mockResolvedValue({
      data: mockMergedPRs,
      error: undefined,
      request: new Request('http://test'),
      response: new Response(),
    })
    vi.mocked(PullRequests.postApiProjectsByProjectIdSync).mockResolvedValue({
      data: { imported: 0, updated: 0, removed: 0 },
      error: undefined,
      request: new Request('http://test'),
      response: new Response(),
    })
  })

  it('renders loading skeletons initially', () => {
    render(<PullRequestsTab projectId="project-1" />, {
      wrapper: createWrapper(),
    })

    expect(screen.getAllByTestId('pr-row-skeleton').length).toBeGreaterThan(0)
  })

  it('renders open PRs section', async () => {
    render(<PullRequestsTab projectId="project-1" />, {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(screen.getByText('#123')).toBeInTheDocument()
    })

    expect(screen.getByText('Open Pull Requests')).toBeInTheDocument()
    expect(screen.getByText('Add new feature')).toBeInTheDocument()
  })

  it('renders merged PRs section', async () => {
    render(<PullRequestsTab projectId="project-1" />, {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(screen.getByText('#120')).toBeInTheDocument()
    })

    expect(screen.getByText('Recently Merged')).toBeInTheDocument()
    expect(screen.getByText('Merged feature')).toBeInTheDocument()
  })

  it('renders empty state when no open PRs', async () => {
    vi.mocked(PullRequests.getApiProjectsByProjectIdPullRequestsOpen).mockResolvedValue({
      data: [],
      error: undefined,
      request: new Request('http://test'),
      response: new Response(),
    })

    render(<PullRequestsTab projectId="project-1" />, {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(screen.getByText('No open pull requests')).toBeInTheDocument()
    })
  })

  it('renders sync button', async () => {
    render(<PullRequestsTab projectId="project-1" />, {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(screen.getByRole('button', { name: /sync from github/i })).toBeInTheDocument()
    })
  })

  it('calls sync when sync button is clicked', async () => {
    const user = userEvent.setup()
    render(<PullRequestsTab projectId="project-1" />, {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(screen.getByRole('button', { name: /sync from github/i })).toBeInTheDocument()
    })

    await user.click(screen.getByRole('button', { name: /sync from github/i }))

    await waitFor(() => {
      expect(PullRequests.postApiProjectsByProjectIdSync).toHaveBeenCalledWith({
        path: { projectId: 'project-1' },
      })
    })
  })

  it('shows PR detail panel when a PR is selected', async () => {
    const user = userEvent.setup()
    render(<PullRequestsTab projectId="project-1" />, {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(screen.getByText('#123')).toBeInTheDocument()
    })

    // Click on the PR row (the button element containing the PR)
    await user.click(screen.getByRole('button', { name: /#123/i }))

    // Detail panel should appear with description section
    await waitFor(() => {
      expect(screen.getByText('Description')).toBeInTheDocument()
    })
  })

  it('renders error state when fetch fails', async () => {
    vi.mocked(PullRequests.getApiProjectsByProjectIdPullRequestsOpen).mockResolvedValue({
      data: undefined,
      error: { detail: 'Failed to fetch' },
      request: new Request('http://test'),
      response: new Response(null, { status: 500 }),
    })

    render(<PullRequestsTab projectId="project-1" />, {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(screen.getByText(/failed to load/i)).toBeInTheDocument()
    })
  })
})
