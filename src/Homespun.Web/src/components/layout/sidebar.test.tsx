import { describe, it, expect, vi, beforeEach, type Mock } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createElement, type ReactNode } from 'react'
import { Sidebar } from './sidebar'
import { Projects } from '@/api'
import type { Project } from '@/api/generated/types.gen'

vi.mock('@/api', () => ({
  Projects: {
    getApiProjects: vi.fn(),
  },
}))

// Mock TanStack Router
vi.mock('@tanstack/react-router', () => ({
  Link: ({
    children,
    to,
    onClick,
    className,
  }: {
    children: React.ReactNode
    to: string
    onClick?: () => void
    className?: string
  }) => (
    <a href={to} onClick={onClick} className={className}>
      {children}
    </a>
  ),
  useRouterState: () => ({
    location: {
      pathname: '/',
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

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
      },
    },
  })
  return ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client: queryClient }, children)
}

describe('Sidebar', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('renders the sidebar with branding', () => {
    const mockGetApiProjects = Projects.getApiProjects as Mock
    mockGetApiProjects.mockResolvedValueOnce({ data: [] })

    render(<Sidebar />, { wrapper: createWrapper() })

    expect(screen.getByText('Homespun')).toBeInTheDocument()
  })

  it('renders All Projects link', () => {
    const mockGetApiProjects = Projects.getApiProjects as Mock
    mockGetApiProjects.mockResolvedValueOnce({ data: [] })

    render(<Sidebar />, { wrapper: createWrapper() })

    expect(screen.getByText('All Projects')).toBeInTheDocument()
  })

  it('renders Sessions link', () => {
    const mockGetApiProjects = Projects.getApiProjects as Mock
    mockGetApiProjects.mockResolvedValueOnce({ data: [] })

    render(<Sidebar />, { wrapper: createWrapper() })

    expect(screen.getByText('Sessions')).toBeInTheDocument()
  })

  it('renders Settings link', () => {
    const mockGetApiProjects = Projects.getApiProjects as Mock
    mockGetApiProjects.mockResolvedValueOnce({ data: [] })

    render(<Sidebar />, { wrapper: createWrapper() })

    expect(screen.getByText('Settings')).toBeInTheDocument()
  })

  it('renders version in footer', () => {
    const mockGetApiProjects = Projects.getApiProjects as Mock
    mockGetApiProjects.mockResolvedValueOnce({ data: [] })

    render(<Sidebar />, { wrapper: createWrapper() })

    expect(screen.getByText('Homespun v0.1.0')).toBeInTheDocument()
  })

  describe('Project links', () => {
    it('displays project links when projects are loaded', async () => {
      const mockGetApiProjects = Projects.getApiProjects as Mock
      mockGetApiProjects.mockResolvedValueOnce({ data: mockProjects })

      render(<Sidebar />, { wrapper: createWrapper() })

      await waitFor(() => {
        expect(screen.getByText('Project Alpha')).toBeInTheDocument()
      })

      expect(screen.getByText('Project Beta')).toBeInTheDocument()
    })

    it('links to correct project URL', async () => {
      const mockGetApiProjects = Projects.getApiProjects as Mock
      mockGetApiProjects.mockResolvedValueOnce({ data: mockProjects })

      render(<Sidebar />, { wrapper: createWrapper() })

      await waitFor(() => {
        expect(screen.getByText('Project Alpha')).toBeInTheDocument()
      })

      const projectAlphaLink = screen.getByText('Project Alpha').closest('a')
      const projectBetaLink = screen.getByText('Project Beta').closest('a')

      expect(projectAlphaLink).toHaveAttribute('href', '/projects/proj-1')
      expect(projectBetaLink).toHaveAttribute('href', '/projects/proj-2')
    })

    it('renders no project links when projects list is empty', async () => {
      const mockGetApiProjects = Projects.getApiProjects as Mock
      mockGetApiProjects.mockResolvedValueOnce({ data: [] })

      render(<Sidebar />, { wrapper: createWrapper() })

      await waitFor(() => {
        expect(mockGetApiProjects).toHaveBeenCalled()
      })

      // Should still have All Projects link
      expect(screen.getByText('All Projects')).toBeInTheDocument()

      // But no individual project links
      expect(screen.queryByText('Project Alpha')).not.toBeInTheDocument()
    })

    it('calls onNavigate when project link is clicked', async () => {
      const mockGetApiProjects = Projects.getApiProjects as Mock
      mockGetApiProjects.mockResolvedValueOnce({ data: mockProjects })

      const onNavigate = vi.fn()
      render(<Sidebar onNavigate={onNavigate} />, { wrapper: createWrapper() })

      await waitFor(() => {
        expect(screen.getByText('Project Alpha')).toBeInTheDocument()
      })

      screen.getByText('Project Alpha').click()

      expect(onNavigate).toHaveBeenCalled()
    })

    it('renders project links with indentation', async () => {
      const mockGetApiProjects = Projects.getApiProjects as Mock
      mockGetApiProjects.mockResolvedValueOnce({ data: mockProjects })

      render(<Sidebar />, { wrapper: createWrapper() })

      await waitFor(() => {
        expect(screen.getByText('Project Alpha')).toBeInTheDocument()
      })

      const projectAlphaLink = screen.getByText('Project Alpha').closest('a')
      expect(projectAlphaLink).toHaveClass('pl-8')
    })
  })
})
