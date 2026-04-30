import { describe, it, expect, vi, beforeEach, type Mock } from 'vitest'
import { render, screen, waitFor, act } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createElement, type ReactNode } from 'react'
import { Sidebar } from './sidebar'
import { Projects, Sessions, Issues, PullRequests, ClaudeSessionStatus, SessionMode } from '@/api'
import type { Project, SessionSummary } from '@/api/generated/types.gen'
import { invalidateAllSessionsQueries } from '@/features/sessions/hooks/use-sessions'

vi.mock('@/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/api')>()
  return {
    ...actual,
    Projects: {
      getApiProjects: vi.fn(),
    },
    Sessions: {
      getApiSessions: vi.fn(),
    },
    Issues: {
      getApiIssuesByIssueId: vi.fn().mockResolvedValue({ data: undefined }),
    },
    PullRequests: {
      getApiPullRequestsById: vi.fn().mockResolvedValue({ data: undefined }),
    },
  }
})

// Mock TanStack Router. `mockPathname` is module-scoped so individual tests
// can drive the active-highlight logic by setting it before render.
let mockPathname = '/'

vi.mock('@tanstack/react-router', () => ({
  Link: ({
    children,
    to,
    params,
    onClick,
    className,
    ...rest
  }: {
    children: React.ReactNode
    to: string
    params?: Record<string, string>
    onClick?: () => void
    className?: string
    [key: string]: unknown
  }) => {
    let href = to
    if (params) {
      for (const [k, v] of Object.entries(params)) {
        href = href.replace(`$${k}`, v)
      }
    }
    return (
      <a href={href} onClick={onClick} className={className} {...rest}>
        {children}
      </a>
    )
  },
  useRouterState: () => ({
    location: {
      pathname: mockPathname,
    },
  }),
}))

const mockProjects: Project[] = [
  {
    id: 'proj-1',
    name: 'Project Alpha',
    localPath: '/path/to/alpha',
    defaultBranch: 'main',
    createdAt: '2024-01-01T00:00:00Z',
    updatedAt: '2024-01-02T00:00:00Z',
  },
  {
    id: 'proj-2',
    name: 'Project Beta',
    localPath: '/path/to/beta',
    defaultBranch: 'develop',
    gitHubOwner: 'owner',
    gitHubRepo: 'repo',
    createdAt: '2024-02-01T00:00:00Z',
    updatedAt: '2024-02-02T00:00:00Z',
  },
]

function makeSession(overrides: Partial<SessionSummary>): SessionSummary {
  return {
    id: 'session-default',
    entityId: 'entity-default',
    projectId: 'proj-1',
    model: 'sonnet',
    mode: SessionMode.BUILD,
    status: ClaudeSessionStatus.RUNNING,
    createdAt: '2024-01-01T10:00:00Z',
    lastActivityAt: '2024-01-01T10:00:00Z',
    ...overrides,
  }
}

function createWrapper(queryClient: QueryClient) {
  return ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client: queryClient }, children)
}

function makeQueryClient() {
  return new QueryClient({ defaultOptions: { queries: { retry: false } } })
}

