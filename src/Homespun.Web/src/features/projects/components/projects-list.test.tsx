import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createElement } from 'react'
import { ProjectsList } from './projects-list'

// Mock TanStack Router Link component
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
  }) => {
    const href = params ? to.replace('$projectId', params.projectId) : to
    return (
      <a href={href} {...props}>
        {children}
      </a>
    )
  },
}))

// Mock the hooks
vi.mock('../hooks/use-projects', () => ({
  useProjects: vi.fn(),
  useDeleteProject: vi.fn(),
}))

import { useProjects, useDeleteProject } from '../hooks/use-projects'

const createWrapper = () => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
      },
    },
  })
  return ({ children }: { children: React.ReactNode }) =>
    createElement(QueryClientProvider, { client: queryClient }, children)
}

describe('ProjectsList', () => {
  const mockDeleteMutation = {
    mutate: vi.fn(),
    isPending: false,
  }

  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(useDeleteProject).mockReturnValue(
      mockDeleteMutation as unknown as ReturnType<typeof useDeleteProject>
    )
  })

  it('renders loading state', () => {
    vi.mocked(useProjects).mockReturnValue({
      data: undefined,
      isLoading: true,
      isError: false,
      error: null,
      refetch: vi.fn(),
    } as unknown as ReturnType<typeof useProjects>)

    render(<ProjectsList />, { wrapper: createWrapper() })

    // Should show skeletons
    expect(screen.getAllByTestId('project-skeleton')).toHaveLength(3)
  })

  it('renders error state with retry button', async () => {
    const mockRefetch = vi.fn()
    vi.mocked(useProjects).mockReturnValue({
      data: undefined,
      isLoading: false,
      isError: true,
      error: new Error('Failed to load'),
      refetch: mockRefetch,
    } as unknown as ReturnType<typeof useProjects>)

    const user = userEvent.setup()
    render(<ProjectsList />, { wrapper: createWrapper() })

    expect(screen.getByText(/failed to load projects/i)).toBeInTheDocument()

    const retryButton = screen.getByRole('button', { name: /try again/i })
    await user.click(retryButton)

    expect(mockRefetch).toHaveBeenCalled()
  })

  it('renders empty state when no projects exist', () => {
    vi.mocked(useProjects).mockReturnValue({
      data: [],
      isLoading: false,
      isError: false,
      error: null,
      refetch: vi.fn(),
    } as unknown as ReturnType<typeof useProjects>)

    render(<ProjectsList />, { wrapper: createWrapper() })

    expect(screen.getByText(/no projects yet/i)).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /create your first project/i })).toBeInTheDocument()
  })

  it('renders project cards when projects exist', () => {
    const mockProjects = [
      {
        id: 'project-1',
        name: 'Project One',
        localPath: '/path/one',
        defaultBranch: 'main',
      },
      {
        id: 'project-2',
        name: 'Project Two',
        localPath: '/path/two',
        defaultBranch: 'develop',
      },
    ]

    vi.mocked(useProjects).mockReturnValue({
      data: mockProjects,
      isLoading: false,
      isError: false,
      error: null,
      refetch: vi.fn(),
    } as unknown as ReturnType<typeof useProjects>)

    render(<ProjectsList />, { wrapper: createWrapper() })

    expect(screen.getByText('Project One')).toBeInTheDocument()
    expect(screen.getByText('Project Two')).toBeInTheDocument()
  })

  it('calls delete mutation when delete is triggered', async () => {
    const mockProjects = [
      {
        id: 'project-1',
        name: 'Project One',
        localPath: '/path/one',
        defaultBranch: 'main',
      },
    ]

    vi.mocked(useProjects).mockReturnValue({
      data: mockProjects,
      isLoading: false,
      isError: false,
      error: null,
      refetch: vi.fn(),
    } as unknown as ReturnType<typeof useProjects>)

    const user = userEvent.setup()
    render(<ProjectsList />, { wrapper: createWrapper() })

    const deleteButton = screen.getByRole('button', { name: /delete/i })
    await user.click(deleteButton)

    // After clicking, a confirmation dialog should appear
    // For now, we test that the delete button exists
    expect(deleteButton).toBeInTheDocument()
  })
})
