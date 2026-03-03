import { describe, it, expect, vi, beforeEach, type Mock } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createElement, type ReactNode } from 'react'
import { ProjectsList } from './projects-list'
import { Projects } from '@/api'
import type { Project } from '@/api/generated/types.gen'

vi.mock('@/api', () => ({
  Projects: {
    getApiProjects: vi.fn(),
    deleteApiProjectsById: vi.fn(),
  },
}))

// Mock TanStack Router Link
vi.mock('@tanstack/react-router', () => ({
  Link: ({
    children,
    to,
    params,
    ...props
  }: {
    children: React.ReactNode
    to: string
    params?: Record<string, string>
    [key: string]: unknown
  }) => {
    let href = to
    if (params) {
      for (const [key, value] of Object.entries(params)) {
        href = href.replace(`$${key}`, value)
      }
    }
    return (
      <a href={href} {...props}>
        {children}
      </a>
    )
  },
}))

const mockProjects: Project[] = [
  {
    id: '1',
    name: 'Project One',
    localPath: '/path/to/project-one',
    defaultBranch: 'main',
    createdAt: '2024-01-01T00:00:00Z',
    updatedAt: '2024-01-02T00:00:00Z',
  },
  {
    id: '2',
    name: 'Project Two',
    localPath: '/path/to/project-two',
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
      mutations: {
        retry: false,
      },
    },
  })
  return ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client: queryClient }, children)
}

describe('ProjectsList', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('shows loading skeletons while fetching', async () => {
    const mockGetApiProjects = Projects.getApiProjects as Mock
    mockGetApiProjects.mockReturnValue(new Promise(() => {})) // Never resolves

    render(<ProjectsList />, { wrapper: createWrapper() })

    expect(screen.getAllByTestId('project-card-skeleton')).toHaveLength(3)
  })

  it('displays projects when loaded', async () => {
    const mockGetApiProjects = Projects.getApiProjects as Mock
    mockGetApiProjects.mockResolvedValueOnce({ data: mockProjects })

    render(<ProjectsList />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText('Project One')).toBeInTheDocument()
    })

    expect(screen.getByText('Project Two')).toBeInTheDocument()
  })

  it('shows empty state when no projects exist', async () => {
    const mockGetApiProjects = Projects.getApiProjects as Mock
    mockGetApiProjects.mockResolvedValueOnce({ data: [] })

    render(<ProjectsList />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText(/no projects/i)).toBeInTheDocument()
    })

    expect(screen.getByRole('link', { name: /create.*project/i })).toBeInTheDocument()
  })

  it('shows error state with retry button when fetch fails', async () => {
    const mockGetApiProjects = Projects.getApiProjects as Mock
    mockGetApiProjects.mockRejectedValueOnce(new Error('Network error'))

    render(<ProjectsList />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText(/error/i)).toBeInTheDocument()
    })

    expect(screen.getByRole('button', { name: /retry/i })).toBeInTheDocument()
  })

  it('retries fetch when retry button is clicked', async () => {
    const user = userEvent.setup()
    const mockGetApiProjects = Projects.getApiProjects as Mock
    mockGetApiProjects
      .mockRejectedValueOnce(new Error('Network error'))
      .mockResolvedValueOnce({ data: mockProjects })

    render(<ProjectsList />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByRole('button', { name: /retry/i })).toBeInTheDocument()
    })

    await user.click(screen.getByRole('button', { name: /retry/i }))

    await waitFor(() => {
      expect(screen.getByText('Project One')).toBeInTheDocument()
    })
  })

  it('deletes project when confirmed', async () => {
    const user = userEvent.setup()
    const mockGetApiProjects = Projects.getApiProjects as Mock
    const mockDeleteApiProjectsById = Projects.deleteApiProjectsById as Mock

    mockGetApiProjects.mockResolvedValue({ data: mockProjects })
    mockDeleteApiProjectsById.mockResolvedValueOnce({})

    render(<ProjectsList />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText('Project One')).toBeInTheDocument()
    })

    // Find delete button for first project
    const deleteButtons = screen.getAllByRole('button', { name: /delete/i })
    await user.click(deleteButtons[0])

    // Confirm deletion
    const confirmButton = screen.getByRole('button', { name: /^delete$/i })
    await user.click(confirmButton)

    await waitFor(() => {
      expect(mockDeleteApiProjectsById).toHaveBeenCalledWith({
        path: { id: '1' },
      })
    })
  })
})
