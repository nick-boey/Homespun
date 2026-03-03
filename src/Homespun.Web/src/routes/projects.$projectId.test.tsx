import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { RouterProvider, createRouter, createMemoryHistory } from '@tanstack/react-router'
import { routeTree } from '@/routeTree.gen'
import { Projects, Graph } from '@/api'
import { BreadcrumbProvider } from '@/hooks/use-breadcrumbs'

vi.mock('@/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/api')>()
  return {
    ...actual,
    Projects: {
      getApiProjectsById: vi.fn(),
    },
    Graph: {
      getApiGraphByProjectIdTaskgraphData: vi.fn(),
    },
    PullRequests: {
      getApiProjectsByProjectIdPullRequestsOpen: vi
        .fn()
        .mockResolvedValue({ data: [], error: undefined }),
      getApiProjectsByProjectIdPullRequestsMerged: vi
        .fn()
        .mockResolvedValue({ data: [], error: undefined }),
      postApiProjectsByProjectIdSync: vi.fn(),
    },
  }
})

// Mock SignalR connection
vi.mock('@/hooks/use-signalr', () => ({
  useSignalR: () => ({
    connection: null,
    status: 'disconnected',
    error: undefined,
    connect: vi.fn(),
    disconnect: vi.fn(),
  }),
}))

// Mock SignalR provider hooks
vi.mock('@/providers/signalr-provider', () => ({
  useNotificationHub: () => ({
    connection: null,
    status: 'disconnected',
    methods: null,
    isConnected: false,
    isReconnecting: false,
  }),
  useClaudeCodeHub: () => ({
    connection: null,
    status: 'disconnected',
    methods: null,
    isConnected: false,
    isReconnecting: false,
  }),
  useSignalRContext: () => ({
    claudeCodeConnection: null,
    claudeCodeStatus: 'disconnected',
    claudeCodeError: undefined,
    claudeCodeMethods: null,
    notificationConnection: null,
    notificationStatus: 'disconnected',
    notificationError: undefined,
    notificationMethods: null,
    isConnecting: false,
    isConnected: false,
    isReconnecting: false,
    connect: vi.fn(),
    disconnect: vi.fn(),
  }),
}))

function createTestRouter(initialPath: string) {
  const memoryHistory = createMemoryHistory({
    initialEntries: [initialPath],
  })
  return createRouter({
    routeTree,
    history: memoryHistory,
  })
}

function renderWithProviders(initialPath: string) {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
      },
    },
  })

  const router = createTestRouter(initialPath)

  return render(
    <QueryClientProvider client={queryClient}>
      <BreadcrumbProvider>
        <RouterProvider router={router} />
      </BreadcrumbProvider>
    </QueryClientProvider>
  )
}

describe('ProjectLayout', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    // Default mock for Graph API (returns empty graph)
    vi.mocked(Graph.getApiGraphByProjectIdTaskgraphData).mockResolvedValue({
      data: { nodes: [], mergedPrs: [], hasMorePastPrs: false },
      response: new Response(),
      request: new Request('http://test'),
      error: undefined,
    } as Awaited<ReturnType<typeof Graph.getApiGraphByProjectIdTaskgraphData>>)
  })

  it('displays loading state while fetching project', async () => {
    vi.mocked(Projects.getApiProjectsById).mockReturnValue(
      new Promise(() => {}) as ReturnType<typeof Projects.getApiProjectsById>
    )

    renderWithProviders('/projects/test-id')

    await waitFor(() => {
      expect(screen.getByTestId('project-loading')).toBeInTheDocument()
    })
  })

  it('displays project name and tabs when loaded', { timeout: 20000 }, async () => {
    vi.mocked(Projects.getApiProjectsById).mockResolvedValue({
      data: {
        id: 'test-id',
        name: 'My Awesome Project',
        localPath: '/path',
        defaultBranch: 'main',
      },
      response: new Response(),
      request: new Request('http://test'),
      error: undefined,
    } as Awaited<ReturnType<typeof Projects.getApiProjectsById>>)

    renderWithProviders('/projects/test-id')

    // Wait for project name
    await waitFor(
      () => {
        expect(screen.getByRole('heading', { name: 'My Awesome Project' })).toBeInTheDocument()
      },
      { timeout: 15000 }
    )

    // Verify all tabs are present
    expect(screen.getByRole('link', { name: 'Issues' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Branches' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Prompts' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Secrets' })).toBeInTheDocument()

    // There are two Settings links (one in sidebar, one in tabs)
    const settingsLinks = screen.getAllByRole('link', { name: 'Settings' })
    expect(
      settingsLinks.some((link) => link.getAttribute('href') === '/projects/test-id/settings')
    ).toBe(true)

    // Verify quick actions button is present
    expect(screen.getByRole('button', { name: 'Project actions' })).toBeInTheDocument()

    // Issues tab should be active by default
    const issuesLink = screen.getByRole('link', { name: 'Issues' })
    expect(issuesLink).toHaveClass('border-primary')
  })

  it('displays 404 error when project not found', { timeout: 20000 }, async () => {
    vi.mocked(Projects.getApiProjectsById).mockResolvedValue({
      data: undefined,
      response: new Response(null, { status: 404 }),
      request: new Request('http://test'),
      error: { detail: 'Project not found' },
    } as Awaited<ReturnType<typeof Projects.getApiProjectsById>>)

    renderWithProviders('/projects/nonexistent')

    await waitFor(
      () => {
        expect(screen.getByTestId('project-not-found')).toBeInTheDocument()
      },
      { timeout: 15000 }
    )
    expect(screen.getByText('Project Not Found')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Try Again/i })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Go Home' })).toBeInTheDocument()
  })
})
