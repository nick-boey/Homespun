import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { PullRequests, PullRequestStatus } from '@/api'
import { PullRequestsTab } from '@/features/pull-requests'
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

describe('PullRequestsPage', () => {
  beforeEach(() => {
    vi.clearAllMocks()

    vi.mocked(PullRequests.getApiProjectsByProjectIdPullRequestsOpen).mockResolvedValue({
      data: [
        {
          pullRequest: {
            number: 123,
            title: 'Test PR',
            status: PullRequestStatus.READY_FOR_REVIEW,
            branchName: 'feature/test',
            htmlUrl: 'https://github.com/owner/repo/pull/123',
          },
          status: PullRequestStatus.READY_FOR_REVIEW,
          time: 0,
        },
      ],
      error: undefined,
      request: new Request('http://test'),
      response: new Response(),
    })

    vi.mocked(PullRequests.getApiProjectsByProjectIdPullRequestsMerged).mockResolvedValue({
      data: [],
      error: undefined,
      request: new Request('http://test'),
      response: new Response(),
    })
  })

  it('renders the Pull Requests tab', async () => {
    render(<PullRequestsTab projectId="test-project" />, {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(screen.getByText('Pull Requests')).toBeInTheDocument()
    })
  })

  it('displays pull requests when loaded', async () => {
    render(<PullRequestsTab projectId="test-project" />, {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(screen.getByText('#123')).toBeInTheDocument()
    })

    expect(screen.getByText('Test PR')).toBeInTheDocument()
  })
})
