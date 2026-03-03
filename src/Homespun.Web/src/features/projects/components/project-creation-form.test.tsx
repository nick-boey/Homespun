import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createElement } from 'react'
import { ProjectCreationForm } from './project-creation-form'

// Mock TanStack Router
const mockNavigate = vi.fn()
vi.mock('@tanstack/react-router', () => ({
  useNavigate: () => mockNavigate,
  Link: ({
    children,
    to,
    ...props
  }: {
    children: React.ReactNode
    to: string
  }) => (
    <a href={to} {...props}>
      {children}
    </a>
  ),
}))

// Mock the hooks
vi.mock('../hooks/use-projects', () => ({
  useCreateProject: vi.fn(),
}))

import { useCreateProject } from '../hooks/use-projects'

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

describe('ProjectCreationForm', () => {
  const mockMutate = vi.fn()

  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(useCreateProject).mockReturnValue({
      mutate: mockMutate,
      mutateAsync: vi.fn(),
      isPending: false,
      isSuccess: false,
      isError: false,
      error: null,
      data: undefined,
      reset: vi.fn(),
    } as unknown as ReturnType<typeof useCreateProject>)
  })

  it('renders form fields', () => {
    render(<ProjectCreationForm />, { wrapper: createWrapper() })

    expect(screen.getByLabelText(/github repository/i)).toBeInTheDocument()
    expect(screen.getByLabelText(/default branch/i)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /create project/i })).toBeInTheDocument()
  })

  it('shows validation error for empty required field', async () => {
    const user = userEvent.setup()

    render(<ProjectCreationForm />, { wrapper: createWrapper() })

    const submitButton = screen.getByRole('button', { name: /create project/i })
    await user.click(submitButton)

    expect(await screen.findByText(/github repository is required/i)).toBeInTheDocument()
    expect(mockMutate).not.toHaveBeenCalled()
  })

  it('submits form with valid data', async () => {
    const user = userEvent.setup()
    const mockMutateAsync = vi.fn().mockResolvedValue({ id: 'new-project-id' })

    vi.mocked(useCreateProject).mockReturnValue({
      mutate: mockMutate,
      mutateAsync: mockMutateAsync,
      isPending: false,
      isSuccess: false,
      isError: false,
      error: null,
      data: undefined,
      reset: vi.fn(),
    } as unknown as ReturnType<typeof useCreateProject>)

    render(<ProjectCreationForm />, { wrapper: createWrapper() })

    const repoInput = screen.getByLabelText(/github repository/i)
    await user.type(repoInput, 'owner/repo')

    const submitButton = screen.getByRole('button', { name: /create project/i })
    await user.click(submitButton)

    await waitFor(() => {
      expect(mockMutateAsync).toHaveBeenCalledWith({
        ownerRepo: 'owner/repo',
        defaultBranch: 'main',
      })
    })
  })

  it('navigates to project page on success', async () => {
    const user = userEvent.setup()
    const mockMutateAsync = vi.fn().mockResolvedValue({ id: 'new-project-id' })

    vi.mocked(useCreateProject).mockReturnValue({
      mutate: mockMutate,
      mutateAsync: mockMutateAsync,
      isPending: false,
      isSuccess: false,
      isError: false,
      error: null,
      data: undefined,
      reset: vi.fn(),
    } as unknown as ReturnType<typeof useCreateProject>)

    render(<ProjectCreationForm />, { wrapper: createWrapper() })

    const repoInput = screen.getByLabelText(/github repository/i)
    await user.type(repoInput, 'owner/repo')

    const submitButton = screen.getByRole('button', { name: /create project/i })
    await user.click(submitButton)

    await waitFor(() => {
      expect(mockNavigate).toHaveBeenCalledWith({
        to: '/projects/$projectId',
        params: { projectId: 'new-project-id' },
      })
    })
  })

  it('disables form while submitting', () => {
    vi.mocked(useCreateProject).mockReturnValue({
      mutate: mockMutate,
      mutateAsync: vi.fn(),
      isPending: true,
      isSuccess: false,
      isError: false,
      error: null,
      data: undefined,
      reset: vi.fn(),
    } as unknown as ReturnType<typeof useCreateProject>)

    render(<ProjectCreationForm />, { wrapper: createWrapper() })

    expect(screen.getByRole('button', { name: /creating/i })).toBeDisabled()
  })

  it('shows error message on failure', async () => {
    const user = userEvent.setup()
    const mockMutateAsync = vi.fn().mockRejectedValue(new Error('Failed to create project'))

    vi.mocked(useCreateProject).mockReturnValue({
      mutate: mockMutate,
      mutateAsync: mockMutateAsync,
      isPending: false,
      isSuccess: false,
      isError: false,
      error: null,
      data: undefined,
      reset: vi.fn(),
    } as unknown as ReturnType<typeof useCreateProject>)

    render(<ProjectCreationForm />, { wrapper: createWrapper() })

    const repoInput = screen.getByLabelText(/github repository/i)
    await user.type(repoInput, 'owner/repo')

    const submitButton = screen.getByRole('button', { name: /create project/i })
    await user.click(submitButton)

    await waitFor(() => {
      expect(screen.getByText(/failed to create project/i)).toBeInTheDocument()
    })
  })

  it('has cancel button that links to projects list', () => {
    render(<ProjectCreationForm />, { wrapper: createWrapper() })

    const cancelLink = screen.getByRole('link', { name: /cancel/i })
    expect(cancelLink).toHaveAttribute('href', '/')
  })
})
