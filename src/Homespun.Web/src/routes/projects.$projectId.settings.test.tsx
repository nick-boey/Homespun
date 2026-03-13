import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { PullRequests, type FullRefreshResult } from '@/api'
import * as React from 'react'

// Mock the API
vi.mock('@/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/api')>()
  return {
    ...actual,
    PullRequests: {
      postApiProjectsByProjectIdFullRefresh: vi.fn(),
    },
  }
})

// Mock sonner
vi.mock('sonner', () => ({
  toast: {
    success: vi.fn(),
    error: vi.fn(),
  },
}))

// Mock TanStack Router
vi.mock('@tanstack/react-router', () => ({
  createFileRoute: vi.fn((path: string) => {
    return (config: { component: React.ComponentType }) => ({
      path,
      component: config.component,
    })
  }),
  useParams: () => ({ projectId: 'test-project-id' }),
}))

// Import the route to get the component
import { Route } from './projects.$projectId.settings'

// Extract the component from the route
// eslint-disable-next-line @typescript-eslint/no-explicit-any
const ProjectSettings = (Route as any).component as React.ComponentType

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  })
  return function Wrapper({ children }: { children: React.ReactNode }) {
    return React.createElement(QueryClientProvider, { client: queryClient }, children)
  }
}

describe('ProjectSettings Page', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('renders the page with full refresh button', () => {
    render(<ProjectSettings />, { wrapper: createWrapper() })

    expect(screen.getByText('GitHub Synchronization')).toBeInTheDocument()
    // Full Refresh appears as both a heading and button text - use the button role to be specific
    expect(screen.getByRole('button', { name: /full refresh/i })).toBeInTheDocument()
    expect(
      screen.getByText(/download all open, closed, and merged pull requests/i)
    ).toBeInTheDocument()
  })

  it('shows loading state when refreshing', async () => {
    let resolvePromise: (value: unknown) => void
    const pendingPromise = new Promise((resolve) => {
      resolvePromise = resolve
    })

    vi.mocked(PullRequests.postApiProjectsByProjectIdFullRefresh).mockReturnValue(
      pendingPromise as never
    )

    const user = userEvent.setup()
    render(<ProjectSettings />, { wrapper: createWrapper() })

    const button = screen.getByRole('button', { name: /full refresh/i })
    await user.click(button)

    await waitFor(() => {
      expect(button).toBeDisabled()
    })

    // Resolve to clean up
    resolvePromise!({
      data: { openPrs: 1, closedPrs: 1, linkedIssues: 0, refreshedAt: new Date().toISOString() },
      error: undefined,
      request: new Request('http://test'),
      response: new Response(),
    })
  })

  it('calls full refresh API when button is clicked', async () => {
    const mockResult: FullRefreshResult = {
      openPrs: 5,
      closedPrs: 15,
      linkedIssues: 3,
      refreshedAt: new Date().toISOString(),
      errors: [],
    }

    vi.mocked(PullRequests.postApiProjectsByProjectIdFullRefresh).mockResolvedValue({
      data: mockResult,
      error: undefined,
      request: new Request('http://test'),
      response: new Response(),
    })

    const user = userEvent.setup()
    render(<ProjectSettings />, { wrapper: createWrapper() })

    const button = screen.getByRole('button', { name: /full refresh/i })
    await user.click(button)

    await waitFor(() => {
      expect(PullRequests.postApiProjectsByProjectIdFullRefresh).toHaveBeenCalledWith({
        path: { projectId: 'test-project-id' },
      })
    })
  })

  it('handles full refresh failure gracefully', async () => {
    vi.mocked(PullRequests.postApiProjectsByProjectIdFullRefresh).mockResolvedValue({
      data: undefined,
      error: { detail: 'GitHub API error' },
      request: new Request('http://test'),
      response: new Response(null, { status: 500 }),
    })

    const user = userEvent.setup()
    render(<ProjectSettings />, { wrapper: createWrapper() })

    const button = screen.getByRole('button', { name: /full refresh/i })
    await user.click(button)

    await waitFor(() => {
      expect(PullRequests.postApiProjectsByProjectIdFullRefresh).toHaveBeenCalled()
    })

    // Button should be enabled again after error
    await waitFor(() => {
      expect(button).not.toBeDisabled()
    })
  })
})
