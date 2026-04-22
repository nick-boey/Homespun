import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, fireEvent } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import * as React from 'react'
import { Projects } from '@/api'

const navigateMock = vi.fn()

vi.mock('@/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/api')>()
  return {
    ...actual,
    Projects: {
      getApiProjectsById: vi.fn(),
      deleteApiProjectsById: vi.fn(),
    },
  }
})

vi.mock('@/features/projects', () => ({
  useProject: () => ({
    project: {
      id: 'proj-1',
      name: 'My Project',
      localPath: '/tmp/project',
      defaultBranch: 'main',
      createdAt: '2024-01-01T00:00:00Z',
      updatedAt: '2024-01-02T00:00:00Z',
    },
    isLoading: false,
    isError: false,
    refetch: vi.fn(),
  }),
  useDeleteProject: () => ({
    mutateAsync: async (projectId: string) => {
      await Projects.deleteApiProjectsById({ path: { id: projectId } })
      return projectId
    },
    isPending: false,
  }),
  PullSyncButton: () => null,
}))

vi.mock('@/hooks/use-breadcrumbs', () => ({
  useBreadcrumbSetter: vi.fn(),
}))

vi.mock('@tanstack/react-router', () => ({
  createFileRoute: vi.fn(() => (config: { component: React.ComponentType }) => ({
    component: config.component,
  })),
  useParams: () => ({ projectId: 'proj-1' }),
  useNavigate: () => navigateMock,
  useRouterState: () => '/projects/proj-1',
  Link: ({ children, ...props }: React.ComponentProps<'a'>) => <a {...props}>{children}</a>,
  Outlet: () => <div data-testid="outlet" />,
}))

// Import after mocks so the module picks up the mocked dependencies
import { Route } from './projects.$projectId'

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const ProjectLayout = (Route as any).component as React.ComponentType

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

describe('ProjectLayout — delete flow', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('wires the Delete Project dropdown item to the confirmation dialog', async () => {
    const user = userEvent.setup()
    render(<ProjectLayout />, { wrapper: createWrapper() })

    await user.click(screen.getByRole('button', { name: /project actions/i }))

    const deleteItem = await screen.findByRole('menuitem', { name: /delete project/i })
    await user.click(deleteItem)

    const dialog = await screen.findByRole('alertdialog')
    expect(dialog).toHaveTextContent(/are you sure you want to delete "my project"/i)
  })

  it('calls delete API and navigates home when confirmed', async () => {
    vi.mocked(Projects.deleteApiProjectsById).mockResolvedValue({
      data: undefined,
      error: undefined,
      request: new Request('http://test'),
      response: new Response(null, { status: 204 }),
    } as Awaited<ReturnType<typeof Projects.deleteApiProjectsById>>)

    const user = userEvent.setup()
    render(<ProjectLayout />, { wrapper: createWrapper() })

    await user.click(screen.getByRole('button', { name: /project actions/i }))
    await user.click(await screen.findByRole('menuitem', { name: /delete project/i }))

    const dialog = await screen.findByRole('alertdialog')
    const confirmButton = Array.from(dialog.querySelectorAll('button')).find(
      (b) => b.textContent === 'Delete'
    )
    expect(confirmButton).toBeDefined()
    fireEvent.click(confirmButton!)

    await waitFor(() => {
      expect(Projects.deleteApiProjectsById).toHaveBeenCalledWith({ path: { id: 'proj-1' } })
    })
    await waitFor(() => {
      expect(navigateMock).toHaveBeenCalledWith({ to: '/' })
    })
  })
})
