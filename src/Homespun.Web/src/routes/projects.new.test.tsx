import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { Projects } from '@/api'
import NewProject from './projects.new'

// Mock the API
vi.mock('@/api', () => ({
  Projects: {
    postApiProjects: vi.fn(),
  },
}))

// Mock the breadcrumb hook
vi.mock('@/hooks/use-breadcrumbs', () => ({
  useBreadcrumbSetter: vi.fn(),
}))

// Mock TanStack Router
const mockNavigate = vi.fn()
vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual('@tanstack/react-router')
  return {
    ...actual,
    useNavigate: () => mockNavigate,
    Link: ({ children, to }: { children: React.ReactNode; to: string }) => (
      <a
        href={to}
        onClick={(e) => {
          e.preventDefault()
          mockNavigate({ to })
        }}
      >
        {children}
      </a>
    ),
    createFileRoute: () => () => ({ component: () => null }),
  }
})

const createWrapper = () => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  })
  return ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  )
}

describe('NewProject Page', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('renders the page with title and form fields', () => {
    render(<NewProject />, { wrapper: createWrapper() })

    expect(screen.getByText('New Project')).toBeInTheDocument()
    expect(screen.getByLabelText(/project name/i)).toBeInTheDocument()
    expect(screen.getByLabelText(/repository/i)).toBeInTheDocument()
    expect(screen.getByLabelText(/default branch/i)).toBeInTheDocument()
  })

  it('shows validation error when submitting without project name', async () => {
    const user = userEvent.setup()
    render(<NewProject />, { wrapper: createWrapper() })

    const submitButton = screen.getByRole('button', { name: /create project/i })
    await user.click(submitButton)

    await waitFor(() => {
      expect(screen.getByText(/project name is required/i)).toBeInTheDocument()
    })
  })

  it('shows validation error when submitting without repository', async () => {
    const user = userEvent.setup()
    render(<NewProject />, { wrapper: createWrapper() })

    const nameInput = screen.getByLabelText(/project name/i)
    await user.type(nameInput, 'My Project')

    const submitButton = screen.getByRole('button', { name: /create project/i })
    await user.click(submitButton)

    await waitFor(() => {
      expect(screen.getByText(/repository.*required/i)).toBeInTheDocument()
    })
  })

  it('creates project successfully and navigates to project detail', async () => {
    const mockProject = {
      id: 'new-project-id',
      name: 'Test Project',
      localPath: '/path/to/repo',
      defaultBranch: 'main',
    }

    vi.mocked(Projects.postApiProjects).mockResolvedValue({
      data: mockProject,
      error: undefined,
      request: new Request('http://test'),
      response: new Response(),
    })

    const user = userEvent.setup()
    render(<NewProject />, { wrapper: createWrapper() })

    const nameInput = screen.getByLabelText(/project name/i)
    const repoInput = screen.getByLabelText(/repository/i)

    await user.type(nameInput, 'Test Project')
    await user.type(repoInput, 'owner/repo')

    const submitButton = screen.getByRole('button', { name: /create project/i })
    await user.click(submitButton)

    await waitFor(() => {
      expect(Projects.postApiProjects).toHaveBeenCalledWith({
        body: {
          name: 'Test Project',
          ownerRepo: 'owner/repo',
          defaultBranch: 'main',
        },
      })
    })

    await waitFor(() => {
      expect(mockNavigate).toHaveBeenCalledWith({
        to: '/projects/$projectId',
        params: { projectId: 'new-project-id' },
      })
    })
  })

  it('shows error message when project creation fails', async () => {
    vi.mocked(Projects.postApiProjects).mockResolvedValue({
      data: undefined,
      error: { message: 'Project already exists' },
      request: new Request('http://test'),
      response: new Response(null, { status: 400 }),
    })

    const user = userEvent.setup()
    render(<NewProject />, { wrapper: createWrapper() })

    const nameInput = screen.getByLabelText(/project name/i)
    const repoInput = screen.getByLabelText(/repository/i)

    await user.type(nameInput, 'Test Project')
    await user.type(repoInput, 'owner/repo')

    const submitButton = screen.getByRole('button', { name: /create project/i })
    await user.click(submitButton)

    await waitFor(() => {
      expect(screen.getByRole('alert')).toBeInTheDocument()
    })
  })

  it('navigates back to projects list when cancel button is clicked', async () => {
    const user = userEvent.setup()
    render(<NewProject />, { wrapper: createWrapper() })

    const cancelButton = screen.getByRole('link', { name: /cancel/i })
    await user.click(cancelButton)

    expect(mockNavigate).toHaveBeenCalledWith({ to: '/' })
  })

  it('uses default branch value of main when not specified', async () => {
    const mockProject = {
      id: 'new-project-id',
      name: 'Test Project',
      localPath: '/path/to/repo',
      defaultBranch: 'main',
    }

    vi.mocked(Projects.postApiProjects).mockResolvedValue({
      data: mockProject,
      error: undefined,
      request: new Request('http://test'),
      response: new Response(),
    })

    const user = userEvent.setup()
    render(<NewProject />, { wrapper: createWrapper() })

    const nameInput = screen.getByLabelText(/project name/i)
    const repoInput = screen.getByLabelText(/repository/i)

    await user.type(nameInput, 'Test Project')
    await user.type(repoInput, 'owner/repo')

    const submitButton = screen.getByRole('button', { name: /create project/i })
    await user.click(submitButton)

    await waitFor(() => {
      expect(Projects.postApiProjects).toHaveBeenCalledWith({
        body: expect.objectContaining({
          defaultBranch: 'main',
        }),
      })
    })
  })

  it('allows custom default branch to be specified', async () => {
    const mockProject = {
      id: 'new-project-id',
      name: 'Test Project',
      localPath: '/path/to/repo',
      defaultBranch: 'develop',
    }

    vi.mocked(Projects.postApiProjects).mockResolvedValue({
      data: mockProject,
      error: undefined,
      request: new Request('http://test'),
      response: new Response(),
    })

    const user = userEvent.setup()
    render(<NewProject />, { wrapper: createWrapper() })

    const nameInput = screen.getByLabelText(/project name/i)
    const repoInput = screen.getByLabelText(/repository/i)
    const branchInput = screen.getByLabelText(/default branch/i)

    await user.type(nameInput, 'Test Project')
    await user.type(repoInput, 'owner/repo')
    await user.clear(branchInput)
    await user.type(branchInput, 'develop')

    const submitButton = screen.getByRole('button', { name: /create project/i })
    await user.click(submitButton)

    await waitFor(() => {
      expect(Projects.postApiProjects).toHaveBeenCalledWith({
        body: expect.objectContaining({
          defaultBranch: 'develop',
        }),
      })
    })
  })

  it('disables submit button while form is submitting', async () => {
    let resolvePromise: (value: unknown) => void
    const pendingPromise = new Promise((resolve) => {
      resolvePromise = resolve
    })

    vi.mocked(Projects.postApiProjects).mockReturnValue(pendingPromise as never)

    const user = userEvent.setup()
    render(<NewProject />, { wrapper: createWrapper() })

    const nameInput = screen.getByLabelText(/project name/i)
    const repoInput = screen.getByLabelText(/repository/i)

    await user.type(nameInput, 'Test Project')
    await user.type(repoInput, 'owner/repo')

    const submitButton = screen.getByRole('button', { name: /create project/i })
    await user.click(submitButton)

    await waitFor(() => {
      expect(submitButton).toBeDisabled()
    })

    // Resolve to clean up
    resolvePromise!({
      data: { id: 'test' },
      error: undefined,
      request: new Request('http://test'),
      response: new Response(),
    })
  })

  it('renders back button to navigate to projects list', () => {
    render(<NewProject />, { wrapper: createWrapper() })

    const backButton = screen.getByRole('link', { name: '' })
    expect(backButton).toHaveAttribute('href', '/')
  })
})