describe('Sidebar', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockPathname = '/'
    ;(Sessions.getApiSessions as Mock).mockResolvedValue({ data: [] })
    ;(Issues.getApiIssuesByIssueId as Mock).mockResolvedValue({ data: undefined })
    ;(PullRequests.getApiPullRequestsById as Mock).mockResolvedValue({ data: undefined })
  })

  it('renders the sidebar with branding', () => {
    ;(Projects.getApiProjects as Mock).mockResolvedValueOnce({ data: [] })

    render(<Sidebar />, { wrapper: createWrapper(makeQueryClient()) })

    expect(screen.getByText('Homespun')).toBeInTheDocument()
  })

  it('renders All Projects link', () => {
    ;(Projects.getApiProjects as Mock).mockResolvedValueOnce({ data: [] })

    render(<Sidebar />, { wrapper: createWrapper(makeQueryClient()) })

    expect(screen.getByText('All Projects')).toBeInTheDocument()
  })

  it('renders Sessions link', () => {
    ;(Projects.getApiProjects as Mock).mockResolvedValueOnce({ data: [] })

    render(<Sidebar />, { wrapper: createWrapper(makeQueryClient()) })

    expect(screen.getByText('Sessions')).toBeInTheDocument()
  })

  it('renders Settings link', () => {
    ;(Projects.getApiProjects as Mock).mockResolvedValueOnce({ data: [] })

    render(<Sidebar />, { wrapper: createWrapper(makeQueryClient()) })

    expect(screen.getByText('Settings')).toBeInTheDocument()
  })

  it('renders version in footer', () => {
    ;(Projects.getApiProjects as Mock).mockResolvedValueOnce({ data: [] })

    render(<Sidebar />, { wrapper: createWrapper(makeQueryClient()) })

    expect(screen.getByText('Homespun v0.1.0')).toBeInTheDocument()
  })

  describe('Project links', () => {
    it('displays project links when projects are loaded', async () => {
      ;(Projects.getApiProjects as Mock).mockResolvedValueOnce({ data: mockProjects })

      render(<Sidebar />, { wrapper: createWrapper(makeQueryClient()) })

      await waitFor(() => {
        expect(screen.getByText('Project Alpha')).toBeInTheDocument()
      })

      expect(screen.getByText('Project Beta')).toBeInTheDocument()
    })

    it('links to correct project URL', async () => {
      ;(Projects.getApiProjects as Mock).mockResolvedValueOnce({ data: mockProjects })

      render(<Sidebar />, { wrapper: createWrapper(makeQueryClient()) })

      await waitFor(() => {
        expect(screen.getByText('Project Alpha')).toBeInTheDocument()
      })

      const projectAlphaLink = screen.getByText('Project Alpha').closest('a')
      const projectBetaLink = screen.getByText('Project Beta').closest('a')

      expect(projectAlphaLink).toHaveAttribute('href', '/projects/proj-1')
      expect(projectBetaLink).toHaveAttribute('href', '/projects/proj-2')
    })

    it('renders no project links when projects list is empty', async () => {
      ;(Projects.getApiProjects as Mock).mockResolvedValueOnce({ data: [] })

      render(<Sidebar />, { wrapper: createWrapper(makeQueryClient()) })

      await waitFor(() => {
        expect(Projects.getApiProjects).toHaveBeenCalled()
      })

      expect(screen.getByText('All Projects')).toBeInTheDocument()
      expect(screen.queryByText('Project Alpha')).not.toBeInTheDocument()
    })

    it('calls onNavigate when project link is clicked', async () => {
      ;(Projects.getApiProjects as Mock).mockResolvedValueOnce({ data: mockProjects })

      const onNavigate = vi.fn()
      render(<Sidebar onNavigate={onNavigate} />, { wrapper: createWrapper(makeQueryClient()) })

      await waitFor(() => {
        expect(screen.getByText('Project Alpha')).toBeInTheDocument()
      })

      screen.getByText('Project Alpha').click()

      expect(onNavigate).toHaveBeenCalled()
    })

    it('renders project links with indentation', async () => {
      ;(Projects.getApiProjects as Mock).mockResolvedValueOnce({ data: mockProjects })

      render(<Sidebar />, { wrapper: createWrapper(makeQueryClient()) })

      await waitFor(() => {
        expect(screen.getByText('Project Alpha')).toBeInTheDocument()
      })

      const projectAlphaLink = screen.getByText('Project Alpha').closest('a')
      expect(projectAlphaLink).toHaveClass('pl-8')
    })
  })

  describe('Session list (live updates)', () => {
    it('renders no chevron toggle for projects with zero non-STOPPED sessions', async () => {
      ;(Projects.getApiProjects as Mock).mockResolvedValueOnce({ data: mockProjects })
      ;(Sessions.getApiSessions as Mock).mockResolvedValue({
        data: [makeSession({ id: 'a', projectId: 'proj-1', status: ClaudeSessionStatus.STOPPED })],
      })

      render(<Sidebar />, { wrapper: createWrapper(makeQueryClient()) })

      await waitFor(() => {
        expect(screen.getByText('Project Alpha')).toBeInTheDocument()
      })

      expect(screen.queryByTestId('sidebar-project-toggle-proj-1')).not.toBeInTheDocument()
    })

    it('renders the chevron + session row for a project with at least one running session', async () => {
      ;(Projects.getApiProjects as Mock).mockResolvedValueOnce({ data: mockProjects })
      ;(Sessions.getApiSessions as Mock).mockResolvedValue({
        data: [
          makeSession({
            id: 'sess-running',
            projectId: 'proj-1',
            entityId: 'Implement OAuth',
            status: ClaudeSessionStatus.RUNNING,
          }),
        ],
      })

      render(<Sidebar />, { wrapper: createWrapper(makeQueryClient()) })

      await waitFor(() => {
        expect(screen.getByTestId('sidebar-project-toggle-proj-1')).toBeInTheDocument()
      })

      await waitFor(() => {
        expect(screen.getByTestId('sidebar-session-sess-running')).toBeInTheDocument()
      })

      const dot = screen.getByTestId('sidebar-session-sess-running-dot')
      expect(dot).toHaveClass('bg-green-500')
    })

    it('SessionStarted: a new row appears after invalidation', async () => {
      const queryClient = makeQueryClient()
      ;(Projects.getApiProjects as Mock).mockResolvedValueOnce({ data: mockProjects })
      ;(Sessions.getApiSessions as Mock).mockResolvedValue({ data: [] })

      render(<Sidebar />, { wrapper: createWrapper(queryClient) })

      await waitFor(() => {
        expect(screen.getByText('Project Alpha')).toBeInTheDocument()
      })
      expect(screen.queryByTestId('sidebar-project-toggle-proj-1')).not.toBeInTheDocument()
      ;(Sessions.getApiSessions as Mock).mockResolvedValue({
        data: [
          makeSession({
            id: 'newly-started',
            projectId: 'proj-1',
            entityId: 'Newly started session',
            status: ClaudeSessionStatus.RUNNING,
          }),
        ],
      })

      await act(async () => {
        await invalidateAllSessionsQueries(queryClient)
      })

      await waitFor(() => {
        expect(screen.getByTestId('sidebar-session-newly-started')).toBeInTheDocument()
      })
    })

    it('SessionStatusChanged: dot colour changes from green to yellow after invalidation', async () => {
      const queryClient = makeQueryClient()
      ;(Projects.getApiProjects as Mock).mockResolvedValueOnce({ data: mockProjects })
      ;(Sessions.getApiSessions as Mock).mockResolvedValue({
        data: [
          makeSession({
            id: 'changing',
            projectId: 'proj-1',
            entityId: 'Status will change',
            status: ClaudeSessionStatus.RUNNING,
          }),
        ],
      })

      render(<Sidebar />, { wrapper: createWrapper(queryClient) })

      await waitFor(() => {
        expect(screen.getByTestId('sidebar-session-changing-dot')).toHaveClass('bg-green-500')
      })
      ;(Sessions.getApiSessions as Mock).mockResolvedValue({
        data: [
          makeSession({
            id: 'changing',
            projectId: 'proj-1',
            entityId: 'Status will change',
            status: ClaudeSessionStatus.WAITING_FOR_INPUT,
          }),
        ],
      })

      await act(async () => {
        await invalidateAllSessionsQueries(queryClient)
      })

      await waitFor(() => {
        expect(screen.getByTestId('sidebar-session-changing-dot')).toHaveClass('bg-yellow-500')
      })
    })

    it('SessionStopped: the row disappears after the session transitions to STOPPED', async () => {
      const queryClient = makeQueryClient()
      ;(Projects.getApiProjects as Mock).mockResolvedValueOnce({ data: mockProjects })
      ;(Sessions.getApiSessions as Mock).mockResolvedValue({
        data: [
          makeSession({
            id: 'will-stop',
            projectId: 'proj-1',
            entityId: 'Will stop',
            status: ClaudeSessionStatus.RUNNING,
          }),
        ],
      })

      render(<Sidebar />, { wrapper: createWrapper(queryClient) })

      await waitFor(() => {
        expect(screen.getByTestId('sidebar-session-will-stop')).toBeInTheDocument()
      })
      ;(Sessions.getApiSessions as Mock).mockResolvedValue({
        data: [
          makeSession({
            id: 'will-stop',
            projectId: 'proj-1',
            entityId: 'Will stop',
            status: ClaudeSessionStatus.STOPPED,
          }),
        ],
      })

      await act(async () => {
        await invalidateAllSessionsQueries(queryClient)
      })

      await waitFor(() => {
        expect(screen.queryByTestId('sidebar-session-will-stop')).not.toBeInTheDocument()
      })
    })

    it('any of the six lifecycle event invalidations refetch the all-sessions query', async () => {
      const queryClient = makeQueryClient()
      ;(Projects.getApiProjects as Mock).mockResolvedValueOnce({ data: mockProjects })
      const sessionsMock = Sessions.getApiSessions as Mock
      sessionsMock.mockResolvedValue({ data: [] })

      render(<Sidebar />, { wrapper: createWrapper(queryClient) })

      await waitFor(() => {
        expect(screen.getByText('Project Alpha')).toBeInTheDocument()
      })

      const callsAfterInitialMount = sessionsMock.mock.calls.length
      expect(callsAfterInitialMount).toBeGreaterThan(0)

      // Each of the six lifecycle events ultimately funnels through
      // `invalidateAllSessionsQueries`, so a single invalidation per event is
      // sufficient to assert that the all-sessions query is registered under
      // the invalidation namespace.
      for (let i = 0; i < 6; i++) {
        await act(async () => {
          await invalidateAllSessionsQueries(queryClient)
        })
      }

      await waitFor(() => {
        expect(sessionsMock.mock.calls.length).toBeGreaterThan(callsAfterInitialMount)
      })
    })
  })

  describe('Active highlight', () => {
    const ACTIVE_CLASSES = ['bg-sidebar-accent', 'text-sidebar-accent-foreground']

    function getGlobalSessionsLink() {
      // The global Sessions <NavItem> is a sibling to Settings; identify it by
      // its href so it can never be confused with a per-project session row.
      const links = screen.getAllByRole('link')
      const link = links.find((el) => el.getAttribute('href') === '/sessions')
      if (!link) throw new Error('Global Sessions link not found')
      return link
    }

    it('global Sessions link is highlighted when on /sessions', async () => {
      mockPathname = '/sessions'
      ;(Projects.getApiProjects as Mock).mockResolvedValueOnce({ data: mockProjects })
      ;(Sessions.getApiSessions as Mock).mockResolvedValue({
        data: [
          makeSession({
            id: 'sess-running',
            projectId: 'proj-1',
            entityId: 'Sess running',
            status: ClaudeSessionStatus.RUNNING,
          }),
        ],
      })

      render(<Sidebar />, { wrapper: createWrapper(makeQueryClient()) })

      await waitFor(() => {
        expect(screen.getByTestId('sidebar-session-sess-running')).toBeInTheDocument()
      })

      const globalLink = getGlobalSessionsLink()
      for (const cls of ACTIVE_CLASSES) {
        expect(globalLink).toHaveClass(cls)
      }

      const row = screen.getByTestId('sidebar-session-sess-running')
      expect(row).not.toHaveAttribute('aria-current', 'page')
      for (const cls of ACTIVE_CLASSES) {
        expect(row).not.toHaveClass(cls)
      }
    })

    it('session row is highlighted (and global Sessions is NOT) when on /sessions/$sessionId', async () => {
      mockPathname = '/sessions/sess-running'
      ;(Projects.getApiProjects as Mock).mockResolvedValueOnce({ data: mockProjects })
      ;(Sessions.getApiSessions as Mock).mockResolvedValue({
        data: [
          makeSession({
            id: 'sess-running',
            projectId: 'proj-1',
            entityId: 'Sess running',
            status: ClaudeSessionStatus.RUNNING,
          }),
        ],
      })

      render(<Sidebar />, { wrapper: createWrapper(makeQueryClient()) })

      await waitFor(() => {
        expect(screen.getByTestId('sidebar-session-sess-running')).toBeInTheDocument()
      })

      const row = screen.getByTestId('sidebar-session-sess-running')
      expect(row).toHaveAttribute('aria-current', 'page')
      for (const cls of ACTIVE_CLASSES) {
        expect(row).toHaveClass(cls)
      }

      const globalLink = getGlobalSessionsLink()
      for (const cls of ACTIVE_CLASSES) {
        expect(globalLink).not.toHaveClass(cls)
      }
    })

    it('no row is highlighted when /sessions/<id> does not match any rendered session', async () => {
      mockPathname = '/sessions/some-other-id'
      ;(Projects.getApiProjects as Mock).mockResolvedValueOnce({ data: mockProjects })
      ;(Sessions.getApiSessions as Mock).mockResolvedValue({
        data: [
          makeSession({
            id: 'sess-running',
            projectId: 'proj-1',
            entityId: 'Sess running',
            status: ClaudeSessionStatus.RUNNING,
          }),
        ],
      })

      render(<Sidebar />, { wrapper: createWrapper(makeQueryClient()) })

      await waitFor(() => {
        expect(screen.getByTestId('sidebar-session-sess-running')).toBeInTheDocument()
      })

      const row = screen.getByTestId('sidebar-session-sess-running')
      expect(row).not.toHaveAttribute('aria-current', 'page')
      for (const cls of ACTIVE_CLASSES) {
        expect(row).not.toHaveClass(cls)
      }
    })
  })
})