describe('NewProject Page - Local Projects', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('renders tabs for GitHub and Local project types', () => {
    render(<NewProject />, { wrapper: createWrapper() })

    expect(screen.getByRole('tab', { name: /github repository/i })).toBeInTheDocument()
    expect(screen.getByRole('tab', { name: /local project/i })).toBeInTheDocument()
  })

  it('defaults to GitHub project type with repository field visible', () => {
    render(<NewProject />, { wrapper: createWrapper() })

    const githubTab = screen.getByRole('tab', { name: /github repository/i })
    expect(githubTab).toHaveAttribute('data-state', 'active')
    expect(screen.getByLabelText(/repository/i)).toBeInTheDocument()
  })

  it('hides repository field when Local tab is selected', async () => {
    const user = userEvent.setup()
    render(<NewProject />, { wrapper: createWrapper() })

    const localTab = screen.getByRole('tab', { name: /local project/i })
    await user.click(localTab)

    expect(screen.queryByLabelText(/repository/i)).not.toBeInTheDocument()
    expect(screen.getByText(/creates a new local git repository/i)).toBeInTheDocument()
  })

  it('creates local project without ownerRepo when Local is selected', async () => {
    const mockProject = {
      id: 'local-project-id',
      name: 'my-local-project',
      localPath: '/path/to/local-project',
      defaultBranch: 'main',
    }

    vi.mocked(Projects.postApiProjects).mockResolvedValue({
      data: mockProject,
      error: undefined,
      request: new Request('http://test'),
      response: new Response(),
    })

    const user = userEvent.setup()
    render(<NewProject />, { wrapper: createWrapper() })

    // Switch to Local tab
    const localTab = screen.getByRole('tab', { name: /local project/i })
    await user.click(localTab)

    // Fill in project name
    const nameInput = screen.getByLabelText(/project name/i)
    await user.type(nameInput, 'my-local-project')

    // Submit the form
    const submitButton = screen.getByRole('button', { name: /create project/i })
    await user.click(submitButton)

    await waitFor(() => {
      expect(Projects.postApiProjects).toHaveBeenCalledWith({
        body: {
          name: 'my-local-project',
          ownerRepo: undefined,
          defaultBranch: 'main',
        },
      })
    })

    await waitFor(() => {
      expect(mockNavigate).toHaveBeenCalledWith({
        to: '/projects/$projectId',
        params: { projectId: 'local-project-id' },
      })
    })
  })

  it('validates local project name allows only alphanumeric, hyphens, underscores', async () => {
    const user = userEvent.setup()
    render(<NewProject />, { wrapper: createWrapper() })

    // Switch to Local tab
    const localTab = screen.getByRole('tab', { name: /local project/i })
    await user.click(localTab)

    // Enter an invalid project name with special characters
    const nameInput = screen.getByLabelText(/project name/i)
    await user.type(nameInput, 'my project@name!')

    // Submit the form
    const submitButton = screen.getByRole('button', { name: /create project/i })
    await user.click(submitButton)

    await waitFor(() => {
      const alert = screen.getByRole('alert')
      expect(alert).toHaveTextContent(
        /project name must use only letters, numbers, hyphens, and underscores/i
      )
    })
  })

  it('still requires ownerRepo when GitHub tab is selected', async () => {
    const user = userEvent.setup()
    render(<NewProject />, { wrapper: createWrapper() })

    // Ensure GitHub tab is selected (default)
    const githubTab = screen.getByRole('tab', { name: /github repository/i })
    expect(githubTab).toHaveAttribute('data-state', 'active')

    // Fill in project name but not repository
    const nameInput = screen.getByLabelText(/project name/i)
    await user.type(nameInput, 'My Project')

    // Submit the form
    const submitButton = screen.getByRole('button', { name: /create project/i })
    await user.click(submitButton)

    await waitFor(() => {
      expect(screen.getByText(/repository.*required/i)).toBeInTheDocument()
    })
  })
})
